using System.Text.Json;
using System.Text.Json.Serialization;
using MovieLibrary.Core.Models;

namespace MovieLibrary.Enrich.Emit;

public sealed class CatalogWriter(string outputRoot)
{
    private readonly Action<string, string> _move = (temp, final) => File.Move(temp, final, overwrite: true);
    private readonly TimeSpan _retryDelay = TimeSpan.FromMilliseconds(100);

    /// <summary>Test seam: lets a test fail or watch the move phase, which a real disk cannot do portably.</summary>
    internal CatalogWriter(string outputRoot, Action<string, string> move, TimeSpan retryDelay) : this(outputRoot)
    {
        _move = move;
        _retryDelay = retryDelay;
    }

    /// <summary>
    /// True once <see cref="WriteAsync"/> has begun moving staged files into place. Until then nothing is published, so
    /// a failure leaves the previous build exactly as it was; from then on the folder may mix new and stale files. Only
    /// this class can tell those apart, and they send whoever has to clean up to opposite ends of the problem, so it
    /// says which. <c>EnrichRun</c> picks its wording from this; the verdict (do not deploy, exit 1) is the same either
    /// way. One writer per run: it is never reset.
    /// </summary>
    public bool MovesBegan { get; private set; }

    /// <summary>
    /// The attribution sentence was not <see cref="CatalogMeta.TmdbAttribution"/>. It has a type of its own because it
    /// is a developer error, where the <see cref="InvalidDataException"/>s raised here are operator ones: a caller
    /// that blames all of them on the Shows tab sends the owner to fix a title that is not wrong. It cannot derive
    /// from <see cref="InvalidDataException"/>, which is sealed, so callers list it beside that one.
    /// </summary>
    public sealed class AttributionException(string message) : InvalidOperationException(message);

    /// <summary>
    /// Minified: these files are downloaded by every visitor. No <c>NewLine</c> is set, unlike
    /// <c>CacheStore</c> whose indented index is committed: minified output holds no line break at all
    /// (string values escape theirs), so it is byte-identical on Windows and Linux. The default encoder is
    /// kept on purpose: it escapes non-ASCII, apostrophes, ampersands and angle brackets as \uXXXX escapes,
    /// costing about 4% raw and 0.5% gzipped, and keeps TMDB's third-party text inert if a file is ever
    /// inlined into HTML.
    /// </summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Grid-level projection; episodes are deliberately absent.</summary>
    private sealed record ShowIndexEntry(
        int I, string T, string Slug, string? P, int? FirstYear, string Ag,
        int SeasonCount, int OwnedEpisodes, int TotalEpisodes, double Mc);

    /// <summary>
    /// Writes movies.json, shows.json, tv/{slug}.json and meta.json under the output root.
    /// </summary>
    /// <remarks>
    /// <para><b>What is guaranteed, and what is not.</b> Each file is published atomically: it is written beside its
    /// destination as <c>*.writing</c> and moved into place, so a reader never sees a truncated file. The
    /// <i>staging</i> phase is all-or-nothing: if any file fails to serialize, or the run is cancelled, nothing has been
    /// published and the previous catalog is exactly as it was (a first run leaves no files). The <i>move</i> phase is
    /// not atomic across files. It is best-effort ordered, with a bounded retry (see <c>MoveWithRetryAsync</c>), so
    /// that a partial failure leaves no dangling reference: series files move first, then <c>movies.json</c> and
    /// <c>shows.json</c>, and <c>meta.json</c> last. If a move still fails after its retries the index therefore
    /// never names a series file that is not there yet, and <c>meta.json</c> is the commit marker: a build is
    /// complete only when it carries this run's <c>builtUtc</c>. That is all the order guarantees. It does <b>not</b>
    /// make the folder consistent: after a failure in the series phase the old <c>shows.json</c> still carries the
    /// old episode and season counts beside <c>tv/*.json</c> files that are new and may disagree with them, and
    /// after a failure in the index phase <c>movies.json</c> may be new beside an old <c>shows.json</c>. A caller
    /// that sees the exception must not publish the folder. Once everything is staged the moves ignore <c>ct</c>:
    /// finishing beats stopping half way.</para>
    /// <para>Slugs name files, so they are checked here too, before anything is written (<c>TvPipeline</c> checks them
    /// first, before it spends a request): a clash would otherwise surface late, as a failed move after other files
    /// had already been published. Files from earlier runs are not deleted: a series that was renamed or dropped
    /// keeps its old tv file until the folder is cleared (CI starts from a clean checkout). Only <c>*.writing</c>
    /// files, abandoned by a run that was killed, are swept.</para>
    /// </remarks>
    /// <exception cref="AttributionException">
    /// <see cref="CatalogMeta.Attribution"/> is not exactly <see cref="CatalogMeta.TmdbAttribution"/>. Nothing is
    /// written.
    /// </exception>
    /// <exception cref="InvalidDataException">
    /// A series slug cannot name a file or repeats another's. Nothing is written.
    /// </exception>
    public async Task WriteAsync(
        IReadOnlyList<MovieRecord> movies,
        IReadOnlyList<ShowRecord> shows,
        CatalogMeta meta,
        CancellationToken ct)
    {
        if (!string.Equals(meta.Attribution, CatalogMeta.TmdbAttribution, StringComparison.Ordinal))
            throw new AttributionException(
                "The attribution sentence is fixed by TMDB's terms and may not be changed. Pass " +
                "CatalogMeta.TmdbAttribution; nothing was written.");

        EnsureSlugsAreUsable(shows);

        Directory.CreateDirectory(outputRoot);
        Directory.CreateDirectory(Path.Combine(outputRoot, "tv"));

        foreach (var abandoned in Directory.EnumerateFiles(outputRoot, "*" + TempSuffix, SearchOption.AllDirectories))
            File.Delete(abandoned);

        var staged = new List<(string Temp, string Final)>();
        try
        {
            // Staging order is publishing order: series files first, then the index, and meta.json last.
            foreach (var show in shows)
                await StageAsync(Path.Combine("tv", $"{show.Slug}.json"), show, staged, ct);

            await StageAsync("movies.json", movies, staged, ct);

            var index = shows.Select(s => new ShowIndexEntry(
                s.I, s.T, s.Slug, s.P, s.FirstYear, s.Ag,
                NumberedSeasons(s), s.OwnedEpisodes, s.TotalEpisodes, s.Mc)).ToList();

            await StageAsync("shows.json", index, staged, ct);

            await StageAsync("meta.json", meta, staged, ct);

            // Nothing above this line is published. From here on a failure can leave some files moved and some not.
            MovesBegan = true;

            while (staged.Count > 0)
            {
                await MoveWithRetryAsync(staged[0].Temp, staged[0].Final);
                staged.RemoveAt(0);
            }
        }
        finally
        {
            // Best effort, one file at a time: a temp that is still held open makes its delete throw, and that must
            // neither replace the exception already in flight nor stop the deletes after it.
            foreach (var (temp, _) in staged) TryDelete(temp);
        }
    }

    private const string TempSuffix = ".writing";
    private const int MoveAttempts = 3;

    /// <summary>
    /// A move is sometimes refused for a moment, not for good: an antivirus scan, a search indexer or a dev server
    /// holds a handle. Three attempts with a pause that grows (100 ms, then 200 ms), so a hold shorter than about
    /// 300 ms is waited out and a longer one surfaces. What is retried is decided by <see cref="MayBeTransient"/>.
    /// </summary>
    /// <remarks>
    /// <b>Measured</b> on Windows 11 (10.0.26300), .NET 10, with <c>File.Move(temp, final, overwrite: true)</c> and a
    /// real handle held by the same process. A held <i>destination</i> throws <see cref="UnauthorizedAccessException"/>
    /// (0x80070005) whatever share mode the holder opened it with, <c>FileShare.ReadWrite | FileShare.Delete</c>
    /// included. A held <i>source</i> throws <see cref="IOException"/> (0x80070020, a sharing violation), unless the
    /// holder shared <c>Delete</c>, in which case the move succeeds. Both are retried. A separate process holding the
    /// handle was not tried, and Linux was not measured: there a rename over an open file is expected to succeed, so
    /// neither case arises.
    /// <see cref="UnauthorizedAccessException"/> is also what Windows throws for causes that will not clear: a
    /// read-only destination, or a directory standing where the file should go (both measured). Its type cannot tell
    /// those from a held handle, so they are retried too and surface after the same 300 ms.
    /// </remarks>
    private async Task MoveWithRetryAsync(string temp, string final)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                _move(temp, final);
                return;
            }
            catch (Exception ex) when (attempt < MoveAttempts && MayBeTransient(ex))
            {
                await Task.Delay(_retryDelay * attempt);
            }
        }
    }

    /// <summary>
    /// <see cref="UnauthorizedAccessException"/> and <see cref="IOException"/>, except the three <see cref="IOException"/>
    /// subtypes that say the path itself is wrong (<see cref="FileNotFoundException"/>,
    /// <see cref="DirectoryNotFoundException"/>, <see cref="PathTooLongException"/>): waiting cannot fix those, so they
    /// surface at once. Nothing else is retried. Other permanent <see cref="IOException"/>s are not told apart: an
    /// over-long file name, for one, arrives as a plain <see cref="IOException"/> (0x8007007B, measured) and costs the
    /// full pause before it surfaces.
    /// </summary>
    private static bool MayBeTransient(Exception ex) => ex switch
    {
        FileNotFoundException or DirectoryNotFoundException or PathTooLongException => false,
        IOException or UnauthorizedAccessException => true,
        _ => false
    };

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* leave it for the next run's sweep */ }
    }

    private static void EnsureSlugsAreUsable(IReadOnlyList<ShowRecord> shows)
    {
        // Case-insensitive: "Alias" and "alias" are one file on Windows and macOS.
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var show in shows)
        {
            var slug = show.Slug;
            if (string.IsNullOrWhiteSpace(slug) || slug != slug.Trim() || slug is "." or ".."
                || slug.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || slug.IndexOfAny(['/', '\\']) >= 0)
                throw new InvalidDataException(
                    $"The series '{show.T}' has the slug '{slug}', which cannot name a file under data/tv. Nothing was written.");

            if (!seen.TryAdd(slug, show.T))
                throw new InvalidDataException(
                    $"The series '{seen[slug]}' and '{show.T}' both have the slug '{slug}', so one would replace the other's " +
                    "data/tv file. Nothing was written.");
        }
    }

    /// <summary>
    /// Season 0 is TvPipeline's "unplaced episodes" bucket (owned rows that matched nothing, or every row of
    /// an unmatched series), not a season. Counting it would show "6 seasons" for a five-season show.
    /// </summary>
    private static int NumberedSeasons(ShowRecord show) => show.Seasons.Count(season => season.N > 0);

    private async Task StageAsync<T>(
        string relativePath, T payload, List<(string Temp, string Final)> staged, CancellationToken ct)
    {
        var final = Path.Combine(outputRoot, relativePath);
        var temp = final + TempSuffix;
        staged.Add((temp, final)); // before the write, so a failure part-way still cleans it up

        await using var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, payload, Options, ct);
    }
}

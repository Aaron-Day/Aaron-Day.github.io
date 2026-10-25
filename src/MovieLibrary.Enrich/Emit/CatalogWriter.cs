using System.Text.Json;
using System.Text.Json.Serialization;
using MovieLibrary.Core.Models;

namespace MovieLibrary.Enrich.Emit;

public sealed class CatalogWriter(string outputRoot)
{
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
    /// Writes movies.json, shows.json, tv/{slug}.json and meta.json under the output root, all or nothing. Every
    /// file is first written beside its destination as <c>*.writing</c>; only when all of them are complete are they
    /// moved into place. A run that throws or is cancelled therefore leaves the previous catalog exactly as it was
    /// (never a truncated <c>movies.json</c>, never new movies beside old shows), and a first run leaves no files.
    /// Once every file is staged the moves are not cancellable: finishing the publish beats leaving it half done.
    /// </summary>
    /// <remarks>
    /// Slugs name files, so they are checked here too, before anything is written (<c>TvPipeline</c> checks them
    /// first, before it spends a request): a clash would otherwise surface late, as a failed move after other files
    /// had already been published. Files from earlier runs are not deleted: a series that was renamed or dropped
    /// keeps its old tv file until the folder is cleared (CI starts from a clean checkout). Only <c>*.writing</c>
    /// files, abandoned by a run that was killed, are swept.
    /// </remarks>
    /// <exception cref="InvalidDataException">
    /// <see cref="CatalogMeta.Attribution"/> is not exactly <see cref="CatalogMeta.TmdbAttribution"/>, or a series slug
    /// cannot name a file or repeats another's. Nothing is written.
    /// </exception>
    public async Task WriteAsync(
        IReadOnlyList<MovieRecord> movies,
        IReadOnlyList<ShowRecord> shows,
        CatalogMeta meta,
        CancellationToken ct)
    {
        if (!string.Equals(meta.Attribution, CatalogMeta.TmdbAttribution, StringComparison.Ordinal))
            throw new InvalidDataException(
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
            await StageAsync("movies.json", movies, staged, ct);

            var index = shows.Select(s => new ShowIndexEntry(
                s.I, s.T, s.Slug, s.P, s.FirstYear, s.Ag,
                NumberedSeasons(s), s.OwnedEpisodes, s.TotalEpisodes, s.Mc)).ToList();

            await StageAsync("shows.json", index, staged, ct);

            foreach (var show in shows)
                await StageAsync(Path.Combine("tv", $"{show.Slug}.json"), show, staged, ct);

            await StageAsync("meta.json", meta, staged, ct);

            while (staged.Count > 0)
            {
                File.Move(staged[0].Temp, staged[0].Final, overwrite: true);
                staged.RemoveAt(0);
            }
        }
        finally
        {
            foreach (var (temp, _) in staged) File.Delete(temp); // a no-op for a file never created
        }
    }

    private const string TempSuffix = ".writing";

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

using System.Net;
using System.Text;
using System.Text.Json;
using MovieLibrary.Core.Throttling;
using MovieLibrary.Enrich.Config;
using MovieLibrary.Enrich.Emit;
using MovieLibrary.Enrich.Run;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// One whole run of the tool with nothing real in it: a Google Sheet that serves whatever CSV the test sets, a TMDB that
/// answers from a delegate (and fails the test on any request it was not told to expect), a frozen clock, and a temp
/// directory for the cache, the output and the two Actions files. Both <see cref="HttpClient"/>s wrap a
/// <see cref="StubHandler"/>, so no test can reach the network.
/// </summary>
internal sealed class RunHarness : IDisposable
{
    public static readonly DateTimeOffset T0 = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "ml-run-" + Guid.NewGuid());
    public string CacheDir => Path.Combine(Root, "cache");
    public string OutDir => Path.Combine(Root, "out");
    public string SummaryPath => Path.Combine(Root, "summary.md");
    public string GitHubOutputPath => Path.Combine(Root, "github-output.txt");

    public StringWriter Out { get; } = new();
    public StringWriter Err { get; } = new();

    public string MoviesCsv { get; set; } = GoogleSheetClientTests.MoviesHeader;
    public string ShowsCsv { get; set; } = GoogleSheetClientTests.ShowsHeader;
    public string OverridesCsv { get; set; } = GoogleSheetClientTests.OverridesHeader;

    /// <summary>Answers a tab ("Movies", "Shows", "Overrides") itself, instead of the CSV properties. Null: use them.</summary>
    public Func<string, HttpResponseMessage?>? SheetOverride { get; set; }

    public Func<HttpRequestMessage, HttpResponseMessage> Tmdb { get; set; } =
        req => throw new Xunit.Sdk.XunitException($"Unexpected TMDB request: {req.RequestUri}");

    public Func<DateTimeOffset> Now { get; set; } = () => T0;
    public CatalogWriter? Writer { get; set; }
    public EnrichOptions Options { get; set; }

    public StubHandler SheetHandler { get; }
    public StubHandler TmdbHandler { get; }

    public RunHarness()
    {
        Directory.CreateDirectory(Root);   // the Actions files live here, and in Actions they exist before the tool starts
        Options = new EnrichOptions("sheet-id", "eyJ.stub.token", 0, CacheDir, OutDir, false, false);
        SheetHandler = new StubHandler(SheetRespond);
        TmdbHandler = new StubHandler(req => Tmdb(req));
    }

    private HttpResponseMessage SheetRespond(HttpRequestMessage req)
    {
        var tab = req.RequestUri!.Query.TrimStart('?').Split('&')
            .Select(p => p.Split('=', 2)).Where(p => p[0] == "sheet")
            .Select(p => Uri.UnescapeDataString(p[1])).Single();

        if (SheetOverride?.Invoke(tab) is { } custom) return custom;

        return GoogleSheetClientTests.Csv(tab switch
        {
            "Movies" => MoviesCsv,
            "Shows" => ShowsCsv,
            "Overrides" => OverridesCsv,
            _ => throw new Xunit.Sdk.XunitException($"Unexpected sheet tab {tab}")
        });
    }

    public RunServices Services() => new(
        new HttpClient(SheetHandler, disposeHandler: false) { BaseAddress = new Uri("https://docs.google.com/") },
        new HttpClient(TmdbHandler, disposeHandler: false) { BaseAddress = new Uri("https://api.themoviedb.org/") },
        Now,
        new SlidingWindowRateLimiter(100_000, TimeSpan.FromSeconds(1)),   // frozen clock: a tight limiter would wait in real time
        Writer ?? new CatalogWriter(OutDir),
        Out, Err, SummaryPath, GitHubOutputPath);

    public async Task<RunResult> RunAsync(CancellationToken ct = default)
    {
        using var services = Services();
        return await new EnrichRun(Options, services).RunAsync(ct);
    }

    // ---- what a run wrote -----------------------------------------------------------------------------

    public string Summary => File.Exists(SummaryPath) ? File.ReadAllText(SummaryPath) : string.Empty;
    public string GitHubOutput => File.Exists(GitHubOutputPath) ? File.ReadAllText(GitHubOutputPath) : string.Empty;
    public bool Wrote(string relative) => File.Exists(Path.Combine(OutDir, relative));

    public JsonDocument Json(string relative) => JsonDocument.Parse(File.ReadAllText(Path.Combine(OutDir, relative)));

    /// <summary>The <c>i</c> of each record in movies.json or shows.json, by title.</summary>
    public Dictionary<string, int> IdsByTitle(string file)
    {
        using var doc = Json(file);
        return doc.RootElement.EnumerateArray()
            .ToDictionary(e => e.GetProperty("t").GetString()!, e => e.GetProperty("i").GetInt32());
    }

    /// <summary>
    /// The review queue as the owner sees it: the job summary's "Needs review" list. It is deliberately NOT read from
    /// <c>meta.json</c>, which publishes an empty list because that file is served publicly and the queue names the
    /// owner's own problem rows (see the remark on <c>EnrichRun.PublishAsync</c>). The summary is the only place the
    /// lines appear, so it is the only place a test can assert them.
    /// </summary>
    public string[] ReviewQueue()
    {
        var lines = Summary.ReplaceLineEndings().Split(Environment.NewLine).ToList();
        var start = lines.FindIndex(l => l.StartsWith("### Needs review", StringComparison.Ordinal));
        if (start < 0) return [];

        return [.. lines.Skip(start + 1)
            .TakeWhile(l => !l.StartsWith("##", StringComparison.Ordinal))
            .Where(l => l.StartsWith("- ", StringComparison.Ordinal))
            .Select(l => l[2..])
            .Where(l => !l.StartsWith("...and ", StringComparison.Ordinal))];
    }

    /// <summary>What <c>meta.json</c> actually carries, for the tests that pin it staying empty.</summary>
    public string[] PublishedReviewQueue()
    {
        using var doc = Json("meta.json");
        return [.. doc.RootElement.GetProperty("reviewQueue").EnumerateArray().Select(e => e.GetString()!)];
    }

    /// <summary>A reloaded cache index, to see what a run left on disk.</summary>
    public async Task<IReadOnlyCollection<MovieLibrary.Core.Caching.CacheEntry>> SavedCacheAsync()
    {
        var store = new MovieLibrary.Enrich.Caching.CacheStore(CacheDir);
        await store.LoadAsync(CancellationToken.None);
        return store.Entries.ToList();
    }

    public void Dispose()
    {
        try { if (Directory.Exists(Root)) Directory.Delete(Root, true); } catch { /* best effort */ }
    }
}

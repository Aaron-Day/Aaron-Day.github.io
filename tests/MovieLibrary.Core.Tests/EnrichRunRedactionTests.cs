namespace MovieLibrary.Core.Tests;

/// <summary>
/// The TMDB token never reaches the console or the job summary. Actions logs on a public repository are world-readable,
/// and this project has already shipped the token twice - once through a record's <c>ToString</c>, once through
/// <c>JsonSerializer.Serialize</c> - so redaction belongs to the print sites rather than to each caller: a message that
/// comes to carry it is covered by construction instead of by someone having checked. The fatal path is pinned by
/// <c>EnrichRunSheetFailureTests</c>; these are the three print sites that were not covered.
/// </summary>
public class EnrichRunRedactionTests
{
    /// <summary>The harness's stub token. Every test here arranges for some text to carry it.</summary>
    private const string Token = "eyJ.stub.token";

    /// <summary>One film TMDB has never heard of, so its Sheet title reaches the review queue.</summary>
    private static RunHarness UnmatchedFilm(string title)
    {
        var h = new RunHarness { MoviesCsv = RunLibrary.Movies((title, "1:30:00", "Nobody", "Drama")) };
        h.Tmdb = req => req.RequestUri!.AbsolutePath == "/3/search/movie"
            ? TestPipeline.Json(TestPipeline.EmptyResults)
            : TestPipeline.NotFound();
        return h;
    }

    /// <summary>
    /// The review queue is built from Sheet titles and printed in full on the summary of a run that succeeded, which
    /// was the one page of this tool's output that nothing redacted.
    /// </summary>
    [Fact]
    public async Task ATokenShapedSheetTitleNeverReachesTheSuccessSummary()
    {
        using var h = UnmatchedFilm($"The {Token} Affair");

        var result = await h.RunAsync();

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Needs review", h.Summary);                  // the line is still there
        Assert.Contains("[redacted]", h.Summary);                    // with the token taken out of it
        Assert.DoesNotContain(Token, h.Summary);
        Assert.DoesNotContain(Token, h.Out.ToString());
    }

    /// <summary>
    /// The cache warning is built from a file-system exception message, which carries the path. A cache root that comes
    /// to contain the token - a checkout path, a runner's temp directory - would put it on stderr and in the summary
    /// banner, neither of which redacted anything.
    /// </summary>
    [Fact]
    public async Task ATokenShapedCachePathNeverReachesTheCacheWarningOrItsBanner()
    {
        using var h = UnmatchedFilm("Zzyzx Road");
        var cacheRoot = Path.Combine(h.Root, $"cache-{Token}");
        h.Options = h.Options with { CacheRoot = cacheRoot };
        Directory.CreateDirectory(Path.Combine(cacheRoot, "index.json"));   // a directory where the file should go

        var result = await h.RunAsync();

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("the cache could not be saved", h.Err.ToString());
        Assert.Contains("[redacted]", h.Err.ToString());
        Assert.DoesNotContain(Token, h.Err.ToString());
        Assert.DoesNotContain(Token, h.Summary);
        Assert.DoesNotContain(Token, h.Out.ToString());
    }

    /// <summary>
    /// An exception whose stack trace carries the token, which a real one cannot: <c>Exception.StackTrace</c> holds
    /// frames, never argument values. That is why this is pinned rather than argued - the print site redacts whatever
    /// it is handed, so the next message to pass through it is covered without anyone having to notice.
    /// </summary>
    private sealed class TokenInTheStackTrace : Exception
    {
        public override string StackTrace => $"   at Somewhere(url: https://example.test/?api_key={Token})";
    }

    [Fact]
    public async Task TheStackTraceTheCatchAllPrintsIsRedactedToo()
    {
        using var h = UnmatchedFilm("Zzyzx Road");
        h.Now = () => throw new TokenInTheStackTrace();

        var result = await h.RunAsync();

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("   at Somewhere", h.Err.ToString());        // the trace is still printed
        Assert.Contains("[redacted]", h.Err.ToString());
        Assert.DoesNotContain(Token, h.Err.ToString());
    }

    /// <summary>
    /// The console lines a healthy run prints interpolate options - the sheet id, the output root, the budget - and a
    /// token-shaped value in one of those would be the first thing on stdout. Redacting inside the print helper rather
    /// than at each call means the next line added there is covered without anyone having to notice.
    /// </summary>
    [Fact]
    public async Task ATokenShapedSheetIdNeverReachesTheConsoleLines()
    {
        using var h = UnmatchedFilm("Zzyzx Road");
        h.Options = h.Options with { SheetId = $"sheet-{Token}" };

        var result = await h.RunAsync();

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Fetching sheet sheet-[redacted]", h.Out.ToString());
        Assert.DoesNotContain(Token, h.Out.ToString());
    }
}

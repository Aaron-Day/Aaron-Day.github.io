using System.Net;
using System.Text;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// Task 17 Step I, and the other ways the first thing the tool does can fail. Every one of these happens before a single
/// TMDB request is spent and before anything is written, and each needs different advice: a sheet that is no longer shared
/// and a tab that was renamed look alike in the log and are fixed in different places.
/// </summary>
public class EnrichRunSheetFailureTests
{
    private static HttpResponseMessage SignInPage() => new(HttpStatusCode.OK)
    {
        Content = new StringContent("<!DOCTYPE html><html><body>Sign in</body></html>", Encoding.UTF8, "text/html")
    };

    private static void AssertNothingSpentOrWritten(RunHarness h)
    {
        Assert.Empty(h.TmdbHandler.Requests);
        Assert.False(h.Wrote("meta.json"));
        Assert.False(h.Wrote("movies.json"));
        Assert.Contains("deployable=false", h.GitHubOutput);
        Assert.Contains("FAILED", h.Summary);
    }

    [Theory]
    [InlineData("Movies")]
    [InlineData("Shows")]
    [InlineData("Overrides")]
    public async Task ASheetThatIsNoLongerSharedIsFatalAndTheAdviceIsAboutSharing(string tab)
    {
        using var h = new RunHarness();
        h.MoviesCsv = RunLibrary.Movies(RunLibrary.Fargo);
        h.SheetOverride = t => t == tab ? SignInPage() : null;

        var result = await h.RunAsync();

        Assert.Equal(1, result.ExitCode);
        var err = h.Err.ToString();
        Assert.Contains("FATAL", err);
        Assert.Contains($"'{tab}'", err);
        Assert.Contains("link-shared", err);
        Assert.Contains("Nothing was written.", err);
        Assert.DoesNotContain("renamed", err);                     // the other advice
        Assert.DoesNotContain("InvalidDataException", err);
        AssertNothingSpentOrWritten(h);
        Assert.Contains("link-shared", h.Summary);
    }

    [Theory]
    [InlineData("Movies")]
    [InlineData("Shows")]
    [InlineData("Overrides")]
    public async Task ATabWithTheWrongColumnsIsFatalAndTheAdviceIsAboutTheTabNotTheSharing(string tab)
    {
        using var h = new RunHarness();
        h.MoviesCsv = RunLibrary.Movies(RunLibrary.Fargo);
        h.SheetOverride = t => t == tab ? GoogleSheetClientTests.Csv("\"Name\",\"Year\"\n\"x\",\"1999\"\n") : null;

        var result = await h.RunAsync();

        Assert.Equal(1, result.ExitCode);
        var err = h.Err.ToString();
        Assert.Contains("FATAL", err);
        Assert.Contains($"'{tab}'", err);
        Assert.Contains("renamed", err);
        Assert.Contains("Nothing was written.", err);
        Assert.DoesNotContain("link-shared", err);                 // the other advice
        Assert.DoesNotContain("shared", err);
        AssertNothingSpentOrWritten(h);
    }

    [Fact]
    public async Task TheTwoSheetFailuresGetDifferentAdvice()
    {
        using var access = new RunHarness { SheetOverride = _ => SignInPage() };
        using var schema = new RunHarness { MoviesCsv = GoogleSheetClientTests.ShowsHeader };

        await access.RunAsync();
        await schema.RunAsync();

        Assert.NotEqual(access.Err.ToString(), schema.Err.ToString());
        Assert.Contains("link-shared", access.Err.ToString());
        Assert.DoesNotContain("link-shared", schema.Err.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task AGoogleErrorStatusIsReportedAsGoogleNotAsATmdbCredential(HttpStatusCode status)
    {
        using var h = new RunHarness { SheetOverride = _ => new HttpResponseMessage(status) };

        var result = await h.RunAsync();

        Assert.Equal(1, result.ExitCode);
        var err = h.Err.ToString();
        Assert.Contains("Google", err);
        Assert.Contains($"{(int)status}", err);
        Assert.Contains("SHEET_ID", err);
        Assert.DoesNotContain("TMDB_TOKEN", err);
        AssertNothingSpentOrWritten(h);
    }

    [Fact]
    public async Task ASheetNetworkFaultIsReportedAsAFaultReachingGoogle()
    {
        using var h = new RunHarness { SheetOverride = _ => throw new HttpRequestException("No such host is known.") };

        var result = await h.RunAsync();

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Google", h.Err.ToString());
        Assert.Contains("No such host is known.", h.Err.ToString());
        Assert.DoesNotContain("TMDB_TOKEN", h.Err.ToString());
    }

    [Fact]
    public async Task TheTmdbTokenNeverReachesTheLogOrTheSummary_EvenInsideAnErrorMessage()
    {
        using var h = new RunHarness();
        h.SheetOverride = _ => throw new HttpRequestException("failed for https://example.test/?api_key=eyJ.stub.token&x=1");

        await h.RunAsync();

        Assert.DoesNotContain("eyJ.stub.token", h.Err.ToString());
        Assert.DoesNotContain("eyJ.stub.token", h.Out.ToString());
        Assert.DoesNotContain("eyJ.stub.token", h.Summary);
        Assert.Contains("[redacted]", h.Err.ToString());
    }

    // ---- a timeout is not a failed request, and not a cancellation --------------------------------

    /// <summary>
    /// Neither <c>HttpClient</c> sets a <c>Timeout</c>, so both carry the 100-second default, and a timeout surfaces
    /// as <c>TaskCanceledException</c> on the client's own token rather than as a failed request. <c>TmdbClient</c>
    /// normalises its own; the Sheet client does not, so without an arm of its own a Sheet that is merely slow lands
    /// in the catch-all and the owner reads a stack trace instead of where to look.
    /// </summary>
    [Fact]
    public async Task ASheetRequestThatTimesOutGetsTheGoogleAdviceAndNotAStackTrace()
    {
        using var h = new RunHarness();
        h.MoviesCsv = RunLibrary.Movies(RunLibrary.Fargo);
        h.SheetOverride = _ => throw new TaskCanceledException(
            "The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.");

        var result = await h.RunAsync();

        Assert.Equal(1, result.ExitCode);
        var err = h.Err.ToString();
        Assert.Contains("timed out", err);
        Assert.Contains("Google Sheets", err);
        Assert.Contains("SHEET_ID", err);
        Assert.Contains("link can view", err);
        Assert.DoesNotContain("Cancelled.", err);                  // not the caller stopping the run
        Assert.DoesNotContain("   at ", err);                      // not a stack trace
        Assert.DoesNotContain("TaskCanceledException", err);
        AssertNothingSpentOrWritten(h);
    }

    /// <summary>
    /// The same exception type, the opposite words: a run the caller stopped keeps its own path, because the cache it
    /// saved on the way out is what makes the re-run cheap and the owner needs to be told that, not told to check a
    /// Sheet that is fine.
    /// </summary>
    [Fact]
    public async Task ACallerCancellationDuringTheSheetFetchIsStillReportedAsCancelled()
    {
        using var h = new RunHarness();
        using var cts = new CancellationTokenSource();
        h.SheetOverride = _ => { cts.Cancel(); throw new TaskCanceledException("stopped by the caller"); };

        var result = await h.RunAsync(cts.Token);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Cancelled.", h.Err.ToString());
        Assert.DoesNotContain("timed out", h.Err.ToString());
    }

    [Fact]
    public void ThePhaseDecidesWhichTimeoutAdviceIsGiven()
    {
        Assert.Contains("SHEET_ID", MovieLibrary.Enrich.Run.EnrichRun.DescribeTimeout(sheet: true));
        Assert.DoesNotContain("SHEET_ID", MovieLibrary.Enrich.Run.EnrichRun.DescribeTimeout(sheet: false));
        Assert.Contains("TMDB", MovieLibrary.Enrich.Run.EnrichRun.DescribeTimeout(sheet: false));
    }
}

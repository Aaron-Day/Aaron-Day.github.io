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
}

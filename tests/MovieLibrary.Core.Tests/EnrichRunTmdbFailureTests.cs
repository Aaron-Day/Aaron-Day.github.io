using System.Net;
using MovieLibrary.Enrich.Run;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// What the operator is told when TMDB, rather than the Sheet, is the problem, and what is checked before any request is
/// spent. The status branches (a bad credential is never retried; a rate limit and a 5xx are retried until they are not)
/// are tested on the message function, because the real retries wait seconds.
/// </summary>
public class EnrichRunTmdbFailureTests
{
    private static HttpRequestException Status(HttpStatusCode? code) => new("TMDB failed", null, code);

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void ARejectedCredentialPointsAtTheSecret(HttpStatusCode code)
    {
        var text = EnrichRun.DescribeTmdbFailure(Status(code));

        Assert.Contains("TMDB rejected the credential", text);
        Assert.Contains("TMDB_TOKEN", text);
    }

    [Fact]
    public void ARateLimitThatOutlastedTheRetriesSaysSo()
    {
        var text = EnrichRun.DescribeTmdbFailure(Status(HttpStatusCode.TooManyRequests));

        Assert.Contains("rate-limited", text);
        Assert.Contains("retries were exhausted", text);
        Assert.DoesNotContain("TMDB_TOKEN", text);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public void AServerErrorThatOutlastedTheRetriesNamesTheStatus(HttpStatusCode code)
    {
        var text = EnrichRun.DescribeTmdbFailure(Status(code));

        Assert.Contains($"{(int)code}", text);
        Assert.Contains("retries were exhausted", text);
    }

    [Fact]
    public void AStatusThatIsNeverRetriedDoesNotClaimItWas()
    {
        var text = EnrichRun.DescribeTmdbFailure(Status(HttpStatusCode.BadRequest));

        Assert.Contains("400", text);
        Assert.DoesNotContain("retries", text);
    }

    [Fact]
    public void AFaultWithNoStatusIsANetworkOrBodyFault()
    {
        var text = EnrichRun.DescribeTmdbFailure(Status(null));

        Assert.Contains("network or response-body fault", text);
        Assert.Contains("five attempts", text);
    }

    // ---- through the whole run ------------------------------------------------------------------------

    private static RunHarness Fargo()
    {
        var h = new RunHarness { MoviesCsv = RunLibrary.Movies(RunLibrary.Fargo) };
        h.Tmdb = RunLibrary.FargoOnly();
        return h;
    }

    [Fact]
    public async Task ARejectedTokenFailsTheRunBeforeAnythingIsWritten()
    {
        using var h = Fargo();
        h.Tmdb = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized);

        var result = await h.RunAsync();

        Assert.Equal(1, result.ExitCode);
        Assert.False(result.Deployable);
        Assert.Contains("TMDB rejected the credential", h.Err.ToString());
        Assert.DoesNotContain("Google", h.Err.ToString());
        Assert.False(h.Wrote("meta.json"));
        Assert.Contains("deployable=false", h.GitHubOutput);
    }

    [Fact]
    public async Task AFailureInTheSeriesPhaseKeepsWhatTheFilmPhaseCost()
    {
        using var h = Fargo();
        h.ShowsCsv = RunLibrary.Shows(("Season 1, Episode 1: Pilot", "The Office", "Comedy"));
        var films = RunLibrary.FargoOnly();
        h.Tmdb = req => req.RequestUri!.AbsolutePath.StartsWith("/3/search/tv", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
            : films(req);

        var result = await h.RunAsync();

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("TMDB_TOKEN", h.Err.ToString());
        Assert.NotEmpty(await h.SavedCacheAsync());               // the film is in the saved index
        Assert.True(File.Exists(Path.Combine(h.CacheDir, "movies", "275.json")));
        Assert.False(h.Wrote("meta.json"));
    }

    [Fact]
    public async Task AnUnexpectedExceptionIsNamedAndStillSavesTheCache()
    {
        using var h = Fargo();
        h.ShowsCsv = RunLibrary.Shows(("Season 1, Episode 1: Pilot", "The Office", "Comedy"));
        var films = RunLibrary.FargoOnly();
        h.Tmdb = req => req.RequestUri!.AbsolutePath.StartsWith("/3/search/tv", StringComparison.Ordinal)
            ? throw new InvalidOperationException("kaboom")
            : films(req);

        var result = await h.RunAsync();

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Enrichment failed", h.Err.ToString());
        Assert.Contains("InvalidOperationException", h.Err.ToString());
        Assert.Contains("kaboom", h.Err.ToString());
        Assert.Contains("   at ", h.Err.ToString());                // an unexpected failure keeps its stack trace for the bug report
        Assert.NotEmpty(await h.SavedCacheAsync());
        Assert.Contains("kaboom", h.Summary);
    }

    [Fact]
    public async Task ADetailsBodyWhoseGenresChangedShapeStopsTheRunLoudly_NotSilently()
    {
        // Task 14 decided this on purpose: a mistyped genres field is wrong for every film, so it aborts the run.
        using var h = Fargo();
        h.Tmdb = RunLibrary.FargoOnly(details: """{"id":275,"title":"Fargo","runtime":98,"genres":"Crime"}""");

        var result = await h.RunAsync();

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Enrichment failed", h.Err.ToString());
        Assert.False(h.Wrote("movies.json"));
        Assert.Contains("deployable=false", h.GitHubOutput);
    }
}

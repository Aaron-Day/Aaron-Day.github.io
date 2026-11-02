using MovieLibrary.Enrich.Emit;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// Task 17 Steps A, B and D. The publish is ordered and retried but not atomic across files, so an exception that
/// surfaces out of <c>CatalogWriter.WriteAsync</c> leaves <c>wwwroot/data</c> mixing new and stale files. The tool must fail
/// the run and say so loudly enough that the deploy step cannot take it for success. Whatever fails, the TMDB responses
/// already paid for are in the cache, and the cache is saved.
/// </summary>
public class EnrichRunPublishFailureTests
{
    private static RunHarness FargoRun()
    {
        var h = new RunHarness();
        h.MoviesCsv = RunLibrary.Movies(RunLibrary.Fargo);
        h.Tmdb = RunLibrary.FargoOnly();
        return h;
    }

    private static CatalogWriter FailingMoves(RunHarness h, Exception failure)
        => new(h.OutDir, (_, _) => throw failure, TimeSpan.Zero);

    public static TheoryData<Exception> MoveFailures => new()
    {
        new IOException("sharing violation on movies.json"),
        new UnauthorizedAccessException("access to shows.json is denied"),
        new InvalidOperationException("something nobody planned for"),
    };

    [Theory]
    [MemberData(nameof(MoveFailures))]
    public async Task AFailureOutOfWriteAsyncFailsTheRunAndSaysDoNotDeploy(Exception failure)
    {
        using var h = FargoRun();
        h.Writer = FailingMoves(h, failure);

        var result = await h.RunAsync();

        Assert.Equal(1, result.ExitCode);
        Assert.False(result.Deployable);
        Assert.StartsWith("FATAL: the catalogue may be only partly published", h.Err.ToString());   // what happened, then what to do
        Assert.Contains(failure.Message, h.Err.ToString());
        Assert.Contains("partly published", h.Err.ToString());
        Assert.Contains("Do not deploy", h.Err.ToString());
        Assert.DoesNotContain("Wrote ", h.Out.ToString());
        Assert.DoesNotContain("FAILED", h.Out.ToString());          // said once, on stderr and the summary page, not echoed
    }

    [Fact]
    public async Task ThePartialPublishIsOnTheJobSummaryAndTheDeployGateSeesFalse()
    {
        using var h = FargoRun();
        h.Writer = FailingMoves(h, new IOException("sharing violation"));

        await h.RunAsync();

        Assert.Contains("FAILED", h.Summary);
        Assert.Contains("Do not deploy", h.Summary);
        Assert.Contains("partly published", h.Summary);
        Assert.Contains("deployable=false", h.GitHubOutput);
        Assert.DoesNotContain("deployable=true", h.GitHubOutput);
    }

    [Fact]
    public async Task APartialPublishStillSavesTheCache_SoTheNextRunDoesNotRespendTheBudget()
    {
        using var h = FargoRun();
        h.Writer = FailingMoves(h, new IOException("sharing violation"));

        await h.RunAsync();

        Assert.NotEmpty(await h.SavedCacheAsync());
        Assert.True(File.Exists(Path.Combine(h.CacheDir, "movies", "275.json")), "and the payload it indexes");
    }

    [Fact]
    public async Task APartialPublishNamesTheOutputFolder()
    {
        using var h = FargoRun();
        h.Writer = FailingMoves(h, new IOException("sharing violation"));

        await h.RunAsync();

        Assert.Contains(h.OutDir, h.Err.ToString());
    }

    [Fact]
    public async Task ACancellationThatLandsDuringTheWriteIsACancellationNotAPartialPublish()
    {
        // The last read of the clock stamps meta.builtUtc, just before the write begins. Count the reads of an identical
        // run, then cancel on that one: staging is all-or-nothing, so nothing has been published.
        int lastRead;
        using (var dry = FargoRun())
        {
            var reads = 0;
            dry.Now = () => { reads++; return RunHarness.T0; };
            await dry.RunAsync();
            lastRead = reads;
        }

        using var h = FargoRun();
        using var cts = new CancellationTokenSource();
        var n = 0;
        h.Now = () => { if (++n == lastRead) cts.Cancel(); return RunHarness.T0; };

        var result = await h.RunAsync(cts.Token);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Cancelled", h.Err.ToString());
        Assert.DoesNotContain("partly published", h.Err.ToString());
        Assert.False(h.Wrote("meta.json"));
        Assert.NotEmpty(await h.SavedCacheAsync());
    }

    // ---- Step B: a bad slug is a data error, named, with no stack trace -------------------------------

    private static RunHarness TwoSeriesOneFile()
    {
        var h = FargoRun();
        h.ShowsCsv = RunLibrary.Shows(
            ("Season 1, Episode 1: Pilot", "Mr. Robot", "Drama"),
            ("Season 1, Episode 1: Pilot", "Mr Robot", "Drama"));
        return h;
    }

    [Fact]
    public async Task ASlugClashNamesBothSeriesAndSaysWhereToFixIt()
    {
        using var h = TwoSeriesOneFile();

        var result = await h.RunAsync();

        Assert.Equal(1, result.ExitCode);
        var err = h.Err.ToString();
        Assert.Contains("FATAL", err);
        Assert.Contains("Mr. Robot", err);
        Assert.Contains("Mr Robot", err);
        Assert.Contains("Shows tab", err);
        Assert.Contains("Overrides", err);
        Assert.DoesNotContain("   at ", err);                    // not a stack trace
        Assert.DoesNotContain("InvalidDataException", err);
    }

    [Fact]
    public async Task ASlugClashWritesNothingAndStillSavesWhatTheFilmsCost()
    {
        using var h = TwoSeriesOneFile();

        await h.RunAsync();

        Assert.False(h.Wrote("meta.json"));
        Assert.False(h.Wrote("movies.json"));
        Assert.NotEmpty(await h.SavedCacheAsync());
        Assert.Contains("deployable=false", h.GitHubOutput);
        Assert.Contains("Mr. Robot", h.Summary);
    }

    [Fact]
    public async Task AFixtureRunNeverSearchesSoTheWritersOwnSlugCheckIsTheOneThatCatchesAClash()
    {
        using var h = TwoSeriesOneFile();
        h.Options = h.Options with { Fixtures = true, Token = "" };

        var result = await h.RunAsync();

        Assert.Equal(1, result.ExitCode);
        var err = h.Err.ToString();
        Assert.Contains("Mr. Robot", err);
        Assert.Contains("Mr Robot", err);
        Assert.Contains("Shows tab", err);
        Assert.DoesNotContain("partly published", err);          // thrown before anything was staged: the folder is untouched
        Assert.False(h.Wrote("meta.json"));
    }

    [Fact]
    public async Task AChangedCreditsShapeIsNotBlamedOnTheShowsTab()
    {
        // The same exception type, from the film phase: TMDB sent credits with the cast as a string.
        using var h = FargoRun();
        h.Tmdb = RunLibrary.FargoOnly(credits: """{"cast":"oops","crew":[]}""");

        var result = await h.RunAsync();

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("credits", h.Err.ToString());
        Assert.Contains("shape this tool does not expect", h.Err.ToString());   // the film-phase advice, not the generic one
        Assert.DoesNotContain("Shows tab", h.Err.ToString());
        Assert.False(h.Wrote("meta.json"));
    }
}

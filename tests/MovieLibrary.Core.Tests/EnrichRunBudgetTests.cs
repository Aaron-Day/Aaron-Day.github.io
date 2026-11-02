namespace MovieLibrary.Core.Tests;

/// <summary>
/// Task 17 Step C. When the request budget halts enrichment, unfetched seasons leave their bare episode rows at season 0:
/// a usable development artefact and a broken public catalogue. The run still writes, because a budget stop is the normal
/// development path, and still exits 0, but it is never marked complete, and Task 18's gate has something to test.
/// </summary>
public class EnrichRunBudgetTests
{
    /// <summary>Three films TMDB knows nothing of. Each costs exactly one search request.</summary>
    private static RunHarness ThreeFilms(int budget)
    {
        var h = new RunHarness();
        h.MoviesCsv = RunLibrary.Movies(
            ("Alien", "1:56:00", "Ridley Scott", "Sci-Fi"),
            ("Brazil", "2:22:00", "Terry Gilliam", "Sci-Fi"),
            ("Casablanca", "1:42:00", "Michael Curtiz", "Drama"));
        h.Tmdb = req => req.RequestUri!.AbsolutePath == "/3/search/movie"
            ? TestPipeline.Json(TestPipeline.EmptyResults)
            : TestPipeline.NotFound();
        h.Options = h.Options with { MaxRequests = budget };
        return h;
    }

    private static string[] OutputLines(RunHarness h)
        => h.GitHubOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    [Fact]
    public async Task ABudgetStoppedRunStillWritesForLocalPreviewAndStillExitsZero()
    {
        using var h = ThreeFilms(budget: 2);

        var result = await h.RunAsync();

        Assert.Equal(0, result.ExitCode);
        Assert.True(h.Wrote("movies.json"));
        Assert.True(h.Wrote("meta.json"));
        Assert.Equal(3, h.IdsByTitle("movies.json").Count);          // every film is emitted, resolved or not
        Assert.Contains("Wrote 3 films and 0 series to", h.Out.ToString());
        Assert.Contains("3 films, 0 episode rows", h.Out.ToString());
    }

    [Fact]
    public async Task ABudgetStoppedRunIsNeverComplete()
    {
        using var h = ThreeFilms(budget: 2);

        var result = await h.RunAsync();

        Assert.True(result.StoppedOnBudget);
        Assert.False(result.Deployable);
        Assert.Equal(2, h.TmdbHandler.Requests.Count);
    }

    [Fact]
    public async Task TheStopIsOnTheConsoleWithTheBudgetAndHowToRaiseIt()
    {
        using var h = ThreeFilms(budget: 2);

        await h.RunAsync();

        var text = h.Out.ToString();
        Assert.Contains("Stopped on the 2-request budget", text);
        Assert.Contains("INCOMPLETE", text);
        Assert.Contains("season 0", text);
        Assert.Contains("MAX_REQUESTS_PER_RUN", text);
        Assert.Contains("before deploying", text);
    }

    [Fact]
    public async Task TheStopIsOnTheJobSummaryAndTheDeployGateCanTestIt()
    {
        using var h = ThreeFilms(budget: 2);

        await h.RunAsync();

        Assert.Contains("INCOMPLETE", h.Summary);
        Assert.Contains("Do not deploy", h.Summary);
        Assert.Contains("Films: **3**", h.Summary);
        Assert.Contains("Series: **0** (0 episodes listed, 0 owned)", h.Summary);
        Assert.Contains("TMDB requests: **2** of 2", h.Summary);
        Assert.Contains("deployable=false", OutputLines(h));
        Assert.Contains("stopped_on_budget=true", OutputLines(h));
    }

    [Fact]
    public async Task ARunThatHadEnoughBudgetIsCompleteAndDeployable()
    {
        using var h = ThreeFilms(budget: 3);                          // exactly the three searches it needs

        var result = await h.RunAsync();

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.StoppedOnBudget);
        Assert.True(result.Deployable);
        Assert.DoesNotContain("INCOMPLETE", h.Out.ToString());
        Assert.DoesNotContain("INCOMPLETE", h.Summary);
        Assert.Contains("deployable=true", OutputLines(h));
        Assert.Contains("stopped_on_budget=false", OutputLines(h));
    }

    [Fact]
    public async Task AnUnlimitedBudgetIsNeverStoppedOnAndTheSummarySaysUnlimited()
    {
        using var h = ThreeFilms(budget: 0);

        var result = await h.RunAsync();

        Assert.True(result.Deployable);
        Assert.Contains("TMDB requests: **3** (unlimited)", h.Summary);
    }

    [Fact]
    public async Task ABudgetThatRunsOutDuringTheSeriesPhaseIsStillIncomplete()
    {
        using var h = ThreeFilms(budget: 3);
        h.ShowsCsv = RunLibrary.Shows(("Season 1, Episode 1: Pilot", "The Office", "Comedy"));
        var films = h.Tmdb;
        h.Tmdb = req => req.RequestUri!.AbsolutePath.StartsWith("/3/search/tv", StringComparison.Ordinal)
            ? TestPipeline.Json(TestPipeline.EmptyResults)
            : films(req);

        var result = await h.RunAsync();

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.StoppedOnBudget);
        Assert.False(result.Deployable);
        Assert.True(h.Wrote("shows.json"));
    }

    /// <summary>
    /// A run can spend its budget in the film phase and then fail in the series phase. It is still a run that stopped
    /// on the budget, and the flag on the outputs file is what Task 18 reads.
    /// </summary>
    [Fact]
    public async Task ARunThatStoppedOnTheBudgetAndThenFailedStillReportsTheStop()
    {
        using var h = ThreeFilms(budget: 1);
        h.ShowsCsv = RunLibrary.Shows(
            ("Season 1, Episode 1: Pilot", "Mr. Robot", "Drama"),
            ("Season 1, Episode 1: Pilot", "Mr Robot", "Drama"));   // one slug for two series: fatal, before any request

        var result = await h.RunAsync();

        Assert.Equal(1, result.ExitCode);
        Assert.True(result.StoppedOnBudget);
        Assert.False(result.Deployable);
        Assert.Contains("stopped_on_budget=true", OutputLines(h));
        Assert.Contains("deployable=false", OutputLines(h));
    }

    /// <summary>And a failure with budget to spare must not claim a stop that never happened.</summary>
    [Fact]
    public async Task AFailureWithBudgetLeftOverReportsNoStop()
    {
        using var h = ThreeFilms(budget: 0);
        h.ShowsCsv = RunLibrary.Shows(
            ("Season 1, Episode 1: Pilot", "Mr. Robot", "Drama"),
            ("Season 1, Episode 1: Pilot", "Mr Robot", "Drama"));

        var result = await h.RunAsync();

        Assert.Equal(1, result.ExitCode);
        Assert.False(result.StoppedOnBudget);
        Assert.Contains("stopped_on_budget=false", OutputLines(h));
    }
}

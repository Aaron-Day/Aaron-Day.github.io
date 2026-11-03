namespace MovieLibrary.Core.Tests;

/// <summary>
/// Task 16's M5. The Sheet client refuses a tab whose body is empty, but a tab that comes back with its header row and
/// no data rows passes every check there is: a renamed or moved tab, a change to the Sheet's sharing, or an export that
/// ran while the tab was empty arrives as a perfectly valid library of nothing. Published, that is an empty
/// <c>movies.json</c>, <c>deployable=true</c>, exit 0, and the live site losing its whole film library behind a green
/// check mark - and neither genre guard can catch it, because both are about matched films and there are none.
/// </summary>
/// <remarks>
/// Everything that reaches the guard, enumerated, because three guards in this project have been found firing where
/// their own reason did not reach: a <b>fixture</b> run cannot reach it at all (the fixture branch returns first, and a
/// synthetic catalogue is never deployed); a <b>budget-stopped</b> run cannot fire it, because every Sheet row is
/// emitted whether or not it was resolved, so a spent budget cannot turn a real library into zero films - and in any
/// case the guard runs before the first request; a <b>small</b> library passes, because the test is zero and not a
/// percentage; and an empty <b>Shows</b> tab is not its business, because a library of films alone is legitimate.
/// </remarks>
public class EnrichRunEmptyLibraryTests
{
    /// <summary>The Movies tab with its header and nothing under it. TMDB is never asked anything.</summary>
    private static RunHarness HeaderOnly()
    {
        var h = new RunHarness();                                  // the harness default IS the bare header
        h.ShowsCsv = RunLibrary.Shows(("Season 1, Episode 1: Pilot", "The Office", "Comedy"));
        return h;
    }

    [Fact]
    public async Task AMoviesTabWithNoRowsIsRefusedInsteadOfPublishingAnEmptyLibrary()
    {
        using var h = HeaderOnly();

        var result = await h.RunAsync();

        Assert.Equal(1, result.ExitCode);
        Assert.False(result.Deployable);
        Assert.False(h.Wrote("movies.json"));
        Assert.False(h.Wrote("meta.json"));
        Assert.Contains("deployable=false", h.GitHubOutput);
        Assert.Empty(h.TmdbHandler.Requests);                      // before a single request is spent
    }

    [Fact]
    public async Task TheRefusalNamesTheTabAndTheLikelyCause()
    {
        using var h = HeaderOnly();

        await h.RunAsync();

        var err = h.Err.ToString();
        Assert.Contains("FATAL", err);
        Assert.Contains("Movies tab", err);
        Assert.Contains("header row but no films", err);
        Assert.Contains("renamed", err);                           // what to go and look at
        Assert.Contains("sharing", err);
        Assert.Contains("nothing was spent", err);
        Assert.Contains("Movies tab", h.Summary);                  // and on the page the owner reads
    }

    /// <summary>
    /// A fixture run is synthesised from the Sheet and is never deployed, so an empty Sheet is an empty fixture and
    /// that is all. The guard sits after the fixture branch has returned, so this is structural, not a flag check.
    /// </summary>
    [Fact]
    public async Task AFixtureRunIsNotRefusedForASheetItCannotDeployAnyway()
    {
        using var h = HeaderOnly();
        h.Options = h.Options with { Fixtures = true, Token = "" };

        var result = await h.RunAsync();

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.Deployable);                           // because it is synthetic, not because it is empty
        Assert.True(h.Wrote("movies.json"));
    }

    [Fact]
    public async Task AOneFilmLibraryIsNotSecondGuessed()
    {
        using var h = new RunHarness { MoviesCsv = RunLibrary.Movies(RunLibrary.Fargo) };
        h.Tmdb = RunLibrary.FargoOnly();

        var result = await h.RunAsync();

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Deployable);
        Assert.Single(h.IdsByTitle("movies.json"));
    }

    /// <summary>
    /// A budget-stopped run still emits every Sheet row, resolved or not, so it cannot present an empty film list -
    /// and the guard has already run by the time any budget could be spent. It is written out because a guard firing
    /// on a budget stop is a mistake this project has made before.
    /// </summary>
    [Fact]
    public async Task ABudgetStoppedRunIsNotRefused_ItsFilmsAreAllStillThere()
    {
        using var h = new RunHarness();
        h.MoviesCsv = RunLibrary.Movies(
            ("Alien", "1:56:00", "Ridley Scott", "Sci-Fi"),
            ("Brazil", "2:22:00", "Terry Gilliam", "Sci-Fi"),
            ("Casablanca", "1:42:00", "Michael Curtiz", "Drama"));
        h.Tmdb = req => req.RequestUri!.AbsolutePath == "/3/search/movie"
            ? TestPipeline.Json(TestPipeline.EmptyResults)
            : TestPipeline.NotFound();
        h.Options = h.Options with { MaxRequests = 1 };

        var result = await h.RunAsync();

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.StoppedOnBudget);
        Assert.Equal(3, h.IdsByTitle("movies.json").Count);
    }

    /// <summary>A library of films and no series is legitimate, and the Shows tab is not what this guard is about.</summary>
    [Fact]
    public async Task AnEmptyShowsTabIsStillPublished()
    {
        using var h = new RunHarness { MoviesCsv = RunLibrary.Movies(RunLibrary.Fargo) };
        h.Tmdb = RunLibrary.FargoOnly();

        var result = await h.RunAsync();

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Deployable);
        Assert.Contains("Wrote 1 films and 0 series to", h.Out.ToString());
    }
}

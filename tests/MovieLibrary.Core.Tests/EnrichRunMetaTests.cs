using MovieLibrary.Core.Models;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// Task 17 Steps E and F, real-run side, and the one-clock rule. <c>meta.json</c> is what the site reads for its counts,
/// its genre facet and its attribution, and the clock that stamps it must be the same non-decreasing clock the rate limiter
/// uses, so a backwards step of the wall clock cannot make two freshness decisions in one run disagree.
/// </summary>
public class EnrichRunMetaTests
{
    /// <summary>Fargo, and The Office as TMDB lists it: three episodes in season 1, of which the Sheet owns one.</summary>
    private static RunHarness Library(string details = RunLibrary.FargoDetails, string credits = RunLibrary.FargoCredits)
    {
        var h = new RunHarness();
        h.MoviesCsv = RunLibrary.Movies(RunLibrary.Fargo);
        h.ShowsCsv = RunLibrary.Shows(("Season 1, Episode 1: Pilot", "The Office", "Comedy"));
        var films = RunLibrary.FargoOnly(details, credits);
        var tv = new FakeTv { AnyQuery = new FakeShow(2316, "The Office", [new FakeSeason(1, ["Pilot", "Diversity Day", "Health Care"])]) };
        h.Tmdb = req => req.RequestUri!.AbsolutePath.StartsWith("/3/search/tv", StringComparison.Ordinal)
                        || req.RequestUri.AbsolutePath.StartsWith("/3/tv/", StringComparison.Ordinal)
            ? tv.Respond(req)
            : films(req);
        return h;
    }

    [Fact]
    public async Task ARealRunThatFinishedIsDeployable()
    {
        using var h = Library();

        var result = await h.RunAsync();

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Deployable);
        Assert.False(result.StoppedOnBudget);
        Assert.Contains("deployable=true", h.GitHubOutput);
        Assert.Contains("Wrote 1 films and 1 series to", h.Out.ToString());
        Assert.Contains("1 films, 1 episode rows", h.Out.ToString());
    }

    [Fact]
    public async Task TheCountsAreTheCataloguesOwn_TotalAndOwnedEpisodesKeptApart()
    {
        using var h = Library();

        await h.RunAsync();

        using var doc = h.Json("meta.json");
        var meta = doc.RootElement;
        Assert.Equal(1, meta.GetProperty("movieCount").GetInt32());
        Assert.Equal(1, meta.GetProperty("showCount").GetInt32());
        Assert.Equal(3, meta.GetProperty("totalEpisodeCount").GetInt32());     // every episode TMDB lists
        Assert.Equal(1, meta.GetProperty("ownedEpisodeCount").GetInt32());     // only the one the Sheet has
    }

    [Fact]
    public async Task TheAttributionIsTmdbsFixedSentence()
    {
        using var h = Library();

        await h.RunAsync();

        using var doc = h.Json("meta.json");
        Assert.Equal(CatalogMeta.TmdbAttribution, doc.RootElement.GetProperty("attribution").GetString());
    }

    [Fact]
    public async Task TheGenreMapIsBuiltFromTheNamesTmdbReturned_ForExactlyTheIdsFilmsCarry()
    {
        using var h = Library();

        await h.RunAsync();

        using var doc = h.Json("meta.json");
        var genres = doc.RootElement.GetProperty("genres").EnumerateObject().Select(p => $"{p.Name}={p.Value.GetString()}").ToList();
        Assert.Equal(["53=Thriller", "80=Crime"], genres.OrderBy(g => g, StringComparer.Ordinal));
        using var movies = h.Json("movies.json");
        Assert.Equal([80, 53], movies.RootElement[0].GetProperty("g").EnumerateArray().Select(g => g.GetInt32()));
    }

    [Fact]
    public async Task AGenreIdTmdbSentWithoutANameStopsTheRunBeforeAnythingIsWritten()
    {
        using var h = Library(details: """{"id":275,"title":"Fargo","runtime":98,"genres":[{"id":80,"name":""},{"id":53,"name":"Thriller"}]}""");

        var result = await h.RunAsync();

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("FATAL", h.Err.ToString());
        Assert.Contains("genre", h.Err.ToString());
        Assert.Contains("80", h.Err.ToString());
        Assert.DoesNotContain("53", h.Err.ToString());
        Assert.False(h.Wrote("meta.json"));
        Assert.False(h.Wrote("movies.json"));
        Assert.Contains("deployable=false", h.GitHubOutput);
        Assert.NotEmpty(await h.SavedCacheAsync());                 // what was paid for is kept
    }

    /// <summary>
    /// The per-id check above is structurally unable to see this one: no genres means no ids to name, so it has
    /// nothing to compare. An absent or renamed <c>genres</c> key is null rather than an error, so this is the shape a
    /// changed TMDB response arrives in - and the genre facet, one of the three approved layouts, would go blank for
    /// the whole library, be deployed, and keep the payloads that caused it for five months.
    /// </summary>
    [Fact]
    public async Task FilmsThatMatchedWithNotOneGenreNameStopTheRunRatherThanPublishAnEmptyFacet()
    {
        using var h = Library(details: """{"id":275,"title":"Fargo","runtime":98}""");

        var result = await h.RunAsync();

        Assert.Equal(1, result.ExitCode);
        Assert.False(result.Deployable);
        Assert.Contains("FATAL", h.Err.ToString());
        Assert.Contains("not one genre name came back", h.Err.ToString());
        Assert.Contains("genre facet", h.Err.ToString());
        Assert.False(h.Wrote("meta.json"));
        Assert.False(h.Wrote("movies.json"));
        Assert.Contains("deployable=false", h.GitHubOutput);
        Assert.NotEmpty(await h.SavedCacheAsync());                 // what was paid for is kept
    }

    /// <summary>
    /// The guard is on films TMDB matched, so it cannot fire on a first run that spent its budget before it resolved
    /// anything: that catalogue has no genre ids either, and it is the normal development path.
    /// </summary>
    [Fact]
    public async Task ARunThatMatchedNoFilmAtAllIsNotStoppedByItsEmptyGenreTable()
    {
        using var h = Library();
        h.MoviesCsv = RunLibrary.Movies(("Zzyzx Road", "1:30:00", "Nobody", "Drama"));
        h.ShowsCsv = GoogleSheetClientTests.ShowsHeader;
        h.Tmdb = req => req.RequestUri!.AbsolutePath == "/3/search/movie"
            ? TestPipeline.Json(TestPipeline.EmptyResults)
            : TestPipeline.NotFound();

        var result = await h.RunAsync();

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(0, h.IdsByTitle("movies.json")["Zzyzx Road"]);
        using var doc = h.Json("meta.json");
        Assert.Empty(doc.RootElement.GetProperty("genres").EnumerateObject());
    }

    /// <summary>
    /// And it must not fire on a budget stop. A film can be matched without its details ever loading - an Overrides
    /// row forces an id with no lookup, and a cache-index hit whose payload file is gone resolves to null - so a run
    /// that spends its budget can hold matched films that contributed no genre name. That is an ordinary budget stop,
    /// the normal development path, and failing it would be the guard firing where its own reason does not reach.
    /// Nothing is risked: a budget-stopped run is never deployable, so no blank facet can reach the site this way.
    /// </summary>
    [Fact]
    public async Task ABudgetStoppedRunIsNotFailedByAForcedFilmThatNeverLoadedItsGenres()
    {
        using var h = Library();
        h.MoviesCsv = RunLibrary.Movies(("Alien", "1:56:00", "Ridley Scott", "Sci-Fi"),
                                        ("Brazil", "2:22:00", "Terry Gilliam", "Sci-Fi"));
        h.ShowsCsv = GoogleSheetClientTests.ShowsHeader;
        h.OverridesCsv = RunLibrary.Overrides(("Brazil", "2:22:00", "948"));
        h.Options = h.Options with { MaxRequests = 1 };
        h.Tmdb = req => req.RequestUri!.AbsolutePath == "/3/search/movie"
            ? TestPipeline.Json(TestPipeline.EmptyResults)
            : TestPipeline.NotFound();

        var result = await h.RunAsync();

        Assert.Equal(0, result.ExitCode);                           // an ordinary budget stop, not a fatal error
        Assert.True(result.StoppedOnBudget);
        Assert.False(result.Deployable);                            // which is what keeps the empty facet off the site
        Assert.DoesNotContain("not one genre name came back", h.Err.ToString());
        Assert.True(h.Wrote("meta.json"));
        Assert.Equal(948, h.IdsByTitle("movies.json")["Brazil"]);   // matched, and it never loaded a genre
        using var doc = h.Json("meta.json");
        Assert.Empty(doc.RootElement.GetProperty("genres").EnumerateObject());
    }

    /// <summary>
    /// The review queue names the owner's own problem rows, and <c>meta.json</c> is served publicly from the site, so
    /// the lines go to the job summary he reads and the published field stays empty. The field itself is kept so the
    /// data contract does not change shape for the web app.
    /// </summary>
    [Fact]
    public async Task TheOwnersReviewNotesReachTheSummaryAndNeverThePublishedMetaFile()
    {
        using var h = Library();
        h.OverridesCsv = RunLibrary.Overrides(("A Film That Is Not On The Sheet", "1:40:00", "550"));

        var result = await h.RunAsync();

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(h.ReviewQueue(), l => l.Contains("A Film That Is Not On The Sheet", StringComparison.Ordinal));
        Assert.Contains("Needs review", h.Summary);
        Assert.Empty(h.PublishedReviewQueue());
    }

    /// <summary>And it cannot fire on a fixture run, whose genre table is FixtureGenerator's own and never empty.</summary>
    [Fact]
    public async Task AFixtureRunIsNotStoppedByTheGuardBecauseItsOwnGenreTableIsNeverEmpty()
    {
        using var h = Library();
        h.Options = h.Options with { Fixtures = true, Token = "" };

        var result = await h.RunAsync();

        Assert.Equal(0, result.ExitCode);
        using var doc = h.Json("meta.json");
        Assert.NotEmpty(doc.RootElement.GetProperty("genres").EnumerateObject());
    }

    [Fact]
    public async Task EveryTmdbRequestCarriesTheConfiguredToken_AndNeverTheSheetOnes()
    {
        using var h = Library();

        await h.RunAsync();

        Assert.NotEmpty(h.TmdbHandler.Requests);
        Assert.All(h.TmdbHandler.Requests, r => Assert.Equal("Bearer eyJ.stub.token", r.Headers.Authorization?.ToString()));
        Assert.All(h.SheetHandler.Requests, r => Assert.Null(r.Headers.Authorization));    // the Sheet is read anonymously
        Assert.Equal(3, h.SheetHandler.Requests.Count);                                     // Movies, Shows, Overrides
    }

    [Fact]
    public async Task EverySheetRequestNamesTheConfiguredSheetAndOneOfItsThreeTabs()
    {
        using var h = Library();
        h.Options = h.Options with { SheetId = "my-sheet-id" };

        await h.RunAsync();

        Assert.All(h.SheetHandler.Requests, r => Assert.StartsWith("/spreadsheets/d/my-sheet-id/gviz/tq", r.RequestUri!.AbsolutePath));
        var tabs = h.SheetHandler.Requests.Select(r => r.RequestUri!.Query.Split("sheet=")[1]).Order().ToList();
        Assert.Equal(["Movies", "Overrides", "Shows"], tabs);
    }

    // ---- one clock per run ---------------------------------------------------------------------------

    private static readonly DateTimeOffset Later = new(2031, 5, 6, 7, 8, 9, TimeSpan.Zero);

    [Fact]
    public async Task EverythingTheRunStampsComesFromTheOneInjectedClock()
    {
        using var h = Library();
        h.Now = () => Later;

        await h.RunAsync();

        using var meta = h.Json("meta.json");
        Assert.Equal(Later.UtcDateTime.ToString("O"), meta.RootElement.GetProperty("builtUtc").GetString());
        Assert.All(await h.SavedCacheAsync(), e => Assert.Equal(Later, e.FetchedUtc));
        using var film = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(h.CacheDir, "movies", "275.json")));
        Assert.Equal(Later, film.RootElement.GetProperty("fetchedUtc").GetDateTimeOffset());
        using var series = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(h.CacheDir, "tv", "2316", "series.json")));
        Assert.Equal(Later, series.RootElement.GetProperty("fetchedUtc").GetDateTimeOffset());
    }

    [Fact]
    public async Task TheTmdbClientAndBothPipelinesAndMetaAllDrawOnTheSameClock()
    {
        using var h = Library();
        var calls = 0;
        h.Now = () => { calls++; return RunHarness.T0; };

        await h.RunAsync();

        // The client reads the clock twice per attempt (wait, then record); each pipeline once; meta once.
        Assert.True(calls >= 2 * h.TmdbHandler.Requests.Count + 3,
            $"{calls} reads of the clock for {h.TmdbHandler.Requests.Count} requests: something is on a different clock");
    }

    // ---- the two silent-failure counters, through the whole run --------------------------------------

    [Fact]
    public async Task AHealthyRunSaysNothingAboutCastCounters()
    {
        using var h = Library();

        await h.RunAsync();

        Assert.DoesNotContain("no cast", h.Summary);
        Assert.DoesNotContain("unreadable", h.Summary);
        Assert.Contains("Films: **1**", h.Summary);
        Assert.Contains("Series: **1** (3 episodes listed, 1 owned)", h.Summary);
    }

    [Fact]
    public async Task AMatchedFilmWithNoCastIsCountedOnTheSummary()
    {
        using var h = Library(credits: """{"cast":[],"crew":[{"job":"Director","name":"Joel Coen"},{"job":"Director","name":"Ethan Coen"}]}""");

        await h.RunAsync();

        Assert.Contains("Films with no cast: **1** of 1 matched", h.Summary);
    }

    [Fact]
    public async Task AFilmTmdbNeverFoundHasNoCastByConstructionAndIsNotCounted()
    {
        using var h = Library();
        h.MoviesCsv = RunLibrary.Movies(RunLibrary.Fargo, ("Zzyzx Road", "1:30:00", "Nobody", "Drama"));

        await h.RunAsync();

        Assert.Equal(0, h.IdsByTitle("movies.json")["Zzyzx Road"]);
        Assert.DoesNotContain("no cast", h.Summary);
    }

    [Fact]
    public async Task TheEmptyCastCountIsOutOfMatchedFilms_NotTheWholeLibrary()
    {
        using var h = Library(credits: """{"cast":[],"crew":[{"job":"Director","name":"Joel Coen"},{"job":"Director","name":"Ethan Coen"}]}""");
        h.MoviesCsv = RunLibrary.Movies(RunLibrary.Fargo, ("Zzyzx Road", "1:30:00", "Nobody", "Drama"));

        await h.RunAsync();

        Assert.Contains("Films with no cast: **1** of 1 matched", h.Summary);
    }

    [Fact]
    public async Task ACastMemberSkippedAsUnreadableIsCountedOnTheSummary()
    {
        using var h = Library(credits: """
            {"cast":[{"name":"Frances McDormand","character":"Marge","order":0},{"name":"","character":"Nobody","order":1}],
             "crew":[{"job":"Director","name":"Joel Coen"},{"job":"Director","name":"Ethan Coen"}]}
            """);

        await h.RunAsync();

        Assert.Contains("Cast members skipped as unreadable: **1**", h.Summary);
        Assert.DoesNotContain("no cast", h.Summary);
    }
}

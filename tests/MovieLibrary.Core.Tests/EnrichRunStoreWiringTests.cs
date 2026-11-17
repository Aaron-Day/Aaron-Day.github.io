namespace MovieLibrary.Core.Tests;

/// <summary>
/// The record store reaching both pipelines, through the whole tool: Sheet and <c>record-store.json</c> in,
/// written JSON out. <c>EnrichRun</c> binds the store once and hands the same <c>BoundStore</c> to the film pass
/// and the series pass, so a run that passed <c>BoundStore.Empty</c> to either would compile and then silently
/// apply nothing at all. These tests assert the ids that come out, so they fail if it does. The pipeline-level
/// store tests cannot see this: they are handed a bound store already.
/// </summary>
/// <remarks>
/// This file replaces <c>EnrichRunOverrideWiringTests</c>, which pinned the same wiring for the Overrides tab
/// (Task 17 Step G, where the two same-typed override maps really were exchanged). The tab is gone; the hole it
/// guarded is not.
/// </remarks>
public class EnrichRunStoreWiringTests
{
    private const string TwinPlain = "The Biggest Loser";
    private const string TwinYear = "The Biggest Loser (2020)";

    /// <summary>
    /// Two Halloweens and two Biggest Losers. TMDB's searches find nothing for the films and find only the plain
    /// twin of the series, so an id can only come from the store: 948 for the 1978 cut, 20 for the 2020 series.
    /// </summary>
    private static RunHarness Library()
    {
        var h = new RunHarness();
        h.MoviesCsv = RunLibrary.Movies(
            ("Halloween", "1:30:47", "John Carpenter", "Horror"),
            ("Halloween (2018)", "1:46:07", "David Gordon Green", "Horror"));
        h.ShowsCsv = RunLibrary.Shows(
            ("Season 1, Episode 1: Pilot", TwinPlain, "Reality"),
            ("Season 1, Episode 1: Pilot", TwinYear, "Reality"));
        h.WriteStore(RunLibrary.Store(
            films: [("Halloween", "1:30:47", "John Carpenter", 948)],
            series: [(TwinYear, 20)]));

        var tv = new FakeTv()
            .Add(new FakeShow(10, TwinPlain, [new FakeSeason(1, ["Pilot"])]))
            .Add(new FakeShow(20, TwinPlain, [new FakeSeason(1, ["Pilot"])]) { SearchAs = "never searched under this name" });

        h.Tmdb = req => req.RequestUri!.AbsolutePath switch
        {
            "/3/search/movie" => TestPipeline.Json(TestPipeline.EmptyResults),
            "/3/movie/948" => TestPipeline.Json(RunLibrary.DetailsWithCredits(948, "Halloween")),
            _ => tv.Respond(req)
        };
        return h;
    }

    [Fact]
    public async Task AStoredFilmIdAppliesEndToEnd_TheFilmTakesItsForcedId()
    {
        using var h = Library();

        var result = await h.RunAsync();

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("record store: 1 film entries, 1 series entries, 0 note(s)", h.Out.ToString());
        var films = h.IdsByTitle("movies.json");
        Assert.Equal(948, films["Halloween"]);
        Assert.Equal(0, films["Halloween (2018)"]);   // the other cut has no entry, is searched and found nothing
    }

    [Fact]
    public async Task AStoredSeriesIdAppliesEndToEnd_TheYearSuffixedSeriesTakesItsOwnId()
    {
        using var h = Library();

        var result = await h.RunAsync();

        Assert.Equal(0, result.ExitCode);
        var shows = h.IdsByTitle("shows.json");
        Assert.Equal(20, shows[TwinYear]);
        Assert.Equal(10, shows[TwinPlain]);           // its plain twin has no entry and is found by search
    }

    [Fact]
    public async Task NeitherForcedIdSpendsASearch_SoNeitherWasFoundTheOrdinaryWay()
    {
        using var h = Library();

        await h.RunAsync();

        var searches = h.TmdbHandler.Requests.Select(r => r.RequestUri!.PathAndQuery).Where(p => p.Contains("/search/")).ToList();
        Assert.Equal(["/3/search/movie?query=Halloween&year=2018", "/3/search/tv?query=The%20Biggest%20Loser"], searches);
    }

    [Fact]
    public async Task EntriesThatApplyLeaveNoNoteInTheReviewQueueOrTheSummary()
    {
        using var h = Library();

        await h.RunAsync();

        Assert.DoesNotContain(h.ReviewQueue(), line => line.StartsWith("Record store entry", StringComparison.Ordinal));
        Assert.DoesNotContain("Record store entry", h.Summary);
    }

    // ---- an entry that matches nothing must be said out loud ------------------------------------------

    /// <summary>
    /// The sheet says 171 minutes and the entry was written against 167, which is past the binder's two-minute
    /// drift window, so the entry matches no row at all. 949 must therefore never reach the catalogue.
    /// </summary>
    private static RunHarness Orphaned()
    {
        var h = new RunHarness();
        h.MoviesCsv = RunLibrary.Movies(("Heat", "2:51:00", "Michael Mann", "Crime"));
        h.WriteStore(RunLibrary.Store(films: [("Heat", "2:47:00", "Michael Mann", 949)]));
        h.Tmdb = req => req.RequestUri!.AbsolutePath == "/3/search/movie"
            ? TestPipeline.Json(TestPipeline.EmptyResults)
            : TestPipeline.NotFound();
        return h;
    }

    [Fact]
    public async Task AnOrphanedEntryProducesANoteNamingTheFilmAndTheRunStillSucceeds()
    {
        using var h = Orphaned();

        var result = await h.RunAsync();

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("record store: 0 film entries, 0 series entries, 1 note(s)", h.Out.ToString());
        var note = Assert.Single(h.ReviewQueue(), line => line.StartsWith("Record store entry", StringComparison.Ordinal));
        // The whole opening, not the bare "'Heat'" this was loosened to: the entry's own title AND the tab it
        // failed against. The runtime the tab-era note carried ("'Heat' 2:51:00 (171 min)") is NOT available
        // here -- OrphanNote names titles only -- so the candidate list stands in for it instead.
        Assert.StartsWith("Record store entry 'Heat' matches no film on the Movies tab", note, StringComparison.Ordinal);
        Assert.Contains("Closest on the sheet: 'Heat'.", note, StringComparison.Ordinal);
        Assert.Contains("Record store entry", h.Summary);
    }

    [Fact]
    public async Task AnOrphanedEntryDoesNotApply_SoTheFilmIsSearchedForInsteadOfTakingTheId()
    {
        using var h = Orphaned();

        await h.RunAsync();

        Assert.Equal(0, h.IdsByTitle("movies.json")["Heat"]);
        Assert.DoesNotContain(h.TmdbHandler.Requests, r => r.RequestUri!.AbsolutePath == "/3/movie/949");
    }

    [Fact]
    public async Task StoreNotesComeBeforeThePipelinesOwnReviewLines()
    {
        using var h = Orphaned();

        await h.RunAsync();

        var queue = h.ReviewQueue();
        var note = Array.FindIndex(queue, l => l.StartsWith("Record store entry", StringComparison.Ordinal));
        var pipeline = Array.FindIndex(queue, l => l.StartsWith("Heat (", StringComparison.Ordinal));
        Assert.True(note >= 0, "the note is in the queue");
        Assert.True(pipeline >= 0, "the pipeline's own line for the unmatched film is in the queue");
        Assert.True(note < pipeline, "the owner's failing correction is listed before a doubtful match");
        Assert.Equal(0, note);
    }

    [Fact]
    public async Task AnOrphanedSeriesEntryIsNotedToo()
    {
        using var h = new RunHarness();
        // A film row, because a real run has to have one: a Movies tab that comes back with its header and no rows is
        // now refused outright (EnrichRun.RequireAFilmLibrary), and this test is about the series side.
        h.MoviesCsv = RunLibrary.Movies(("Heat", "2:51:00", "Michael Mann", "Crime"));
        h.ShowsCsv = RunLibrary.Shows(("Season 1, Episode 1: Pilot", "The Office", "Comedy"));
        h.WriteStore(RunLibrary.Store(series: [("The Office (US)", 2316)]));
        var tv = new FakeTv { AnyQuery = new FakeShow(2316, "The Office", [new FakeSeason(1, ["Pilot"])]) };
        h.Tmdb = req => req.RequestUri!.AbsolutePath == "/3/search/movie"
            ? TestPipeline.Json(TestPipeline.EmptyResults)
            : tv.Respond(req);

        await h.RunAsync();

        Assert.Contains(h.ReviewQueue(), l => l.StartsWith("Record store entry", StringComparison.Ordinal)
                                              && l.Contains("matches no series", StringComparison.OrdinalIgnoreCase));
    }
}

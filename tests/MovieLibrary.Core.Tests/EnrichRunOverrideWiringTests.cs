using MovieLibrary.Enrich.Run;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// Task 17 Step G. <c>SheetOverrides.Films</c> and <c>.Series</c> are the same type, so handing the wrong one to the wrong
/// pipeline compiles and then silently applies no override at all. These tests run the whole tool, Sheet to written JSON,
/// and assert the ids that come out, so exchanging the two arguments fails them. The Overrides notes are the other thing
/// the body of the plan dropped, and <c>WithUnmatchedNotes</c> is a separate call whose omission produces nothing.
/// </summary>
public class EnrichRunOverrideWiringTests
{
    private const string TwinPlain = "The Biggest Loser";
    private const string TwinYear = "The Biggest Loser (2020)";

    /// <summary>
    /// Two Halloweens and two Biggest Losers. TMDB's searches find nothing for the films and find only the plain twin of the
    /// series, so an id can only come from an override: 948 for the 1978 cut, 20 for the 2020 series.
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
        h.OverridesCsv = RunLibrary.Overrides(("Halloween", "1:30:47", "948"), (TwinYear, "", "20"));

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
    public async Task AFilmOverrideAppliesEndToEnd_TheFilmTakesItsForcedId()
    {
        using var h = Library();

        var result = await h.RunAsync();

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("1 film overrides, 1 series overrides, 0 notes", h.Out.ToString());
        var films = h.IdsByTitle("movies.json");
        Assert.Equal(948, films["Halloween"]);
        Assert.Equal(0, films["Halloween (2018)"]);   // the other cut has no override, is searched and found nothing
    }

    [Fact]
    public async Task ASeriesOverrideAppliesEndToEnd_TheYearSuffixedSeriesTakesItsOwnId()
    {
        using var h = Library();

        var result = await h.RunAsync();

        Assert.Equal(0, result.ExitCode);
        var shows = h.IdsByTitle("shows.json");
        Assert.Equal(20, shows[TwinYear]);
        Assert.Equal(10, shows[TwinPlain]);           // its plain twin is untouched by the override and found by search
    }

    [Fact]
    public async Task NeitherOverrideSpendsASearch_SoNeitherWasFoundTheOrdinaryWay()
    {
        using var h = Library();

        await h.RunAsync();

        var searches = h.TmdbHandler.Requests.Select(r => r.RequestUri!.PathAndQuery).Where(p => p.Contains("/search/")).ToList();
        Assert.Equal(["/3/search/movie?query=Halloween&year=2018", "/3/search/tv?query=The%20Biggest%20Loser"], searches);
    }

    [Fact]
    public async Task OverridesThatApplyLeaveNoNoteInTheReviewQueueOrTheSummary()
    {
        using var h = Library();

        await h.RunAsync();

        Assert.DoesNotContain(h.ReviewQueue(), line => line.StartsWith("Overrides row", StringComparison.Ordinal));
        Assert.DoesNotContain("Overrides row", h.Summary);
    }

    // ---- a drifted override must be said out loud ----------------------------------------------------

    private static RunHarness Drifted()
    {
        var h = new RunHarness();
        h.MoviesCsv = RunLibrary.Movies(("Heat", "2:51:00", "Michael Mann", "Crime"));     // the sheet now says 171 minutes
        h.OverridesCsv = RunLibrary.Overrides(("Heat", "2:50:00", "949"));                 // the override still says 170
        h.Tmdb = req => req.RequestUri!.AbsolutePath == "/3/search/movie"
            ? TestPipeline.Json(TestPipeline.EmptyResults)
            : TestPipeline.NotFound();
        return h;
    }

    [Fact]
    public async Task ADriftedOverrideProducesANoteNamingTheRowAndTheRuntimeTheSheetHasNow()
    {
        using var h = Drifted();

        var result = await h.RunAsync();

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("1 film overrides, 0 series overrides, 1 notes", h.Out.ToString());
        var note = Assert.Single(h.ReviewQueue(), line => line.StartsWith("Overrides row 2", StringComparison.Ordinal));
        Assert.Contains("matches no film on the Movies tab", note);
        Assert.Contains("'Heat' 2:51:00 (171 min)", note);
        Assert.Contains("Overrides row 2", h.Summary);
    }

    [Fact]
    public async Task ADriftedOverrideDoesNotApply_SoTheFilmIsSearchedForInsteadOfTakingTheId()
    {
        using var h = Drifted();

        await h.RunAsync();

        Assert.Equal(0, h.IdsByTitle("movies.json")["Heat"]);
        Assert.DoesNotContain(h.TmdbHandler.Requests, r => r.RequestUri!.AbsolutePath == "/3/movie/949");
    }

    [Fact]
    public async Task OverrideNotesComeBeforeThePipelinesOwnReviewLines()
    {
        using var h = Drifted();

        await h.RunAsync();

        var queue = h.ReviewQueue();
        var note = Array.FindIndex(queue, l => l.StartsWith("Overrides row 2", StringComparison.Ordinal));
        var pipeline = Array.FindIndex(queue, l => l.StartsWith("Heat (", StringComparison.Ordinal));
        Assert.True(note >= 0, "the note is in the queue");
        Assert.True(pipeline >= 0, "the pipeline's own line for the unmatched film is in the queue");
        Assert.True(note < pipeline, "the owner's failing correction is listed before a doubtful match");
        Assert.Equal(0, note);
    }

    [Fact]
    public async Task AMissingOverridesTabIsReportedRatherThanDroppedSilently()
    {
        using var h = Drifted();
        h.OverridesCsv = "";                                  // Google answers an unknown tab with nothing useful

        await h.RunAsync();

        Assert.Contains(h.ReviewQueue(), l => l.StartsWith("The 'Overrides' tab was not found", StringComparison.Ordinal));
        Assert.Contains("The 'Overrides' tab was not found", h.Summary);
    }

    [Fact]
    public async Task ADriftedSeriesOverrideIsNotedToo()
    {
        using var h = new RunHarness();
        h.ShowsCsv = RunLibrary.Shows(("Season 1, Episode 1: Pilot", "The Office", "Comedy"));
        h.OverridesCsv = RunLibrary.Overrides(("The Office (US)", "", "2316"));
        h.Tmdb = new FakeTv { AnyQuery = new FakeShow(2316, "The Office", [new FakeSeason(1, ["Pilot"])]) }.Respond;

        await h.RunAsync();

        Assert.Contains(h.ReviewQueue(), l => l.StartsWith("Overrides row 2", StringComparison.Ordinal)
                                              && l.Contains("matches no series on the Shows tab", StringComparison.Ordinal));
    }
}

using MovieLibrary.Core.Models;
using MovieLibrary.Enrich.Sheets;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// The film key is exact to the minute, so a re-export that shifts a runtime by one minute, or a title typed without its
/// year, silently stops an override applying. <see cref="SheetOverrides.WithUnmatchedNotes"/> makes that loud: every
/// override that matches no sheet row gets a note naming the row and the closest thing the sheet does have.
/// </summary>
public class GoogleSheetClientUnmatchedTests
{
    private const string Header = OverrideCsv.Header;
    private static Task<SheetOverrides> Fetch(string body) => OverrideCsv.Fetch(body);
    private static string Line(string title, string runtime, string id, string note = "")
        => OverrideCsv.Line(title, runtime, id, note);

    private static SheetRow Film(string title, string runtime)
        => new(title, runtime, "Someone", "", "Horror", "");

    private static SheetRow Episode(string series)
        => new("Season 1, Episode 1: Pilot", "44:00:00", "", series + ": The Complete Series", "Drama", series);

    private static readonly SheetRow[] NoFilms = [];
    private static readonly SheetRow[] NoEpisodes = [];

    [Fact]
    public async Task OverridesThatMatchASheetRowGetNoNote()
    {
        var o = await Fetch(Header
            + Line("Halloween", "1:30:47", "948")
            + Line("Halloween (2018)", "1:46:07", "424")
            + Line("The Biggest Loser (2020)", "", "20")
            + Line("The Office", "", "2316"));

        var checkedAgainst = o.WithUnmatchedNotes(
            [Film("Halloween", "1:30:47"), Film("Halloween (2018)", "1:46:07"), Film("Heat", "2:50:00")],
            [Episode("The Biggest Loser (2020)"), Episode("The Office"), Episode("The Office")]);

        Assert.Empty(checkedAgainst.Notes);
        Assert.Equal(o.Films, checkedAgainst.Films);
        Assert.Equal(o.Series, checkedAgainst.Series);
    }

    [Fact]
    public async Task AFilmWhoseRuntimeDriftedByAMinuteIsNamedWithTheRuntimesTheSheetHasNow()
    {
        var o = await Fetch(Header + Line("Halloween", "1:30:47", "948"));

        var note = Assert.Single(o.WithUnmatchedNotes(
            [Film("Halloween", "1:31:12"), Film("Halloween", "1:50:18"), Film("Heat", "2:50:00")], NoEpisodes).Notes);

        Assert.Contains("row 2", note);
        Assert.Contains("('Halloween', 1:30:47 (90 min))", note);        // what the override says, to the minute
        Assert.Contains("matches no film on the Movies tab", note);
        Assert.Contains("not applied", note);
        Assert.Contains("title, its year in brackets if it has one, and its runtime to the minute", note);
        Assert.Contains("'Halloween' 1:31:12 (91 min); 'Halloween' 1:50:18 (110 min)", note);   // closest, in sheet order
        Assert.DoesNotContain("Heat", note);                             // a different title is not close
        Assert.EndsWith("Copy the Title and Runtime from the Movies tab exactly.", note);   // what to do about it
    }

    [Fact]
    public async Task NoToleranceWindow_AnOverrideAtOneMinuteIsNotSatisfiedByAFilmAtAnother()
    {
        var o = await Fetch(Header + Line("Captain America: The First Avenger", "2:05:15", "1771"));

        var note = Assert.Single(o.WithUnmatchedNotes(
            [Film("Captain America: The First Avenger", "2:04:09")], NoEpisodes).Notes);

        Assert.Contains("(125 min)", note);
        Assert.Contains("2:04:09 (124 min)", note);
    }

    [Fact]
    public async Task AFilmTypedWithoutItsYearIsUnmatchedAndTheNoteShowsTheYearTheSheetUses()
    {
        var o = await Fetch(Header + Line("Overboard", "1:52:17", "21"));

        var note = Assert.Single(o.WithUnmatchedNotes(
            [Film("Overboard (1987)", "1:52:17"), Film("Overboard (2018)", "1:52:18")], NoEpisodes).Notes);

        Assert.Contains("'Overboard (1987)' 1:52:17 (112 min); 'Overboard (2018)' 1:52:18 (112 min)", note);
    }

    [Fact]
    public async Task AFilmWithNoSameTitledFilmOnTheSheetSaysSo()
    {
        var o = await Fetch(Header + Line("Halloween", "1:30:47", "948"));

        var note = Assert.Single(o.WithUnmatchedNotes([Film("Heat", "2:50:00")], NoEpisodes).Notes);

        Assert.Contains("matches no film", note);
        Assert.Contains("No film on the sheet has that title.", note);
        Assert.DoesNotContain("Closest", note);
    }

    [Fact]
    public async Task AtMostThreeCandidatesAreListedAndTheRestAreCounted()
    {
        var o = await Fetch(Header + Line("Heat", "2:50:00", "949"));

        var note = Assert.Single(o.WithUnmatchedNotes(
            [Film("Heat", "1:01:00"), Film("Heat", "1:02:00"), Film("Heat", "1:03:00"),
             Film("Heat", "1:04:00"), Film("Heat", "1:05:00"), Film("Heat", "1:05:00")], NoEpisodes).Notes);

        Assert.Contains("1:01:00 (61 min); 'Heat' 1:02:00 (62 min); 'Heat' 1:03:00 (63 min); and 2 more", note);
        Assert.DoesNotContain("1:04:00", note);
    }

    [Fact]
    public async Task ASeriesWhoseYearTheSheetDoesNotHaveListsTheNamesItDoes()
    {
        var o = await Fetch(Header + Line("The Biggest Loser (2020)", "", "20"));

        var note = Assert.Single(o.WithUnmatchedNotes(
            NoFilms, [Episode("The Biggest Loser"), Episode("The Biggest Loser"), Episode("The Office")]).Notes);

        Assert.Contains("row 2", note);
        Assert.Contains("('The Biggest Loser (2020)')", note);
        Assert.Contains("matches no series on the Shows tab", note);
        Assert.Contains("not applied", note);
        Assert.Contains("its name and its year in brackets if it has one", note);
        Assert.Contains("Closest on the sheet: 'The Biggest Loser'.", note);       // once, not twice
        Assert.DoesNotContain("Office", note);
        Assert.EndsWith("Copy the name from the Series column of the Shows tab exactly.", note);   // what to do about it
    }

    [Fact]
    public async Task ASeriesTheSheetDoesNotHaveSaysSo()
    {
        var o = await Fetch(Header + Line("Alias", "", "4490"));

        var note = Assert.Single(o.WithUnmatchedNotes(NoFilms, [Episode("The Office")]).Notes);

        Assert.Contains("matches no series", note);
        Assert.Contains("No series on the sheet has that name.", note);
    }

    [Fact]
    public async Task AFilmOverrideIsNeverSatisfiedByASeriesOfTheSameNameAndViceVersa()
    {
        var o = await Fetch(Header + Line("Halloween", "1:30:47", "948") + Line("Heat", "", "5"));

        var notes = o.WithUnmatchedNotes([Film("Heat", "2:50:00")], [Episode("Halloween")]).Notes;

        Assert.Equal(2, notes.Count);
        Assert.Contains("matches no film", notes[0]);
        Assert.Contains("matches no series", notes[1]);
    }

    [Fact]
    public async Task TheTabsOwnNotesComeFirstThenTheUnmatchedOnesInSheetRowOrder()
    {
        var o = await Fetch(Header
            + Line("Halloween", "1:30:47", "948")      // row 2: unmatched film
            + Line("Alias", "", "oops")                // row 3: bad id, noted while reading the tab
            + Line("Lost", "", "77"));                 // row 4: unmatched series

        var notes = o.WithUnmatchedNotes(NoFilms, NoEpisodes).Notes;

        Assert.Equal(3, notes.Count);
        Assert.Contains("row 3", notes[0]);
        Assert.Contains("TmdbId", notes[0]);
        Assert.Contains("row 2", notes[1]);
        Assert.Contains("row 4", notes[2]);
    }

    [Fact]
    public async Task AMissingTabAddsNothingToItsOwnNote()
    {
        var o = await Fetch("");

        var checkedAgainst = o.WithUnmatchedNotes([Film("Heat", "2:50:00")], [Episode("The Office")]);

        Assert.Equal(o.Notes, checkedAgainst.Notes);
        Assert.Single(checkedAgainst.Notes);
    }

    [Fact]
    public async Task ASheetRowWithAnUnreadableRuntimeOrNoSeriesNameIsSkippedNotFatal()
    {
        var o = await Fetch(Header + Line("Halloween", "1:30:47", "948") + Line("The Office", "", "2316"));

        var checkedAgainst = o.WithUnmatchedNotes(
            [Film("Broken", "n/a"), Film("Halloween", "1:30:47")],
            [Episode("   "), Episode("The Office")]);

        Assert.Empty(checkedAgainst.Notes);
    }

    [Fact]
    public async Task OnlyTheRowThatWonAConflictIsChecked()
    {
        var o = await Fetch(Header + Line("Alias", "", "1") + Line("Alias", "", "2"));

        var notes = o.WithUnmatchedNotes(NoFilms, NoEpisodes).Notes;

        Assert.Equal(2, notes.Count);                       // the conflict, then ONE unmatched note
        Assert.Contains("rows 2 and 3", notes[0]);
        Assert.Contains("Overrides row 3 ('Alias')", notes[1]);   // the row that won; the one it beat is not reported twice
        Assert.DoesNotContain("Overrides row 2 (", notes[1]);
    }

    [Fact]
    public async Task CheckingTwiceDoesNotDuplicateAndLeavesTheOriginalAlone()
    {
        var o = await Fetch(Header + Line("Halloween", "1:30:47", "948"));

        var once = o.WithUnmatchedNotes(NoFilms, NoEpisodes);

        Assert.Empty(o.Notes);               // the original is unchanged: records are immutable
        Assert.Single(once.Notes);
        Assert.Single(o.WithUnmatchedNotes(NoFilms, NoEpisodes).Notes);
    }

    [Fact]
    public async Task TheWholeChainFromThreeTabsFlagsOnlyTheRowThatDrifted()
    {
        // Movies, Shows and Overrides all read through the client, then checked against each other as Task 17 will:
        // the rows the pipelines will get are the rows the keys are compared with.
        var (client, _) = GoogleSheetClientTests.BuildWithHandler(req => GoogleSheetClientTests.Csv(
            req.RequestUri!.Query.EndsWith("sheet=Movies")
                ? GoogleSheetClientTests.MoviesHeader
                  + "\"Halloween\",\"1:30:47\",\"John Carpenter\",\"\",\"Horror\",\"\"\n"
                  + "\"Halloween\",\"1:50:18\",\"Rob Zombie\",\"\",\"Horror\",\"\"\n"
                  + "\"Halloween (2018)\",\"1:46:07\",\"David Gordon Green\",\"\",\"Horror\",\"\"\n"
            : req.RequestUri!.Query.EndsWith("sheet=Shows")
                ? GoogleSheetClientTests.ShowsHeader
                  + "\"Season 1, Episode 1: Pilot\",\"44:00\",\"The Biggest Loser (2020)\",\"\",\"Reality\",\"\"\n"
            : Header
              + Line("Halloween", "1:30:47", "948")             // row 2: matches
              + Line("Halloween", "1:50:19", "949")             // row 3: a second off the sheet's 1:50:18 but the same minute, so it still matches
              + Line("Halloween (2018)", "1:47:00", "424")      // row 4: 107 minutes where the sheet now says 106
              + Line("The Biggest Loser (2020)", "", "20")      // row 5: matches
              + Line("The Biggest Loser", "", "10")));          // row 6: the sheet has no year-less one

        var movies = await client.FetchMoviesAsync("sheet", CancellationToken.None);
        var shows = await client.FetchShowsAsync("sheet", CancellationToken.None);
        var overrides = (await client.FetchOverridesAsync("sheet", CancellationToken.None))
            .WithUnmatchedNotes(movies, shows);

        Assert.Equal(2, overrides.Notes.Count);
        Assert.Contains("Overrides row 4 ('Halloween (2018)', 1:47:00 (107 min))", overrides.Notes[0]);
        Assert.Contains("'Halloween (2018)' 1:46:07 (106 min)", overrides.Notes[0]);
        Assert.Contains("Overrides row 6 ('The Biggest Loser')", overrides.Notes[1]);
        Assert.Contains("'The Biggest Loser (2020)'", overrides.Notes[1]);
    }

    [Fact]
    public async Task NotesFollowTheSheetRowOrderAcrossFilmsAndSeries()
    {
        var o = await Fetch(Header
            + Line("Lost", "", "77")                           // row 2: a series
            + Line("Halloween", "1:30:47", "948"));            // row 3: a film

        var notes = o.WithUnmatchedNotes(NoFilms, NoEpisodes).Notes;

        Assert.Equal(2, notes.Count);
        Assert.Contains("Overrides row 2 ('Lost')", notes[0]);
        Assert.Contains("Overrides row 3 ('Halloween'", notes[1]);
    }

    [Fact]
    public async Task ASheetFilmWhoseRuntimeCannotBeReadNeverMatchesAnOverride()
    {
        // An unreadable runtime is not "zero minutes": a 0-minute override must not be satisfied by it.
        var o = await Fetch(Header + Line("Broken", "0:00:30", "5"));

        var note = Assert.Single(o.WithUnmatchedNotes([Film("Broken", "n/a")], NoEpisodes).Notes);

        Assert.Contains("matches no film", note);
        Assert.Contains("'Broken' n/a", note);
    }

    [Fact]
    public async Task ExactlyThreeCandidatesAreAllListedWithNoMoreCount()
    {
        var o = await Fetch(Header + Line("Halloween", "1:00:00", "948"));

        var note = Assert.Single(o.WithUnmatchedNotes(
            [Film("Halloween", "1:30:47"), Film("Halloween", "1:50:18"), Film("Halloween", "1:46:07")], NoEpisodes).Notes);

        Assert.Contains("'Halloween' 1:30:47 (90 min); 'Halloween' 1:50:18 (110 min); 'Halloween' 1:46:07 (106 min).", note);
        Assert.DoesNotContain("more", note);
    }

    [Fact]
    public async Task APlainSeriesOverrideIsNotSatisfiedByAYearSuffixedSeries()
    {
        // X and X (2020) are two series: the key keeps the year, on the sheet side too.
        var o = await Fetch(Header + Line("The Biggest Loser", "", "10"));

        var note = Assert.Single(o.WithUnmatchedNotes(NoFilms, [Episode("The Biggest Loser (2020)")]).Notes);

        Assert.Contains("matches no series", note);
        Assert.Contains("Closest on the sheet: 'The Biggest Loser (2020)'.", note);
    }
}

using MovieLibrary.Core.Identity;
using MovieLibrary.Core.Models;
using MovieLibrary.Core.Store;
using MovieLibrary.Core.Text;
using MovieLibrary.Enrich.Store;

namespace MovieLibrary.Core.Tests;

public class StoreBinderTests
{
    private const string Fincher = "David Fincher";

    private static SheetRow Film(string title, string runtime, string directors = Fincher)
        => new(title, runtime, directors, "", "Thriller", "");

    private static SheetRow Show(string series) => new("Season 1, Episode 1: Pilot", "0:22:00", "", "", "Comedy", series);

    /// <summary>The key the pipeline will compute for a film row, derived exactly as the pipeline derives it.</summary>
    private static string FilmKey(string title, int minutes, string directors = Fincher)
        => IdentityKey.ForFilm(TitleNormalizer.Normalize(title), minutes, TitleNormalizer.NormalizeDirectors(directors));

    // NOT IdentityKey.ForSeries(Normalize(name)): TvPipeline folds a trailing year back INTO the value first,
    // so "X" and "X (2020)" stay two series. IdentityKey.ForSeriesName is the one place that rule lives.
    private static string SeriesKey(string series) => IdentityKey.ForSeriesName(series);

    private static RecordStore WithFilm(string key, int? tmdbId = 807, string sheetTitle = "Se7en") => new()
    {
        Version = RecordStore.CurrentVersion,
        Series = [],
        Films = [new FilmEntry
        {
            IdentityKey = key,
            Sheet = new SheetEcho(sheetTitle, "1:50:00", Fincher),
            Records = [new RecordOverride(TmdbId: tmdbId)],
        }],
    };

    [Fact]
    public void AnEntryWhoseKeyIsTheRowsKeyBindsSilently()
    {
        var key = FilmKey("Se7en", 110);

        var bound = StoreBinder.Bind(WithFilm(key), [Film("Se7en", "1:50:00")], []);

        Assert.Empty(bound.Notes);
        Assert.Equal(807, Assert.Contains(key, bound.Films).Records.Single().TmdbId);
    }

    [Theory]
    // Apple re-encoded and the runtime moved. The entry was written against the OLD runtime, so the binder has
    // to find it by hashing the new runtime at each offset. 1:50:00 is 110 minutes.
    [InlineData(108)]
    [InlineData(109)]
    [InlineData(111)]
    [InlineData(112)]
    public void AnEntryWrittenAgainstADriftedRuntimeStillBindsAndSaysSo(int oldMinutes)
    {
        var rowKey = FilmKey("Se7en", 110);

        var bound = StoreBinder.Bind(WithFilm(FilmKey("Se7en", oldMinutes)), [Film("Se7en", "1:50:00")], []);

        // Filed under the ROW's key, so the pipeline finds it with the key it already computes.
        Assert.Equal(807, Assert.Contains(rowKey, bound.Films).Records.Single().TmdbId);
        var note = Assert.Single(bound.Notes);
        Assert.Contains("Se7en", note, StringComparison.Ordinal);
        Assert.Contains("runtime", note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ThreeMinutesIsTooFarAndTheEntryIsOrphanedRatherThanGuessedAt()
    {
        var bound = StoreBinder.Bind(WithFilm(FilmKey("Se7en", 107)), [Film("Se7en", "1:50:00")], []);

        Assert.Empty(bound.Films);
        Assert.Contains("matches no film", Assert.Single(bound.Notes), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnEntryForATitleThatHasLeftTheLibraryIsNotedWithTheOneWayToQuietIt()
    {
        var bound = StoreBinder.Bind(WithFilm(FilmKey("A Sold Film", 95), sheetTitle: "A Sold Film"), [Film("Se7en", "1:50:00")], []);

        var note = Assert.Single(bound.Notes);
        Assert.Contains("A Sold Film", note, StringComparison.Ordinal);
        Assert.Contains("delete", note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AChangedTitleOrDirectorIsOrphanedBecauseTheKeyFoldsThemIn()
    {
        var bound = StoreBinder.Bind(
            WithFilm(FilmKey("Se7en", 110, directors: "Someone Else")), [Film("Se7en", "1:50:00")], []);

        Assert.Empty(bound.Films);
        Assert.Single(bound.Notes);
    }

    [Fact]
    public void TwoIdenticalRowsShareOneEntryBecauseTheyShareOneIdentityKey()
    {
        // Two Blade Runner rows at the same runtime with the same director are genuinely one film listed
        // twice; the reconciler collapses them downstream. One entry covers both, with nothing to warn about.
        var key = FilmKey("Blade Runner", 117);

        var bound = StoreBinder.Bind(
            WithFilm(key, tmdbId: 78, sheetTitle: "Blade Runner"),
            [Film("Blade Runner", "1:57:00"), Film("Blade Runner", "1:57:30")],
            []);

        Assert.Empty(bound.Notes);
        Assert.Equal(78, Assert.Contains(key, bound.Films).Records.Single().TmdbId);
    }

    [Fact]
    public void TwoEntriesDriftingOntoOneRowAreBothRefusedRatherThanOneWinning()
    {
        var store = new RecordStore
        {
            Version = RecordStore.CurrentVersion,
            Series = [],
            Films =
            [
                new FilmEntry { IdentityKey = FilmKey("Se7en", 109), Sheet = new SheetEcho("Se7en (one)", "1:49:00", Fincher), Records = [new RecordOverride(TmdbId: 1)] },
                new FilmEntry { IdentityKey = FilmKey("Se7en", 111), Sheet = new SheetEcho("Se7en (two)", "1:51:00", Fincher), Records = [new RecordOverride(TmdbId: 2)] },
            ],
        };

        var bound = StoreBinder.Bind(store, [Film("Se7en", "1:50:00")], []);

        Assert.Empty(bound.Films);
        Assert.NotEmpty(bound.Notes);          // T1: the All below would pass over nothing
        Assert.Equal(2, bound.Notes.Count);
        Assert.All(bound.Notes, n => Assert.Contains("more than one", n, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AnExactClaimBeatsADriftedOneAndTheLoserIsToldWhy()
    {
        var exactKey = FilmKey("Se7en", 110);
        var store = new RecordStore
        {
            Films =
            [
                new FilmEntry { IdentityKey = FilmKey("Se7en", 111), Sheet = new SheetEcho("Se7en (drifted)", "", Fincher), Records = [new RecordOverride(TmdbId: 2)] },
                new FilmEntry { IdentityKey = exactKey, Sheet = new SheetEcho("Se7en (exact)", "", Fincher), Records = [new RecordOverride(TmdbId: 1)] },
            ],
        };

        var bound = StoreBinder.Bind(store, [Film("Se7en", "1:50:00")], []);

        Assert.Equal(1, Assert.Contains(exactKey, bound.Films).Records.Single().TmdbId);
        var note = Assert.Single(bound.Notes);
        Assert.Contains("Se7en (drifted)", note, StringComparison.Ordinal);
        Assert.Contains("which names it exactly", note, StringComparison.Ordinal);
        Assert.DoesNotContain("matches no film", note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ADriftNoteSaysWhichWayAndHowFarTheRuntimeMoved()
    {
        // Entry written at 108, sheet now 110: the entry was written against a SHORTER runtime.
        var shorter = Assert.Single(StoreBinder.Bind(WithFilm(FilmKey("Se7en", 108)), [Film("Se7en", "1:50:00")], []).Notes);
        // Entry written at 109, sheet now 110: SHORTER by one.
        var oneShorter = Assert.Single(StoreBinder.Bind(WithFilm(FilmKey("Se7en", 109)), [Film("Se7en", "1:50:00")], []).Notes);
        // Entry written at 112: a LONGER runtime.
        var longer = Assert.Single(StoreBinder.Bind(WithFilm(FilmKey("Se7en", 112)), [Film("Se7en", "1:50:00")], []).Notes);

        Assert.Contains("a runtime 2 minute(s) shorter than the sheet now has", shorter, StringComparison.Ordinal);
        Assert.Contains("a runtime 1 minute(s) shorter than the sheet now has", oneShorter, StringComparison.Ordinal);
        Assert.Contains("a runtime 2 minute(s) longer than the sheet now has", longer, StringComparison.Ordinal);
        Assert.Contains("was matched anyway", longer, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEntryReachingTwoRowsBindsTheNearerOneAndSaysNothingOfDrift()
    {
        // Two cuts of one film, a minute apart. The entry names the 110-minute cut exactly; the 111-minute row
        // can also reach it at offset -1. The exact row is the truer account, so it binds, with no drift note.
        var exactRow = FilmKey("Se7en", 110);
        var farRow = FilmKey("Se7en", 111);

        var bound = StoreBinder.Bind(WithFilm(exactRow), [Film("Se7en", "1:51:00"), Film("Se7en", "1:50:00")], []);

        Assert.Empty(bound.Notes);
        Assert.Equal(807, Assert.Contains(exactRow, bound.Films).Records.Single().TmdbId);
        Assert.DoesNotContain(farRow, bound.Films.Keys);
    }

    [Fact]
    public void ASeriesEntryBindsOnItsNameAndHasNoDriftRuleAtAll()
    {
        var store = new RecordStore
        {
            Version = RecordStore.CurrentVersion,
            Films = [],
            Series = [new SeriesEntry
            {
                IdentityKey = SeriesKey("The Office"),
                Sheet = new SheetEcho("The Office"),
                Record = new RecordOverride(TmdbId: 2316),
            }],
        };

        var bound = StoreBinder.Bind(store, [], [Show("The Office")]);

        Assert.Empty(bound.Notes);
        Assert.Equal(2316, Assert.Contains(SeriesKey("The Office"), bound.Series).Record.TmdbId);
    }

    [Fact]
    public void ASeriesWhoseNameCarriesAYearIsKeyedWithTheYearFoldedBackIn()
    {
        // The silent failure: skipping the fold keys "X (2020)" as plain "X", and every year-bearing series
        // would orphan with a note that reads like a stale entry.
        Assert.NotEqual(SeriesKey("Doctor Who"), SeriesKey("Doctor Who (2005)"));
        Assert.NotEqual(
            IdentityKey.ForSeries(TitleNormalizer.Normalize("Doctor Who (2005)")), SeriesKey("Doctor Who (2005)"));

        var store = new RecordStore
        {
            Series = [new SeriesEntry
            {
                IdentityKey = SeriesKey("Doctor Who (2005)"),
                Sheet = new SheetEcho("Doctor Who (2005)"),
                Record = new RecordOverride(TmdbId: 57243),
            }],
        };

        var bound = StoreBinder.Bind(store, [], [Show("Doctor Who (2005)"), Show("Doctor Who")]);

        Assert.Empty(bound.Notes);
        Assert.Equal(57243, Assert.Contains(SeriesKey("Doctor Who (2005)"), bound.Series).Record.TmdbId);
        Assert.DoesNotContain(SeriesKey("Doctor Who"), bound.Series.Keys);
    }

    [Fact]
    public void ASeriesEntryNamingNoSeriesOnTheSheetIsNoted()
    {
        var store = new RecordStore
        {
            Version = RecordStore.CurrentVersion,
            Films = [],
            Series = [new SeriesEntry { IdentityKey = SeriesKey("Gone"), Sheet = new SheetEcho("Gone"), Record = new RecordOverride(TmdbId: 1) }],
        };

        var bound = StoreBinder.Bind(store, [], [Show("The Office")]);

        Assert.Empty(bound.Series);
        Assert.Contains("matches no series", Assert.Single(bound.Notes), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnEmptyStoreBindsNothingAndSaysNothing()
    {
        var bound = StoreBinder.Bind(RecordStore.Empty, [Film("Se7en", "1:50:00")], [Show("The Office")]);

        Assert.Empty(bound.Films);
        Assert.Empty(bound.Series);
        Assert.Empty(bound.Notes);
    }

    [Fact]
    public void ARowWhoseRuntimeIsUnreadableIsSkippedRatherThanThrowing()
    {
        // EnrichRun rejects unreadable runtimes before the store is bound, so this can only be reached by a
        // direct call -- but a binder that threw would turn a data problem into a stack trace in a later task.
        var bound = StoreBinder.Bind(WithFilm(FilmKey("Se7en", 110)), [Film("Se7en", "not a runtime")], []);

        Assert.Empty(bound.Films);
        Assert.Single(bound.Notes);
    }
}

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

    // ---- the orphan note's "Closest on the sheet" half ------------------------------------------------
    //
    // The actionable half of the note: it names the row the entry should be re-keyed FROM. Its only tests
    // lived on the tab side and went out with the tab, after which MaxCandidatesInANote = 0, or deleting the
    // whole clause, left the suite green.

    /// <summary>
    /// One entry keyed at 170 minutes against rows nowhere near it, so it is orphaned and the single note is
    /// <c>OrphanNote</c> rather than a drift, tie or contested one.
    /// </summary>
    private static string OrphanNoteFor(string echoTitle, params SheetRow[] rows)
    {
        var bound = StoreBinder.Bind(WithFilm(FilmKey(echoTitle, 170), sheetTitle: echoTitle), rows, []);

        Assert.Empty(bound.Films);
        return Assert.Single(bound.Notes);
    }

    [Fact]
    public void AnOrphanedEntryWithNoSameTitledRowSaysSoRatherThanListingNothing()
    {
        var note = OrphanNoteFor("Heat", Film("Se7en", "1:50:00"));

        Assert.Contains("No film on the sheet has that title.", note, StringComparison.Ordinal);
        Assert.DoesNotContain("Closest", note, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOrphanedEntryNamesTheRowsItCouldBeReKeyedFrom()
    {
        var note = OrphanNoteFor("Heat", Film("Heat", "1:01:00"), Film("heat", "1:02:00"));

        Assert.Contains("Closest on the sheet: 'Heat'; 'heat'.", note, StringComparison.Ordinal);
        Assert.DoesNotContain("No film on the sheet", note, StringComparison.Ordinal);
    }

    [Fact]
    public void AtMostThreeClosestRowsAreNamedAndTheRestAreCounted()
    {
        // Five rows whose raw titles all fold to "heat", so all five are candidates and the note has to choose.
        var note = OrphanNoteFor("Heat",
            Film("Heat", "1:01:00"), Film("heat", "1:02:00"), Film("HEAT", "1:03:00"),
            Film("Heat!", "1:04:00"), Film("Heat.", "1:05:00"));

        Assert.Contains("Closest on the sheet: 'Heat'; 'heat'; 'HEAT'; and 2 more.", note, StringComparison.Ordinal);
        Assert.DoesNotContain("'Heat!'", note, StringComparison.Ordinal);
        Assert.DoesNotContain("'Heat.'", note, StringComparison.Ordinal);
    }

    [Fact]
    public void ExactlyThreeClosestRowsAreAllNamedWithNoCounter()
    {
        var note = OrphanNoteFor("Heat",
            Film("Heat", "1:01:00"), Film("heat", "1:02:00"), Film("HEAT", "1:03:00"));

        Assert.Contains("Closest on the sheet: 'Heat'; 'heat'; 'HEAT'.", note, StringComparison.Ordinal);
        Assert.DoesNotContain("more", note, StringComparison.Ordinal);
    }

    [Fact]
    public void AChangedTitleOrDirectorIsOrphanedBecauseTheKeyFoldsThemIn()
    {
        var bound = StoreBinder.Bind(
            WithFilm(FilmKey("Se7en", 110, directors: "Someone Else")), [Film("Se7en", "1:50:00")], []);

        Assert.Empty(bound.Films);
        Assert.Contains("matches no film", Assert.Single(bound.Notes), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The owner's own two Blade Runner rows: the theatrical cut at 1:57:10 and the Final Cut at 1:57:29. They
    /// fold to one title, truncate to the same 117 minutes and share one director, so they carry ONE identity
    /// key (<c>IdentityKeyRealLibraryTests</c> asserts that from the raw rows). They are two cuts he owns
    /// separately, NOT one film listed twice, so an entry on that key correcting both of them is a thing he
    /// has to be told about: both corrected records then take the same <c>RecordKey</c> and
    /// <c>CatalogueReconciler</c> drops one.
    /// </summary>
    /// <remarks>
    /// The binder cannot refuse the claim, and must not: nothing writable in <c>record-store.json</c>
    /// addresses one of the two rows, so refusing would leave him with a correction he cannot express at all.
    /// What it owes him is the note. <c>--print-keys</c> is not a substitute, because it warns when the entry
    /// is WRITTEN and this state can arrive afterwards -- the real pair at 124 and 125 minutes of Captain
    /// America: The First Avenger is one Apple re-encode away from it.
    /// </remarks>
    [Fact]
    public void OneEntryBoundToAKeyTwoRowsCarryCorrectsBothAndSaysSo()
    {
        var key = FilmKey("Blade Runner", 117, "Ridley Scott");

        var bound = StoreBinder.Bind(
            new RecordStore
            {
                Version = RecordStore.CurrentVersion,
                Series = [],
                Films = [new FilmEntry
                {
                    IdentityKey = key,
                    Sheet = new SheetEcho("Blade Runner", "1:57:10", "Ridley Scott"),
                    Records = [new RecordOverride(TmdbId: 78)],
                }],
            },
            [Film("Blade Runner", "1:57:10", "Ridley Scott"),
             Film("Blade Runner (The Final Cut)", "1:57:29", "Ridley Scott")],
            []);

        // The premise, stated rather than assumed: both rows really do carry the one key.
        Assert.Equal(key, FilmKey("Blade Runner (The Final Cut)", 117, "Ridley Scott"));
        Assert.Equal(78, Assert.Contains(key, bound.Films).Records.Single().TmdbId);

        var note = Assert.Single(bound.Notes);
        Assert.Contains("'Blade Runner'", note, StringComparison.Ordinal);
        Assert.Contains("2 rows on the Movies tab carry", note, StringComparison.Ordinal);
        Assert.Contains("applied to every one of them", note, StringComparison.Ordinal);
        Assert.Contains("'Blade Runner' at 117 minutes", note, StringComparison.Ordinal);
        Assert.Contains("'Blade Runner (The Final Cut)' at 117 minutes", note, StringComparison.Ordinal);
        Assert.Contains("Nothing you can write in record-store.json separates them.", note, StringComparison.Ordinal);
    }

    [Fact]
    public void ARowWhoseKeyIsItsOwnStillBindsWithoutThatNote()
    {
        // The note above means nothing unless it is absent for the ordinary case. One Blade Runner row, same
        // entry, same key: the film binds and the binder says nothing at all.
        var key = FilmKey("Blade Runner", 117, "Ridley Scott");

        var bound = StoreBinder.Bind(
            Films(Entry(key, "Blade Runner", 78)), [Film("Blade Runner", "1:57:10", "Ridley Scott")], []);

        Assert.Equal(78, Assert.Contains(key, bound.Films).Records.Single().TmdbId);
        Assert.Empty(bound.Notes);
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
        var note = Assert.Single(bound.Notes);
        Assert.Contains("matches no series", note, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No series on the sheet has that name.", note, StringComparison.Ordinal);
        Assert.DoesNotContain("Closest", note, StringComparison.Ordinal);
    }

    /// <summary>
    /// The series note's own "Closest on the sheet" half, which is what tells the owner the name to re-key
    /// from. <b>It has no cap</b>, unlike the film note's: <c>BindSeries</c> lists every sheet name that folds
    /// to the entry's echo, with no <c>MaxCandidatesInANote</c> and no "and N more" counter. Four candidates
    /// here, one past the film note's limit, so the difference is pinned rather than assumed.
    /// </summary>
    [Fact]
    public void AnOrphanedSeriesEntryNamesEveryClosestNameOnTheSheetAndCountsNone()
    {
        var store = new RecordStore
        {
            Version = RecordStore.CurrentVersion,
            Films = [],
            Series = [new SeriesEntry
            {
                IdentityKey = SeriesKey("Gone"),
                Sheet = new SheetEcho("The Office"),
                Record = new RecordOverride(TmdbId: 1),
            }],
        };

        // Four distinct raw names that all fold to "the office", so all four are candidates.
        var bound = StoreBinder.Bind(store, [],
            [Show("The Office"), Show("the office."), Show("THE OFFICE"), Show("The  Office")]);

        Assert.Empty(bound.Series);
        var note = Assert.Single(bound.Notes);
        Assert.Contains("Closest on the sheet: 'The Office'; 'the office.'; 'THE OFFICE'; 'The  Office'.",
                        note, StringComparison.Ordinal);
        Assert.DoesNotContain("more", note, StringComparison.Ordinal);
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

    private static RecordStore Films(params FilmEntry[] entries)
        => new() { Version = RecordStore.CurrentVersion, Series = [], Films = entries };

    private static FilmEntry Entry(string key, string title, int tmdbId) => new()
    {
        IdentityKey = key,
        Sheet = new SheetEcho(title, "", Fincher),
        Records = [new RecordOverride(TmdbId: tmdbId)],
    };

    private static int Count(string text, string part)
        => text.Split(part, StringSplitOptions.None).Length - 1;

    [Fact]
    public void AnEntryEquallyFarFromTwoRowsBindsNeitherAndNamesBothInOneNote()
    {
        // Rows at 109 and 111, an entry written against 110: both are one minute away. Picking either would
        // land the correction on a cut the owner may not have meant, in silence.
        var bound = StoreBinder.Bind(
            Films(Entry(FilmKey("Se7en", 110), "Se7en (mine)", 807)),
            [Film("Se7en", "1:49:00"), Film("Se7en", "1:51:00")],
            []);

        Assert.Empty(bound.Films);
        var note = Assert.Single(bound.Notes);   // one tie note, and NOT an orphan note as well
        Assert.Contains("'Se7en (mine)'", note, StringComparison.Ordinal);
        Assert.Contains("equal distance", note, StringComparison.Ordinal);
        Assert.Contains("'Se7en' at 109 minutes", note, StringComparison.Ordinal);
        Assert.Contains("'Se7en' at 111 minutes", note, StringComparison.Ordinal);
        Assert.Contains("Re-key", note, StringComparison.Ordinal);
        Assert.Contains("--print-keys", note, StringComparison.Ordinal);
        Assert.DoesNotContain("matches no film", note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ATiedEntryDoesNotDisturbAnotherEntryThatNamesARowExactly()
    {
        var exactKey = FilmKey("Se7en", 109);
        var bound = StoreBinder.Bind(
            Films(Entry(FilmKey("Se7en", 110), "tied", 1), Entry(exactKey, "exact", 2)),
            [Film("Se7en", "1:49:00"), Film("Se7en", "1:51:00")],
            []);

        Assert.Equal(2, Assert.Contains(exactKey, bound.Films).Records.Single().TmdbId);
        Assert.Contains("'tied'", Assert.Single(bound.Notes), StringComparison.Ordinal);
    }

    [Fact]
    public void AContestedNoteNamesTheOtherEntryAndNotItsOwnAsItsRival()
    {
        var bound = StoreBinder.Bind(
            Films(Entry(FilmKey("Se7en", 109), "Se7en (one)", 1), Entry(FilmKey("Se7en", 111), "Se7en (two)", 2)),
            [Film("Se7en", "1:50:00")],
            []);

        var one = Assert.Single(bound.Notes, n => n.StartsWith("Record store entry 'Se7en (one)'", StringComparison.Ordinal));
        var two = Assert.Single(bound.Notes, n => n.StartsWith("Record store entry 'Se7en (two)'", StringComparison.Ordinal));
        // Each note opens with its own name, so its own name appears exactly once and the rival's exactly once.
        Assert.Equal(1, Count(one, "Se7en (one)"));
        Assert.Equal(1, Count(one, "Se7en (two)"));
        Assert.Equal(1, Count(two, "Se7en (two)"));
        Assert.Equal(1, Count(two, "Se7en (one)"));
    }

    [Fact]
    public void ARivalIsExcludedByIdentityNotByValueEquality()
    {
        // Two entries that are record-EQUAL but are two entries. The reader rejects duplicate keys, so a real
        // file cannot do this, but the binder must not depend on that: != would hide the twin as "itself".
        var one = Entry(FilmKey("Se7en", 109), "Twin", 1);
        var bound = StoreBinder.Bind(Films(one, one with { }), [Film("Se7en", "1:50:00")], []);

        Assert.Equal(2, bound.Notes.Count);
        Assert.All(bound.Notes, n => Assert.Equal(2, Count(n, "'Twin'")));
    }

    [Fact]
    public void TwoShowsWhoseNamesFoldToOneKeyDoNotCrashTheBind()
    {
        // "the office." folds to the same key as "The Office". ToDictionary would throw on the second.
        Assert.Equal(SeriesKey("The Office"), SeriesKey("the office."));

        var store = new RecordStore
        {
            Films = [],
            Series = [new SeriesEntry
            {
                IdentityKey = SeriesKey("The Office"),
                Sheet = new SheetEcho("The Office"),
                Record = new RecordOverride(TmdbId: 2316),
            }],
        };

        var bound = StoreBinder.Bind(store, [], [Show("The Office"), Show("the office.")]);

        Assert.Empty(bound.Notes);
        Assert.Equal(2316, Assert.Contains(SeriesKey("The Office"), bound.Series).Record.TmdbId);
    }

    [Theory]
    [InlineData("0:01:00", 0)]
    [InlineData("0:01:00", -1)]
    [InlineData("0:00:30", 0)]
    public void ARuntimeAtOrBelowZeroIsNeverHashedSoNoEntryBindsToIt(string rowRuntime, int entryMinutes)
    {
        var bound = StoreBinder.Bind(
            Films(Entry(FilmKey("Se7en", entryMinutes), "Se7en", 1)), [Film("Se7en", rowRuntime)], []);

        Assert.Empty(bound.Films);
        Assert.Contains("matches no film", Assert.Single(bound.Notes), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnUnreadableRuntimeRowIsNotIndexedAtAll()
    {
        // Unreadable reads as 0 minutes. Without the skip, offset +1 would hash the row at 1 minute and an
        // entry keyed at 1 minute would bind to a row that has no runtime.
        var bound = StoreBinder.Bind(
            Films(Entry(FilmKey("Se7en", 1), "Se7en", 1)), [Film("Se7en", "not a runtime")], []);

        Assert.Empty(bound.Films);
        Assert.Contains("matches no film", Assert.Single(bound.Notes), StringComparison.OrdinalIgnoreCase);
    }
}

using System.Text.Json;
using MovieLibrary.Web.Models;
using MovieLibrary.Web.Services;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// The owner's own state over the catalogue: what it saves, what it refuses, and what it does with a
/// document written somewhere else.
/// </summary>
/// <remarks>
/// <para>
/// <b>No browser anywhere in this class</b>, which is the reason <c>IPreferenceStorage</c> exists: the rules
/// worth pinning are about the DOCUMENT -- an empty entry is dropped, a moved key is recovered by TMDB id,
/// a merge never erases a score -- and each of them would otherwise need a JS runtime and a real
/// <c>localStorage</c> to reach. The interop itself is one call each way and is pinned in
/// <c>PreferenceInteropTests</c>, where a JS runtime is the subject rather than the obstacle.
/// </para>
/// <para>
/// The film keys here are real <c>RecordKey</c> shapes -- <c>807-110</c> for a matched film,
/// <c>u-d2f1c6e2</c> for an unmatched one -- because the recovery walk's whole behaviour turns on which
/// form a key is in, and a fixture of <c>"a"</c> and <c>"b"</c> would have reached none of it.
/// </para>
/// </remarks>
public class PreferenceStoreTests
{
    private const string Film = "807-110";
    private const string Unmatched = "u-d2f1c6e2";
    private const string Series = "the-bureau";
    private const int FilmId = 807;

    private static (PreferenceStore Store, FakePreferenceStorage Storage) New(string? saved = null)
    {
        var storage = new FakePreferenceStorage(saved);
        return (new PreferenceStore(storage), storage);
    }

    private static async Task<(PreferenceStore Store, FakePreferenceStorage Storage)> LoadedAsync(string? saved = null)
    {
        var (store, storage) = New(saved);
        await store.LoadAsync();

        return (store, storage);
    }

    [Fact]
    public async Task ABrowserWithNothingSavedAnswersEveryQuestionWithNothing()
    {
        var (store, storage) = await LoadedAsync();

        Assert.True(store.Loaded);
        Assert.Equal(1, storage.Reads);
        Assert.Empty(store.Lists);
        Assert.False(store.IsFavourite(TitleKind.Film, Film));
        Assert.Null(store.RatingOf(TitleKind.Film, Film));
        Assert.True(store.For(TitleKind.Film, Film).IsEmpty);
    }

    /// <remarks>
    /// A page that renders on navigation calls this again, and the document in memory may hold edits the
    /// browser has not been asked for yet -- a second read would throw them away.
    /// </remarks>
    [Fact]
    public async Task LoadingTwiceReadsOnceAndKeepsWhatHasHappenedSince()
    {
        var (store, storage) = await LoadedAsync();
        await store.SetFavouriteAsync(TitleKind.Film, Film, FilmId, true);

        await store.LoadAsync();

        Assert.Equal(1, storage.Reads);
        Assert.True(store.IsFavourite(TitleKind.Film, Film));
    }

    [Fact]
    public async Task AStarIsReadableAtOnceAndReachesStorageOnce()
    {
        var (store, storage) = await LoadedAsync();

        await store.SetFavouriteAsync(TitleKind.Film, Film, FilmId, true);

        Assert.True(store.IsFavourite(TitleKind.Film, Film));
        Assert.Equal(1, storage.Writes);
        Assert.Contains("\"807-110\"", storage.Json);
    }

    /// <remarks>
    /// The whole point of the <c>IsEmpty</c> rule: the file the owner carries to another device holds what
    /// he SAID, not every title he ever clicked and undid.
    /// </remarks>
    [Fact]
    public async Task UnstarringTheLastTitleLeavesNoRecordOfItAtAll()
    {
        var (store, storage) = await LoadedAsync();

        await store.SetFavouriteAsync(TitleKind.Film, Film, FilmId, true);
        await store.SetFavouriteAsync(TitleKind.Film, Film, FilmId, false);

        Assert.False(store.IsFavourite(TitleKind.Film, Film));
        Assert.DoesNotContain(Film, storage.Json);
    }

    /// <remarks>
    /// A film and a series can be keyed the same -- nothing stops a slug reading like a published key --
    /// and one map would move a star from one to the other with nothing on screen to say it happened.
    /// </remarks>
    [Fact]
    public async Task AFilmAndASeriesWithTheSameKeyAreTwoDifferentTitles()
    {
        var (store, _) = await LoadedAsync();

        await store.SetFavouriteAsync(TitleKind.Film, "24", 0, true);

        Assert.True(store.IsFavourite(TitleKind.Film, "24"));
        Assert.False(store.IsFavourite(TitleKind.Series, "24"));
    }

    /// <remarks>
    /// Zero is a score on TMDB's scale and this app uses those units, so "zero" and "not scored" have to be
    /// two different answers -- a sort by his own score has to be able to put them in different places.
    /// </remarks>
    [Fact]
    public async Task AScoreOfZeroIsAScoreAndIsNotTheSameAsNoScore()
    {
        var (store, storage) = await LoadedAsync();

        await store.SetRatingAsync(TitleKind.Film, Film, FilmId, 0);

        Assert.Equal(0, store.RatingOf(TitleKind.Film, Film));
        Assert.False(store.For(TitleKind.Film, Film).IsEmpty);
        Assert.Contains("\"rate\":0", storage.Json);

        await store.SetRatingAsync(TitleKind.Film, Film, FilmId, null);

        Assert.Null(store.RatingOf(TitleKind.Film, Film));
        Assert.DoesNotContain(Film, storage.Json);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    public async Task AScoreOffTheScaleIsRefusedRatherThanClamped(int rating)
    {
        var (store, storage) = await LoadedAsync();

        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => store.SetRatingAsync(TitleKind.Film, Film, FilmId, rating));

        Assert.Contains("TMDB's own scale", ex.Message, StringComparison.Ordinal);
        Assert.Equal(0, storage.Writes);
    }

    /// <remarks>
    /// Membership is kept on the title, so this is also what proves the entry carries list IDS and not
    /// names: the rename below leaves every entry untouched.
    /// </remarks>
    [Fact]
    public async Task ATitleGoesIntoAListAndComesOutAgain()
    {
        var (store, _) = await LoadedAsync();
        var (result, list) = await store.CreateListAsync("Christmas");

        Assert.Equal(ListEdit.Done, result);
        Assert.NotNull(list);

        await store.SetMembershipAsync(TitleKind.Series, Series, 2309, list.Id, member: true);
        Assert.True(store.IsInList(TitleKind.Series, Series, list.Id));

        Assert.Equal(ListEdit.Done, await store.RenameListAsync(list.Id, "Christmas films"));
        Assert.True(store.IsInList(TitleKind.Series, Series, list.Id));

        await store.SetMembershipAsync(TitleKind.Series, Series, 2309, list.Id, member: false);
        Assert.False(store.IsInList(TitleKind.Series, Series, list.Id));
    }

    /// <remarks>
    /// Two checkboxes reading "Christmas" on a record page is a pair a screen-reader reader cannot tell
    /// apart, so the second one is refused where it is typed. Case is ignored for the comparison and kept
    /// in what is stored.
    /// </remarks>
    [Theory]
    [InlineData("Christmas")]
    [InlineData("christmas")]
    [InlineData("  Christmas  ")]
    public async Task ANameTheOwnerAlreadyUsedIsRefused(string second)
    {
        var (store, _) = await LoadedAsync();
        await store.CreateListAsync("Christmas");

        var (result, list) = await store.CreateListAsync(second);

        Assert.Equal(ListEdit.DuplicateName, result);
        Assert.Null(list);
        Assert.Single(store.Lists);
        Assert.Equal("Christmas", store.Lists[0].Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AListWithNoNameIsRefused(string name)
    {
        var (store, storage) = await LoadedAsync();

        var (result, _) = await store.CreateListAsync(name);

        Assert.Equal(ListEdit.EmptyName, result);
        Assert.Empty(store.Lists);
        Assert.Equal(0, storage.Writes);
    }

    /// <remarks>
    /// Correcting the case of a list's own name is a rename and not a clash with itself.
    /// </remarks>
    [Fact]
    public async Task AListCanBeRenamedToADifferentSpellingOfItsOwnName()
    {
        var (store, _) = await LoadedAsync();
        var (_, list) = await store.CreateListAsync("christmas");

        Assert.Equal(ListEdit.Done, await store.RenameListAsync(list!.Id, "Christmas"));
        Assert.Equal("Christmas", store.Lists[0].Name);
    }

    /// <remarks>
    /// Both halves in one write. A membership left behind is an id nothing names -- invisible, and
    /// resurrected the day an id is reused -- and a title whose only state was that membership is dropped
    /// by the same rule that drops an unstarred one.
    /// </remarks>
    [Fact]
    public async Task DeletingAListTakesEveryMembershipOfItWithIt()
    {
        var (store, storage) = await LoadedAsync();
        var (_, list) = await store.CreateListAsync("Christmas");

        await store.SetMembershipAsync(TitleKind.Film, Film, FilmId, list!.Id, member: true);
        await store.SetMembershipAsync(TitleKind.Series, Series, 2309, list.Id, member: true);
        await store.SetFavouriteAsync(TitleKind.Series, Series, 2309, true);

        Assert.Equal(ListEdit.Done, await store.DeleteListAsync(list.Id));

        Assert.Empty(store.Lists);
        Assert.False(store.IsInList(TitleKind.Film, Film, list.Id));
        Assert.DoesNotContain(list.Id, storage.Json);

        // The film's only state was the membership, so it is gone; the series was starred as well, so it stays.
        Assert.DoesNotContain(Film, storage.Json);
        Assert.True(store.IsFavourite(TitleKind.Series, Series));
    }

    [Fact]
    public async Task ATitleCannotJoinAListThatDoesNotExist()
    {
        var (store, storage) = await LoadedAsync();

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => store.SetMembershipAsync(TitleKind.Film, Film, FilmId, "nosuchid", member: true));

        Assert.Contains("nosuchid", ex.Message, StringComparison.Ordinal);
        Assert.Equal(0, storage.Writes);
    }

    /// <remarks>
    /// The owner starred something, the star is on screen, and the browser will not keep it. The exception
    /// is the only thing that lets a page say so, which is why it is not swallowed in the storage.
    /// </remarks>
    [Fact]
    public async Task AStorageThatRefusesTheWriteIsReportedAndNotSwallowed()
    {
        var (store, storage) = await LoadedAsync();
        storage.Refuse = true;

        await Assert.ThrowsAsync<PreferenceStorageException>(
            () => store.SetFavouriteAsync(TitleKind.Film, Film, FilmId, true));
    }

    // ============================================================ what another device wrote

    /// <remarks>
    /// A document this build cannot read is replaced rather than guessed at, and the guess is the dangerous
    /// half: a wrong one puts a favourite on the wrong title. Junk, half a document and a file from some
    /// other app all land here.
    /// </remarks>
    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"v\":1,")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ADocumentThisBuildCannotReadIsIgnoredRatherThanGuessedAt(string saved)
    {
        var (store, _) = await LoadedAsync(saved);

        Assert.Empty(store.Lists);
        Assert.True(store.For(TitleKind.Film, Film).IsEmpty);
    }

    [Fact]
    public async Task ADocumentFromAnotherDeviceIsReadBackAsItWasWritten()
    {
        var (first, storage) = await LoadedAsync();
        var (_, list) = await first.CreateListAsync("Christmas");
        await first.SetRatingAsync(TitleKind.Film, Film, FilmId, 9);
        await first.SetMembershipAsync(TitleKind.Film, Film, FilmId, list!.Id, member: true);

        var (second, _) = await LoadedAsync(storage.Json);

        Assert.Equal(9, second.RatingOf(TitleKind.Film, Film));
        Assert.True(second.IsInList(TitleKind.Film, Film, list.Id));
        Assert.Equal("Christmas", Assert.Single(second.Lists).Name);
        Assert.Equal(FilmId, second.For(TitleKind.Film, Film).TmdbId);
    }

    [Fact]
    public async Task AFileFromALaterVersionOfTheSiteIsRefusedAndChangesNothing()
    {
        var (store, storage) = await LoadedAsync();
        await store.SetFavouriteAsync(TitleKind.Film, Film, FilmId, true);
        var before = storage.Json;

        var report = await store.ImportAsync(
            "{\"v\":99,\"films\":{\"1-1\":{\"fav\":true}},\"series\":{},\"lists\":[]}", ImportMode.Merge);

        Assert.Equal(ImportOutcome.NewerVersion, report.Outcome);
        Assert.False(store.IsFavourite(TitleKind.Film, "1-1"));
        Assert.Equal(before, storage.Json);
    }

    [Fact]
    public async Task AFileThisAppDidNotWriteIsRefusedAndChangesNothing()
    {
        var (store, storage) = await LoadedAsync();
        await store.SetFavouriteAsync(TitleKind.Film, Film, FilmId, true);
        var before = storage.Json;

        var report = await store.ImportAsync("<html>sign in to wi-fi</html>", ImportMode.Merge);

        Assert.Equal(ImportOutcome.NotOurFile, report.Outcome);
        Assert.Equal(before, storage.Json);
        Assert.True(store.IsFavourite(TitleKind.Film, Film));
    }

    /// <remarks>
    /// The mode that exists so nothing is lost: a star on either side stays, a score in the file wins over
    /// one here, and a score only here SURVIVES a file that says nothing about it.
    /// </remarks>
    [Fact]
    public async Task AMergeKeepsBothSidesAndNeverErasesAScoreWithSilence()
    {
        var (store, _) = await LoadedAsync();
        await store.SetRatingAsync(TitleKind.Film, Film, FilmId, 7);
        await store.SetFavouriteAsync(TitleKind.Series, Series, 2309, true);

        var report = await store.ImportAsync(
            "{\"v\":1,\"lists\":[],\"series\":{},\"films\":{"
            + "\"807-110\":{\"i\":807,\"fav\":true},"
            + "\"120-178\":{\"i\":120,\"rate\":10}}}", ImportMode.Merge);

        Assert.Equal(ImportOutcome.Done, report.Outcome);
        Assert.Equal(2, report.Films);

        // The file said nothing about his 7, so his 7 is still here, and it starred what he had not.
        Assert.Equal(7, store.RatingOf(TitleKind.Film, Film));
        Assert.True(store.IsFavourite(TitleKind.Film, Film));

        // What only the file had arrived, and what only this browser had stayed.
        Assert.Equal(10, store.RatingOf(TitleKind.Film, "120-178"));
        Assert.True(store.IsFavourite(TitleKind.Series, Series));
    }

    /// <remarks>
    /// The same list made on two devices has two ids and one name. Matched by NAME, so the owner ends up
    /// with one "Christmas" holding the titles from both -- and not the two identical checkboxes that
    /// <c>CreateListAsync</c> refuses to let him make by hand.
    /// </remarks>
    [Fact]
    public async Task AListMadeTwiceOnTwoDevicesComesBackAsOneList()
    {
        var (store, _) = await LoadedAsync();
        var (_, mine) = await store.CreateListAsync("Christmas");
        await store.SetMembershipAsync(TitleKind.Film, Film, FilmId, mine!.Id, member: true);

        var report = await store.ImportAsync(
            "{\"v\":1,\"series\":{},\"lists\":[{\"id\":\"theirs01\",\"name\":\"Christmas\"}],"
            + "\"films\":{\"120-178\":{\"i\":120,\"lists\":[\"theirs01\"]}}}", ImportMode.Merge);

        Assert.Equal(ImportOutcome.Done, report.Outcome);
        Assert.Equal(0, report.Lists);

        var only = Assert.Single(store.Lists);
        Assert.Equal(mine.Id, only.Id);

        // Both devices' titles are in the one list, under this browser's own id for it.
        Assert.True(store.IsInList(TitleKind.Film, Film, mine.Id));
        Assert.True(store.IsInList(TitleKind.Film, "120-178", mine.Id));
    }

    /// <remarks>
    /// Importing the same file twice is something a person does. The second one must not leave a third copy
    /// of every list, which is what matching on the id the file carries buys.
    /// </remarks>
    [Fact]
    public async Task ImportingTheSameFileTwiceChangesNothingTheSecondTime()
    {
        var (store, _) = await LoadedAsync();
        const string file =
            "{\"v\":1,\"series\":{},\"lists\":[{\"id\":\"theirs01\",\"name\":\"Rewatch\"}],"
            + "\"films\":{\"120-178\":{\"i\":120,\"fav\":true,\"lists\":[\"theirs01\"]}}}";

        await store.ImportAsync(file, ImportMode.Merge);
        var after = store.Export();

        await store.ImportAsync(file, ImportMode.Merge);

        Assert.Equal(after, store.Export());
        Assert.Single(store.Lists);
    }

    [Fact]
    public async Task ReplaceKeepsOnlyWhatTheFileSays()
    {
        var (store, _) = await LoadedAsync();
        await store.CreateListAsync("Christmas");
        await store.SetRatingAsync(TitleKind.Film, Film, FilmId, 7);

        var report = await store.ImportAsync(
            "{\"v\":1,\"lists\":[],\"series\":{},\"films\":{\"120-178\":{\"i\":120,\"fav\":true}}}",
            ImportMode.Replace);

        Assert.Equal(ImportOutcome.Done, report.Outcome);
        Assert.Empty(store.Lists);
        Assert.Null(store.RatingOf(TitleKind.Film, Film));
        Assert.True(store.IsFavourite(TitleKind.Film, "120-178"));
    }

    /// <remarks>
    /// One hand-edited field may not cost 1,500 good entries, so the score is dropped and the entry kept --
    /// and the count comes back so the page can say it happened rather than mangling it silently.
    /// </remarks>
    [Fact]
    public async Task AScoreOffTheScaleInAFileIsDroppedAndTheRestOfTheEntryIsKept()
    {
        var (store, _) = await LoadedAsync();

        var report = await store.ImportAsync(
            "{\"v\":1,\"lists\":[],\"series\":{},\"films\":{"
            + "\"807-110\":{\"i\":807,\"fav\":true,\"rate\":99},"
            + "\"120-178\":{\"i\":120,\"rate\":-3}}}", ImportMode.Merge);

        Assert.Equal(ImportOutcome.Done, report.Outcome);
        Assert.Equal(2, report.DroppedRatings);

        Assert.True(store.IsFavourite(TitleKind.Film, Film));
        Assert.Null(store.RatingOf(TitleKind.Film, Film));

        // The second entry's only field was the bad score, so there is nothing left of it to keep.
        Assert.True(store.For(TitleKind.Film, "120-178").IsEmpty);
    }

    [Fact]
    public async Task AnExportIsTheDocumentItselfAndReadsBackIntoAnEmptyBrowser()
    {
        var (store, _) = await LoadedAsync();
        await store.SetFavouriteAsync(TitleKind.Film, Film, FilmId, true);

        var file = store.Export();
        var (fresh, _) = await LoadedAsync();
        var report = await fresh.ImportAsync(file, ImportMode.Replace);

        Assert.Equal(ImportOutcome.Done, report.Outcome);
        Assert.True(fresh.IsFavourite(TitleKind.Film, Film));
        Assert.Equal(PreferenceDocument.CurrentVersion, JsonDocument.Parse(file).RootElement.GetProperty("v").GetInt32());
    }

    // ============================================================ keys that moved

    /// <remarks>
    /// <b>The defect this exists for is silent.</b> Apple re-encodes a file, the sheet's runtime moves one
    /// minute, and <c>RecordKey</c> gives the same film a new published key -- so the entry saved under the
    /// old one belongs to nothing, the star is simply off, and that reads as "I never starred this".
    /// </remarks>
    [Fact]
    public async Task AnEntryWhoseKeyMovedIsRefiledUnderTheKeyTheCatalogueUsesNow()
    {
        var (store, storage) = await LoadedAsync();
        await store.SetRatingAsync(TitleKind.Film, Film, FilmId, 9);

        var moved = await store.RecoverMovedKeysAsync(
            known: (_, key) => key == "807-111",
            keyOf: (_, id) => id == FilmId ? "807-111" : null);

        Assert.Equal(1, moved);
        Assert.Equal(9, store.RatingOf(TitleKind.Film, "807-111"));
        Assert.Null(store.RatingOf(TitleKind.Film, Film));
        Assert.DoesNotContain(Film, storage.Json);
    }

    /// <remarks>
    /// The owner owns several cuts of some films and every cut carries the same TMDB id, so an id matching
    /// two records says nothing about which one the entry meant. The entry is left alone: a later edit to
    /// the sheet can still resolve it, and moving it onto the wrong cut cannot be undone.
    /// </remarks>
    [Fact]
    public async Task AnIdThatMatchesSeveralRecordsLeavesTheEntryWhereItIs()
    {
        var (store, _) = await LoadedAsync();
        await store.SetFavouriteAsync(TitleKind.Film, Film, FilmId, true);

        var moved = await store.RecoverMovedKeysAsync(known: (_, _) => false, keyOf: (_, _) => null);

        Assert.Equal(0, moved);
        Assert.True(store.IsFavourite(TitleKind.Film, Film));
    }

    /// <remarks>
    /// An unmatched record has no id at all, so nothing can recover its entry -- which is the trade
    /// <c>RecordKey</c> records for the hash form, and the reason this walk must not touch it.
    /// </remarks>
    [Fact]
    public async Task AnUnmatchedTitleHasNoFallbackAndIsLeftAlone()
    {
        var (store, _) = await LoadedAsync();
        await store.SetFavouriteAsync(TitleKind.Film, Unmatched, 0, true);

        var moved = await store.RecoverMovedKeysAsync(known: (_, _) => false, keyOf: (_, _) => "807-110");

        Assert.Equal(0, moved);
        Assert.True(store.IsFavourite(TitleKind.Film, Unmatched));
    }

    [Fact]
    public async Task AKeyTheCatalogueStillCarriesIsNotTouchedAndNothingIsWritten()
    {
        var (store, storage) = await LoadedAsync();
        await store.SetFavouriteAsync(TitleKind.Film, Film, FilmId, true);
        var writes = storage.Writes;

        var moved = await store.RecoverMovedKeysAsync(known: (_, _) => true, keyOf: (_, _) => "something-else");

        Assert.Equal(0, moved);
        Assert.Equal(writes, storage.Writes);
        Assert.True(store.IsFavourite(TitleKind.Film, Film));
    }

    /// <remarks>
    /// Two keys of one id, one of which the catalogue now agrees with: the two entries are merged rather
    /// than one overwriting the other, so neither the older star nor the newer score is lost.
    /// </remarks>
    [Fact]
    public async Task TwoEntriesForOneFilmAreMergedRatherThanOneWinning()
    {
        var (store, _) = await LoadedAsync();
        await store.SetFavouriteAsync(TitleKind.Film, Film, FilmId, true);
        await store.SetRatingAsync(TitleKind.Film, "807-111", FilmId, 6);

        var moved = await store.RecoverMovedKeysAsync(
            known: (_, key) => key == "807-111",
            keyOf: (_, _) => "807-111");

        Assert.Equal(1, moved);
        Assert.True(store.IsFavourite(TitleKind.Film, "807-111"));
        Assert.Equal(6, store.RatingOf(TitleKind.Film, "807-111"));
        Assert.True(store.For(TitleKind.Film, Film).IsEmpty);
    }
}

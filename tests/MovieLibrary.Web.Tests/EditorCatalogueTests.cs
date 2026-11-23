using Bunit;
using MovieLibrary.Core.Identity;
using MovieLibrary.Core.Matching;
using MovieLibrary.Core.Models;
using MovieLibrary.Core.Store;
using MovieLibrary.Core.Text;
using MovieLibrary.Web.Editor.Store;

namespace MovieLibrary.Web.Tests;

/// <summary>The editor's load: the catalogue and the store bound together, over a stub source and a scripted script.</summary>
public sealed class EditorCatalogueTests
{
    private sealed class Source(
        CatalogueState state, IReadOnlyList<MovieRecord> movies, CatalogMeta? meta = null) : ICatalogueSource
    {
        public Task<(CatalogueState State, IReadOnlyList<MovieRecord> Movies)> MoviesAsync() => Task.FromResult((state, movies));

        public Task<(CatalogueState State, CatalogMeta? Meta)> MetaAsync() => Task.FromResult((state, meta));
    }

    /// <summary>
    /// A published film the way the pipeline publishes it: a matched record's key is its id and runtime, an
    /// unmatched one's is the hash of the identity key.
    /// </summary>
    internal static MovieRecord Film(int id, string title, int runtime, double mc, params string[] directors)
    {
        var identity = IdentityKey.ForFilm(
            TitleNormalizer.Normalize(title), runtime,
            TitleNormalizer.NormalizeDirectors(string.Join(" & ", directors)));

        return new MovieRecord(
            I: id, K: RecordKey.ForFilm(id == 0 ? null : id, runtime, identity),
            T: title, Y: 1995, Rt: runtime, P: null, B: null, G: [], Ag: "", Vote: 0, Dir: directors,
            Ov: null, Tag: null, Cast: [], Col: null, Own: null, Mc: mc);
    }

    /// <summary>A scripted folder: what the script answers to the three questions the load asks.</summary>
    private static (BunitContext Context, LocalDirectory Directory) Folder(
        string? text, bool supported = true, string? folder = "MovieLibrary")
    {
        var context = new BunitContext();
        var module = context.JSInterop.SetupModule(LocalDirectory.ModulePath);
        module.Setup<bool>("supported").SetResult(supported);
        module.Setup<string?>("status").SetResult(folder);
        module.Setup<string?>("readText", RecordStoreReader.DefaultFileName).SetResult(text);
        return (context, new LocalDirectory(context.JSInterop.JSRuntime));
    }

    private static async Task<EditorState> Load(
        IReadOnlyList<MovieRecord> movies, string? storeText = null, CatalogueState state = CatalogueState.Ready,
        CatalogMeta? meta = null)
    {
        var (context, directory) = Folder(storeText);
        using (context)
            return await new EditorCatalogue(new Source(state, movies, meta), directory).LoadAsync();
    }

    private static FilmEntry EntryFor(string identityKey, int tmdbId, int runtime) => new()
    {
        IdentityKey = identityKey,
        Records = [new RecordOverride(TmdbId: tmdbId, Runtime: runtime)],
    };

    private static string StoreOf(params FilmEntry[] films) =>
        RecordStoreReader.Write(RecordStore.Empty with { Films = films });

    private static readonly string KeyA = new('a', 40);
    private static readonly string KeyB = new('b', 40);

    [Fact]
    public async Task EveryPublishedRecordBecomesOneRowWithItsOwnIdentityKey()
    {
        MovieRecord[] movies = [Film(550, "Fight Club", 139, 0.9, "David Fincher"), Film(0, "Lost Reel", 90, 0, "A. Nobody"),
                                Film(807, "Se7en", 127, 0.6, "David Fincher")];

        var state = await Load(movies);

        Assert.Equal(3, state.Rows.Count);
        Assert.All(state.Rows, row => Assert.Equal(RecordLink.IdentityKeyOf(row.Movie), row.IdentityKey));
    }

    [Fact]
    public async Task TheBandAndTheBadgeComeFromCoreAndNotFromHere()
    {
        var state = await Load([Film(0, "Lost Reel", 90, 0), Film(550, "Fight Club", 139, 0.99), Film(807, "Se7en", 127, 0.5)]);

        var unmatched = state.Rows.Single(r => r.Movie.I == 0);
        var confident = state.Rows.Single(r => r.Movie.I == 550);

        Assert.Equal(Confidence.Unmatched, unmatched.Band);
        Assert.Equal(ConfidenceBand.BadgeFor(Confidence.Unmatched), unmatched.Badge);
        Assert.Equal(Confidence.Confident, confident.Band);
        Assert.Null(confident.Badge);
    }

    [Fact]
    public async Task TheCountsAreTheThreeBandsAndTheyAddUp()
    {
        var state = await Load([Film(0, "A", 90, 0), Film(0, "B", 91, 0), Film(807, "C", 127, 0.5), Film(550, "D", 139, 0.99)]);

        Assert.Equal(2, state.Unmatched);
        Assert.Equal(1, state.Uncertain);
        Assert.Equal(1, state.Confident);
        Assert.Equal(state.Rows.Count, state.Unmatched + state.Uncertain + state.Confident);
        Assert.Equal(3, state.NeedAttention);
    }

    [Fact]
    public async Task AnExistingStoreEntryIsAttachedToTheRowItBelongsTo()
    {
        var state = await Load([Film(550, "Fight Club", 139, 0.99), Film(807, "Se7en", 127, 0.5)],
            StoreOf(EntryFor(KeyA, 807, 127)));

        Assert.Equal(KeyA, state.Rows.Single(r => r.Movie.I == 807).Entry?.IdentityKey);
        Assert.Null(state.Rows.Single(r => r.Movie.I == 550).Entry);
        Assert.Empty(state.Unplaceable);
    }

    /// <summary>
    /// The silent-corruption path. A record with an entry may have had its title, runtime or directors replaced,
    /// so its identity key cannot be recomputed from what it publishes; the row must carry the ENTRY's key, or a
    /// save binds to an entry that does not exist and writes a duplicate.
    /// </summary>
    [Fact]
    public async Task ARecordWithAnEntryIsKeyedByTheEntryAndNeverRecomputed()
    {
        var movie = Film(807, "Se7en", 127, 0.5, "David Fincher");

        var state = await Load([movie], StoreOf(EntryFor(KeyB, 807, 127)));

        Assert.NotEqual(RecordLink.IdentityKeyOf(movie), state.Rows.Single().IdentityKey);
        Assert.Equal(KeyB, state.Rows.Single().IdentityKey);
    }

    [Fact]
    public async Task AnEntryThatCannotBePlacedIsReportedRatherThanDropped()
    {
        var state = await Load([Film(807, "Se7en", 127, 0.5)],
            StoreOf(EntryFor(KeyA, 807, 127), EntryFor(KeyB, 999, 100)));

        Assert.Equal(KeyB, Assert.Single(state.Unplaceable).IdentityKey);
        Assert.Equal(1, state.Rows.Count(r => r.Entry is not null));
    }

    /// <summary>
    /// Two entries with different identity keys that publish ONE key: a matched record's key is its id and runtime
    /// and holds no part of the identity key. The map keeps the first, so the second must be NAMED, not dropped.
    /// </summary>
    [Fact]
    public async Task TwoEntriesResolvingToOnePublishedKeyNameTheLoserRatherThanDroppingIt()
    {
        var movie = Film(550, "Fight Club", 139, 0.5);

        var state = await Load([movie], StoreOf(EntryFor(KeyA, 550, 139), EntryFor(KeyB, 550, 139)));

        Assert.Equal(KeyA, state.Rows.Single().Entry?.IdentityKey);
        Assert.Equal(KeyB, Assert.Single(state.Unplaceable).IdentityKey);
    }

    /// <summary>
    /// An entry with several records is placed only when EVERY record is bound. Here the second entry's first record
    /// publishes a key the first entry already holds, while its second record binds cleanly; counting an entry as
    /// placed when any one record binds would leave the colliding correction neither shown nor named.
    /// </summary>
    [Fact]
    public async Task AMultiRecordEntryWithOneCollidingRecordIsNamedEvenThoughItsOtherRecordBinds()
    {
        var multi = new FilmEntry
        {
            IdentityKey = KeyB,
            Records = [new RecordOverride(TmdbId: 550, Runtime: 139), new RecordOverride(TmdbId: 807, Runtime: 127)],
        };

        var state = await Load([Film(550, "Fight Club", 139, 0.5), Film(807, "Se7en", 127, 0.5)],
            StoreOf(EntryFor(KeyA, 550, 139), multi));

        Assert.Equal(KeyB, Assert.Single(state.Unplaceable).IdentityKey);
        Assert.Equal(KeyA, state.Rows.Single(r => r.Movie.I == 550).Entry?.IdentityKey);
        Assert.Equal(KeyB, state.Rows.Single(r => r.Movie.I == 807).Entry?.IdentityKey);
    }

    /// <summary>A prompt on load is forbidden (A52) and would be rejected without a click anyway.</summary>
    [Fact]
    public async Task LoadingAsksTheScriptForStatusAndNeverForTheReconfirmThatPrompts()
    {
        var (context, directory) = Folder(text: StoreOf());
        using (context)
        {
            await new EditorCatalogue(new Source(CatalogueState.Ready, []), directory).LoadAsync();

            Assert.Contains(context.JSInterop.Invocations, i => i.Identifier == "status");
            Assert.DoesNotContain(context.JSInterop.Invocations, i => i.Identifier == "reconfirm");
        }
    }

    [Fact]
    public async Task AFailedCatalogueFetchYieldsNoRowsAndSaysFailed()
    {
        var state = await Load([], state: CatalogueState.Failed);

        Assert.Equal(CatalogueState.Failed, state.Catalogue);
        Assert.Empty(state.Rows);
        Assert.False(state.Editable);
    }

    [Fact]
    public async Task ASyntheticCatalogueIsFlaggedAndNotSilentlyTrusted()
    {
        var state = await Load([Film(550, "Fight Club", 139, 0.99)], meta: CatalogMeta.Empty with { Fixtures = true });

        Assert.True(state.Synthetic);
        Assert.False(state.Editable);
    }

    [Fact]
    public async Task AMissingStoreFileIsAnEmptyStoreAndNotAFailure()
    {
        var state = await Load([Film(807, "Se7en", 127, 0.5)], storeText: null);

        Assert.Equal(StoreReading.NotRead, state.Store);
        Assert.Empty(state.Draft.Store.Films);
        Assert.Equal(RecordStore.CurrentVersion, state.Draft.Store.Version);
        Assert.Single(state.Rows);
        Assert.True(state.Editable);
    }

    [Fact]
    public async Task AMalformedStoreFileIsReportedWithTheReadersOwnMessageAndNothingIsEditable()
    {
        const string broken = "{ this is not json";
        var expected = Assert.Throws<RecordStoreException>(
            () => RecordStoreReader.Read(broken, RecordStoreReader.DefaultFileName));

        var state = await Load([Film(807, "Se7en", 127, 0.5)], broken);

        Assert.Equal(StoreReading.Refused, state.Store);
        Assert.Equal(expected.Message, state.StoreProblem);
        Assert.Empty(state.Rows);
        Assert.False(state.Editable);
    }

    [Fact]
    public async Task ABrowserThatCannotWriteToAFolderSaysSoAndCarriesNoFolder()
    {
        var (context, directory) = Folder(text: null, supported: false, folder: null);
        using (context)
        {
            var state = await new EditorCatalogue(new Source(CatalogueState.Ready, []), directory).LoadAsync();

            Assert.False(state.ApiSupported);
            Assert.Null(state.FolderName);
        }
    }

    [Fact]
    public async Task AGrantedFolderCarriesItsNameAndItsStoreIsRead()
    {
        var (context, directory) = Folder(text: StoreOf());
        using (context)
        {
            var state = await new EditorCatalogue(new Source(CatalogueState.Ready, []), directory).LoadAsync();

            Assert.True(state.ApiSupported);
            Assert.Equal("MovieLibrary", state.FolderName);
            Assert.Equal(StoreReading.Read, state.Store);
        }
    }

    /// <summary>
    /// The 30 records the pipeline matched to nothing are the ones this editor exists for, and the first thing
    /// a correction does is give one an id -- which moves the key the entry will publish and leaves the
    /// published catalogue, the previous build's output, where it was. The hash in an unmatched key is the
    /// first eight characters of its ROW's identity key, which no override can move, so it is the way back.
    /// </summary>
    [Fact]
    public async Task AnUnmatchedRecordWhoseCorrectionNowAssertsAnIdIsStillBoundToItsEntry()
    {
        var movie = Film(0, "Lost Reel", 90, 0, "A. Nobody");
        var key = RecordLink.IdentityKeyOf(movie);

        var state = await Load([movie], StoreOf(EntryFor(key, 807, 90)));

        Assert.Equal(key, state.Rows.Single().IdentityKey);
        Assert.Equal(key, state.Rows.Single().Entry?.IdentityKey);
        Assert.True(state.Rows.Single().Corrected);
        Assert.Empty(state.Unplaceable);
    }

    /// <summary>
    /// And the check is a check. An entry whose key shares nothing with the published key's hash is not this
    /// row's, so it stays unbound and is named: a fallback that bound it anyway would pre-fill the form with
    /// another film's correction and save over it.
    /// </summary>
    [Fact]
    public async Task AnUnmatchedRecordIsNotBoundToAnEntryWhoseHashDisagrees()
    {
        var state = await Load([Film(0, "Lost Reel", 90, 0, "A. Nobody")], StoreOf(EntryFor(KeyB, 807, 90)));

        Assert.Null(state.Rows.Single().Entry);
        Assert.Equal(KeyB, Assert.Single(state.Unplaceable).IdentityKey);
    }

    /// <summary>
    /// A bundle's leftover record is paired with a published record on the runtime it states, because nothing
    /// recomputes an Apple row's identity key from a per-film record. Two published records that fit it equally
    /// well say nothing about which it is, so it binds to neither and is named instead.
    /// </summary>
    [Fact]
    public async Task ABundlesLeftoverRecordIsNotGuessedAtWhenTwoPublishedRecordsFitItEqually()
    {
        var bundle = new FilmEntry
        {
            IdentityKey = KeyB,
            Records = [new RecordOverride(TmdbId: 550, Runtime: 139), new RecordOverride(TmdbId: 807, Runtime: 139)],
        };

        var state = await Load(
            [Film(550, "Fight Club", 139, 0.5), Film(998, "A", 139, 0.5), Film(999, "B", 139, 0.5)],
            StoreOf(bundle));

        Assert.Equal(KeyB, state.Rows.Single(r => r.Movie.I == 550).Entry?.IdentityKey);
        Assert.Null(state.Rows.Single(r => r.Movie.I == 998).Entry);
        Assert.Null(state.Rows.Single(r => r.Movie.I == 999).Entry);
        Assert.Equal(KeyB, Assert.Single(state.Unplaceable).IdentityKey);
    }

    /// <summary>
    /// Pass 3's pool is ONE Apple row's own published records, and this construction is why. The owner
    /// corrected the first film's runtime from 139 to 119 and has not rebuilt: pass 1 misses that record
    /// because a matched published key is {id}-{runtime} and ANY runtime edit moves it, and pass 2 misses
    /// because the bundle's identity key hashed the Apple row and not the film. Tied on the runtime alone over
    /// the whole catalogue, the one published record left at 119 minutes was another film entirely -- so it
    /// took this slot, wore the Corrected badge, pre-filled id 550, and a save on it would have written over
    /// the bundle's real correction and reported a success. Nothing said so, because the slot was claimed.
    /// </summary>
    [Fact]
    public async Task APublishedRecordOfAnotherRowIsNeverBoundToABundleSlotThatMerelySharesItsRuntime()
    {
        const string row = "Fight Club / Se7en";
        var bundle = new FilmEntry
        {
            IdentityKey = KeyB,
            Records = [new RecordOverride(TmdbId: 550, Runtime: 119), new RecordOverride(TmdbId: 807, Runtime: 127)],
        };

        var state = await Load(
            [Film(550, row, 139, 0.5, "David Fincher"), Film(807, row, 127, 0.5, "David Fincher"),
             Film(9552, "The Exorcist", 119, 0.5, "William Friedkin")],
            StoreOf(bundle));

        var stranger = state.Rows.Single(r => r.Movie.I == 9552);
        Assert.Null(stranger.Entry);
        Assert.False(stranger.Corrected);

        // The construction, pinned: the sibling whose published key did not move IS bound, and the slot whose
        // runtime the owner corrected is the one left over.
        Assert.Equal(KeyB, state.Rows.Single(r => r.Movie.I == 807).Entry?.IdentityKey);
        Assert.Null(state.Rows.Single(r => r.Movie.I == 550).Entry);

        // And the correction nobody could place is NAMED, rather than claimed by a film it never came from.
        Assert.Equal(KeyB, Assert.Single(state.Unplaceable).IdentityKey);
    }

    /// <summary>
    /// And the narrowed pool still binds the case pass 3 exists for. The owner gave the first film of this
    /// bundle a new id and has not rebuilt, so its published key moved and the runtime it states is all that is
    /// left to tie it; a stranger published at the same runtime makes that tie ambiguous over the whole
    /// catalogue, which is the ordinary case and not the exotic one -- a runtime no other record in the library
    /// shares is rare. Narrowed to the records the Apple row itself published, named by the sibling pass 1
    /// placed, the tie is unique again and the correction is found.
    /// </summary>
    [Fact]
    public async Task ABundleSlotWhoseIdMovedIsBoundThroughASiblingEvenWhenAStrangerSharesItsRuntime()
    {
        const string row = "Fight Club / Se7en";
        var bundle = new FilmEntry
        {
            IdentityKey = KeyB,
            Records = [new RecordOverride(TmdbId: 550, Runtime: 139), new RecordOverride(TmdbId: 807, Runtime: 127)],
        };

        var state = await Load(
            [Film(901, row, 139, 0.5, "David Fincher"), Film(807, row, 127, 0.5, "David Fincher"),
             Film(9552, "The Exorcist", 139, 0.5, "William Friedkin")],
            StoreOf(bundle));

        var moved = state.Rows.Single(r => r.Movie.I == 901);
        Assert.Equal(KeyB, moved.Entry?.IdentityKey);
        Assert.Equal(1, moved.Position);
        Assert.True(moved.Corrected);

        Assert.Null(state.Rows.Single(r => r.Movie.I == 9552).Entry);
        Assert.Empty(state.Unplaceable);
    }
}

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
}

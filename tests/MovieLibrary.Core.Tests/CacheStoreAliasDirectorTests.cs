using System.Text.Json;
using MovieLibrary.Core.Caching;
using MovieLibrary.Core.Identity;
using MovieLibrary.Core.Models;
using MovieLibrary.Core.Parsing;
using MovieLibrary.Core.Text;
using MovieLibrary.Enrich.Caching;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// The alias path absorbs Apple re-encoding drift: a re-encoded film has a slightly different runtime, so its identity
/// key (which includes the runtime) misses, and an entry with the same folded title and a runtime within two minutes
/// is reused instead of spending a search. Title and runtime cannot tell a re-encode from a remake: the folded title
/// has already moved a trailing year into a hint that is not stored, so <c>Carrie (1976)</c> and <c>Carrie (2013)</c>
/// are one title. Directors can, and they are what <see cref="IdentityKey"/> and the match scorer already weigh.
/// Everything here runs on the owner's real pairs (<see cref="RealFilmPair"/>), identified exactly as
/// <c>MoviePipeline</c> identifies a row.
/// </summary>
public class CacheStoreAliasDirectorTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken Ct = CancellationToken.None;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ml-alias-" + Guid.NewGuid());

    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private CacheStore Store() => new(_dir);

    /// <summary>A sheet row as <c>MoviePipeline</c> identifies it: the real key, folded title, minutes and directors.</summary>
    private sealed record Identity(string Key, string Title, int Minutes, string Directors)
    {
        public static Identity Of(SheetRow row) => Of(row.Title, row.Runtime, row.Directors);

        public static Identity Of(string title, string runtime, string directors)
        {
            var t = TitleNormalizer.Normalize(title);
            var minutes = RuntimeParser.FilmMinutes(runtime);
            var d = TitleNormalizer.NormalizeDirectors(directors);
            return new Identity(IdentityKey.ForFilm(t, minutes, d), t.Value, minutes, d);
        }

        public CacheEntry Entry(int tmdbId) => new(Key, tmdbId, 0.95, Now, false, Title, Minutes, Directors);

        public CacheEntry? Resolve(CacheStore store) => store.Resolve(Key, Title, Minutes, Directors, Now);
    }

    private static (Identity Cached, Identity Arriving) Order(string name, bool secondArrivesFirst)
    {
        var pair = RealFilmPair.Named(name);
        var (first, second) = (Identity.Of(pair.First), Identity.Of(pair.Second));
        return secondArrivesFirst ? (second, first) : (first, second);
    }

    // ---- the data is the real thing: each pair is exactly what the alias scan would have paired ---------

    [Fact]
    public void EveryRealPairShareAFoldedTitleAndRunTheSameOrWithinTwoMinutes()
    {
        Assert.Equal(5, RealFilmPair.All.Count);
        foreach (var pair in RealFilmPair.All)
        {
            var (a, b) = (Identity.Of(pair.First), Identity.Of(pair.Second));
            Assert.Equal(a.Title, b.Title);
            Assert.InRange(Math.Abs(a.Minutes - b.Minutes), 0, 2);
        }
    }

    // ---- the three that are one film still alias: blocking them would cost real requests --------------

    [Theory]
    [InlineData("BladeRunner", false)]
    [InlineData("BladeRunner", true)]
    [InlineData("CaptainAmerica", false)]
    [InlineData("CaptainAmerica", true)]
    [InlineData("Hitman", false)]
    [InlineData("Hitman", true)]
    public void TheSameFilmReEncodedResolvesToTheEntryAlreadyCached(string name, bool secondArrivesFirst)
    {
        var (cached, arriving) = Order(name, secondArrivesFirst);
        var store = Store();
        store.Put(cached.Entry(1000));

        Assert.Equal(1000, arriving.Resolve(store)?.TmdbId);
    }

    [Fact]
    public void BladeRunnerAndItsFinalCutShareOneKeyAndSoNeverReachTheAliasScan()
    {
        // 117 minutes each and one director: the identity key is already the same, so this is an exact hit.
        var (first, second) = Order("BladeRunner", false);
        Assert.Equal(first.Key, second.Key);

        var store = Store();
        store.Put(first.Entry(1000));

        Assert.Equal(1000, second.Resolve(store)?.TmdbId);
        Assert.Single(store.Entries);
    }

    [Theory]
    [InlineData("CaptainAmerica")]   // 125 and 124 minutes
    [InlineData("Hitman")]           // 92 and 94 minutes, a year on one row and none on the other
    public void AReEncodedFilmIsRecordedAsAnAliasThatCarriesTheDirectors(string name)
    {
        var (cached, arriving) = Order(name, false);
        Assert.NotEqual(cached.Key, arriving.Key);   // so this is the alias scan, not an exact hit

        var store = Store();
        store.Put(cached.Entry(1000));
        var hit = arriving.Resolve(store);

        Assert.Equal(cached.Key, hit?.Key);          // the SOURCE entry comes back
        var recorded = Assert.Single(store.Entries, e => e.Key == arriving.Key);
        Assert.Equal((1000, arriving.Minutes, arriving.Directors),
                     (recorded.TmdbId, recorded.RuntimeMinutes, recorded.NormalizedDirectors));
    }

    // ---- the two that are different films do not: this is the corruption -------------------------------

    [Theory]
    [InlineData("Carrie", false)]      // 98 and 99 minutes; Stephen King and Kimberly Peirce
    [InlineData("Carrie", true)]
    [InlineData("Overboard", false)]   // 112 and 112 minutes; Garry Marshall and Rob Greenberg
    [InlineData("Overboard", true)]
    public void ARemakeDoesNotInheritTheOriginalsTmdbIdWhicheverArrivesFirst(string name, bool secondArrivesFirst)
    {
        var (cached, arriving) = Order(name, secondArrivesFirst);
        var store = Store();
        store.Put(cached.Entry(1000));

        Assert.Null(arriving.Resolve(store));
        Assert.Single(store.Entries);   // and it was not recorded as an alias either
    }

    // ---- an entry from before directors were stored is exact-key only -----------------------------------

    [Fact]
    public void AnEntryWrittenBeforeDirectorsWereStoredIsNotAliasable()
    {
        var store = Store();
        store.Put(new CacheEntry("k-old", 348, 0.9, Now, false, "alien", 117));   // the old seven-field shape

        // Everything an alias needs but the entry's directors: same title, one minute apart, a director on the row.
        Assert.Null(store.Resolve("k-new", "alien", 118, "ridley scott", Now));
        Assert.Single(store.Entries);
        // The entry is not unusable, only un-aliasable: its own key still hits.
        Assert.Equal(348, store.Resolve("k-old", "alien", 117, "ridley scott", Now)?.TmdbId);
    }

    [Fact]
    public async Task AnIndexOnDiskFromBeforeDirectorsWereStoredLoadsAndIsNotAliasable()
    {
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(Path.Combine(_dir, "index.json"), """
            [
              {
                "key": "k-old",
                "tmdbId": 348,
                "confidence": 0.9,
                "fetchedUtc": "2026-11-02T12:00:00+00:00",
                "negative": false,
                "normalizedTitle": "alien",
                "runtimeMinutes": 117
              }
            ]
            """, Ct);

        var store = Store();
        await store.LoadAsync(Ct);

        Assert.Null(store.Resolve("k-new", "alien", 118, "ridley scott", Now));
        Assert.Equal(348, store.Resolve("k-old", "alien", 117, "ridley scott", Now)?.TmdbId);
    }

    // ---- a row with no director names nobody: two of them are not demonstrably the same film -------------

    [Fact]
    public void TwoFilmsThatBothLackDirectorsDoNotAlias()
    {
        var store = Store();
        store.Put(new CacheEntry("k-a", 348, 0.9, Now, false, "alien", 117, string.Empty));

        Assert.Null(store.Resolve("k-b", "alien", 118, string.Empty, Now));
    }

    [Fact]
    public void ARowWithNoDirectorsDoesNotAliasToAnEntryThatHasThem()
    {
        var store = Store();
        store.Put(new CacheEntry("k-a", 348, 0.9, Now, false, "alien", 117, "ridley scott"));

        Assert.Null(store.Resolve("k-b", "alien", 118, string.Empty, Now));
    }

    [Fact]
    public void ARowWithDirectorsDoesNotAliasToAnEntryThatHasNone()
    {
        var store = Store();
        store.Put(new CacheEntry("k-a", 348, 0.9, Now, false, "alien", 117, string.Empty));

        Assert.Null(store.Resolve("k-b", "alien", 118, "ridley scott", Now));
    }

    [Fact]
    public void ARowWithNoDirectorsStillHitsItsOwnExactKey()
    {
        var store = Store();
        var film = Identity.Of("Some Obscure Film", "1:35:00", "");
        Assert.Equal("", film.Directors);
        store.Put(film.Entry(500));

        Assert.Equal(500, film.Resolve(store)?.TmdbId);
    }

    // ---- how directors are compared -------------------------------------------------------------------

    [Theory]
    [InlineData("Ethan Coen & Joel Coen")]       // the other way round
    [InlineData("Joel Coen, Ethan Coen")]        // punctuation, not an ampersand
    [InlineData("COEN, ETHAN; joel coen")]       // case, surname first
    public void DirectorsAgreeWhateverOrderCasePunctuationOrAmpersandTheSheetUses(string reEncodedDirectors)
    {
        // Apple's order is not guaranteed stable, so order must not part a film from its own re-encode.
        var cached = Identity.Of("Fargo", "1:38:00", "Joel Coen & Ethan Coen");
        var reEncoded = Identity.Of("Fargo", "1:39:00", reEncodedDirectors);
        Assert.NotEqual(cached.Key, reEncoded.Key);

        var store = Store();
        store.Put(cached.Entry(275));

        Assert.Equal(275, reEncoded.Resolve(store)?.TmdbId);
    }

    [Fact]
    public void ADifferentSetOfDirectorsIsADifferentFilmEvenWhenTheyOverlap()
    {
        // Strict on purpose: a miss costs one search, a wrong merge shows one film's poster for another.
        var store = Store();
        store.Put(Identity.Of("Fargo", "1:38:00", "Joel Coen").Entry(275));

        Assert.Null(Identity.Of("Fargo", "1:39:00", "Joel Coen & Ethan Coen").Resolve(store));
    }

    // ---- films and series share the index and must not alias to each other ------------------------------

    [Fact]
    public void ASeriesLookupNeverAliasesToAFilmOfTwoMinutesOrLess()
    {
        // TvPipeline asks with runtime 0 and no directors.
        var store = Store();
        store.Put(new CacheEntry("k-film", 11, 0.9, Now, false, "the office", 1, "ridley scott"));

        Assert.Null(store.Resolve("k-series", "the office", 0, string.Empty, Now));
    }

    [Fact]
    public void AFilmLookupNeverAliasesToASeriesEntry()
    {
        // TvPipeline stores a series with runtime 0 and no directors.
        var store = Store();
        store.Put(new CacheEntry("k-series", 2316, 0.9, Now, false, "the office", 0, string.Empty));

        Assert.Null(store.Resolve("k-film", "the office", 1, "ridley scott", Now));
    }

    // ---- production only ever alias-resolves against a store that was loaded from disk -----------------

    [Fact]
    public async Task TheDirectorsSurviveSaveAndLoadSoAReEncodeStillAliasesAndARemakeStillDoesNot()
    {
        var (captainAmerica125, captainAmerica124) = Order("CaptainAmerica", false);
        var (carrie1976, carrie2013) = Order("Carrie", false);
        var store = Store();
        store.Put(captainAmerica125.Entry(1771));
        store.Put(carrie1976.Entry(7340));
        await store.SaveAsync(Ct);

        var reloaded = Store();
        await reloaded.LoadAsync(Ct);

        Assert.Equal(1771, captainAmerica124.Resolve(reloaded)?.TmdbId);
        Assert.Null(carrie2013.Resolve(reloaded));

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_dir, "index.json"), Ct));
        var carrie = doc.RootElement.EnumerateArray().Single(e => e.GetProperty("key").GetString() == carrie1976.Key);
        Assert.Equal("king stephen", carrie.GetProperty("normalizedDirectors").GetString());
    }
}

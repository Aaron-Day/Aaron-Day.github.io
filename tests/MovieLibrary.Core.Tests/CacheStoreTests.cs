using System.Text.Json;
using MovieLibrary.Core.Caching;
using MovieLibrary.Enrich.Caching;

namespace MovieLibrary.Core.Tests;

public class CacheStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 0, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ml-cache-" + Guid.NewGuid());

    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private CacheStore New() => new(_dir);

    private static CacheEntry Entry(string key, string title, int runtime, DateTimeOffset fetched,
                                    int? id = 348, bool negative = false)
        => new(key, id, 0.9, fetched, negative, title, runtime);

    [Fact]
    public void ExactKeyHitsWithoutAlias()
    {
        var store = New();
        store.Put(Entry("k1", "alien", 117, Now));
        Assert.Equal(348, store.Resolve("k1", "alien", 117, Now)!.TmdbId);
    }

    [Fact]
    public void RuntimeDriftWithinTwoMinutesResolvesViaAlias()
    {
        var store = New();
        store.Put(Entry("k1", "alien", 117, Now));
        // A re-encode shifted the runtime, so the computed key differs.
        Assert.Equal(348, store.Resolve("k-different", "alien", 118, Now)!.TmdbId);
    }

    [Fact]
    public void CollidingTitlesBeyondTheToleranceDoNotAlias()
    {
        var store = New();
        store.Put(Entry("k-guy", "aladdin", 129, Now));
        Assert.Null(store.Resolve("k-ron", "aladdin", 91, Now)); // 38 minutes apart
    }

    [Fact]
    public void EntriesPastTheRefreshWindowCountAsMisses()
    {
        var store = New();
        store.Put(Entry("k1", "alien", 117, Now.AddDays(-151)));
        Assert.Null(store.Resolve("k1", "alien", 117, Now));
    }

    [Fact]
    public void NegativeEntriesSuppressRefetchForThirtyDays()
    {
        var store = New();
        store.Put(Entry("k1", "obscure", 95, Now.AddDays(-10), id: null, negative: true));

        var hit = store.Resolve("k1", "obscure", 95, Now);
        Assert.NotNull(hit);
        Assert.True(hit!.Negative);

        var stale = New();
        stale.Put(Entry("k2", "obscure", 95, Now.AddDays(-31), id: null, negative: true));
        Assert.Null(stale.Resolve("k2", "obscure", 95, Now));
    }

    [Fact]
    public async Task IndexSurvivesARoundTrip()
    {
        var store = New();
        store.Put(Entry("k1", "alien", 117, Now));
        await store.SaveAsync(CancellationToken.None);

        var reloaded = New();
        await reloaded.LoadAsync(CancellationToken.None);
        Assert.Equal(348, reloaded.Resolve("k1", "alien", 117, Now)!.TmdbId);
    }

    [Fact]
    public async Task PayloadsRoundTripByRelativePath()
    {
        var store = New();
        await store.WritePayloadAsync("movies/348.json", new { id = 348 }, CancellationToken.None);
        var back = await store.ReadPayloadAsync<Dictionary<string, int>>(
            "movies/348.json", CancellationToken.None);
        Assert.Equal(348, back!["id"]);
    }

    // NOT in the brief; added because the controller asked that sorted output be pinned.
    // The index is committed to git: an unsorted write reshuffles the file on every run
    // instead of producing a diff that shows exactly which films were added.
    [Fact]
    public async Task IndexIsWrittenSortedByKeyRegardlessOfInsertionOrder()
    {
        var store = New();
        foreach (var key in new[] { "c3", "a1", "b2" })
            store.Put(Entry(key, "title-" + key, 100, Now));
        await store.SaveAsync(CancellationToken.None);

        using var doc = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(_dir, "index.json")));
        var keys = doc.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("key").GetString()!)
            .ToArray();

        Assert.Equal(new[] { "a1", "b2", "c3" }, keys);
    }

    // ---- Review amendment A-H: requirements the brief's tests did not pin. ----

    // A. Data-corruption path: without the title comparison, any entry within +/-2 minutes matches,
    // so a new film silently inherits an unrelated film's TMDB id, poster and synopsis.
    [Fact]
    public void AliasRequiresTheSameNormalizedTitle()
    {
        var store = New();
        store.Put(Entry("k1", "alien", 117, Now));
        Assert.Null(store.Resolve("k-other", "aliens", 117, Now)); // same runtime, different film
    }

    // B. TMDB forbids caching beyond six months; the alias path must not serve an expired entry.
    [Fact]
    public void AnExpiredEntryIsNotServedViaAlias()
    {
        var store = New();
        store.Put(Entry("k1", "alien", 117, Now.AddDays(-151)));
        Assert.Null(store.Resolve("k-different", "alien", 118, Now)); // alias candidate, but stale
    }

    // C. Positives use the five-month window. Sixty days is past the 30-day negative retry.
    [Fact]
    public void PositiveEntriesUseTheFiveMonthWindowNotTheNegativeRetry()
    {
        var store = New();
        store.Put(Entry("k1", "alien", 117, Now.AddDays(-60)));
        Assert.NotNull(store.Resolve("k1", "alien", 117, Now));
    }

    // D. The alias write-back: the new key must afterwards resolve EXACTLY. The second lookup
    // passes a title and runtime that cannot alias, so only a recorded key can answer it.
    [Fact]
    public void ResolvingViaAliasRecordsTheNewKey()
    {
        var store = New();
        store.Put(Entry("k1", "alien", 117, Now));

        Assert.NotNull(store.Resolve("k-different", "alien", 118, Now));

        Assert.Equal(2, store.Entries.Count);
        var exact = store.Resolve("k-different", "no-such-title", 999, Now);
        Assert.NotNull(exact);
        Assert.Equal(348, exact!.TmdbId);
    }

    // E. A refresh re-Puts the same key; if Put did not overwrite, the cache could never self-heal.
    [Fact]
    public void PutOverwritesAnExistingKey()
    {
        var store = New();
        store.Put(Entry("k1", "alien", 117, Now, id: 111));
        store.Put(Entry("k1", "alien", 117, Now, id: 348));

        Assert.Equal(348, store.Resolve("k1", "alien", 117, Now)!.TmdbId);
        Assert.Single(store.Entries);
    }

    // F. The very first run on a clean checkout has no index.json and no payloads.
    [Fact]
    public async Task LoadAsyncOnAMissingIndexIsAnEmptyCache()
    {
        var store = New();
        await store.LoadAsync(CancellationToken.None);
        Assert.Empty(store.Entries);
    }

    [Fact]
    public async Task ReadPayloadAsyncOnAMissingFileReturnsNull()
    {
        var store = New();
        Assert.Null(await store.ReadPayloadAsync<Dictionary<string, int>>(
            "movies/404.json", CancellationToken.None));
    }

    // G. Exactly +/-2 minutes, in both directions. Each probe gets a fresh store so a hit's
    // write-back cannot widen the window for the next one.
    [Fact]
    public void AliasToleranceIsExactlyTwoMinutesInBothDirections()
    {
        CacheEntry? Probe(int queryRuntime)
        {
            var store = New();
            store.Put(Entry("k1", "alien", 117, Now));
            return store.Resolve("k-probe", "alien", queryRuntime, Now);
        }

        Assert.NotNull(Probe(119)); // +2: a longer query matches a shorter entry
        Assert.NotNull(Probe(115)); // -2
        Assert.Null(Probe(120));    // +3
        Assert.Null(Probe(114));    // -3
    }

    // H. enrich-cache/ is committed. A minified index is one giant line, so every run would be
    // an unreadable whole-file diff instead of one line per added film.
    [Fact]
    public async Task IndexIsWrittenIndentedForReadableDiffs()
    {
        var store = New();
        foreach (var key in new[] { "a1", "b2", "c3" })
            store.Put(Entry(key, "title-" + key, 100, Now));
        await store.SaveAsync(CancellationToken.None);

        var lines = await File.ReadAllLinesAsync(Path.Combine(_dir, "index.json"));

        Assert.Equal(3, lines.Count(l => l.TrimStart().StartsWith("\"key\"", StringComparison.Ordinal)));
    }
}

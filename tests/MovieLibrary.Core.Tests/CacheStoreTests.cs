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
}

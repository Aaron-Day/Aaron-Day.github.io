using MovieLibrary.Core.Caching;
using MovieLibrary.Enrich.Caching;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// Pins the empty-title guard in <see cref="CacheStore.Resolve"/> where it lives, rather than only through the
/// pipeline: a title that folds to "" (a pure parenthetical or pure punctuation) names no film, so it must never
/// alias to another, though its own exact key must still hit.
/// </summary>
public class CacheStoreEmptyTitleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    // Put and Resolve touch only memory, so this root is never created on disk.
    private static CacheStore Store() => new(Path.Combine(Path.GetTempPath(), "ml-never-created-" + Guid.NewGuid()));

    private const string Director = "ridley scott";

    private static CacheEntry Entry(string key, string folded, int runtime)
        => new(key, 100, 0.9, Now, false, folded, runtime, Director);

    [Fact]
    public void ATitleThatFoldsToNothingNeverAliasesToAnotherWithinTheRuntimeWindow()
    {
        var store = Store();
        store.Put(Entry("k-bang", "", 95));

        Assert.Null(store.Resolve("k-question", "", 96, Director, Now)); // empty title, one minute apart: not the same film
    }

    [Fact]
    public void ATitleThatFoldsToNothingStillHitsItsOwnExactKey()
    {
        var store = Store();
        store.Put(Entry("k-bang", "", 95));

        Assert.Equal(100, store.Resolve("k-bang", "", 95, Director, Now)?.TmdbId);
    }

    [Fact]
    public void AnOrdinaryTitleStillAliases()
    {
        var store = Store();
        store.Put(Entry("k-alien", "alien", 117));

        // The guard must not be over-broad. On an alias hit Resolve returns the SOURCE entry.
        Assert.Equal("k-alien", store.Resolve("k-alien-reencoded", "alien", 118, Director, Now)?.Key);
    }
}

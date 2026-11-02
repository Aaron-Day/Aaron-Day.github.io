using MovieLibrary.Core.Caching;
using MovieLibrary.Enrich.Caching;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// Task 17 Step D, and the hazards around it. Every TMDB response already paid for is in the cache, and the first full run
/// costs thousands of requests over a budget of 40 a run, so a failure after the fetches must not throw them away. Saving
/// has its own ways to do harm: saving an index that was never loaded overwrites the real one, and saving under a
/// cancelled token writes nothing.
/// </summary>
public class EnrichRunCacheTests
{
    private static readonly (string Title, string Runtime, string Directors, string Genre)[] Library =
    [
        ("Alien", "1:56:00", "Ridley Scott", "Sci-Fi"),
        ("Brazil", "2:22:00", "Terry Gilliam", "Sci-Fi"),
        ("Casablanca", "1:42:00", "Michael Curtiz", "Drama"),
    ];

    /// <summary>The first <paramref name="count"/> films of a library TMDB has never heard of: one search each, none found.</summary>
    private static RunHarness Films(int count)
    {
        var h = new RunHarness();
        h.MoviesCsv = RunLibrary.Movies(Library[..count]);
        h.Tmdb = req => req.RequestUri!.AbsolutePath == "/3/search/movie"
            ? TestPipeline.Json(TestPipeline.EmptyResults)
            : TestPipeline.NotFound();
        return h;
    }

    private static async Task SeedAsync(RunHarness h, string key)
    {
        var seed = new CacheStore(h.CacheDir);
        seed.Put(new CacheEntry(key, 275, 0.9, RunHarness.T0, false, "fargo", 98));
        await seed.SaveAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ACancelledRunSavesWhatItHadSpent()
    {
        using var h = Films(3);
        using var cts = new CancellationTokenSource();
        var search = h.Tmdb;
        var requests = 0;
        h.Tmdb = req => { if (++requests == 2) cts.Cancel(); return search(req); };   // the user stops it during the second film

        var result = await h.RunAsync(cts.Token);

        Assert.Equal(1, result.ExitCode);
        Assert.False(result.Deployable);
        Assert.Contains("Cancelled", h.Err.ToString());
        Assert.Contains("deployable=false", h.GitHubOutput);
        Assert.NotEmpty(await h.SavedCacheAsync());                 // the first film, whose lookup was paid for
        Assert.False(h.Wrote("meta.json"));
    }

    [Fact]
    public async Task ARunThatSucceedsSavesTheCacheToo()
    {
        using var h = Films(2);

        var result = await h.RunAsync();

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(2, (await h.SavedCacheAsync()).Count);
    }

    [Fact]
    public async Task ACacheThatCannotBeReadIsReportedAndLeftExactlyAsItWas()
    {
        using var h = Films(2);
        Directory.CreateDirectory(h.CacheDir);
        var index = Path.Combine(h.CacheDir, "index.json");
        await File.WriteAllTextAsync(index, "{ this was cut off by a crash");

        var result = await h.RunAsync();

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("index.json", h.Err.ToString());
        Assert.Contains("Delete the cache index", h.Err.ToString());
        Assert.Equal("{ this was cut off by a crash", await File.ReadAllTextAsync(index));   // never overwritten with an empty one
        Assert.Empty(h.TmdbHandler.Requests);
        Assert.False(h.Wrote("meta.json"));
    }

    [Fact]
    public async Task AForcedRunThatDiesBeforeSpendingAnythingLeavesTheExistingIndexAlone()
    {
        using var h = Films(2);
        await SeedAsync(h, "kept");
        h.Options = h.Options with { ForceRefresh = true };
        h.Tmdb = _ => new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized);

        var result = await h.RunAsync();

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(["kept"], (await h.SavedCacheAsync()).Select(e => e.Key));
    }

    [Fact]
    public async Task ARunStartsFromTheSavedIndexUnlessForced()
    {
        using var h = Films(2);
        await SeedAsync(h, "kept");

        await h.RunAsync();

        var keys = (await h.SavedCacheAsync()).Select(e => e.Key).ToList();
        Assert.Contains("kept", keys);
        Assert.Equal(3, keys.Count);
    }

    [Fact]
    public async Task AForcedRunIgnoresTheSavedIndexAndReplacesIt()
    {
        using var h = Films(2);
        await SeedAsync(h, "kept");
        h.Options = h.Options with { ForceRefresh = true };

        await h.RunAsync();

        var keys = (await h.SavedCacheAsync()).Select(e => e.Key).ToList();
        Assert.DoesNotContain("kept", keys);
        Assert.Equal(2, keys.Count);
    }

    // ---- saving can itself fail ----------------------------------------------------------------------

    private static void MakeTheIndexUnwritable(RunHarness h)
        => Directory.CreateDirectory(Path.Combine(h.CacheDir, "index.json"));      // a directory where the file should go

    /// <summary>
    /// A lost cache costs requests on a later run, not correctness, so it must not withhold a catalogue that was
    /// published in full. Failing the run would do real harm: the re-run starts from the same index the save could not
    /// replace and pays for every response again.
    /// </summary>
    [Fact]
    public async Task ACacheThatCannotBeSavedStillDeploysTheCatalogueItPublished()
    {
        using var h = Films(2);
        MakeTheIndexUnwritable(h);

        var result = await h.RunAsync();

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Deployable);
        Assert.True(h.Wrote("meta.json"));
        Assert.Contains("deployable=true", h.GitHubOutput);
    }

    /// <summary>Not failing the run is exactly why it has to be loud: once on the console, once where the owner reads.</summary>
    [Fact]
    public async Task ACacheThatCannotBeSavedIsReportedOnTheConsoleAndTheSummary()
    {
        using var h = Films(2);
        MakeTheIndexUnwritable(h);

        await h.RunAsync();

        Assert.Contains("WARNING: the cache could not be saved", h.Err.ToString());
        Assert.Equal(1, h.Err.ToString().Split("cache could not be saved").Length - 1);   // once, not once as WARNING and once as ALSO
        Assert.Contains("The TMDB cache was not saved.", h.Summary);
        Assert.Contains("cache could not be saved", h.Summary);
    }

    /// <summary>The healthy run must not carry the banner, or it stops meaning anything.</summary>
    [Fact]
    public async Task ARunThatSavesItsCacheSaysNothingAboutTheCache()
    {
        using var h = Films(2);

        var result = await h.RunAsync();

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("cache", h.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WARNING", h.Err.ToString());
    }

    [Fact]
    public async Task ACacheSaveFailureDoesNotHideTheErrorThatCameFirst()
    {
        using var h = Films(2);
        MakeTheIndexUnwritable(h);
        h.Tmdb = _ => new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized);

        var result = await h.RunAsync();

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("TMDB rejected the credential", h.Err.ToString());
        Assert.Contains("cache could not be saved", h.Err.ToString());
    }

    // ---- the lost cache has to reach the deploy gate, not just the summary page -----------------------

    /// <summary>
    /// The exit code and <c>deployable</c> deliberately do not move for a lost cache, which left a line on a summary
    /// page as the only durable sign of it. A runner with a disk or permissions problem loses its cache on every run
    /// and re-spends the whole budget each time, and nothing forces anyone to read that page.
    /// </summary>
    [Fact]
    public async Task ACacheThatCannotBeSavedIsOnTheOutputsFileWhereTheWorkflowCanActOnIt()
    {
        using var h = Films(2);
        MakeTheIndexUnwritable(h);

        await h.RunAsync();

        Assert.Contains("cache_saved=false", h.GitHubOutput);
        Assert.Contains("deployable=true", h.GitHubOutput);         // and it is still not a reason to withhold it
    }

    /// <summary>The healthy run must say so, or the key means nothing to read.</summary>
    [Fact]
    public async Task ARunThatSavedItsCacheSaysCacheSavedTrue()
    {
        using var h = Films(2);

        await h.RunAsync();

        Assert.Contains("cache_saved=true", h.GitHubOutput);
        Assert.DoesNotContain("cache_saved=false", h.GitHubOutput);
    }

    /// <summary>It is written on every outcome, the failures included, like the other two keys.</summary>
    [Fact]
    public async Task ARunThatFailedAndAlsoLostItsCacheSaysSoOnTheOutputsFile()
    {
        using var h = Films(2);
        MakeTheIndexUnwritable(h);
        h.Tmdb = _ => new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized);

        await h.RunAsync();

        Assert.Contains("cache_saved=false", h.GitHubOutput);
        Assert.Contains("deployable=false", h.GitHubOutput);
    }

    /// <summary>A failure that never reached the cache lost nothing, so the key stays true and says only one thing.</summary>
    [Fact]
    public async Task AFailureBeforeTheCacheWasEverTouchedStillSaysCacheSavedTrue()
    {
        using var h = Films(2);
        h.SheetOverride = _ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);

        await h.RunAsync();

        Assert.Contains("cache_saved=true", h.GitHubOutput);
        Assert.Contains("deployable=false", h.GitHubOutput);
    }
}

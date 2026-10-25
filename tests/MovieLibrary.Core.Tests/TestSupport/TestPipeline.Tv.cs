using MovieLibrary.Core.Throttling;
using MovieLibrary.Enrich.Caching;
using MovieLibrary.Enrich.Pipeline;
using MovieLibrary.Enrich.Tmdb;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// The TV half of <see cref="TestPipeline"/>: a <see cref="TvPipeline"/> over a real <see cref="TmdbClient"/>
/// whose <see cref="HttpClient"/> is backed by a <see cref="StubHandler"/>, so nothing here can reach the network.
/// </summary>
internal static partial class TestPipeline
{
    /// <summary>The pipeline plus what a test may want to inspect or tamper with: the cache, the requests, its directory.</summary>
    public sealed record TvHarness(TvPipeline Pipeline, CacheStore Cache, StubHandler Handler, string CacheDir);

    /// <summary>TMDB knows one series, <paramref name="seriesId"/>, and returns it for whatever name is searched. Unlimited budget.</summary>
    public static TvPipeline Tv(int seriesId, (int Season, string[] Episodes)[] seasons)
        => CreateTv(budget: 0, new FakeTv
        {
            AnyQuery = new FakeShow(seriesId, "Stub series", [.. seasons.Select(s => new FakeSeason(s.Season, s.Episodes))])
        }.Respond).Pipeline;

    /// <summary>Every series search comes back empty. Unlimited budget.</summary>
    public static TvPipeline TvWithNoResults() => CreateTv(budget: 0, new FakeTv().Respond).Pipeline;

    /// <param name="clock">Defaults to the frozen <see cref="Now"/>; pass a mutable one to age the cache.</param>
    /// <param name="shareCacheWith">
    /// Reuse another harness's <see cref="CacheStore"/> and directory, so a second run has a fresh client and
    /// budget (a new "job") over the cache the first one left behind.
    /// </param>
    /// <param name="freshIndex">
    /// With <paramref name="shareCacheWith"/>: keep its directory (payload files) but start a new, empty index,
    /// as a run does when <c>index.json</c> was lost or never saved.
    /// </param>
    public static TvHarness CreateTv(
        int budget, Func<HttpRequestMessage, HttpResponseMessage> respond,
        Func<DateTimeOffset>? clock = null, TvHarness? shareCacheWith = null, bool freshIndex = false)
    {
        clock ??= () => Now;
        var handler = new StubHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.themoviedb.org/") };

        // Frozen clock, so a generous limiter: a tight one would make the stub run wait in real time.
        var tmdb = new TmdbClient(http, "eyJ.stub.token",
            new SlidingWindowRateLimiter(100_000, TimeSpan.FromSeconds(1)),
            new RequestBudget(budget), clock);

        string dir;
        CacheStore cache;
        if (shareCacheWith is null)
        {
            dir = Path.Combine(Path.GetTempPath(), "ml-pipeline-" + Guid.NewGuid());
            lock (TempDirs) TempDirs.Add(dir);
            cache = new CacheStore(dir);
        }
        else
        {
            dir = shareCacheWith.CacheDir;
            cache = freshIndex ? new CacheStore(dir) : shareCacheWith.Cache;
        }

        return new TvHarness(new TvPipeline(tmdb, cache, clock), cache, handler, dir);
    }
}

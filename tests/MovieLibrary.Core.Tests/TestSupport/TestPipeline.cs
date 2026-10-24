using System.Net;
using System.Text;
using MovieLibrary.Core.Throttling;
using MovieLibrary.Enrich.Caching;
using MovieLibrary.Enrich.Pipeline;
using MovieLibrary.Enrich.Tmdb;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// Builds a <see cref="MoviePipeline"/> over a real <see cref="TmdbClient"/> whose
/// <see cref="HttpClient"/> is backed by a <see cref="StubHandler"/>, so nothing here can reach the
/// network. The cache is a real <see cref="CacheStore"/> on a throwaway temp directory.
/// </summary>
internal static class TestPipeline
{
    /// <summary>The pipeline plus what a test may want to inspect or tamper with: the cache, the requests, its directory.</summary>
    public sealed record Harness(MoviePipeline Pipeline, CacheStore Cache, StubHandler Handler, string CacheDir);

    public static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    public const string EmptyResults = """{"results":[]}""";

    private static readonly List<string> TempDirs = [];

    static TestPipeline()
    {
        // Factories return a bare MoviePipeline that tests never dispose, so sweep at exit.
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            lock (TempDirs)
                foreach (var dir in TempDirs)
                    try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { /* best effort */ }
        };
    }

    /// <summary>Every search comes back empty and nothing else exists. Unlimited budget.</summary>
    public static MoviePipeline WithNoTmdbResults()
        => Create(budget: 0, req => IsSearch(req) ? Json(EmptyResults) : NotFound()).Pipeline;

    /// <summary>
    /// TMDB knows <paramref name="tmdbId"/> (details only) but any search throws, so a pipeline that
    /// searches when it should not fails the test instead of quietly taking a different path.
    /// </summary>
    public static MoviePipeline ThatFailsIfSearched(int tmdbId)
        => Create(budget: 0, req =>
        {
            if (IsSearch(req))
                throw new Xunit.Sdk.XunitException($"Unexpected TMDB search: {req.RequestUri}");

            return req.RequestUri!.AbsolutePath == $"/3/movie/{tmdbId}"
                ? Json($$"""
                    {"id":{{tmdbId}},"title":"Stub film","overview":"Stub overview.","tagline":"Stub tagline.",
                     "runtime":91,"release_date":"1978-10-25","vote_average":7.5,
                     "poster_path":"/poster.jpg","backdrop_path":"/backdrop.jpg"}
                    """)
                : NotFound();
        }).Pipeline;

    /// <summary>Searches come back empty, but only <paramref name="budget"/> requests may go out.</summary>
    public static MoviePipeline WithBudget(int budget)
        => Create(budget, req => IsSearch(req) ? Json(EmptyResults) : NotFound()).Pipeline;

    /// <param name="clock">Defaults to the frozen <see cref="Now"/>; pass a mutable one to age the cache.</param>
    public static Harness Create(
        int budget, Func<HttpRequestMessage, HttpResponseMessage> respond, Func<DateTimeOffset>? clock = null)
    {
        clock ??= () => Now;
        var handler = new StubHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.themoviedb.org/") };

        // Frozen clock, so a generous limiter: a tight one would make the stub run wait in real time.
        var tmdb = new TmdbClient(http, "eyJ.stub.token",
            new SlidingWindowRateLimiter(100_000, TimeSpan.FromSeconds(1)),
            new RequestBudget(budget), clock);

        var dir = Path.Combine(Path.GetTempPath(), "ml-pipeline-" + Guid.NewGuid());
        lock (TempDirs) TempDirs.Add(dir);
        var cache = new CacheStore(dir);

        return new Harness(new MoviePipeline(tmdb, cache, clock), cache, handler, dir);
    }

    public static bool IsSearch(HttpRequestMessage req) => req.RequestUri!.AbsolutePath == "/3/search/movie";

    public static HttpResponseMessage Json(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage NotFound() => new(HttpStatusCode.NotFound);
}

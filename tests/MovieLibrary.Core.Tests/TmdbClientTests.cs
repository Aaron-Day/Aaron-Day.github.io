using System.Net;
using MovieLibrary.Core.Throttling;
using MovieLibrary.Enrich.Tmdb;

namespace MovieLibrary.Core.Tests;

public class TmdbClientTests
{
    // Nested rather than `file`-scoped: C# rejects a file-local type in the signature of
    // Build() below (CS9051), even though Build is private.
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private static readonly DateTimeOffset T0 = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private static (TmdbClient Client, StubHandler Handler) Build(
        string token, Func<HttpRequestMessage, HttpResponseMessage> respond, int budget = 0)
    {
        var handler = new StubHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.themoviedb.org/") };
        var client = new TmdbClient(http, token,
            new SlidingWindowRateLimiter(40, TimeSpan.FromSeconds(10)),
            new RequestBudget(budget), () => T0);
        return (client, handler);
    }

    [Fact]
    public async Task V4TokenIsSentAsBearerHeaderAndNotInTheUrl()
    {
        var (client, handler) = Build("eyJhbGciOiJIUzI1NiJ9.a.b",
            _ => Json("""{"results":[]}"""));

        await client.SearchMoviesAsync("Alien", 1979, CancellationToken.None);

        var req = handler.Requests.Single();
        Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
        Assert.DoesNotContain("api_key", req.RequestUri!.Query);
    }

    [Fact]
    public async Task V3KeyIsSentAsQueryParameter()
    {
        var (client, handler) = Build("0123456789abcdef0123456789abcdef",
            _ => Json("""{"results":[]}"""));

        await client.SearchMoviesAsync("Alien", null, CancellationToken.None);

        var req = handler.Requests.Single();
        Assert.Null(req.Headers.Authorization);
        Assert.Contains("api_key=0123456789abcdef0123456789abcdef", req.RequestUri!.Query);
    }

    [Fact]
    public async Task SearchMapsResultsToCandidates()
    {
        var (client, _) = Build("eyJ.a.b", _ => Json("""
            {"results":[{"id":348,"title":"Alien","release_date":"1979-05-25","popularity":42.5}]}
            """));

        var results = await client.SearchMoviesAsync("Alien", null, CancellationToken.None);

        var c = Assert.Single(results);
        Assert.Equal(348, c.TmdbId);
        Assert.Equal(1979, c.Year);
        Assert.Equal(42.5, c.Popularity);
    }

    [Fact]
    public async Task ExhaustedBudgetReturnsEmptyWithoutCallingTmdb()
    {
        var (client, handler) = Build("eyJ.a.b", _ => Json("""{"results":[]}"""), budget: 1);

        await client.SearchMoviesAsync("First", null, CancellationToken.None);
        var second = await client.SearchMoviesAsync("Second", null, CancellationToken.None);

        Assert.Empty(second);
        Assert.True(client.BudgetExhausted);
        Assert.Single(handler.Requests); // the second call never went out
    }

    [Fact]
    public async Task RetriesOnTooManyRequests()
    {
        var calls = 0;
        var (client, handler) = Build("eyJ.a.b", _ =>
        {
            calls++;
            if (calls == 1)
            {
                var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                r.Headers.Add("Retry-After", "0");
                return r;
            }
            return Json("""{"results":[{"id":1,"title":"X","release_date":"2000-01-01","popularity":1}]}""");
        });

        var results = await client.SearchMoviesAsync("X", null, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(2, handler.Requests.Count);
    }

    // Amendment 2 (D, E): pins for the 404 handling and the per-attempt limiter pair.

    private static HttpResponseMessage TooManyRequestsRetryAfterZero()
    {
        var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        r.Headers.Add("Retry-After", "0");
        return r;
    }

    [Fact]
    public async Task MissingResourceReturnsNullAndIsNotRetried()
    {
        var (client, handler) = Build("eyJ.a.b", _ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var season = await client.GetSeasonAsync(1399, 99, CancellationToken.None);

        Assert.Null(season);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task EveryAttemptIncludingRetriesIsRecordedWithTheRateLimiter()
    {
        var calls = 0;
        var limiter = new SlidingWindowRateLimiter(2, TimeSpan.FromSeconds(10));
        var handler = new StubHandler(_ =>
            ++calls == 1 ? TooManyRequestsRetryAfterZero() : Json("""{"results":[]}"""));
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.themoviedb.org/") };
        var client = new TmdbClient(http, "eyJ.a.b", limiter, new RequestBudget(0), () => T0);

        await client.SearchMoviesAsync("X", null, CancellationToken.None); // 429, then 200

        Assert.Equal(2, handler.Requests.Count);
        // Both attempts were recorded at T0, so a 2-per-window limiter is now full.
        Assert.True(limiter.DelayBefore(T0) > TimeSpan.Zero);
    }

    [Fact]
    public async Task RetryWaitsForTheRateLimiterBeforeSending()
    {
        var calls = 0;
        // One request per hour with a frozen clock: the retry must be told to wait ~1h.
        var limiter = new SlidingWindowRateLimiter(1, TimeSpan.FromHours(1));
        var handler = new StubHandler(_ =>
            ++calls == 1 ? TooManyRequestsRetryAfterZero() : Json("""{"results":[]}"""));
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.themoviedb.org/") };
        var client = new TmdbClient(http, "eyJ.a.b", limiter, new RequestBudget(0), () => T0);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.SearchMoviesAsync("X", null, cts.Token));

        Assert.Single(handler.Requests); // the retry was still waiting on the limiter
    }
}

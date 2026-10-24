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

    // ---- Amendment 3 (G-K): per-attempt budget, limiter contract, token hygiene, retries ----

    private static HttpResponseMessage Status(HttpStatusCode code)
    {
        var r = new HttpResponseMessage(code);
        r.Headers.Add("Retry-After", "0"); // so a retry never really waits
        return r;
    }

    private static HttpResponseMessage NotJson200()
    {
        var r = Json("not json"); // a proxy or captive-portal page behind a 200
        r.Headers.Add("Retry-After", "0");
        return r;
    }

    private static (TmdbClient Client, StubHandler Handler) BuildWith(
        string token, Func<HttpRequestMessage, HttpResponseMessage> respond,
        SlidingWindowRateLimiter limiter, Func<DateTimeOffset> clock, int budget = 0)
    {
        var handler = new StubHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.themoviedb.org/") };
        return (new TmdbClient(http, token, limiter, new RequestBudget(budget), clock), handler);
    }

    private const string EverythingBody =
        """{"results":[],"crew":[],"id":348,"title":"Alien","vote_average":8.1,"season_number":1,"episodes":[]}""";

    [Fact]
    public async Task BudgetIsConsumedPerAttemptAndRunsOutMidRetry()
    {
        var (client, handler) = Build("eyJ.a.b", _ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            r.Headers.Add("Retry-After", "0");
            return r;
        }, budget: 2);

        var results = await client.SearchMoviesAsync("X", null, CancellationToken.None);

        Assert.Empty(results);
        Assert.True(client.BudgetExhausted);
        Assert.Equal(2, handler.Requests.Count);   // two attempts, not one call
        Assert.Equal(2, client.RequestsUsed);
    }

    [Fact]
    public async Task RecordsTheTimeAfterTheWaitNotTheTimeBeforeIt()
    {
        var t = T0;
        var limiter = new SlidingWindowRateLimiter(1, TimeSpan.FromSeconds(10));
        // Each read moves the clock on a second. The client reads it twice per attempt: once
        // for DelayBefore (T0+1s) and once for Record (T0+2s), so the stamp must be T0+2s.
        var (client, _) = BuildWith("eyJ.a.b", _ => Json("""{"results":[]}"""),
            limiter, () => t += TimeSpan.FromSeconds(1));

        await client.SearchMoviesAsync("X", null, CancellationToken.None);

        // A pre-wait stamp (T0+1s) would leave only 9s here.
        Assert.Equal(TimeSpan.FromSeconds(10), limiter.DelayBefore(T0.AddSeconds(2)));
    }

    [Fact]
    public async Task TheFirstAttemptOfACallWaitsForTheRateLimiter()
    {
        var limiter = new SlidingWindowRateLimiter(1, TimeSpan.FromHours(1));
        var (client, handler) = BuildWith("eyJ.a.b", _ => Json("""{"results":[]}"""), limiter, () => T0);
        await client.SearchMoviesAsync("First", null, CancellationToken.None);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.SearchMoviesAsync("Second", null, cts.Token));

        Assert.Single(handler.Requests); // the second call never went out
    }

    [Fact]
    public async Task V4TokenNeverAppearsInAnyUrl()
    {
        const string token = "eyJhbGciOiJIUzI1NiJ9.SECRETPAYLOAD.SIGNATURE";
        var (client, handler) = Build(token, _ => Json(EverythingBody));

        await client.SearchMoviesAsync("Alien", 1979, CancellationToken.None);
        await client.GetMovieAsync(348, CancellationToken.None);
        await client.GetDirectorsAsync(348, CancellationToken.None);
        await client.SearchSeriesAsync("Alien", CancellationToken.None);
        await client.GetSeasonAsync(1399, 1, CancellationToken.None);

        Assert.Equal(5, handler.Requests.Count);
        foreach (var req in handler.Requests)
        {
            Assert.Equal(token, req.Headers.Authorization!.Parameter);
            Assert.DoesNotContain(token, req.RequestUri!.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("SECRETPAYLOAD", req.RequestUri!.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task V3KeyOnANonSearchCallStartsTheQueryString()
    {
        var (client, handler) = Build("0123456789abcdef0123456789abcdef", _ => Json(EverythingBody));

        await client.GetMovieAsync(348, CancellationToken.None);

        var req = handler.Requests.Single();
        Assert.Equal("/3/movie/348", req.RequestUri!.AbsolutePath);
        Assert.Equal("?api_key=0123456789abcdef0123456789abcdef", req.RequestUri!.Query);
    }

    [Fact]
    public async Task PersistentServerErrorThrowsAfterExactlyFiveAttempts()
    {
        var (client, handler) = Build("eyJ.a.b", _ => Status(HttpStatusCode.ServiceUnavailable));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.SearchMoviesAsync("X", null, CancellationToken.None));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
        Assert.Equal(5, handler.Requests.Count);
    }

    [Fact]
    public async Task UnauthorizedThrowsImmediatelyWithoutRetrying()
    {
        var (client, handler) = Build("eyJ.a.b", _ => Status(HttpStatusCode.Unauthorized));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.SearchMoviesAsync("X", null, CancellationToken.None));

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ServerErrorFollowedBySuccessSucceeds()
    {
        var calls = 0;
        var (client, handler) = Build("eyJ.a.b", _ => ++calls == 1
            ? Status(HttpStatusCode.InternalServerError)
            : Json("""{"results":[{"id":1,"title":"X","release_date":"2000-01-01","popularity":1}]}"""));

        var results = await client.SearchMoviesAsync("X", null, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task TransportFaultIsRetriedThenSucceeds()
    {
        var calls = 0;
        var (client, handler) = Build("eyJ.a.b", _ => ++calls == 1
            ? throw new HttpRequestException("connection reset")
            : Json("""{"results":[{"id":1,"title":"X","release_date":"2000-01-01","popularity":1}]}"""));

        var results = await client.SearchMoviesAsync("X", null, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task TimeoutIsRetriedThenSucceeds()
    {
        var calls = 0;
        var (client, handler) = Build("eyJ.a.b", _ => ++calls == 1
            ? throw new TaskCanceledException("timed out", new TimeoutException())
            : Json("""{"results":[{"id":1,"title":"X","release_date":"2000-01-01","popularity":1}]}"""));

        var results = await client.SearchMoviesAsync("X", null, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task NonJsonOkBodyIsRetriedThenSucceeds()
    {
        var calls = 0;
        var (client, handler) = Build("eyJ.a.b", _ => ++calls == 1
            ? NotJson200()
            : Json("""{"results":[{"id":1,"title":"X","release_date":"2000-01-01","popularity":1}]}"""));

        var results = await client.SearchMoviesAsync("X", null, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task PersistentBadBodyEndsAsHttpRequestExceptionKeepingTheInnerCause()
    {
        var (client, handler) = Build("eyJ.a.b", _ => NotJson200());

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.SearchMoviesAsync("X", null, CancellationToken.None));

        Assert.IsAssignableFrom<System.Text.Json.JsonException>(ex.InnerException);
        Assert.Equal(5, handler.Requests.Count);
    }

    [Fact]
    public async Task CallerCancellationIsNotRetried()
    {
        using var cts = new CancellationTokenSource();
        var (client, handler) = Build("eyJ.a.b", _ =>
        {
            cts.Cancel();
            throw new TaskCanceledException();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.SearchMoviesAsync("X", null, cts.Token));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task CallerCancellationOnTheFinalAttemptStaysACancellation()
    {
        // On the final attempt a swallowed cancellation would be rethrown as HttpRequestException.
        // Earlier attempts cannot show this: the backoff Task.Delay(ct) rethrows cancellation anyway.
        using var cts = new CancellationTokenSource();
        var calls = 0;
        var (client, handler) = Build("eyJ.a.b", _ =>
        {
            if (++calls < 5) return Status(HttpStatusCode.ServiceUnavailable);
            cts.Cancel();
            throw new TaskCanceledException();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.SearchMoviesAsync("X", null, cts.Token));

        Assert.Equal(5, handler.Requests.Count);
    }

    [Fact]
    public async Task AbsurdRetryAfterIsClampedInsteadOfStallingOrCrashingTheWait()
    {
        // int.MaxValue seconds is ~68 years: unclamped, Task.Delay throws ArgumentOutOfRangeException.
        // Clamped, the retry is still waiting when the 100 ms cancellation lands.
        var (client, handler) = Build("eyJ.a.b", _ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            r.Headers.Add("Retry-After", int.MaxValue.ToString());
            return r;
        });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.SearchMoviesAsync("X", null, cts.Token));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task TokenIsTrimmedBeforeItIsSent()
    {
        var (v4, v4Handler) = Build("  eyJ.a.b\r\n", _ => Json("""{"results":[]}"""));
        var (v3, v3Handler) = Build("  0123456789abcdef0123456789abcdef\n", _ => Json("""{"results":[]}"""));

        await v4.SearchMoviesAsync("X", null, CancellationToken.None);
        await v3.SearchMoviesAsync("X", null, CancellationToken.None);

        Assert.Equal("eyJ.a.b", v4Handler.Requests.Single().Headers.Authorization!.Parameter);
        Assert.EndsWith("&api_key=0123456789abcdef0123456789abcdef",
            v3Handler.Requests.Single().RequestUri!.Query);
    }
}

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
}

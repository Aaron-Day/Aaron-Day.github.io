using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MovieLibrary.Core.Matching;
using MovieLibrary.Core.Throttling;
using MovieLibrary.Core.Tmdb;

namespace MovieLibrary.Enrich.Tmdb;

/// <summary>
/// Thin TMDB client: budget first, then rate limit, then send with backoff. Transport faults,
/// timeouts, a malformed body, 429 and 5xx are retried; 404 is a null result; any other status,
/// and a caller-requested cancellation, are not retried.
/// </summary>
/// <param name="http">
/// <see cref="HttpClient.BaseAddress"/> must be set <b>with a trailing slash</b>
/// (<c>https://api.themoviedb.org/</c>); requests use relative URLs, so without a base address the
/// first call throws <see cref="InvalidOperationException"/> and without the slash the path is
/// resolved against the wrong directory.
/// </param>
/// <param name="clock">
/// Must be monotonic: <see cref="SlidingWindowRateLimiter"/> requires a non-decreasing
/// <c>now</c>. Production callers pass a fixed UTC base plus <c>Stopwatch.Elapsed</c>,
/// never a raw <c>DateTimeOffset.UtcNow</c>, which NTP can step backwards mid-run.
/// </param>
public sealed class TmdbClient(
    HttpClient http,
    string token,
    SlidingWindowRateLimiter limiter,
    RequestBudget budget,
    Func<DateTimeOffset> clock)
{
    private const int MaxAttempts = 5;

    // An unbounded Retry-After silently stalls a run (TMDB could send 3600), and past ~49.7 days
    // Task.Delay throws. Anything the server asks for beyond this is capped.
    private static readonly TimeSpan MaxRetryWait = TimeSpan.FromSeconds(60);

    // Trimmed once so that auth-mode detection and what goes on the wire agree on one string.
    // Trimmed once: Detect and the send must agree on the exact same string, or a token
    // with stray whitespace could be classified one way and transmitted another.
    private static string Clean(string? raw) => (raw ?? string.Empty).Trim();

    private readonly string _token = Clean(token);
    private readonly TmdbAuthMode _mode = TmdbAuth.Detect(Clean(token));

    public bool BudgetExhausted { get; private set; }

    public int RequestsUsed => budget.Used;

    public async Task<IReadOnlyList<MovieCandidate>> SearchMoviesAsync(
        string title, int? year, CancellationToken ct)
    {
        var path = $"3/search/movie?query={Uri.EscapeDataString(title)}" +
                   (year is { } y ? $"&year={y}" : string.Empty);

        using var doc = await GetAsync(path, ct);
        if (doc is null) return [];

        var results = new List<MovieCandidate>();
        foreach (var el in doc.RootElement.GetProperty("results").EnumerateArray())
        {
            var date = el.TryGetProperty("release_date", out var d) ? d.GetString() : null;
            int? parsedYear = date is { Length: >= 4 } && int.TryParse(date[..4], out var yy) ? yy : null;

            results.Add(new MovieCandidate(
                el.GetProperty("id").GetInt32(),
                el.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                parsedYear,
                null,  // search results carry no runtime; filled by GetMovieAsync
                [],    // nor credits
                el.TryGetProperty("popularity", out var p) ? p.GetDouble() : 0d));
        }
        return results;
    }

    public async Task<TmdbMovie?> GetMovieAsync(int id, CancellationToken ct)
        => await GetAsAsync<TmdbMovie>($"3/movie/{id}", ct);

    public async Task<IReadOnlyList<string>> GetDirectorsAsync(int movieId, CancellationToken ct)
    {
        using var doc = await GetAsync($"3/movie/{movieId}/credits", ct);
        if (doc is null) return [];

        return [.. doc.RootElement.GetProperty("crew").EnumerateArray()
            .Where(c => c.TryGetProperty("job", out var j) && j.GetString() == "Director")
            .Select(c => c.GetProperty("name").GetString() ?? "")
            .Where(n => n.Length > 0)];
    }

    public Task<TmdbSeries?> SearchSeriesAsync(string name, CancellationToken ct)
        => SearchSeriesAsync(name, null, ct);

    /// <param name="firstAirYear">
    /// When given, TMDB is asked for series whose first air date falls in that year, which is what tells
    /// "The Biggest Loser" (2004) from "The Biggest Loser" (2020). Mirrors the year on <see cref="SearchMoviesAsync"/>.
    /// </param>
    public async Task<TmdbSeries?> SearchSeriesAsync(string name, int? firstAirYear, CancellationToken ct)
    {
        var path = $"3/search/tv?query={Uri.EscapeDataString(name)}" +
                   (firstAirYear is { } y ? $"&first_air_date_year={y}" : string.Empty);

        using var doc = await GetAsync(path, ct);
        if (doc is null) return null;

        var first = doc.RootElement.GetProperty("results").EnumerateArray().FirstOrDefault();
        if (first.ValueKind == JsonValueKind.Undefined) return null;

        return await GetSeriesAsync(first.GetProperty("id").GetInt32(), ct);
    }

    public async Task<TmdbSeries?> GetSeriesAsync(int id, CancellationToken ct)
        => await GetAsAsync<TmdbSeries>($"3/tv/{id}", ct);

    public async Task<TmdbSeason?> GetSeasonAsync(int seriesId, int season, CancellationToken ct)
        => await GetAsAsync<TmdbSeason>($"3/tv/{seriesId}/season/{season}", ct);

    private async Task<T?> GetAsAsync<T>(string path, CancellationToken ct) where T : class
    {
        using var doc = await GetAsync(path, ct);
        return doc is null ? null : doc.RootElement.Deserialize<T>();
    }

    /// <summary>
    /// Single choke point: budget first, then rate limit, then send with backoff.
    /// Returns null when the budget is spent so callers stop cleanly, and for a 404.
    /// Every attempt, retries included, passes budget -> DelayBefore -> wait -> Record.
    /// Throws <see cref="HttpRequestException"/> for a non-retryable status or once the retries
    /// are spent; transport faults and bad bodies surface as its <c>InnerException</c>.
    /// </summary>
    private async Task<JsonDocument?> GetAsync(string path, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            if (!budget.TryConsume()) { BudgetExhausted = true; return null; }

            var delay = limiter.DelayBefore(clock());
            if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
            limiter.Record(clock());

            HttpRequestException failure;
            TimeSpan? retryAfter = null;
            var retryable = true;
            try
            {
                using var req = CreateRequest(path);
                using var res = await http.SendAsync(req, ct);
                retryAfter = res.Headers.RetryAfter?.Delta;

                if (res.IsSuccessStatusCode)
                    return JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));

                if (res.StatusCode == HttpStatusCode.NotFound) return null;

                retryable = res.StatusCode == HttpStatusCode.TooManyRequests || (int)res.StatusCode >= 500;
                failure = new HttpRequestException(
                    $"TMDB {(int)res.StatusCode} for {path}", null, res.StatusCode);
            }
            catch (Exception ex) when (
                (ex is HttpRequestException or TaskCanceledException or JsonException)
                && !ct.IsCancellationRequested)
            {
                // A reset connection, a timeout, or a 200 carrying a proxy or captive-portal page.
                // A cancellation the caller asked for is excluded and propagates untouched.
                failure = new HttpRequestException($"TMDB request for {path} failed: {ex.Message}", ex);
            }

            if (!retryable || attempt == MaxAttempts) throw failure;

            var wait = retryAfter ?? TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
            if (wait > MaxRetryWait) wait = MaxRetryWait;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
        }

        throw new UnreachableException(); // the final attempt always returns or throws above
    }

    private HttpRequestMessage CreateRequest(string path)
    {
        var url = path;
        if (_mode == TmdbAuthMode.QueryKey)
            url += (path.Contains('?') ? "&" : "?") + $"api_key={Uri.EscapeDataString(_token)}";

        var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (_mode == TmdbAuthMode.BearerToken)
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        return req;
    }
}

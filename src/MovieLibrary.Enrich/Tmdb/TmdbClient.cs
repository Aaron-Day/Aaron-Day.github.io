using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MovieLibrary.Core.Matching;
using MovieLibrary.Core.Throttling;
using MovieLibrary.Core.Tmdb;

namespace MovieLibrary.Enrich.Tmdb;

/// <summary>
/// Thin TMDB client: budget first, then rate limit, then send with backoff.
/// </summary>
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
    private readonly TmdbAuthMode _mode = TmdbAuth.Detect(token);

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

    public async Task<TmdbSeries?> SearchSeriesAsync(string name, CancellationToken ct)
    {
        using var doc = await GetAsync($"3/search/tv?query={Uri.EscapeDataString(name)}", ct);
        if (doc is null) return null;

        var first = doc.RootElement.GetProperty("results").EnumerateArray().FirstOrDefault();
        if (first.ValueKind == JsonValueKind.Undefined) return null;

        return await GetAsAsync<TmdbSeries>($"3/tv/{first.GetProperty("id").GetInt32()}", ct);
    }

    public async Task<TmdbSeason?> GetSeasonAsync(int seriesId, int season, CancellationToken ct)
        => await GetAsAsync<TmdbSeason>($"3/tv/{seriesId}/season/{season}", ct);

    private async Task<T?> GetAsAsync<T>(string path, CancellationToken ct) where T : class
    {
        using var doc = await GetAsync(path, ct);
        return doc is null ? null : doc.RootElement.Deserialize<T>();
    }

    /// <summary>
    /// Single choke point: budget first, then rate limit, then send with backoff.
    /// Returns null when the budget is spent so callers stop cleanly.
    /// </summary>
    private async Task<JsonDocument?> GetAsync(string path, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            if (!budget.TryConsume()) { BudgetExhausted = true; return null; }

            var delay = limiter.DelayBefore(clock());
            if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
            limiter.Record(clock());

            var url = path;
            if (_mode == TmdbAuthMode.QueryKey)
                url += (path.Contains('?') ? "&" : "?") + $"api_key={Uri.EscapeDataString(token)}";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (_mode == TmdbAuthMode.BearerToken)
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var res = await http.SendAsync(req, ct);

            if (res.IsSuccessStatusCode)
                return JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));

            if (res.StatusCode == HttpStatusCode.NotFound) return null;

            var retryable = res.StatusCode == HttpStatusCode.TooManyRequests || (int)res.StatusCode >= 500;
            if (!retryable || attempt == MaxAttempts)
                throw new HttpRequestException($"TMDB {(int)res.StatusCode} for {path}");

            var wait = res.Headers.RetryAfter?.Delta
                       ?? TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
        }

        return null;
    }
}

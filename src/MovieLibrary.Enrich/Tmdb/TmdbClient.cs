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
        => (await GetCreditsAsync(movieId, 0, ct))?.Directors ?? [];

    /// <summary>
    /// One credits request, read in full: the directors (the scorer needs them) and the top
    /// <paramref name="castLimit"/> billed cast (the record keeps them). Throwing the cast away after paying for
    /// it is what made it unrecoverable from the cache. Null for a 404 or an exhausted budget.
    /// <para>A <i>member</i> that cannot be read (not an object, no usable <c>name</c>, a mistyped <c>order</c>) is
    /// skipped: this sits in the scoring path, and one odd entry must not abort a seed.</para>
    /// <para>A <i>container</i> of the wrong shape is a different thing. A body that is not an object, or a
    /// <c>cast</c> or <c>crew</c> that is present but not an array, means TMDB changed the response for every film, and
    /// the pipeline would cache the empty result as "TMDB has none" for five months. It throws instead.</para>
    /// </summary>
    /// <exception cref="InvalidDataException">The body, <c>cast</c> or <c>crew</c> has the wrong shape. The cast is not
    /// read when <paramref name="castLimit"/> is 0 or less, so its shape is not checked then.</exception>
    public async Task<TmdbCredits?> GetCreditsAsync(int movieId, int castLimit, CancellationToken ct)
    {
        using var doc = await GetAsync($"3/movie/{movieId}/credits", ct);
        if (doc is null) return null;

        RequireObject(doc.RootElement, movieId);
        return new TmdbCredits(
            Directors(doc.RootElement, movieId), TopBilledCast(doc.RootElement, castLimit, movieId));
    }

    /// <summary>
    /// Details (with genres and collection) and the top-billed cast in a single request, for the paths that never saw
    /// the credits during a search: a payload past its refresh window and a film named by an override. A body with no
    /// <c>credits</c> property at all (TMDB omits it without <c>append_to_response</c>) gives an empty cast rather than
    /// a second request or an exception. A <c>credits</c> that is present but not an object, or whose <c>cast</c> is
    /// present but not an array, throws: see <see cref="GetCreditsAsync"/>.
    /// </summary>
    /// <exception cref="InvalidDataException">The <c>credits</c> value or its <c>cast</c> has the wrong shape.</exception>
    public async Task<TmdbMovieWithCast?> GetMovieWithCreditsAsync(int id, int castLimit, CancellationToken ct)
    {
        using var doc = await GetAsync($"3/movie/{id}?append_to_response=credits", ct);
        if (doc is null) return null;

        var movie = doc.RootElement.Deserialize<TmdbMovie>();
        if (movie is null) return null;

        IReadOnlyList<TmdbCastMember> cast = [];
        if (doc.RootElement.TryGetProperty("credits", out var credits))
        {
            RequireObject(credits, id);
            cast = TopBilledCast(credits, castLimit, id);
        }
        return new TmdbMovieWithCast(movie, cast);
    }

    /// <summary>A string property, or null when it is absent, null, or not a string (GetString would throw on a number).</summary>
    private static string? StringOf(JsonElement obj, string name)
        => obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    /// <summary>
    /// The credits object itself, checked once by each caller before <see cref="Directors"/> and
    /// <see cref="TopBilledCast"/> read it. Callers pass what TMDB sent for <c>credits</c>, so anything but an object
    /// is a changed response, not a film with no credits.
    /// </summary>
    private static void RequireObject(JsonElement credits, int movieId)
    {
        if (credits.ValueKind != JsonValueKind.Object) throw ShapeChanged(movieId, "credits", credits, "an object");
    }

    /// <summary>
    /// The <c>cast</c> or <c>crew</c> array of a credits object: null when the property is absent, an exception when it
    /// is present as anything but an array. Absent is not the same as mistyped: a credits object with no <c>cast</c>
    /// yields no cast, as it always has.
    /// </summary>
    private static JsonElement? ArrayOf(JsonElement credits, string name, int movieId)
    {
        if (!credits.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind != JsonValueKind.Array) throw ShapeChanged(movieId, $"credits.{name}", value, "an array");
        return value;
    }

    private static InvalidDataException ShapeChanged(int movieId, string field, JsonElement actual, string expected)
        => new($"TMDB sent the credits of film {movieId} with '{field}' as {actual.ValueKind.ToString().ToLowerInvariant()}, " +
               $"not {expected}. A container that changes shape changes for every film, and caching the empty result " +
               "would hide it for months, so the run stops here.");

    private static IReadOnlyList<string> Directors(JsonElement credits, int movieId)
    {
        if (ArrayOf(credits, "crew", movieId) is not { } crew) return [];

        return [.. crew.EnumerateArray()
            .Where(c => StringOf(c, "job") == "Director")
            .Select(c => StringOf(c, "name") ?? "")
            .Where(n => n.Length > 0)];
    }

    /// <summary>TMDB lists the whole cast, hundreds of people for a big film; billing is its <c>order</c> field.</summary>
    private static IReadOnlyList<TmdbCastMember> TopBilledCast(JsonElement credits, int limit, int movieId)
    {
        if (limit <= 0 || ArrayOf(credits, "cast", movieId) is not { } cast) return [];

        return [.. cast.EnumerateArray()
            .Select((member, position) => (Member: member, Position: position))
            .Where(x => !string.IsNullOrWhiteSpace(StringOf(x.Member, "name")))
            .OrderBy(x => BillingOrder(x.Member))
            .ThenBy(x => x.Position)
            .Take(limit)
            .Select(x => new TmdbCastMember(
                StringOf(x.Member, "name")!.Trim(), StringOf(x.Member, "character"), StringOf(x.Member, "profile_path")))];
    }

    /// <summary>A missing or non-integer order sorts last; the array position then keeps TMDB's own sequence.</summary>
    private static int BillingOrder(JsonElement member)
        => member.TryGetProperty("order", out var o) && o.ValueKind == JsonValueKind.Number && o.TryGetInt32(out var n)
            ? n
            : int.MaxValue;

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

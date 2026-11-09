using System.Net.Http.Json;
using System.Text.Json;
using MovieLibrary.Core.Models;
using MovieLibrary.Web.Models;

namespace MovieLibrary.Web.Services;

/// <summary>Fetches the published catalogue once and holds it for the session.</summary>
/// <remarks>
/// <para>
/// <b>The TASK is cached, not the result.</b> movies.json is about 800 KB on disk, so two components rendering
/// in the same frame share one request instead of racing. (It is SMALLER over the wire, not larger as an earlier
/// note here claimed: GitHub Pages gzips application/json, so roughly 200 to 300 KB arrives. The caching is
/// still right; the premise used to justify it was not.)
/// </para>
/// <para>
/// <b>No announcer, deliberately.</b> Caller rule R11 is one alert per failure the USER can see, not one per
/// fetch, and two fetches behind one grid are one failure. The page decides what the user has lost and says it
/// once; a service that also alerted would double-speak, and the announcer's latest-wins would then pick which
/// half of the sentence survived.
/// </para>
/// <para>
/// <b>A failure is never cached, and the eviction happens at the NEXT call rather than inside
/// <see cref="Load"/>.</b> The obvious shape -- passing an <c>evict</c> closure into <c>Load</c> -- is wrong in
/// a way that only a test can see: <c>movies ??= Load(..., () =&gt; movies = null)</c> assigns the field AFTER
/// <c>Load</c> returns, so when the chain completes synchronously (a stub handler that throws inline, or a
/// cached <c>Task.FromException</c>) the closure clears a field that is then overwritten with the completed
/// FAILED task. The failure is cached, every later call serves it, and Try again becomes a button that cannot
/// help. In a real browser the fetch is genuinely async and the same code works, so the defect's presence
/// depends on the stub. Checking the cached task at the next call has no ordering question in it at all.
/// </para>
/// <para>
/// <b>An empty value may never mean failure.</b> <see cref="CatalogResult{T}.State"/> carries that; the value is
/// only a safe default so callers need no null checks.
/// </para>
/// </remarks>
public sealed class CatalogService(HttpClient http)
{
    private readonly TextWriter log = Console.Error;

    /// <summary>Test seam: lets a test read what was reported without touching process-wide console state.</summary>
    internal CatalogService(HttpClient http, TextWriter log) : this(http) => this.log = log;

    private Task<CatalogResult<IReadOnlyList<MovieRecord>>>? movies;
    private Task<CatalogResult<CatalogMeta>>? meta;

    public Task<CatalogResult<IReadOnlyList<MovieRecord>>> GetMoviesAsync()
    {
        if (HasFailed(movies)) movies = null;
        return movies ??= Load<IReadOnlyList<MovieRecord>>("data/movies.json", []);
    }

    public Task<CatalogResult<CatalogMeta>> GetMetaAsync()
    {
        if (HasFailed(meta)) meta = null;
        return meta ??= Load("data/meta.json", CatalogMeta.Empty);
    }

    /// <summary>
    /// Whether a cached task has already settled on a failure, which is the only thing worth re-fetching. A task
    /// still in flight is left alone however it will end: concurrent callers must share one request, and
    /// evicting a pending task would issue a second.
    /// <para>
    /// Each field is evicted on its own. Clearing both on any failure -- which an earlier draft did, because
    /// <see cref="Load"/> is generic and cannot know which field it serves -- would throw away a perfectly good
    /// cached meta because movies.json failed, and force a needless re-fetch of it.
    /// </para>
    /// </summary>
    private static bool HasFailed<T>(Task<CatalogResult<T>>? cached) =>
        cached is { IsCompletedSuccessfully: true } settled && settled.Result.State == CatalogState.Failed;

    private async Task<CatalogResult<T>> Load<T>(string path, T empty)
    {
        try
        {
            var value = await http.GetFromJsonAsync<T>(path);

            if (value is not null) return new(CatalogState.Ready, value);

            // A null value means a literal JSON `null` body, which is a broken or half-deployed catalogue and
            // NOT an empty library. Reporting it Ready would show "0 films" and let a grid render "No titles
            // match these filters." over a broken deployment -- word for word the false statement this type
            // exists to prevent. A genuinely empty ARRAY still arrives as Ready, with no records in it, and the
            // page has its own sentence for that. A 204 was measured NOT to reach here: the empty body throws
            // JsonException and is caught below, which is the same Failed outcome by a different route.
            Report(path, "the body was JSON null, which is not a catalogue");
            return new(CatalogState.Failed, empty);
        }
        catch (Exception ex)
        {
            // Broadly, and then reported by kind. Every escape from here reaches OnInitializedAsync, the runtime
            // reveals #blazor-error-ui with its own role="alert" -- a SECOND assertive source for a fetch
            // failure, which this project's rules forbid -- and the page then sits on "Loading films..." for
            // ever with no Try again button. So nothing escapes; what differs is whether it is also logged.
            if (!IsAFetchFailure(ex)) Report(path, ex.GetType().Name + ": " + ex.Message);

            return new(CatalogState.Failed, empty);
        }
    }

    /// <summary>
    /// The exception types that mean the network or the server rather than this code, and so are a failure to
    /// report to the user and nothing to log.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The captive portal, which is why this catch is broad at all.</b> An earlier draft of this service
    /// required <see cref="NotSupportedException"/> to PROPAGATE, reasoning that a serializer misconfiguration
    /// is a bug to surface; what it is really thrown for is a response whose content type is not JSON, and on a
    /// phone that is a hotel or airport Wi-Fi interstitial, a carrier proxy, or an in-app browser handing back
    /// <c>text/html</c> with status 200. Routing that through the user's assistive technology as an unhandled
    /// exception is the worst available outcome.
    /// </para>
    /// <para>
    /// MEASURED, and worth knowing: on this stack (.NET 10, <c>System.Net.Http.Json</c>) a <c>text/html</c>
    /// body does not produce <see cref="NotSupportedException"/> at all. It reaches the deserialiser and throws
    /// <see cref="JsonException"/>, so a captive portal is indistinguishable from a truncated file -- which is
    /// fine, because both are the network rather than this code. <see cref="NotSupportedException"/> stays in
    /// the list because the type is documented for exactly that case and a runtime that starts throwing it
    /// again must not reach the user as an unhandled exception;
    /// <c>CatalogServiceTests.AFetchFailureIsFailedWithAnEmptyButNonNullValueAndIsNotLogged</c> raises it
    /// directly, so the entry is a failable subject rather than a decorative one.
    /// </para>
    /// <para>
    /// <see cref="OperationCanceledException"/> rather than only its <see cref="TaskCanceledException"/>
    /// subclass, because <c>HttpClient</c> raises the bare type in some cancellation paths, and
    /// <see cref="IOException"/> because a response stream can drop mid-read.
    /// </para>
    /// <para>
    /// Anything else still returns Failed -- the user's experience must not depend on which exception type a bug
    /// produced -- but is written to <see cref="log"/> as well, so a "no console errors" gate still catches a
    /// real bug instead of the bug being spoken to the user as a network problem.
    /// </para>
    /// </remarks>
    private static bool IsAFetchFailure(Exception ex) => ex is
        HttpRequestException            // offline, DNS, TLS, a 404 or a 500
        or JsonException                // a truncated, mangled or half-deployed file
        or OperationCanceledException   // a timeout, or a navigation away mid-fetch
        or IOException                  // the response stream dropped part-way through
        or NotSupportedException;       // the content type was not JSON: a captive portal serving text/html

    private void Report(string path, string what) =>
        log.WriteLine($"CatalogService: {path} did not yield a catalogue. {what}");
}

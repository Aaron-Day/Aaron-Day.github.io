using System.Net;
using System.Text;
using MovieLibrary.Web.Services;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// An <see cref="HttpMessageHandler"/> that answers data/movies.json and data/meta.json from a script, and
/// counts what was asked for. bUnit supplies no <see cref="HttpClient"/>, and the two things worth pinning about
/// <c>CatalogService</c> -- that concurrent callers share one request, and that a failure is not cached -- are
/// both assertions about the number of requests, never about timing.
/// </summary>
/// <remarks>
/// Responders are consumed in order and the LAST one repeats, so "fail once, then succeed for ever" is two
/// entries. A path with no script at all is a test that forgot to set one up, and says so rather than 404ing:
/// a 404 would arrive as a plain fetch failure and the test would pass for the wrong reason.
/// </remarks>
internal sealed class StubCatalog : HttpMessageHandler
{
    /// <summary>Any absolute base: what the service builds are the two relative paths below.</summary>
    public const string Base = "https://films.test/";

    public const string Movies = "data/movies.json";
    public const string Meta = "data/meta.json";

    private readonly object gate = new();
    private readonly Dictionary<string, List<Func<Task<HttpResponseMessage>>>> plan = new(StringComparer.Ordinal);
    private readonly List<string> asked = new();

    public StubCatalog Serves(string path, params Func<Task<HttpResponseMessage>>[] responders)
    {
        lock (gate) plan[path] = responders.ToList();
        return this;
    }

    /// <summary>How many requests this handler has been given for one path.</summary>
    public int RequestsFor(string path)
    {
        lock (gate) return asked.Count(a => string.Equals(a, path, StringComparison.Ordinal));
    }

    /// <summary>A client bound to this handler, at an absolute base so the relative paths resolve.</summary>
    public HttpClient Client() => new(this) { BaseAddress = new Uri(Base) };

    public CatalogService Service(out StringWriter log)
    {
        log = new StringWriter();
        return new CatalogService(Client(), log);
    }

    public CatalogService Service() => Service(out _);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = new Uri(Base).MakeRelativeUri(request.RequestUri!).ToString();

        Func<Task<HttpResponseMessage>> responder;
        lock (gate)
        {
            asked.Add(path);

            if (!plan.TryGetValue(path, out var responders) || responders.Count == 0)
                throw new InvalidOperationException(
                    $"This test asked for \"{path}\", which it never scripted. Call Serves(\"{path}\", ...) "
                    + "first: leaving it unscripted would reach CatalogService as an ordinary fetch failure "
                    + "and the test would go green for a reason that has nothing to do with its subject.");

            responder = responders[0];
            if (responders.Count > 1) responders.RemoveAt(0);   // the last one repeats
        }

        return responder();
    }

    // ------------------------------------------------------------------ responders

    /// <summary>A 200 with a JSON body. The content type matters: without it GetFromJsonAsync refuses to read it.</summary>
    public static Func<Task<HttpResponseMessage>> Json(string body) => () => Task.FromResult(
        new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });

    /// <summary>A 200 whose body is not JSON at all: a captive portal's sign-in page, which is the real case.</summary>
    public static Func<Task<HttpResponseMessage>> Html() => () => Task.FromResult(
        new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<!doctype html><title>Sign in to Wi-Fi</title>", Encoding.UTF8, "text/html"),
        });

    public static Func<Task<HttpResponseMessage>> Status(HttpStatusCode code) => () => Task.FromResult(
        new HttpResponseMessage(code) { Content = new StringContent("", Encoding.UTF8, "application/json") });

    /// <summary>
    /// A 204. MEASURED, because the brief assumed otherwise: GetFromJsonAsync throws JsonException on it
    /// ("The input does not contain any JSON tokens"), so an empty body never reaches the null check at all.
    /// </summary>
    public static Func<Task<HttpResponseMessage>> NoContent() => () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));

    /// <summary>Fails without ever yielding, so the whole fetch chain completes synchronously.</summary>
    public static Func<Task<HttpResponseMessage>> Throws(Exception ex) => () => throw ex;

    /// <summary>Fails after yielding, so the fetch chain is genuinely asynchronous as it is in a browser.</summary>
    public static Func<Task<HttpResponseMessage>> ThrowsAfterYielding(Exception ex) => async () =>
    {
        await Task.Yield();
        throw ex;
    };

    /// <summary>Never answers, which is what a page sitting on "Loading films..." is waiting for.</summary>
    public static Func<Task<HttpResponseMessage>> NeverAnswers() =>
        () => new TaskCompletionSource<HttpResponseMessage>().Task;

    /// <summary>
    /// Holds a response until the test releases it, so the page can be read in the MIDDLE of a fetch. Without
    /// one, every stub answer lands inside the click handler and only the end state is ever observable.
    /// </summary>
    public sealed class Gate
    {
        private readonly TaskCompletionSource opened = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Func<Task<HttpResponseMessage>> Holding(Func<Task<HttpResponseMessage>> response) => async () =>
        {
            await opened.Task;
            return await response();
        };

        public void Open() => opened.TrySetResult();
    }
}

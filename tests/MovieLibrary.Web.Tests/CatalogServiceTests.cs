using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MovieLibrary.Core.Models;
using MovieLibrary.Web.Models;
using MovieLibrary.Web.Services;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// What <c>CatalogService</c> has to guarantee for Tasks 6 to 8, which never fetch.
/// <para>
/// Every assertion here is about the OUTCOME or the number of requests, never about timing. The two that matter
/// most are the ones whose absence would make the films page state something false: a body that is not a
/// catalogue must not come back Ready, and a failure must not be cached, or Try again is a button that cannot
/// help.
/// </para>
/// </summary>
public class CatalogServiceTests
{
    private const string ThreeFilms =
        """
        [{"i":807,"k":"807-90","t":"A Confident Film","rt":90,"g":[],"ag":"Drama","vote":7.1,"dir":[],"cast":[],"mc":0.95},
         {"i":601,"k":"601-88","t":"A Film Needing Review","rt":88,"g":[],"ag":"Drama","vote":6.2,"dir":[],"cast":[],"mc":0.55},
         {"i":0,"k":"u-abcd1234","t":"An Unmatched Film","rt":101,"g":[],"ag":"Drama","vote":0,"dir":[],"cast":[],"mc":0}]
        """;

    private const string MetaBody =
        """
        {"builtUtc":"2026-11-04T09:00:00Z","movieCount":3,"showCount":0,"totalEpisodeCount":0,
         "genres":{"18":"Drama"},
         "attribution":"This website uses TMDB and the TMDB APIs but is not endorsed, certified, or otherwise approved by TMDB.",
         "reviewQueue":[]}
        """;

    // ------------------------------------------------------------------ one fetch, held for the session

    [Fact]
    public void TwoConcurrentCallersShareOneRequest()
    {
        // The real case: two components rendering in the same frame. Asserted on the handler's count, because a
        // timing assertion here would pass or fail on how fast the machine is.
        var stub = new StubCatalog().Serves(StubCatalog.Movies, StubCatalog.NeverAnswers());
        var service = stub.Service();

        var first = service.GetMoviesAsync();
        var second = service.GetMoviesAsync();

        Assert.Same(first, second);
        Assert.Equal(1, stub.RequestsFor(StubCatalog.Movies));
        Assert.False(first.IsCompleted);   // still in flight, so the shared task is the pending one
    }

    [Fact]
    public async Task ASuccessfulFetchIsServedFromCacheAndNotRequestedAgain()
    {
        var stub = new StubCatalog().Serves(StubCatalog.Movies, StubCatalog.Json(ThreeFilms));
        var service = stub.Service();

        var first = await service.GetMoviesAsync();
        var second = await service.GetMoviesAsync();

        Assert.Equal(CatalogState.Ready, first.State);
        Assert.Equal(CatalogState.Ready, second.State);
        Assert.Equal(3, second.Value.Count);
        Assert.Equal(1, stub.RequestsFor(StubCatalog.Movies));
    }

    [Fact]
    public async Task MetaIsCachedOnItsOwnTheSameWay()
    {
        var stub = new StubCatalog().Serves(StubCatalog.Meta, StubCatalog.Json(MetaBody));
        var service = stub.Service();

        var first = await service.GetMetaAsync();
        await service.GetMetaAsync();

        Assert.Equal(CatalogState.Ready, first.State);
        Assert.Equal(3, first.Value.MovieCount);
        // And again, because the eviction is lazy. The SECOND movies call is where the failure is actually
        // observed, so an eviction that took the other field with it would not show up until after that one.
        Assert.Equal(CatalogState.Failed, (await service.GetMoviesAsync()).State);
        Assert.Equal(CatalogState.Ready, (await service.GetMetaAsync()).State);

        Assert.Equal(1, stub.RequestsFor(StubCatalog.Meta));
    }

    // ------------------------------------------------------------------ failures

    /// <remarks>
    /// Every one of these is the network or the server rather than a bug, so none is logged: a "no console
    /// errors" gate has to stay useful on a phone in airplane mode.
    /// <para>
    /// They are raised directly rather than provoked through a response, which is what makes the LIST a
    /// failable subject rather than a decorative one. NotSupportedException is the case that needs it: an
    /// earlier draft of this task required that type to PROPAGATE, and on this stack no response produces it
    /// any more (see <see cref="ACaptivePortalIsAFetchFailureAndNotAnUnhandledException"/>), so without this
    /// line it could be struck from the list with nothing going red -- rule T1's shape exactly. It stays in the
    /// list because the type is documented on ReadFromJsonAsync for a non-JSON content type and a runtime that
    /// starts throwing it again must not reach the user as an unhandled exception.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("HttpRequestException")]
    [InlineData("JsonException")]
    [InlineData("TaskCanceledException")]
    [InlineData("OperationCanceledException")]
    [InlineData("IOException")]
    [InlineData("NotSupportedException")]
    public async Task AFetchFailureIsFailedWithAnEmptyButNonNullValueAndIsNotLogged(string kind)
    {
        var stub = new StubCatalog().Serves(StubCatalog.Movies, StubCatalog.Throws(Raise(kind)));
        var service = stub.Service(out var log);

        var result = await service.GetMoviesAsync();

        Assert.Equal(CatalogState.Failed, result.State);
        Assert.NotNull(result.Value);
        Assert.Empty(result.Value);
        Assert.Equal("", log.ToString());
    }

    private static Exception Raise(string kind) => kind switch
    {
        "HttpRequestException" => new HttpRequestException("the network is gone"),
        "JsonException" => new JsonException("unexpected end of data"),
        "TaskCanceledException" => new TaskCanceledException("the request timed out"),
        "OperationCanceledException" => new OperationCanceledException("cancelled"),
        "IOException" => new IOException("the response stream dropped"),
        "NotSupportedException" => new NotSupportedException("the provided ContentType is not supported"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "unknown exception kind"),
    };

    [Fact]
    public async Task AFailedMetaFetchStillCarriesTheLicenceBoundSentenceAndNoNulls()
    {
        // m3. The attribution is fixed by TMDB's terms, so it must not be able to vanish because a fetch failed,
        // and Genres and ReviewQueue have to be readable without a null check on either path.
        var stub = new StubCatalog().Serves(StubCatalog.Meta, StubCatalog.Throws(new HttpRequestException("offline")));

        var result = await stub.Service().GetMetaAsync();

        Assert.Equal(CatalogState.Failed, result.State);
        Assert.Equal(CatalogMeta.TmdbAttribution, result.Value.Attribution);
        Assert.NotNull(result.Value.Genres);
        Assert.Empty(result.Value.Genres);
        Assert.NotNull(result.Value.ReviewQueue);
        Assert.Empty(result.Value.ReviewQueue);
        Assert.Equal("", result.Value.BuiltUtc);   // empty, not a date that would render as 0001-01-01
    }

    /// <remarks>
    /// C2. A real response, not a raised exception: status 200 with a text/html body, which is a hotel or
    /// airport Wi-Fi interstitial, a carrier proxy, or an in-app browser. Had it propagated it would escape
    /// OnInitializedAsync, the runtime would reveal #blazor-error-ui with its own role="alert" -- a second
    /// assertive source for a fetch failure, which this project's rules forbid -- and the page would sit on
    /// "Loading films..." for ever with no Try again button.
    /// <para>
    /// MEASURED, and not what the brief said. It said GetFromJsonAsync throws NotSupportedException for a
    /// content type that is not JSON. On this stack (.NET 10, System.Net.Http.Json) it does not look at the
    /// content type at all on this path: the HTML reaches the deserialiser and throws JsonException, "'&lt;' is
    /// an invalid start of a value". That makes a captive portal indistinguishable from a truncated file, which
    /// is fine -- both are fetch failures, neither is the user's problem to debug, and neither is logged -- but
    /// it does mean no response in this suite produces NotSupportedException, so the theory above raises that
    /// type directly rather than leaving the list entry unfailable.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ACaptivePortalIsAFetchFailureAndNotAnUnhandledException()
    {
        var stub = new StubCatalog().Serves(StubCatalog.Movies, StubCatalog.Html());
        var service = stub.Service(out var log);

        var result = await service.GetMoviesAsync();

        Assert.Equal(CatalogState.Failed, result.State);
        Assert.Empty(result.Value);
        Assert.Equal("", log.ToString());
    }

    [Fact]
    public async Task AServerErrorIsAFetchFailureToo()
    {
        var stub = new StubCatalog().Serves(StubCatalog.Movies, StubCatalog.Status(HttpStatusCode.InternalServerError));

        var result = await stub.Service().GetMoviesAsync();

        Assert.Equal(CatalogState.Failed, result.State);
        Assert.Empty(result.Value);
    }

    /// <remarks>
    /// An exception type outside the fetch-failure list must still reach the user as Failed -- what they see
    /// cannot depend on which exception a bug produced -- but it is also written to the log, so a gate asserting
    /// "no console errors" still catches a real bug instead of it being spoken as a network problem.
    /// </remarks>
    [Fact]
    public async Task AnUnexpectedExceptionIsStillFailedButIsReportedAsWell()
    {
        var stub = new StubCatalog().Serves(
            StubCatalog.Movies, StubCatalog.Throws(new InvalidTimeZoneException("a real bug")));
        var service = stub.Service(out var log);

        var result = await service.GetMoviesAsync();

        Assert.Equal(CatalogState.Failed, result.State);
        Assert.Empty(result.Value);
        Assert.Contains("InvalidTimeZoneException", log.ToString(), StringComparison.Ordinal);
        Assert.Contains("a real bug", log.ToString(), StringComparison.Ordinal);
        Assert.Contains(StubCatalog.Movies, log.ToString(), StringComparison.Ordinal);
    }


    // ------------------------------------------------------------------ a body that is not a catalogue

    /// <remarks>
    /// C1, and the half the brief's premise got right. A literal JSON <c>null</c> body deserialises to a null
    /// value, which is a broken or half-deployed catalogue and not an empty library. Ready would show "0 films",
    /// and Task 8 would then render "No titles match these filters." over a broken deployment -- word for word
    /// the false statement this whole task exists to prevent. It is logged, because a half-deployed catalogue is
    /// the owner's bug to see rather than a network condition to hide from him.
    /// </remarks>
    [Fact]
    public async Task ALiteralJsonNullBodyIsFailedAndNeverReady()
    {
        var stub = new StubCatalog().Serves(StubCatalog.Movies, StubCatalog.Json("null"));
        var service = stub.Service(out var log);

        var result = await service.GetMoviesAsync();

        Assert.Equal(CatalogState.Failed, result.State);
        Assert.NotNull(result.Value);
        Assert.Empty(result.Value);
        Assert.Contains("not a catalogue", log.ToString(), StringComparison.Ordinal);
    }

    /// <remarks>
    /// The other half of C1, and MEASURED rather than assumed, because the brief's premise is wrong about it.
    /// It said GetFromJsonAsync returns null for a 204 as well as for a literal null. On this stack
    /// (.NET 10, System.Net.Http.Json) a 204 does not reach the null check at all: the empty body throws
    /// JsonException, "The input does not contain any JSON tokens". So the outcome C1 asked for is right -- the
    /// state is Failed and never Ready -- but it arrives through the catch rather than through the null branch,
    /// which also means it is NOT logged, because an empty body is indistinguishable here from a truncated one
    /// and a truncated response is an ordinary fetch failure. Asserted as it behaves; if a future runtime starts
    /// returning null for a 204 instead, the log assertion is the line that will need adding.
    /// </remarks>
    [Fact]
    public async Task A204IsFailedThroughTheParseFailureRatherThanTheNullCheck()
    {
        var stub = new StubCatalog().Serves(StubCatalog.Movies, StubCatalog.NoContent());
        var service = stub.Service(out var log);

        var result = await service.GetMoviesAsync();

        Assert.Equal(CatalogState.Failed, result.State);
        Assert.NotNull(result.Value);
        Assert.Empty(result.Value);
        Assert.Equal("", log.ToString());
    }

    /// <remarks>
    /// The other half of C1, and the reason the rule is about null and not about emptiness: a genuinely empty
    /// published array IS a successful fetch of an empty library, and the page has its own sentence for it.
    /// </remarks>
    [Fact]
    public async Task AGenuinelyEmptyArrayIsReady()
    {
        var stub = new StubCatalog().Serves(StubCatalog.Movies, StubCatalog.Json("[]"));
        var service = stub.Service(out var log);

        var result = await service.GetMoviesAsync();

        Assert.Equal(CatalogState.Ready, result.State);
        Assert.Empty(result.Value);
        Assert.Equal("", log.ToString());
    }

    // ------------------------------------------------------------------ a failure is not cached

    /// <remarks>
    /// C3, and the shape of this test is the point. The obvious implementation passes an eviction closure into
    /// the loader, which assigns the cache field AFTER the loader returns: when the chain completes
    /// synchronously the closure clears a field that is then overwritten with the completed FAILED task, so the
    /// failure is cached for ever and Try again cannot help. In a real browser the fetch genuinely yields and
    /// the same code works -- so the defect's presence depends on the stub, which is why both shapes are tested
    /// and why this one asserts that it really did complete synchronously.
    /// </remarks>
    [Fact]
    public async Task AFailureThatCompletedSynchronouslyIsStillNotCached()
    {
        var stub = new StubCatalog().Serves(
            StubCatalog.Movies,
            StubCatalog.Throws(new HttpRequestException("offline")),
            StubCatalog.Json(ThreeFilms));
        var service = stub.Service();

        var failing = service.GetMoviesAsync();
        Assert.True(failing.IsCompleted,
            "This test only exercises the ordering defect if the fetch chain completed without yielding. It "
            + "did not, so it is now the same test as the asynchronous one below and proves nothing extra. "
            + "Make the handler fail more synchronously, or delete this test and say why in the report.");
        Assert.Equal(CatalogState.Failed, (await failing).State);

        var retried = await service.GetMoviesAsync();

        Assert.Equal(CatalogState.Ready, retried.State);
        Assert.Equal(3, retried.Value.Count);
        Assert.Equal(2, stub.RequestsFor(StubCatalog.Movies));
    }

    [Fact]
    public async Task AFailureThatCompletedAsynchronouslyIsNotCachedEither()
    {
        var stub = new StubCatalog().Serves(
            StubCatalog.Movies,
            StubCatalog.ThrowsAfterYielding(new HttpRequestException("offline")),
            StubCatalog.Json(ThreeFilms));
        var service = stub.Service();

        Assert.Equal(CatalogState.Failed, (await service.GetMoviesAsync()).State);
        var retried = await service.GetMoviesAsync();

        Assert.Equal(CatalogState.Ready, retried.State);
        Assert.Equal(2, stub.RequestsFor(StubCatalog.Movies));
    }

    /// <remarks>
    /// The defect the per-field eviction exists to prevent, so it gets the test that would have caught it. An
    /// earlier draft cleared BOTH cache fields on any failure, because the generic loader cannot know which
    /// field it serves, so a movies.json failure threw away a perfectly good cached meta and forced a needless
    /// re-fetch of it.
    /// </remarks>
    [Fact]
    public async Task AMoviesFailureDoesNotEvictAGoodMeta()
    {
        var stub = new StubCatalog()
            .Serves(StubCatalog.Meta, StubCatalog.Json(MetaBody))
            .Serves(StubCatalog.Movies, StubCatalog.Throws(new HttpRequestException("offline")));
        var service = stub.Service();

        Assert.Equal(CatalogState.Ready, (await service.GetMetaAsync()).State);
        Assert.Equal(CatalogState.Failed, (await service.GetMoviesAsync()).State);
        Assert.Equal(CatalogState.Ready, (await service.GetMetaAsync()).State);

        // And again, because the eviction is lazy. The SECOND movies call is where the failure is actually
        // observed, so an eviction that took the other field with it would not show up until after that one.
        Assert.Equal(CatalogState.Failed, (await service.GetMoviesAsync()).State);
        Assert.Equal(CatalogState.Ready, (await service.GetMetaAsync()).State);

        Assert.Equal(1, stub.RequestsFor(StubCatalog.Meta));
    }

    [Fact]
    public async Task ASuccessAfterARetryIsThenCachedLikeAnyOtherSuccess()
    {
        var stub = new StubCatalog().Serves(
            StubCatalog.Movies,
            StubCatalog.Throws(new HttpRequestException("offline")),
            StubCatalog.Json(ThreeFilms));
        var service = stub.Service();

        await service.GetMoviesAsync();
        await service.GetMoviesAsync();
        await service.GetMoviesAsync();

        Assert.Equal(2, stub.RequestsFor(StubCatalog.Movies));
    }

    // ------------------------------------------------------------------ the registration actually resolves

    /// <remarks>
    /// The service has an internal second constructor for the log sink. Dependency injection only considers
    /// public constructors, but "only considers" is worth a test when the alternative is a DI error at boot on
    /// the first page render, which no bUnit test would see because every one of them registers its own
    /// instance. SourceContractTests pins that Program.cs still carries the line.
    /// </remarks>
    [Fact]
    public void TheRegistrationShapeProgramUsesResolves()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(StubCatalog.Base) });
        services.AddScoped<CatalogService>();

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<CatalogService>());
    }
}

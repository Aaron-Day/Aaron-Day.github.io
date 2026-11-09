using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MovieLibrary.Web.Services;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// Which state a contract wants the films page in. Without this every one of the 320 contracts would render
/// whichever single state the stub happened to produce, and Task 6's grid contracts would be proving things
/// about "Loading films...".
/// </summary>
// Public, not internal: xunit theory data crosses the public surface, and a [Theory] method taking an
// internal enum will not compile.
public enum CatalogOutcome
{
    /// <summary>The default, because it is the ordinary state and the one a grid contract needs.</summary>
    Ready,

    /// <summary>The fetch fails, so the failure sentence and the Try again button are in the markup.</summary>
    Failed,

    /// <summary>The fetch never answers, so the page stays on the loading copy.</summary>
    NeverCompletes,
}

internal static class AnnouncerTestExtensions
{
    /// <summary>
    /// A tiny catalogue, inline rather than read from tests/fixtures, because 320 contracts should not each
    /// read a file. PublishedShapeTests is where the real published shape is pinned.
    /// </summary>
    private const string TwoFilms =
        """
        [{"i":807,"k":"807-110","t":"Se7en","y":1995,"rt":110,"g":[],"ag":"Thriller","vote":8.4,"dir":[],"cast":[],"mc":0.95},
         {"i":0,"k":"u-d2f1c6e2","t":"An Unlisted Film","rt":101,"g":[],"ag":"Documentary","vote":0,"dir":[],"cast":[],"mc":0}]
        """;

    /// <summary>The meta document for the same tiny catalogue, so <c>movieCount</c> agrees with it.</summary>
    private const string TwoFilmsMeta =
        """
        {"builtUtc":"2026-11-04T09:00:00Z","movieCount":2,"showCount":0,"totalEpisodeCount":0,
         "genres":{"53":"Thriller","99":"Documentary"},
         "attribution":"This website uses TMDB and the TMDB APIs but is not endorsed, certified, or otherwise approved by TMDB.",
         "reviewQueue":[]}
        """;

    /// <summary>
    /// Registers the announcer the way Program.cs does (one instance for the session) but on a clock the test
    /// owns, and the catalogue the way Program.cs does but against a stub handler. Returns the clock.
    /// Anything that renders MainLayout or App needs this, and since Task 5 anything that renders the films
    /// page needs the catalogue too -- which is why both live here and nowhere else: a second private copy of
    /// either means a new service has to be registered in two places.
    /// </summary>
    /// <remarks>
    /// BOTH documents are scripted, and they behave the same way. Only movies.json was, which would have cost
    /// the first Task 6 or 7 component that calls <c>GetMetaAsync()</c>: it would hit the stub's
    /// unscripted-path guard, have the refusal swallowed by <c>CatalogService</c>'s broad catch, and render a
    /// FAILED meta -- no build date, no genre names -- with every test in the suite still green. Meta mirrors
    /// the outcome rather than always succeeding, so a component reading meta in a Failed test sees a failed
    /// meta and not a stray good one.
    /// </remarks>
    public static ManualTimeProvider AddAnnouncer(this BunitContext context, CatalogOutcome catalog = CatalogOutcome.Ready)
    {
        var (clock, stub) = context.AddAnnouncerAndCatalog(Responder(catalog));

        stub.Serves(StubCatalog.Meta, MetaResponder(catalog));
        return clock;
    }

    /// <summary>
    /// As <see cref="AddAnnouncer"/>, but hands the catalogue stub back as well, for a test that needs to
    /// script more than one response -- a retry -- or to count requests. Responders are consumed in order and
    /// the last one repeats. The responders are the MOVIES script; meta is scripted Ready, and a test that
    /// wants it otherwise calls <c>Serves(StubCatalog.Meta, ...)</c> on the stub it gets back.
    /// </summary>
    public static (ManualTimeProvider Clock, StubCatalog Catalog) AddAnnouncerAndCatalog(
        this BunitContext context, params Func<Task<HttpResponseMessage>>[] responders)
    {
        var clock = new ManualTimeProvider();
        context.Services.AddSingleton(clock);
        context.Services.AddSingleton(new Announcer(clock));

        var stub = new StubCatalog()
            .Serves(StubCatalog.Movies, responders)
            .Serves(StubCatalog.Meta, MetaResponder(CatalogOutcome.Ready));
        var client = stub.Client();

        // Both, because Program.cs registers both and a later component may inject the client directly. And
        // SCOPED, as Program.cs has them: the service was registered as a singleton instance here, which is
        // harmless in bUnit -- it resolves everything from one provider, so the lifetimes coincide -- but it
        // was the one divergence from "the way Program.cs does it" that this summary claims. The client is
        // still a captured instance, because it is the one thing that CANNOT be built the way Program.cs
        // builds it: it has to be bound to this test's stub handler.
        context.Services.AddScoped(_ => client);
        context.Services.AddScoped<CatalogService>();

        return (clock, stub);
    }

    /// <summary>The movies.json response for an outcome.</summary>
    public static Func<Task<HttpResponseMessage>> Responder(CatalogOutcome outcome) => outcome switch
    {
        CatalogOutcome.Ready => StubCatalog.Json(TwoFilms),
        CatalogOutcome.Failed => StubCatalog.Throws(new HttpRequestException("the stub was asked to fail")),
        CatalogOutcome.NeverCompletes => StubCatalog.NeverAnswers(),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "no responder for this outcome"),
    };

    /// <summary>The meta.json response for the same outcome, so an outcome covers both documents.</summary>
    public static Func<Task<HttpResponseMessage>> MetaResponder(CatalogOutcome outcome) => outcome switch
    {
        CatalogOutcome.Ready => StubCatalog.Json(TwoFilmsMeta),
        CatalogOutcome.Failed => StubCatalog.Throws(new HttpRequestException("the stub was asked to fail")),
        CatalogOutcome.NeverCompletes => StubCatalog.NeverAnswers(),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "no responder for this outcome"),
    };
}

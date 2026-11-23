using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
#if DEBUG
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MovieLibrary.Web.Editor;
#endif
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
    /// <para>
    /// ONE FILM PER CONFIDENCE BAND, and the asymmetries between them are load-bearing rather than
    /// decorative. Every markup contract that runs through <c>MarkupContractTests</c> renders the app from
    /// this catalogue, so a band absent from here is a branch absent from every Ready scope.
    /// </para>
    /// <para>
    /// <b>The poster asymmetry.</b> Se7en carries a <c>p</c> and the other two do not. With no <c>p</c> on
    /// any film the films page emits no image at all: the img contracts' subject count stays at 1 --
    /// index.html's TMDB logo -- and they read as covering 60 posters while covering none of them. With one
    /// poster and two without, both tile branches are in every Ready scope.
    /// <c>TheImageAndControlContractsCanSeeTheFilmsPagesOwnMarkup</c> is what fails if the poster is ever
    /// removed from here.
    /// </para>
    /// <para>
    /// <b>The band asymmetry, added by Task 7 for the same reason.</b> This catalogue held a Confident film
    /// and an Unmatched one and no Uncertain one at all, so <c>Confidence.Uncertain</c> -- the band the
    /// "Needs review" badge is for, and the largest degraded band in the real catalogue -- had ZERO failable
    /// subjects anywhere in the app's own markup. A badge contract over these scopes would have read as
    /// covering the badges while covering one of the two badged bands: rule T1, one level up, and the same
    /// argument the poster asymmetry above records. The Uncertain film carries <c>mc</c> 0.55 with a real
    /// TMDB id, which is the shape <c>ConfidenceBand.Of</c> needs in order to tell "a poor match was
    /// accepted" from "no match was made at all" -- the same record
    /// <c>tests/fixtures/movies-three-records.json</c> carries, so the two fixtures agree.
    /// </para>
    /// <para>
    /// <b>And the degraded shape is real.</b> The Unmatched film has no <c>y</c>, no <c>p</c> and no cast,
    /// so <c>PosterTile</c>'s degraded-record branches are reached rather than stepped over. A fixture that
    /// gave it a synthetic year and a four-person cast would render a tile indistinguishable from a healthy
    /// one, which is the vacuity Task 6 recorded in <c>PosterTile.razor</c> in advance.
    /// </para>
    /// </summary>
    private const string OneFilmPerBand =
        """
        [{"i":807,"k":"807-110","t":"Se7en","y":1995,"rt":110,"p":"/se7en.jpg","g":[],"ag":"Thriller","vote":8.4,"dir":[],"cast":[],"mc":0.95},
         {"i":601,"k":"601-88","t":"The Peeled Edition","y":1982,"rt":88,"g":[],"ag":"Drama","vote":6.2,"dir":[],"cast":[],"mc":0.55},
         {"i":0,"k":"u-d2f1c6e2","t":"An Unlisted Film","rt":101,"g":[],"ag":"Documentary","vote":0,"dir":[],"cast":[],"mc":0}]
        """;

    /// <summary>The meta document for the same tiny catalogue, so <c>movieCount</c> agrees with it.</summary>
    private const string OneFilmPerBandMeta =
        """
        {"builtUtc":"2026-11-04T09:00:00Z","movieCount":3,"showCount":0,"totalEpisodeCount":0,
         "genres":{"18":"Drama","53":"Thriller","99":"Documentary"},
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

        // The SAME clock for the films page's debounce timers, registered as the abstract TimeProvider because
        // that is what the page injects. One clock for the announcer and the page is what lets a test step the
        // filter debounce, the announce debounce and the settle delay as one timeline instead of three.
        context.Services.AddSingleton<TimeProvider>(clock);

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

        // The films page writes its filter into the address through this, and Program.cs registers it the same way.
        context.Services.AddScoped<QueryState>();

#if DEBUG
        // The editor's registrations, the one call Program.cs makes under the same condition. Debug only,
        // because in Release the web project does not reference the editor, "/editor" is not routed and
        // nothing renders it. The environment is here as well because the page injects it and bUnit has no
        // WebAssembly host: Development, so the page shows its own content rather than the refusal. It is
        // TryAdd, so a test that wants Production registers its own first.
        context.Services.AddRecordEditor<EditorCatalogueSource, EditorStatusRegion>();
        context.Services.TryAddSingleton<IWebAssemblyHostEnvironment>(new DevelopmentHost());
#endif

        return (clock, stub);
    }

#if DEBUG
    private sealed class DevelopmentHost : IWebAssemblyHostEnvironment
    {
        public string Environment => "Development";
        public string BaseAddress => "http://localhost/";
    }
#endif

    /// <summary>The movies.json response for an outcome.</summary>
    public static Func<Task<HttpResponseMessage>> Responder(CatalogOutcome outcome) => outcome switch
    {
        CatalogOutcome.Ready => StubCatalog.Json(OneFilmPerBand),
        CatalogOutcome.Failed => StubCatalog.Throws(new HttpRequestException("the stub was asked to fail")),
        CatalogOutcome.NeverCompletes => StubCatalog.NeverAnswers(),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "no responder for this outcome"),
    };

    /// <summary>The meta.json response for the same outcome, so an outcome covers both documents.</summary>
    public static Func<Task<HttpResponseMessage>> MetaResponder(CatalogOutcome outcome) => outcome switch
    {
        CatalogOutcome.Ready => StubCatalog.Json(OneFilmPerBandMeta),
        CatalogOutcome.Failed => StubCatalog.Throws(new HttpRequestException("the stub was asked to fail")),
        CatalogOutcome.NeverCompletes => StubCatalog.NeverAnswers(),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "no responder for this outcome"),
    };
}

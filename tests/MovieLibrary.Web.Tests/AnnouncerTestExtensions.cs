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
    /// <para>
    /// <b>THE DETAIL FIELDS, AND EVERY ASYMMETRY IN THEM IS A BRANCH.</b> Se7en alone carries <c>b</c>,
    /// <c>ov</c>, <c>tag</c>, <c>col</c>, <c>own</c>, a real <c>dir</c>, a genre id the meta names, a cast and
    /// <c>ex</c>, because <c>MarkupContractTests</c> renders one scope at <see cref="FilmAddress"/> and a
    /// field absent from here is a branch absent from it. The other two films keep the detail page's
    /// everything-missing shape, which is what the <c>/movies/1</c> scope's not-found state does NOT cover.
    /// <list type="bullet">
    /// <item>The cast has three members on purpose: one with a photo AND a character, one with a character
    /// and no photo (the portrait placeholder), and one whose character is the empty string the pipeline
    /// writes for a missing one (the page renders the person alone, never "An Extra as").</item>
    /// <item><c>ex</c> carries four entries of which only two render: <c>Disc</c> has an empty VALUE and the
    /// fourth has an empty KEY. Both are the shape that passes every automated check and says nothing, so
    /// both have to be in front of the rule that drops them. <c>Shelf</c> and <c>Case</c> are also out of
    /// Ordinal order here, which is what makes the page's sort failable.</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <summary>
    /// The published key of the one film in this catalogue that carries EVERY field a detail page can render,
    /// so that nothing retypes it. <c>MarkupContractTests</c> renders a scope at <see cref="FilmAddress"/>.
    /// </summary>
    public const string FilmKey = "807-110";

    /// <summary>The detail page's address for <see cref="FilmKey"/>.</summary>
    public const string FilmAddress = "/movies/" + FilmKey;

    private const string OneFilmPerBand =
        """
        [{"i":807,"k":"807-110","t":"Se7en","y":1995,"rt":110,"p":"/se7en.jpg","b":"/se7en-wide.jpg","g":[53],"ag":"Thriller","vote":8.4,
          "dir":["David Fincher"],"ov":"Two detectives hunt a killer who uses the seven deadly sins as his motives.",
          "tag":"Seven deadly sins. Seven ways to die.","col":"The Se7en Collection","own":"New Line 3-Film Collection",
          "cast":[{"n":"Brad Pitt","c":"Mills","p":"/pitt.jpg"},{"n":"Morgan Freeman","c":"Somerset"},{"n":"An Extra","c":""}],
          "ex":{"Shelf":"B3","Disc":"","":"orphaned","Case":"keep case"},"mc":0.95},
         {"i":601,"k":"601-88","t":"The Peeled Edition","y":1982,"rt":88,"g":[],"ag":"Drama","vote":6.2,"dir":[],"cast":[],"mc":0.55},
         {"i":0,"k":"u-d2f1c6e2","t":"An Unlisted Film","rt":101,"g":[],"ag":"Documentary","vote":0,"dir":[],"cast":[],"mc":0}]
        """;

    /// <summary>The meta document for the same tiny catalogue, so <c>movieCount</c> agrees with it.</summary>
    private const string OneFilmPerBandMeta =
        """
        {"builtUtc":"2026-11-04T09:00:00Z","movieCount":3,"showCount":4,"totalEpisodeCount":17,
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
        stub.Serves(StubCatalog.Shows, ShowsResponder(catalog));

        // The series' own files mirror the outcome too, so there is no state in which the index fails and a
        // series file is quietly healthy.
        foreach (var (slug, _) in SeriesFiles)
        {
            stub.Serves(StubCatalog.Series(slug), SeriesResponder(slug, catalog));
        }

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

        // THE SERIES DOCUMENTS ARE SCRIPTED HERE AND NOT ONLY IN AddAnnouncer, for the reason the remark on
        // that method gives about meta: an unscripted path is swallowed by CatalogService's broad catch and
        // arrives as an ordinary Failed, so a test rendering /tv without them would have scanned a heading,
        // a sentence and a Try again button -- no tile, no badge, no pager -- and gone green.
        stub.Serves(StubCatalog.Shows, ShowsResponder(CatalogOutcome.Ready));
        foreach (var (slug, json) in SeriesFiles)
        {
            stub.Serves(StubCatalog.Series(slug), StubCatalog.Json(json));
        }

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

    /// <summary>
    /// The slug of the series that carries EVERY branch the series page renders, so nothing retypes it.
    /// </summary>
    public const string ShowSlug = "the-bureau";

    /// <summary>The series page's address for <see cref="ShowSlug"/>.</summary>
    public const string ShowAddress = "/tv/" + ShowSlug;

    /// <summary>
    /// A series whose match scored BELOW the accept threshold with a real TMDB id behind it, which is the
    /// one shape that makes <c>ShowDetail.Trusted</c> false while an episode list is still on screen.
    /// </summary>
    public const string UncertainShowSlug = "a-doubtful-series";

    /// <summary>A series that matched nothing: no year, no poster, nothing but the unplaced bucket.</summary>
    public const string UnmatchedShowSlug = "an-unlisted-series";

    /// <summary>A series that arrives with no seasons at all, which is the series page's fourth state.</summary>
    public const string BareShowSlug = "a-bare-series";

    /// <summary>
    /// The series index, and every asymmetry in it is a branch -- the same argument
    /// <see cref="OneFilmPerBand"/> records for the films.
    /// <list type="bullet">
    /// <item><b>One row per confidence band</b>, because <c>ConfidenceBand.Of(ShowSummary)</c> decides the
    /// tile's badge and a band absent from here is a badge absent from every Ready scope. The uncertain row
    /// carries a real id with <c>mc</c> 0.55; the unmatched row carries id 0.</item>
    /// <item><b>One poster between four rows</b>, so both of <c>PosterTile</c>'s art branches are reached
    /// on the series wall as well as on the films wall.</item>
    /// <item><b>The unmatched row has no <c>firstYear</c></b>, which is the degraded shape the real library
    /// has: it is also one of the rows whose <c>seasonCount</c> is 0 while it still owns episodes, because
    /// everything it owns sits in the unplaced bucket.</item>
    /// <item><b><see cref="BareShowSlug"/> owns nothing at all.</b> No series in the owner's library is in
    /// that state today, and it is here because the PAGE has a branch for it.</item>
    /// </list>
    /// </summary>
    private const string FourSeries =
        """
        [{"i":2309,"t":"The Bureau","slug":"the-bureau","p":"/bureau.jpg","firstYear":2015,"ag":"Drama",
          "seasonCount":2,"ownedEpisodes":7,"totalEpisodes":9,"mc":0.95},
         {"i":444,"t":"A Doubtful Series","slug":"a-doubtful-series","firstYear":1998,"ag":"Comedy",
          "seasonCount":2,"ownedEpisodes":2,"totalEpisodes":5,"mc":0.55},
         {"i":0,"t":"An Unlisted Series","slug":"an-unlisted-series","ag":"Home recordings",
          "seasonCount":0,"ownedEpisodes":3,"totalEpisodes":3,"mc":0},
         {"i":881,"t":"A Bare Series","slug":"a-bare-series","firstYear":2001,"ag":"Drama",
          "seasonCount":0,"ownedEpisodes":0,"totalEpisodes":0,"mc":0.9}]
        """;

    /// <summary>
    /// <see cref="ShowSlug"/>'s own file: three seasons, and each one is a branch of the page.
    /// <list type="bullet">
    /// <item><b>Season 0 is the UNPLACED bucket</b> -- not the specials -- so it proves both that the page
    /// calls it "Season unknown" and that <c>Ordered</c> sorts it after the numbered seasons although the
    /// build writes it first. Its episodes are numbered from 1, as <c>TvPipeline</c> numbers them, so no
    /// two rows in one season share a <c>@key</c>.</item>
    /// <item><b>Season 1 is incomplete and season 2 is whole</b>, so the "Missing" mark has a row to sit on
    /// and a row to be absent from.</item>
    /// <item><b>One episode carries every optional field and the rest carry none</b>, which is the shape a
    /// row has to survive: the page prints the number and the title and nothing else, so an episode with an
    /// air date, a runtime, a still and an overview may not render any of them.</item>
    /// <item><b><c>ex</c> is the owner's own columns</b>, which is the "Additional information" section.</item>
    /// </list>
    /// </summary>
    private const string TheBureau =
        """
        {"i":2309,"t":"The Bureau","slug":"the-bureau","p":"/bureau.jpg","firstYear":2015,"ag":"Drama",
         "ownedEpisodes":7,"totalEpisodes":9,"mc":0.95,"ex":{"Shelf":"C1"},
         "seasons":[
           {"n":0,"owned":2,"total":2,"eps":[
             {"e":1,"t":"A Row That Matched Nothing","o":true},
             {"e":2,"t":"Another Stray Row","o":true}]},
           {"n":1,"owned":3,"total":4,"eps":[
             {"e":1,"t":"Pilot","o":true,"air":"2015-04-27","rt":52,"s":"/pilot.jpg","ov":"An agent comes home."},
             {"e":2,"t":"The Second","o":true},
             {"e":3,"t":"The Missing One","o":false},
             {"e":4,"t":"The Fourth","o":true}]},
           {"n":2,"owned":2,"total":2,"eps":[
             {"e":1,"t":"Return","o":true},
             {"e":2,"t":"Cover","o":true}]}]}
        """;

    /// <summary>
    /// <see cref="UncertainShowSlug"/>'s own file. Season 1 holds owned and unowned episodes TOGETHER, which
    /// is what makes the untrusted page's filter failable; season 2 holds nothing the owner has, so the page
    /// renders a season whose list is empty rather than hiding the season.
    /// </summary>
    private const string ADoubtfulSeries =
        """
        {"i":444,"t":"A Doubtful Series","slug":"a-doubtful-series","firstYear":1998,"ag":"Comedy",
         "ownedEpisodes":2,"totalEpisodes":5,"mc":0.55,
         "seasons":[
           {"n":1,"owned":2,"total":3,"eps":[
             {"e":1,"t":"Owned One","o":true},
             {"e":2,"t":"Never Had This","o":false},
             {"e":3,"t":"Owned Two","o":true}]},
           {"n":2,"owned":0,"total":2,"eps":[
             {"e":1,"t":"None Of It","o":false},
             {"e":2,"t":"Nor This","o":false}]}]}
        """;

    /// <summary>
    /// <see cref="UnmatchedShowSlug"/>'s own file: the unplaced bucket and nothing else, which is the shape
    /// the 7 series in the owner's library with no numbered season actually have.
    /// </summary>
    private const string AnUnlistedSeries =
        """
        {"i":0,"t":"An Unlisted Series","slug":"an-unlisted-series","ag":"Home recordings",
         "ownedEpisodes":3,"totalEpisodes":3,"mc":0,
         "seasons":[
           {"n":0,"owned":3,"total":3,"eps":[
             {"e":1,"t":"Tape 1","o":true},
             {"e":2,"t":"Tape 2","o":true},
             {"e":3,"t":"Tape 3","o":true}]}]}
        """;

    /// <summary>
    /// <see cref="BareShowSlug"/>'s own file: a series that matched and carries no episode list at all.
    /// </summary>
    private const string ABareSeries =
        """
        {"i":881,"t":"A Bare Series","slug":"a-bare-series","firstYear":2001,"ag":"Drama",
         "ownedEpisodes":0,"totalEpisodes":0,"mc":0.9,"seasons":[]}
        """;

    /// <summary>Every series file this fixture publishes, keyed by the slug the index spells.</summary>
    private static readonly (string Slug, string Json)[] SeriesFiles =
    [
        (ShowSlug, TheBureau),
        (UncertainShowSlug, ADoubtfulSeries),
        (UnmatchedShowSlug, AnUnlistedSeries),
        (BareShowSlug, ABareSeries),
    ];

    /// <summary>The movies.json response for an outcome.</summary>
    public static Func<Task<HttpResponseMessage>> Responder(CatalogOutcome outcome) => outcome switch
    {
        CatalogOutcome.Ready => StubCatalog.Json(OneFilmPerBand),
        CatalogOutcome.Failed => StubCatalog.Throws(new HttpRequestException("the stub was asked to fail")),
        CatalogOutcome.NeverCompletes => StubCatalog.NeverAnswers(),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "no responder for this outcome"),
    };

    /// <summary>
    /// The movies.json response for an outcome, served after yielding so the page renders at least once
    /// while the fetch is still in flight. See <c>StubCatalog.JsonAfterYielding</c> for why that difference
    /// is observable; the Failed and NeverCompletes arms are the same ones, because Failed already has an
    /// asynchronous spelling and NeverCompletes never answers at all.
    /// </summary>
    public static Func<Task<HttpResponseMessage>> ColdResponder(CatalogOutcome outcome) => outcome switch
    {
        CatalogOutcome.Ready => StubCatalog.JsonAfterYielding(OneFilmPerBand),
        CatalogOutcome.Failed => StubCatalog.ThrowsAfterYielding(new HttpRequestException("the stub was asked to fail")),
        CatalogOutcome.NeverCompletes => StubCatalog.NeverAnswers(),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "no responder for this outcome"),
    };

    /// <summary>The shows.json response for an outcome.</summary>
    public static Func<Task<HttpResponseMessage>> ShowsResponder(CatalogOutcome outcome) => outcome switch
    {
        CatalogOutcome.Ready => StubCatalog.Json(FourSeries),
        CatalogOutcome.Failed => StubCatalog.Throws(new HttpRequestException("the stub was asked to fail")),
        CatalogOutcome.NeverCompletes => StubCatalog.NeverAnswers(),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "no responder for this outcome"),
    };

    /// <summary>One series file's response for an outcome.</summary>
    public static Func<Task<HttpResponseMessage>> SeriesResponder(string slug, CatalogOutcome outcome) => outcome switch
    {
        CatalogOutcome.Ready => StubCatalog.Json(SeriesFiles.First(f => f.Slug == slug).Json),
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

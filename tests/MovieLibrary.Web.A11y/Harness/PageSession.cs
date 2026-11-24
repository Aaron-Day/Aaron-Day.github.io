using Microsoft.Playwright;
using MovieLibrary.Web.Pages;
using MovieLibrary.Web.Services;

namespace MovieLibrary.Web.A11y.Harness;

/// <summary>
/// One browser context and one page, plus the drivers that put the films page into each of its states.
/// </summary>
/// <remarks>
/// <b>Every wait is on a known selector or a known string, and never on a fixed sleep.</b> A gate that slept
/// would be slow on a fast machine and wrong on a slow one, and - worse - a scan that ran before the state
/// arrived would report a clean pass on a DOM that was not the one under test. That is the same false pass a
/// static-HTML scanner gives for the boot shell, reached by a different route.
/// </remarks>
public sealed class PageSession : IAsyncDisposable
{
    /// <summary>
    /// The selector every wait-for-boot in this project uses. Public so that the live guard asserting the
    /// boot-failure shell cannot match it reads the same string this waits on. See <see cref="BootedAsync"/>.
    /// </summary>
    public const string BootedSelector = "#app main#main-content h1";

    /// <summary>The catalogue address, as CatalogService requests it, matched for route interception.</summary>
    public const string CataloguePattern = "**/data/movies.json";

    /// <summary>
    /// The series index, and every series' own file: what a scan of the SERIES pages' loading and failure
    /// states has to intercept.
    /// </summary>
    /// <remarks>
    /// <b>These are why the methods below take a pattern.</b> They were hard-wired to
    /// <see cref="CataloguePattern"/>, which is movies.json alone -- so there was no way to put the series
    /// list or a series' own page into its loading or failed state from here, and neither state was ever
    /// scanned in a browser although both render a container, a sentence and a Try again button of their
    /// own. A pattern that covered every data file instead would have been worse: the films page's own
    /// failure scans assert the exact number of catalogue requests, and a wildcard would have counted the
    /// series documents into them.
    /// </remarks>
    public const string ShowsPattern = "**/data/shows.json";

    public const string SeriesPattern = "**/data/tv/*.json";

    /// <summary>
    /// Long enough for a cold WebAssembly boot on a loaded CI runner, short enough that a genuinely stuck
    /// page fails the gate instead of hanging it. Playwright's own default is 30 s; the boot is the slow part.
    /// </summary>
    private const int BootTimeout = 60_000;

    private readonly IBrowserContext context;
    private readonly string baseUrl;
    private int catalogueCalls;

    public PageSession(IPage page, IBrowserContext context, string baseUrl)
    {
        Page = page;
        this.context = context;
        this.baseUrl = baseUrl;
    }

    public IPage Page { get; }

    /// <summary>How many times the page asked for the catalogue. A subject floor for the state drivers.</summary>
    public int CatalogueCalls => catalogueCalls;

    /// <summary>The sentences the page renders, read from the page's own constants and never retyped.</summary>
    public static string CouldNotLoad => Movies.CouldNotLoad;

    public static string LoadingVisible => Movies.LoadingVisible;

    public static string LoadingSpoken => Movies.LoadingSpoken;

    /// <summary>The film page's own sentences. Different strings from the films page's, and deliberately so:
    /// the gate tells the two pages apart by the words on screen as well as by the container's class.</summary>
    public static string FilmCouldNotLoad => FilmDetail.CouldNotLoad;

    public static string FilmLoadingVisible => FilmDetail.LoadingVisible;

    public static string NoSuchFilm => FilmDetail.NoSuchFilm;

    /// <summary>The SERIES LIST's own sentences: a third set of strings for the same reason the film page
    /// has a second, and the series PAGE below has a fourth.</summary>
    public static string TvCouldNotLoad => Tv.CouldNotLoad;

    public static string TvLoadingVisible => Tv.LoadingVisible;

    /// <summary>The SERIES page's own sentences.</summary>
    public static string ShowCouldNotLoad => ShowDetail.CouldNotLoad;

    public static string ShowLoadingVisible => ShowDetail.LoadingVisible;

    public static string NoSuchShow => ShowDetail.NoSuchShow;

    public string UrlFor(string address) => baseUrl + "/" + address.TrimStart('/');

    /// <summary>Loads an address and waits for Blazor to have replaced the boot shell with a real page.</summary>
    public async Task<IResponse?> GotoAsync(string address)
    {
        var response = await Page.GotoAsync(UrlFor(address), new PageGotoOptions { Timeout = BootTimeout });
        await BootedAsync();
        return response;
    }

    /// <summary>
    /// The one signal that Blazor has replaced the boot shell with a real page. <c>main#main-content</c> is
    /// MainLayout's own landmark - <c>MovieLibrary.Web.Tests.ShellTests</c> pins both the element and its id -
    /// and the <c>h1</c> inside it is the rendered page's own heading, so this says the layout AND the page
    /// are up.
    /// </summary>
    /// <remarks>
    /// <b>This used to be <c>#app h1</c>, which cannot survive the boot shell gaining a heading.</b> That
    /// gate worked only because index.html's shell happened to have no heading in it - and giving the shell
    /// a main landmark and an h1 is a real improvement for a reader navigating a failed boot, so the gate
    /// rested on something the project had a reason to change. With a heading in the shell, <c>#app h1</c>
    /// matches before WebAssembly starts: every GotoAsync returns on the parsed document,
    /// <c>.catalog-state</c> is absent so the settle wait waits for nothing, and axe scans the un-booted
    /// shell - which, with a landmark and a heading, may well be clean. Fifteen green scans of a page the
    /// gate never rendered, which is precisely the false pass this project exists to rule out. So the signal
    /// is MainLayout's own landmark, which the shell may never reproduce, and
    /// <c>BootFailureTests.TheBootGateSelectorCannotMatchTheBootFailureShell</c> is the live guard that keeps
    /// it that way.
    /// </remarks>
    public Task BootedAsync() =>
        Page.WaitForSelectorAsync(BootedSelector, new PageWaitForSelectorOptions { Timeout = BootTimeout });

    /// <summary>
    /// Holds the catalogue fetch open for ever, which is the Loading state. The handler returns without
    /// resolving the route, so the request stays in flight exactly as a stalled download does.
    /// </summary>
    public Task HangTheCatalogueAsync(string pattern = CataloguePattern) =>
        context.RouteAsync(pattern, _ => Interlocked.Increment(ref catalogueCalls));

    /// <summary>Fails the catalogue fetch, which is the Failed state.</summary>
    public Task FailTheCatalogueAsync(string pattern = CataloguePattern) =>
        context.RouteAsync(pattern, async route =>
        {
            Interlocked.Increment(ref catalogueCalls);
            await route.AbortAsync("connectionfailed");
        });

    /// <summary>
    /// Fails the FIRST fetch and holds every later one open: the Failed state, and then - after Try again -
    /// the retry-in-flight state, where the button carries aria-disabled and keeps its place.
    /// </summary>
    public Task FailThenHangTheCatalogueAsync() =>
        context.RouteAsync(CataloguePattern, async route =>
        {
            if (Interlocked.Increment(ref catalogueCalls) == 1) await route.AbortAsync("connectionfailed");
        });

    /// <summary>Serves a catalogue body of this harness's choosing.</summary>
    public Task ServeCatalogueAsync(string json) =>
        context.RouteAsync(CataloguePattern, async route =>
        {
            Interlocked.Increment(ref catalogueCalls);
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "application/json; charset=utf-8",
                Body = json,
            });
        });

    /// <summary>Stops the catalogue being intercepted, so a retry reaches the served file and succeeds.</summary>
    public Task ReleaseTheCatalogueAsync() => context.UnrouteAsync(CataloguePattern);

    /// <summary>Waits for the one-sentence state paragraph to hold a sentence.</summary>
    public Task StateSentenceAsync(string sentence) => SentenceAsync(".catalog-state p", sentence);

    /// <summary>Waits for the one-sentence state paragraph on the FILM page to hold a sentence.</summary>
    public Task FilmSentenceAsync(string sentence) => SentenceAsync(".film-state p", sentence);

    /// <summary>Waits for the one-sentence state paragraph on the SERIES page to hold a sentence.</summary>
    public Task ShowSentenceAsync(string sentence) => SentenceAsync(".show-state p", sentence);

    /// <summary>Waits for the one-sentence state paragraph on the SERIES LIST to hold a sentence.</summary>
    public Task TvSentenceAsync(string sentence) => SentenceAsync(".tv-state p", sentence);

    /// <summary>
    /// One wait, parameterised by the container, because the three above differ in nothing else. The third
    /// page is what moved it: each page has a state container of its own so the gate can tell which page it
    /// is looking at, and three copies of one Playwright wait is how that design turns into drift.
    /// </summary>
    private Task SentenceAsync(string selector, string sentence) =>
        Page.Locator(selector)
            .Filter(new LocatorFilterOptions { HasTextString = sentence })
            .WaitForAsync(new LocatorWaitForOptions { Timeout = BootTimeout });

    /// <summary>
    /// Waits for a DETAIL page -- a film or a series -- to have its subject on it.
    /// </summary>
    /// <remarks>
    /// <c>dl.facts</c> is the Details list, which the Ready branch of either page always renders and which
    /// none of their other branches can, so it is the one signal that separates a loaded subject from one
    /// still loading and from an address no record answers. On the series page it is rendered in EVERY
    /// Ready branch, including the series that matched with no episode list, where there are no seasons to
    /// wait for and waiting on them would hang the gate on a page that is perfectly fine.
    /// </remarks>
    public Task SubjectAsync() =>
        Page.Locator("dl.facts").First.WaitForAsync(new LocatorWaitForOptions { Timeout = BootTimeout });

    /// <summary>Waits for the grid to hold a given number of tiles.</summary>
    public Task GridAsync(int tiles = Catalogue.PageSize) =>
        Page.WaitForFunctionAsync(Scripts.TileCountIs, tiles,
            new PageWaitForFunctionOptions { Timeout = BootTimeout });

    /// <summary>
    /// Waits for one of the shell's announcer regions to hold text. An announcement arrives one settle delay
    /// after the render that armed it - see Announcer.SettleDelay - so this is the only honest way to scan a
    /// state in which a live region is non-empty.
    /// </summary>
    public Task AnnouncedAsync(string regionId, string text) =>
        Page.WaitForFunctionAsync(Scripts.RegionHolds, new[] { regionId, text },
            new PageWaitForFunctionOptions
            {
                Timeout = BootTimeout + (int)(Announcer.SettleDelay.TotalMilliseconds * 10),
            });

    /// <summary>The id of whatever holds focus, or the empty string when the body does.</summary>
    public Task<string> FocusedIdAsync() => Page.EvaluateAsync<string>(Scripts.FocusedId);

    /// <summary>A description of whatever holds focus, for a failure message a reader can act on.</summary>
    public Task<string> FocusedDescriptionAsync() => Page.EvaluateAsync<string>(Scripts.DescribeFocus);

    public async ValueTask DisposeAsync() => await context.CloseAsync();
}

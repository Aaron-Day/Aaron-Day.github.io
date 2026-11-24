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
    private const string CataloguePattern = "**/data/movies.json";

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
    public Task HangTheCatalogueAsync() =>
        context.RouteAsync(CataloguePattern, _ => Interlocked.Increment(ref catalogueCalls));

    /// <summary>Fails the catalogue fetch, which is the Failed state.</summary>
    public Task FailTheCatalogueAsync() =>
        context.RouteAsync(CataloguePattern, async route =>
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
    public Task StateSentenceAsync(string sentence) =>
        Page.Locator(".catalog-state p")
            .Filter(new LocatorFilterOptions { HasTextString = sentence })
            .WaitForAsync(new LocatorWaitForOptions { Timeout = BootTimeout });

    /// <summary>Waits for the one-sentence state paragraph on the FILM page to hold a sentence.</summary>
    public Task FilmSentenceAsync(string sentence) =>
        Page.Locator(".film-state p")
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

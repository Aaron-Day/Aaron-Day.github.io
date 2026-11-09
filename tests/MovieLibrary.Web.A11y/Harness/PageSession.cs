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

    public string UrlFor(string address) => baseUrl + "/" + address.TrimStart('/');

    /// <summary>Loads an address and waits for Blazor to have replaced the boot shell with a real page.</summary>
    public async Task<IResponse?> GotoAsync(string address)
    {
        var response = await Page.GotoAsync(UrlFor(address), new PageGotoOptions { Timeout = BootTimeout });
        await BootedAsync();
        return response;
    }

    /// <summary>
    /// Waits for the app to have booted. <c>#app h1</c> is the signal because index.html's boot shell has no
    /// heading in it: while the shell is up the selector cannot match, so this cannot pass on the shell.
    /// </summary>
    public Task BootedAsync() =>
        Page.WaitForSelectorAsync("#app h1", new PageWaitForSelectorOptions { Timeout = BootTimeout });

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

    /// <summary>Answers every poster request from inside this process with a 1x1 PNG.</summary>
    /// <remarks>
    /// The fixture carries no poster path, so nothing requests a poster and this does nothing - except in the
    /// one scan that deliberately gives a single record a path, to put a real img element in front of axe.
    /// That scan still reaches no CDN, because the request is answered here. Keep it that way: sixty live
    /// requests to image.tmdb.org per scanned state, from CI on a public repository, would make this gate red
    /// for CDN reasons rather than accessibility ones.
    /// </remarks>
    public Task StubPostersAsync() =>
        context.RouteAsync("https://image.tmdb.org/**", async route =>
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "image/png",
                BodyBytes = OnePixelPng,
            }));

    private static byte[] OnePixelPng => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    /// <summary>Waits for the one-sentence state paragraph to hold a sentence.</summary>
    public Task StateSentenceAsync(string sentence) =>
        Page.Locator(".catalog-state p")
            .Filter(new LocatorFilterOptions { HasTextString = sentence })
            .WaitForAsync(new LocatorWaitForOptions { Timeout = BootTimeout });

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

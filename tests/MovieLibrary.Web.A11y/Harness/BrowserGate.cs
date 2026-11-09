using Microsoft.Playwright;

namespace MovieLibrary.Web.A11y.Harness;

/// <summary>
/// One browser and one server for the whole test run, shared through a single xunit collection.
/// </summary>
/// <remarks>
/// <para>
/// One collection for every class in this project, which means the classes do not run in parallel. That is
/// deliberate: these tests measure layout at five viewports and read <c>document.activeElement</c>, and both
/// are cheaper and steadier with one browser answering one page at a time than with a browser per class.
/// </para>
/// <para>
/// <b>Nothing in here throws when the harness is missing.</b> <see cref="InitializeAsync"/> returns having done
/// nothing, and every test that would have used it was already Skipped by <see cref="Gate.Unavailable"/> at
/// discovery. A fixture that threw would turn the whole collection red on a machine with no browser.
/// </para>
/// </remarks>
public sealed class BrowserGate : IAsyncLifetime
{
    private IPlaywright? playwright;
    private IBrowser? browser;
    private SiteServer? server;

    /// <summary>The origin the published site is answering on, with no trailing slash.</summary>
    public string BaseUrl { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        if (Gate.Unavailable is not null) return;

        if (Gate.BaseUrl is { } external)
        {
            BaseUrl = external.TrimEnd('/');
        }
        else
        {
            var root = Gate.SiteRoot!;
            Catalogue.WriteInto(root);
            server = await SiteServer.StartAsync(root);
            BaseUrl = server.BaseUrl;
        }

        playwright = await Playwright.CreateAsync();
        browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Channel = Gate.Channel,   // null means Playwright's own Chromium
            Headless = true,
        });
    }

    /// <summary>
    /// A fresh browser context sized to one viewport, wrapped in the helpers the state drivers need. Every
    /// test opens its own: a context carries the route interceptions that produce the Loading and Failed
    /// states, and those must not leak into the next test.
    /// </summary>
    public async Task<PageSession> OpenAsync(int width = 1280, int height = 1024)
    {
        var context = await browser!.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },

            // The app declares color-scheme: dark and its contrast is measured against the dark palette. A
            // context defaulting to light would have axe evaluate colours the site never shows.
            ColorScheme = ColorScheme.Dark,

            // Blazor WebAssembly takes its culture from the browser, which this project decided is correct
            // behaviour and not a bug to stamp out - so the gate pins the browser rather than the app. Any
            // assertion on a grouped number ("1,456") would otherwise depend on the runner's own locale.
            Locale = "en-US",
        });

        await StubPostersAsync(context);

        return new PageSession(await context.NewPageAsync(), context, BaseUrl);
    }

    /// <summary>Answers every poster request from inside this process with a 1x1 PNG.</summary>
    /// <remarks>
    /// <b>Registered on EVERY context, which is a budget rule and not a convenience.</b> It used to be a
    /// per-test call with one caller, and that left a hole on a path this project documents: Gate's own skip
    /// message offers A11Y_BASE_URL as a way to scan a server already running the published site, and on
    /// that path <c>Catalogue.WriteInto</c> never runs - this class writes the synthetic catalogue only when
    /// it is serving the site itself. A published catalogue carrying real poster paths would therefore have
    /// had every Ready-state scan render img elements pointing at <c>https://image.tmdb.org/t/p/w342...</c>,
    /// and <c>loading="lazy"</c> caps that at the tiles above the fold rather than at zero: dozens of CDN
    /// reads per scanned state across about ten scans, from CI, on a public repository. They spend none of
    /// the owner's TMDB request budget, but a slow or flaky CDN would make an accessibility gate red for a
    /// reason that has nothing to do with accessibility, and Harness/Catalogue.cs states that as a rule.
    /// Registering the route here means NO code path in this project can reach the CDN, whatever catalogue
    /// it finds.
    /// <para>
    /// axe's image-alt does not need the bytes to arrive, only the element, so a 1x1 PNG is enough for the
    /// one scan that deliberately gives a record a poster path.
    /// </para>
    /// </remarks>
    private static Task StubPostersAsync(IBrowserContext context) =>
        context.RouteAsync("https://image.tmdb.org/**", async route =>
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "image/png",
                BodyBytes = OnePixelPng,
            }));

    private static byte[] OnePixelPng => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    public async Task DisposeAsync()
    {
        if (browser is not null) await browser.CloseAsync();
        playwright?.Dispose();
        if (server is not null) await server.DisposeAsync();
    }
}

/// <summary>
/// The single collection every class in this project belongs to. See <see cref="BrowserGate"/> for why there
/// is only one.
/// </summary>
[CollectionDefinition(Name)]
public sealed class BrowserCollection : ICollectionFixture<BrowserGate>
{
    public const string Name = "browser";
}

using MovieLibrary.Web.A11y.Harness;
using Xunit.Abstractions;

namespace MovieLibrary.Web.A11y;

/// <summary>
/// Every address a visitor can land on, scanned in the state they land in.
/// </summary>
/// <remarks>
/// <para>
/// Five addresses over three page types: the route templates read from RouteAttribute - "/", "/movies",
/// "/movies/{Key}" with a key the gate's own catalogue carries, and "/not-found" - plus "/zzz", which matches
/// no route and so reaches the not-found page by the router's own NotFoundPage path rather than by its route.
/// Adding a page extends this scan; nothing here needs editing for that.
/// </para>
/// <para>
/// <b>A static-HTML scanner would be useless for all of this.</b> Anything that fetches index.html without
/// executing WebAssembly sees the boot shell - a spinner, a visually-hidden status line and an error bar that
/// is display: none - and reports a clean pass on an app it never rendered. Every scan here drives a real
/// browser and waits on a selector that cannot exist until Blazor has replaced the shell.
/// </para>
/// </remarks>
[Collection(BrowserCollection.Name)]
public class RouteScanTests(BrowserGate gate, ITestOutputHelper output)
{
    /// <summary>
    /// A floor rather than an exact list, so that adding a page extends the scan instead of failing this.
    /// Rule T1: a theory driven by a discovered set proves nothing if the set can quietly empty, and this is
    /// the assertion that says it has not.
    /// </summary>
    [Fact]
    public void TheScanHasRealAddressesToScan()
    {
        var addresses = Site.Addresses();
        output.WriteLine("addresses: " + string.Join(" ", addresses));

        Assert.True(addresses.Count >= 5,
            "The route scan found " + addresses.Count + " address(es). The site serves at least five: \"/\" and "
            + "\"/movies\" (both the films page), one film, \"/not-found\", and an unmatched address. Fewer "
            + "means the reflection over RouteAttribute stopped finding pages, and every scan below would pass "
            + "vacuously.");

        Assert.Contains("/", addresses);
        Assert.Contains("/not-found", addresses);
        Assert.Contains(Site.UnmatchedUrl, addresses);
        Assert.Contains("/movies/" + Catalogue.FirstKey, addresses);

        // An address Site could not fill is the hole this guard exists for: a template with an unfilled
        // parameter in it is not a URL, the page answers it with its not-found branch, and the scan stays
        // green over a page that renders almost nothing. Loud here, rather than silent in ten scans.
        Assert.DoesNotContain(addresses, a => a.Contains('{', StringComparison.Ordinal));
    }

    [BrowserTheory]
    [MemberData(nameof(Site.AddressData), MemberType = typeof(Site))]
    public async Task EveryAddressIsCleanOnArrival(string address)
    {
        await using var session = await gate.OpenAsync();
        var response = await session.GotoAsync(address);

        // The unmatched address is served 404.html - a copy of index.html made by the deploy workflow - with a
        // real 404, which is what GitHub Pages does rather than rewriting to a 200. Recorded because it is
        // surprising, not because anything depends on it: the rendered DOM is identical either way.
        output.WriteLine($"{address} -> HTTP {response?.Status}");

        await SettledAsync(session);

        var scan = await AxeScan.OfAsync(session.Page, $"booted at {address}", output);

        // Rule T1 from the applicability side, per address. Every booted page has the skip link and the
        // nav drawer's li elements; link-in-text-block has a subject only where a link sits inside a block
        // of prose, which in Part A is the not-found page's "Back to the library" and nothing on the films
        // page. Measured, not assumed. Naming it per address means the day the films page gains an in-prose
        // link this scan starts covering it, and the day the not-found page loses its link this scan says
        // so instead of quietly covering one criterion less than it reads as covering.
        scan.MustBeClean(InProse(address)
            ? ["skip-link", "listitem", "link-in-text-block"]
            : ["skip-link", "listitem"]);
    }

    /// <summary>Whether this address renders a link inside a block of prose. Both go to NotFound.razor.</summary>
    private static bool InProse(string address) => address is "/not-found" or Site.UnmatchedUrl;

    /// <summary>
    /// Waits for the address to have finished becoming whatever it becomes, so the scan cannot run against a
    /// page in transition. On the films page that means the grid, on a film page the Details list; everywhere
    /// else the heading is the end of it.
    /// </summary>
    /// <remarks>
    /// <b>The film page is asked about FIRST, and the order is the whole point.</b> Both pages render a state
    /// container with a state paragraph in it, and the two class names - <c>.catalog-state</c> and
    /// <c>.film-state</c> - exist only so that this can tell them apart; they share every styling rule in
    /// app.css. A film page reaching the <c>.catalog-state</c> arm would wait for a tile grid that is never
    /// coming and fail the gate on a timeout, and one reaching neither arm would be scanned mid-fetch, with
    /// the heading reading "Loading film" and none of the page rendered.
    /// </remarks>
    private static async Task SettledAsync(PageSession session)
    {
        if (await session.Page.Locator(".film-state").CountAsync() > 0)
        {
            await session.FilmAsync();
            return;
        }

        if (await session.Page.Locator(".catalog-state").CountAsync() > 0) await session.GridAsync();
    }
}

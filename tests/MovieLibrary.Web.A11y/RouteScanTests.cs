using MovieLibrary.Web.A11y.Harness;
using Xunit.Abstractions;

namespace MovieLibrary.Web.A11y;

/// <summary>
/// Every address a visitor can land on, scanned in the state they land in.
/// </summary>
/// <remarks>
/// <para>
/// Four addresses over two page types: the route templates read from RouteAttribute - "/", "/movies" and
/// "/not-found", all three real reachable URLs - plus "/zzz", which matches no route and so reaches the
/// not-found page by the router's own NotFoundPage path rather than by its route. Tasks 6 and 8 extend this
/// scan by adding a page; nothing here needs editing for that.
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

        Assert.True(addresses.Count >= 4,
            "The route scan found " + addresses.Count + " address(es). Part A serves at least four: \"/\" and "
            + "\"/movies\" (both the films page), \"/not-found\", and an unmatched address. Fewer means the "
            + "reflection over RouteAttribute stopped finding pages, and every scan below would pass "
            + "vacuously.");

        Assert.Contains("/", addresses);
        Assert.Contains("/not-found", addresses);
        Assert.Contains(Site.UnmatchedUrl, addresses);
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
        scan.MustBeClean();
    }

    /// <summary>
    /// Waits for the address to have finished becoming whatever it becomes, so the scan cannot run against a
    /// page in transition. On the films page that means the grid; everywhere else the heading is the end of it.
    /// </summary>
    private static async Task SettledAsync(PageSession session)
    {
        if (await session.Page.Locator(".catalog-state").CountAsync() > 0) await session.GridAsync();
    }
}

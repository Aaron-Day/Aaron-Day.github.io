using Microsoft.AspNetCore.Components;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// A navigation manager that tells the router about a navigation LATER, when the test says so, and until then keeps
/// reporting the address it had before the navigation.
/// </summary>
/// <remarks>
/// <para>
/// bUnit's own manager moves <see cref="NavigationManager.Uri"/> and raises the location change inside the
/// <c>NavigateTo</c> call, so the router hands the page its own address back before the handler that wrote it has
/// returned. In a browser that is not the order: Blazor WebAssembly sets <c>Uri</c> only when the location change is
/// reported to .NET, which is an asynchronous call back from JavaScript, so a keystroke can be dispatched between
/// the page writing the address and the router reporting it, and in that gap <c>Uri</c> still holds the OLD address.
/// Every test that ran against the synchronous manager was therefore blind to both halves of the one interleaving
/// that matters for typing: the page has moved on, the late echo describes where it WAS, and anything that builds an
/// address from <c>Uri</c> in the meantime (<c>QueryState.WritePage</c> names only the page and inherits the rest)
/// builds it from a base that has not caught up.
/// </para>
/// <para>
/// Two things are modelled and nothing cleverer. A navigation does not move <see cref="NavigationManager.Uri"/>:
/// <see cref="BrowserAddress"/> is where the browser now is, and <see cref="Deliver"/> is the report that moves
/// <c>Uri</c> to it and raises one location change for each navigation still pending. When several are pending
/// <c>Uri</c> goes straight to the newest, so a late report of an earlier write reads as the later address, which is
/// what the page's <c>AdoptAddress</c> assumes of the router. That last part is an assumption this double makes
/// and does not check against the real manager: if WebAssembly reports each write's own address instead, a late
/// echo would look like a link followed, and no test here would show it.
/// </para>
/// <para>
/// The first address is not a navigation: Blazor is handed it at boot. Until <see cref="BootFinished"/> is called a
/// navigation therefore applies at once, which is how the app is started at an address; after it, they are late.
/// </para>
/// </remarks>
internal sealed class DeferredNavigationManager : NavigationManager
{
    private int pending;
    private bool booted;

    public DeferredNavigationManager() => Initialize("http://localhost/", "http://localhost/");

    /// <summary>Every navigation made, in order, and whether it asked to replace the history entry.</summary>
    public List<(string Uri, bool Replace)> Navigations { get; } = [];

    /// <summary>
    /// Where the browser is: the newest navigation, reported or not. <see cref="NavigationManager.Uri"/> is only this
    /// once the report has been delivered.
    /// </summary>
    public string BrowserAddress { get; private set; } = "http://localhost/";

    protected override void NavigateToCore(string uri, NavigationOptions options)
    {
        BrowserAddress = ToAbsoluteUri(uri).ToString();
        Navigations.Add((BrowserAddress, options.ReplaceHistoryEntry));

        if (!booted)
        {
            Uri = BrowserAddress;
            return;
        }

        pending++;
    }

    /// <summary>
    /// The app is up and listening: the address it started at has been taken, and from here a navigation is
    /// reported late. Forgets any the router was not yet listening to hear about.
    /// </summary>
    public void BootFinished()
    {
        booted = true;
        pending = 0;
    }

    /// <summary>Reports every pending navigation to whoever is listening, moving <c>Uri</c> to the newest first.</summary>
    public void Deliver()
    {
        var count = pending;
        pending = 0;

        if (count == 0) return;

        Uri = BrowserAddress;

        for (var i = 0; i < count; i++) NotifyLocationChanged(isInterceptedLink: false);
    }
}

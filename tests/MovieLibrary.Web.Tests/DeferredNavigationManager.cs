using Microsoft.AspNetCore.Components;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// A navigation manager that applies a navigation to <see cref="NavigationManager.Uri"/> at once and tells the
/// router about it LATER, when the test says so.
/// </summary>
/// <remarks>
/// <para>
/// bUnit's own manager raises the location change inside the <c>NavigateTo</c> call, so the router hands the page
/// its own address back before the handler that wrote it has returned. In a browser that is not the order:
/// Blazor WebAssembly tells .NET about a history change by an asynchronous call back from JavaScript, so a
/// keystroke can be dispatched between the page writing the address and the router reporting it. Every test that
/// ran against the synchronous manager was therefore blind to the one interleaving that matters for typing --
/// the page has moved on, and the late echo describes where it WAS.
/// </para>
/// <para>
/// This is that browser ordering and nothing cleverer: <see cref="Deliver"/> raises one location change for each
/// navigation still pending, and the router reads the manager's CURRENT address when it does, exactly as it
/// would.
/// </para>
/// </remarks>
internal sealed class DeferredNavigationManager : NavigationManager
{
    private int pending;

    public DeferredNavigationManager() => Initialize("http://localhost/", "http://localhost/");

    /// <summary>Every navigation made, in order, and whether it asked to replace the history entry.</summary>
    public List<(string Uri, bool Replace)> Navigations { get; } = [];

    protected override void NavigateToCore(string uri, NavigationOptions options)
    {
        Uri = ToAbsoluteUri(uri).ToString();
        Navigations.Add((Uri, options.ReplaceHistoryEntry));
        pending++;
    }

    /// <summary>Forgets navigations nobody was listening for yet, such as the one that set the first address.</summary>
    public void Discard() => pending = 0;

    /// <summary>Reports every pending navigation to whoever is listening.</summary>
    public void Deliver()
    {
        var count = pending;
        pending = 0;

        for (var i = 0; i < count; i++) NotifyLocationChanged(isInterceptedLink: false);
    }
}

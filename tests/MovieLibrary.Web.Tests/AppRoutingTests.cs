using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// Renders the real router. These pin what the brief's manual checks 1 and 4 look at in a browser, as far as a
/// component test can: one set of landmarks for every route, a route with an h1 to focus, and the selector scoped
/// to the app. They do not replace the browser pass: bUnit has no layout engine and no Blazor runtime.
/// </summary>
public class AppRoutingTests : BunitContext
{
    public AppRoutingTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddAnnouncer();   // MainLayout injects it; Program.cs registers the real one
    }

    private IRenderedComponent<App> RenderAt(string path)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(path);
        return Render<App>();
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/movies")]
    [InlineData("/zzz")]
    public void EveryRouteRendersOneBannerOneNavOneMainAndNoFooter(string path)
    {
        var cut = RenderAt(path);

        Assert.Single(cut.FindAll("header"));
        Assert.Single(cut.FindAll("nav[aria-label='Main']"));
        Assert.Single(cut.FindAll("main#main-content"));
        Assert.Empty(cut.FindAll("footer"));
        Assert.Single(cut.FindAll("h1"));
    }

    [Fact]
    public void SlashRendersTheLibraryDirectlyWithNoRedirect()
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        var cut = RenderAt("/");

        Assert.Equal("Films", cut.Find("h1").TextContent.Trim());
        Assert.EndsWith("/", nav.Uri);
    }

    [Fact]
    public void AnUnknownUrlStillRendersInsideTheShell()
    {
        var cut = RenderAt("/zzz");

        Assert.Equal("Page not found", cut.Find("main#main-content h1").TextContent.Trim());
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/movies")]
    public void FocusOnNavigateIsScopedToTheAppAndFiresOnTheFirstRender(string path)
    {
        RenderAt(path);

        var focus = JSInterop.Invocations["Blazor._internal.domWrapper.focusBySelector"];
        Assert.Equal("#app h1", Assert.Single(focus).Arguments[0]);
    }

    private const string FocusBySelector = "Blazor._internal.domWrapper.focusBySelector";

    // The nav's "return focus to the Menu button" rule rests on this: FocusOnNavigate does not fire when the
    // target is the page already showing. If a framework change ever makes it fire, or a Part B route splits
    // one nav item across two page types, these fail instead of the focus rule quietly misfiring.
    [Fact]
    public void NavigatingBetweenTheTwoAddressesOfTheFilmsPageDoesNotRefocusTheHeading()
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        RenderAt("/");
        Assert.Single(JSInterop.Invocations[FocusBySelector]);

        nav.NavigateTo("/movies");
        Assert.Single(JSInterop.Invocations[FocusBySelector]);

        // Positive control: a change of page does refocus within the same synchronous test, so the count
        // above was not low merely because the router had not finished.
        nav.NavigateTo("/zzz");
        Assert.Equal(2, JSInterop.Invocations[FocusBySelector].Count);
    }

    [Fact]
    public void NavigatingFromAnotherPageToTheFilmsPageRefocusesTheHeading()
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        RenderAt("/zzz");
        Assert.Single(JSInterop.Invocations[FocusBySelector]);

        nav.NavigateTo("/movies");

        Assert.Equal(2, JSInterop.Invocations[FocusBySelector].Count);
    }
}

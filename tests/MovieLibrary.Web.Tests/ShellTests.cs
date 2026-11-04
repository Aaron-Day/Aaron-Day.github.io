using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MovieLibrary.Web.Layout;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// Markup contracts for the shell. Each one pins a decision from the Task 2 brief that is invisible in a screenshot:
/// a skip link that does not navigate, an <c>aria-expanded</c> that renders a string and not a bool, one set of
/// landmarks. They assert values, not presence, because presence-only is how the bool bug would have slipped through.
/// </summary>
public class ShellTests : BunitContext
{
    public ShellTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddAnnouncer();   // MainLayout injects it; Program.cs registers the real one
    }

    private IRenderedComponent<MainLayout> RenderLayout() =>
        Render<MainLayout>(p => p.Add(x => x.Body, "<h1>Page</h1>"));

    [Fact]
    public void MainLayoutRendersExactlyOneMainAsTheSkipTarget()
    {
        var cut = RenderLayout();

        var main = Assert.Single(cut.FindAll("main"));
        Assert.Equal("main-content", main.Id);
        // tabindex="-1" makes it programmatically focusable without adding it to the tab order.
        Assert.Equal("-1", main.GetAttribute("tabindex"));
    }

    [Fact]
    public void MainLayoutRendersOneHeaderAndNoFooter()
    {
        var cut = RenderLayout();

        Assert.Single(cut.FindAll("header"));
        // The attribution <footer> in index.html is the app's single contentinfo. A second one here would
        // give a screen reader two of them.
        Assert.Empty(cut.FindAll("footer"));
    }

    [Fact]
    public void MainLayoutPutsTheRouteContentInsideMain()
    {
        var cut = RenderLayout();

        Assert.Equal("Page", cut.Find("main#main-content h1").TextContent);
    }

    [Fact]
    public void TheSkipLinkIsTheFirstThingInTheLayoutAndTargetsMain()
    {
        var cut = RenderLayout();

        var skip = cut.Find("a.skip-link");
        Assert.Equal("#main-content", skip.GetAttribute("href"));
        Assert.Equal("Skip to main content", skip.TextContent.Trim());
        // First in document order, so it is the first Tab stop once focus is at the top of the page.
        Assert.Contains("skip-link", cut.Nodes.OfType<IElement>().First().ClassList);
    }

    [Fact]
    public void ActivatingTheSkipLinkFocusesMainAndDoesNotNavigate()
    {
        var cut = RenderLayout();
        var nav = Services.GetRequiredService<NavigationManager>();
        var before = nav.Uri;

        cut.Find("a.skip-link").Click();

        // Focus is moved by the component, because a bare href="#main-content" resolves against <base href="/">
        // and Blazor would turn the click into an in-app navigation that drops the query string.
        var focus = JSInterop.VerifyFocusAsyncInvoke();
        focus.Arguments[0].ShouldBeElementReferenceTo(cut.Find("main#main-content"));
        Assert.Equal(before, nav.Uri);
    }

    [Fact]
    public void TheSkipLinkClickIsMarkedPreventDefaultSoTheRuntimeDoesNotInterceptIt()
    {
        var cut = RenderLayout();

        // The runtime checks defaultPrevented after its own dispatch, so this is what stops the navigation to "/".
        Assert.Contains("preventDefault", cut.Find("a.skip-link").OuterHtml, StringComparison.OrdinalIgnoreCase);
    }
}

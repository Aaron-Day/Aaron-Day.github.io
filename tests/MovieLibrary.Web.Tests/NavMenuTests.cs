using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MovieLibrary.Web.Layout;

namespace MovieLibrary.Web.Tests;

public class NavMenuTests : BunitContext
{
    [Fact]
    public void RendersExactlyOneNavLandmarkNamedMain()
    {
        var cut = Render<NavMenu>();

        var nav = Assert.Single(cut.FindAll("nav"));
        // Task 6 adds <nav aria-label="Pagination">, so this one has to be told apart by its name.
        Assert.Equal("Main", nav.GetAttribute("aria-label"));
    }

    [Fact]
    public void RendersExactlyOneNavLink()
    {
        var cut = Render<NavMenu>();

        // Part A ships one real route. A link to a route that does not exist is a dead link, so there is no TV link.
        var link = Assert.Single(cut.FindAll("#nav-links a"));
        Assert.Equal("Films", link.TextContent.Trim());
    }

    [Fact]
    public void AriaExpandedIsTheLiteralStringFalseWhenCollapsed()
    {
        var cut = Render<NavMenu>();

        // A bool would render the attribute present-and-empty or omit it. Assert the value, not the presence.
        Assert.Equal("false", cut.Find("button.nav-toggle").GetAttribute("aria-expanded"));
    }

    [Fact]
    public void AriaExpandedIsTheLiteralStringTrueWhenExpanded()
    {
        var cut = Render<NavMenu>();

        cut.Find("button.nav-toggle").Click();

        Assert.Equal("true", cut.Find("button.nav-toggle").GetAttribute("aria-expanded"));
    }

    [Fact]
    public void TheToggleCollapsesAgainOnASecondClick()
    {
        var cut = Render<NavMenu>();

        cut.Find("button.nav-toggle").Click();
        cut.Find("button.nav-toggle").Click();

        Assert.Equal("false", cut.Find("button.nav-toggle").GetAttribute("aria-expanded"));
        Assert.Contains("collapsed", cut.Find("#nav-links").ClassList);
    }

    [Fact]
    public void TheListIsCollapsedUntilTheToggleOpensIt()
    {
        var cut = Render<NavMenu>();
        Assert.Contains("collapsed", cut.Find("#nav-links").ClassList);

        cut.Find("button.nav-toggle").Click();

        Assert.DoesNotContain("collapsed", cut.Find("#nav-links").ClassList);
    }

    [Fact]
    public void TheToggleControlsTheListAndIsAButton()
    {
        var cut = Render<NavMenu>();

        var toggle = cut.Find("button.nav-toggle");
        Assert.Equal("button", toggle.GetAttribute("type"));
        Assert.Equal("nav-links", toggle.GetAttribute("aria-controls"));
        Assert.Single(cut.FindAll("#nav-links"));
    }

    [Fact]
    public void TheToggleHasAnAccessibleNameAndItsIconIsHidden()
    {
        var cut = Render<NavMenu>();
        var toggle = cut.Find("button.nav-toggle");

        // No aria-label, so the name comes from content. The SVG must contribute nothing to it.
        var icon = toggle.QuerySelector("svg");
        Assert.NotNull(icon);
        Assert.Equal("true", icon.GetAttribute("aria-hidden"));
        Assert.Equal("false", icon.GetAttribute("focusable"));

        var name = string.Concat(toggle.ChildNodes
            .Where(n => n is not IElement e || e.GetAttribute("aria-hidden") != "true")
            .Select(n => n.TextContent)).Trim();
        Assert.Equal("Menu", name);
    }

    [Fact]
    public void TheMenuIconIsStrokedWithCurrentColorSoForcedColorsKeepsIt()
    {
        var cut = Render<NavMenu>();

        var path = cut.Find("button.nav-toggle svg path");
        Assert.Equal("currentColor", path.GetAttribute("stroke"));
    }

    [Fact]
    public void TheBrandLinksToTheRoot()
    {
        var cut = Render<NavMenu>();

        var brand = cut.Find("a.brand");
        Assert.Equal("Film and TV library", brand.TextContent.Trim());
        Assert.Equal("", brand.GetAttribute("href"));   // "" resolves to the base, "/", not to a page called anything
    }

    [Fact]
    public void TheActiveLinkIsMarkedForAssistiveTechnologyNotJustPainted()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("/movies");
        var cut = Render<NavMenu>();

        var link = cut.Find("#nav-links a");
        Assert.Contains("active", link.ClassList);
        Assert.Equal("page", link.GetAttribute("aria-current"));
    }

    [Fact]
    public void TheLinkIsNotMarkedCurrentOnAnotherRoute()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("/somewhere-else");
        var cut = Render<NavMenu>();

        var link = cut.Find("#nav-links a");
        Assert.DoesNotContain("active", link.ClassList);
        Assert.Null(link.GetAttribute("aria-current"));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/movies")]
    [InlineData("/movies?genre=Drama&page=3")]
    [InlineData("/movies#results")]
    [InlineData("/movies/")]
    [InlineData("/Movies")]   // the router is case-insensitive, so the nav has to be
    [InlineData("/MOVIES?genre=Drama")]
    public void FilmsIsCurrentOnEveryAddressThatServesTheFilmsPage(string path)
    {
        // "/" and "/movies" are one page. NavLink cannot express "either path", so the menu computes it.
        Services.GetRequiredService<NavigationManager>().NavigateTo(path);
        var cut = Render<NavMenu>();

        var link = cut.Find("#nav-links a");
        Assert.Contains("active", link.ClassList);
        Assert.Equal("page", link.GetAttribute("aria-current"));
    }

    [Theory]
    [InlineData("/zzz")]
    [InlineData("/not-found")]
    [InlineData("/moviesx")]
    [InlineData("/movies/123")]   // no detail route exists in Part A, so it is not the Films page
    public void FilmsIsNotCurrentElsewhere(string path)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(path);
        var cut = Render<NavMenu>();

        var link = cut.Find("#nav-links a");
        Assert.DoesNotContain("active", link.ClassList);
        Assert.Null(link.GetAttribute("aria-current"));
    }

    [Fact]
    public void TheCurrentMarkFollowsNavigationWithoutARerenderFromOutside()
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/zzz");
        var cut = Render<NavMenu>();
        Assert.Null(cut.Find("#nav-links a").GetAttribute("aria-current"));

        nav.NavigateTo("/");
        cut.WaitForAssertion(() => Assert.Equal("page", cut.Find("#nav-links a").GetAttribute("aria-current")));

        nav.NavigateTo("/zzz");
        cut.WaitForAssertion(() => Assert.Null(cut.Find("#nav-links a").GetAttribute("aria-current")));
    }

    [Fact]
    public void NavigatingClosesTheDrawerSoFocusIsNotLeftInsideAHiddenList()
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/zzz");
        var cut = Render<NavMenu>();
        cut.Find("button.nav-toggle").Click();
        Assert.Equal("true", cut.Find("button.nav-toggle").GetAttribute("aria-expanded"));

        nav.NavigateTo("/movies");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("false", cut.Find("button.nav-toggle").GetAttribute("aria-expanded"));
            Assert.Contains("collapsed", cut.Find("#nav-links").ClassList);
        });
    }

    // Closing the drawer is display:none, which takes the link the user just activated out of the tab order.
    // FocusOnNavigate does not fire when the target is the page already showing (observed: no focus() call
    // at all), so without this focus drops to <body> and a keyboard user is back at the top of the document.
    [Fact]
    public void FollowingTheCurrentPagesOwnLinkMovesFocusToTheMenuButton()
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/movies");
        var cut = Render<NavMenu>();
        // bUnit only writes the element-reference id into the markup of the first render, so read it now.
        var toggleId = cut.Find("button.nav-toggle").GetAttribute("blazor:elementreference");
        Assert.False(string.IsNullOrEmpty(toggleId));
        cut.Find("button.nav-toggle").Click();
        cut.Find("#nav-links a").Click();

        nav.NavigateTo("/");

        cut.WaitForAssertion(() =>
        {
            var focus = JSInterop.VerifyFocusAsyncInvoke();
            Assert.Equal(toggleId, ((ElementReference)focus.Arguments[0]!).Id);
        });
    }

    [Fact]
    public void FocusIsLeftAloneWhenTheNavigationDidNotComeFromTheDrawerLink()
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/movies");
        var cut = Render<NavMenu>();
        cut.Find("button.nav-toggle").Click();

        nav.NavigateTo("/");   // e.g. the brand link, or the browser's Back button: no activation of the link

        cut.WaitForAssertion(() => Assert.Contains("collapsed", cut.Find("#nav-links").ClassList));
        Assert.Empty(JSInterop.Invocations);
    }

    [Fact]
    public void FocusIsLeftToFocusOnNavigateWhenTheLinkLeadsToADifferentPage()
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/zzz");
        var cut = Render<NavMenu>();
        cut.Find("button.nav-toggle").Click();
        cut.Find("#nav-links a").Click();

        nav.NavigateTo("/movies");

        cut.WaitForAssertion(() => Assert.Contains("collapsed", cut.Find("#nav-links").ClassList));
        Assert.Empty(JSInterop.Invocations);   // the page changed, so FocusOnNavigate moves focus to its h1
    }

    [Fact]
    public void TheSamePageCheckIsCaseInsensitiveLikeTheRouter()
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/movies");
        var cut = Render<NavMenu>();
        var toggleId = cut.Find("button.nav-toggle").GetAttribute("blazor:elementreference");
        cut.Find("button.nav-toggle").Click();
        cut.Find("#nav-links a").Click();

        nav.NavigateTo("/Movies");

        cut.WaitForAssertion(() =>
            Assert.Equal(toggleId, ((ElementReference)JSInterop.VerifyFocusAsyncInvoke().Arguments[0]!).Id));
    }

    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    public void AModifiedClickIsNotAnActivationBecauseTheRuntimeDoesNotInterceptIt(bool ctrl, bool meta, bool shift, bool alt)
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/movies");
        var cut = Render<NavMenu>();
        cut.Find("button.nav-toggle").Click();
        cut.Find("#nav-links a").Click(new MouseEventArgs { CtrlKey = ctrl, MetaKey = meta, ShiftKey = shift, AltKey = alt });

        nav.NavigateTo("/");

        cut.WaitForAssertion(() => Assert.Contains("collapsed", cut.Find("#nav-links").ClassList));
        Assert.Empty(JSInterop.Invocations);
    }

    [Fact]
    public void ActivatingTheLinkWhileTheDrawerIsClosedMovesNoFocus()
    {
        // On a wide screen the list is always visible and the drawer is never "open", so the Menu button is hidden
        // and there is nothing to return focus to.
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/movies");
        var cut = Render<NavMenu>();
        cut.Find("#nav-links a").Click();

        nav.NavigateTo("/");

        cut.WaitForAssertion(() => Assert.Equal("page", cut.Find("#nav-links a").GetAttribute("aria-current")));
        Assert.Empty(JSInterop.Invocations);
    }

    [Fact]
    public void ReopeningTheMenuForgetsAnActivationThatNeverNavigated()
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/movies");
        var cut = Render<NavMenu>();
        cut.Find("button.nav-toggle").Click();
        cut.Find("#nav-links a").Click();                 // an activation that produced no location change
        cut.Find("button.nav-toggle").Click();            // close
        cut.Find("button.nav-toggle").Click();            // open again

        nav.NavigateTo("/");                              // e.g. the browser's Back button

        cut.WaitForAssertion(() => Assert.Contains("collapsed", cut.Find("#nav-links").ClassList));
        Assert.Empty(JSInterop.Invocations);
    }

    [Fact]
    public void TheSamePageCheckComparesAgainstTheLatestAddressNotTheOneTheMenuStartedOn()
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/zzz");
        var cut = Render<NavMenu>();
        var toggleId = cut.Find("button.nav-toggle").GetAttribute("blazor:elementreference");
        cut.Find("button.nav-toggle").Click();
        cut.Find("#nav-links a").Click();
        nav.NavigateTo("/movies");                        // a different page: FocusOnNavigate's job
        cut.WaitForAssertion(() => Assert.Contains("collapsed", cut.Find("#nav-links").ClassList));
        Assert.Empty(JSInterop.Invocations);

        cut.Find("button.nav-toggle").Click();
        cut.Find("#nav-links a").Click();
        nav.NavigateTo("/");                              // now the same page as where it already was

        cut.WaitForAssertion(() =>
            Assert.Equal(toggleId, ((ElementReference)JSInterop.VerifyFocusAsyncInvoke().Arguments[0]!).Id));
    }

    [Fact]
    public void TheLinkCarriesNoBootstrapNavClassBecauseItsVariablesAreUndefinedOutsideANavAncestor()
    {
        var cut = Render<NavMenu>();

        Assert.DoesNotContain("nav-link", cut.Find("#nav-links a").ClassList);
    }

    [Fact]
    public async Task TheMenuUnsubscribesFromLocationChangesWhenItIsDisposed()
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        var before = LocationChangedSubscribers(nav);

        Render<NavMenu>();
        Assert.Equal(before + 1, LocationChangedSubscribers(nav));

        await DisposeComponentsAsync();
        Assert.Equal(before, LocationChangedSubscribers(nav));
    }

    // NavigationManager exposes no subscriber count. Reading its backing field is brittle, so a framework
    // rename fails this helper loudly instead of letting the leak test pass vacuously.
    private static int LocationChangedSubscribers(NavigationManager nav)
    {
        var field = typeof(NavigationManager).GetField("_locationChanged",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.True(field is not null, "NavigationManager no longer has _locationChanged; update this helper");
        return (field.GetValue(nav) as Delegate)?.GetInvocationList().Length ?? 0;
    }
}

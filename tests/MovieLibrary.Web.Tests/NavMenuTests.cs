using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
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

        Assert.Equal("Film and TV library", cut.Find("a.brand").TextContent.Trim());
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
}

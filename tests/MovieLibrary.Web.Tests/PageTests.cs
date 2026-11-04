using System.Reflection;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MovieLibrary.Web.Pages;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// Every route needs one h1 and a PageTitle in its FIRST render. FocusOnNavigate fires once, right after that
/// render, and never retries, so a heading behind an <c>@if (loaded)</c> would silently never receive focus.
/// </summary>
public class PageTests : BunitContext
{
    public PageTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    [Fact]
    public void NotFoundRendersAnH1()
    {
        var cut = Render<NotFound>();

        Assert.Equal("Page not found", Assert.Single(cut.FindAll("h1")).TextContent.Trim());
    }

    [Fact]
    public void NotFoundSetsTheDocumentTitle()
    {
        var head = Render<HeadOutlet>();
        Render<NotFound>();

        head.WaitForAssertion(() =>
            Assert.Equal("Page not found - Film and TV library", head.Find("title").TextContent));
    }

    [Fact]
    public void NotFoundOffersAWayBackToTheLibrary()
    {
        var cut = Render<NotFound>();

        // Inline in a sentence of prose, so SC 2.5.8's inline exemption applies and it needs no 44px target.
        var link = cut.Find("p a");
        Assert.Equal("Back to the library", link.TextContent.Trim());
        Assert.Equal("movies", link.GetAttribute("href"));
    }

    [Fact]
    public void NotFoundDeclaresTheMainLayoutBecauseTheNotFoundRenderPathDoesNotApplyTheDefault()
    {
        var layout = typeof(NotFound).GetCustomAttribute<LayoutAttribute>();

        Assert.NotNull(layout);
        Assert.Equal(typeof(Layout.MainLayout), layout.LayoutType);
    }

    [Fact]
    public void MoviesRendersOneH1InItsFirstRender()
    {
        var cut = Render<Movies>();

        Assert.Equal("Films", Assert.Single(cut.FindAll("h1")).TextContent.Trim());
    }

    [Fact]
    public void MoviesSetsTheDocumentTitleInItsFirstRender()
    {
        var head = Render<HeadOutlet>();
        Render<Movies>();

        head.WaitForAssertion(() =>
            Assert.Equal("Films - Film and TV library", head.Find("title").TextContent));
    }

    [Fact]
    public void MoviesAnswersBothSlashAndSlashMovies()
    {
        var templates = typeof(Movies).GetCustomAttributes<RouteAttribute>().Select(r => r.Template);

        Assert.Equal(new[] { "/", "/movies" }, templates.OrderBy(t => t, StringComparer.Ordinal));
    }

    [Fact]
    public void NothingElseClaimsTheRootRouteAndNoRedirectComponentExists()
    {
        var assembly = typeof(Movies).Assembly;

        var rootOwners = assembly.GetTypes()
            .Where(t => t.GetCustomAttributes<RouteAttribute>().Any(r => r.Template == "/"))
            .ToList();
        // Two components on "/" would make the router throw at startup, and a redirect would cost an empty
        // first render and make the route announcer see "/" as the first path.
        Assert.Equal(new[] { typeof(Movies) }, rootOwners);
        Assert.DoesNotContain(assembly.GetTypes(), t => t.Name.Contains("Redirect", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheOnlyRoutesTheSiteServesAreTheFilmsPageAndNotFound()
    {
        // The stock /counter and /weather pages are live routes on a public site if they are left in.
        var templates = typeof(Movies).Assembly.GetTypes()
            .SelectMany(t => t.GetCustomAttributes<RouteAttribute>())
            .Select(r => r.Template)
            .OrderBy(t => t, StringComparer.Ordinal);

        Assert.Equal(new[] { "/", "/movies", "/not-found" }, templates);
    }
}

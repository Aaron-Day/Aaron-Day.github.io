using System.Reflection;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MovieLibrary.Web.Pages;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// Every route needs one h1 and a PageTitle in its FIRST render. FocusOnNavigate fires once, right after that
/// render, and never retries, so a heading behind an <c>@if (loaded)</c> would silently never receive focus.
/// <para>
/// The two h1 tests that used to be here are gone, subsumed by
/// RouteAnnouncerTests.EveryRouteTheSiteServesIsAnnouncedByTheNameItsOwnHeadingGives, which reads each
/// route's heading through the real router, and by MarkupContractTests.EveryRouteRendersExactlyOneH1, which
/// adds the count. The two title tests below are NOT subsumed:
/// MarkupContractTests.EveryRouteSetsADocumentTitleOfItsOwn only asserts the shape of a title -- non-empty,
/// ending with the site name, not the host page's -- and these assert the exact strings. A reflective check
/// over a pattern is weaker than a literal, and both are worth having.
/// </para>
/// </summary>
public class PageTests : BunitContext
{
    // The films page has needed a CatalogService since Task 5, and NotFound needs the announcer through
    // its layout, so both registrations come from the one helper that owns them.
    public PageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddAnnouncer();
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

        Assert.Equal(new[] { "/", "/movies", "/movies/{Key}", "/not-found" }, templates);
    }
}

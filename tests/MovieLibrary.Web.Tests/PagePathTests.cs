using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MovieLibrary.Web.Services;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// The one definition of "which page is this address". RouteAnnouncer and NavMenu both compare these for
/// equality, so the two have to agree exactly: a reader that trims differently decides a different page changed,
/// and then the nav highlight and the announcement disagree about where the user is. They held identical copies
/// of this until it was pulled out here, which is the kind of duplication that drifts quietly.
/// </summary>
public class PagePathTests : BunitContext
{
    private readonly NavigationManager nav;

    public PagePathTests() => nav = Services.GetRequiredService<NavigationManager>();

    private string Of(string uri) => PagePath.Of(nav, nav.ToAbsoluteUri(uri).ToString());

    [Theory]
    [InlineData("movies", "movies")]                       // already base-relative
    [InlineData("/movies", "movies")]                      // the leading slash is the base, not part of the path
    [InlineData("/movies?q=ab&genre=Drama", "movies")]     // a filter is not a page
    [InlineData("/movies#results", "movies")]              // nor is a fragment
    [InlineData("/movies/?q=a#results", "movies")]         // trailing slash, query and fragment together
    [InlineData("/movies/", "movies")]                     // a trailing slash is the same page
    [InlineData("/Movies", "Movies")]                      // case is left alone: callers compare case-insensitively
    [InlineData("/not-found", "not-found")]
    [InlineData("/a/b/c", "a/b/c")]                        // inner slashes survive; only the trailing one goes
    [InlineData("/", "")]                                  // the root is the empty string, never "/"
    [InlineData("", "")]
    [InlineData("/?q=a", "")]                              // the root with a filter is still the root
    public void TheAddressIsReducedToThePageItShows(string uri, string expected)
    {
        Assert.Equal(expected, Of(uri));
    }

    [Fact]
    public void TheRootAndItsTrailingSlashFormAreTheSamePage()
    {
        // "/" and "" have to agree, because "/" is where a first visit lands and "" is what the nav table holds.
        Assert.Equal(Of("/"), Of(""));
    }
}

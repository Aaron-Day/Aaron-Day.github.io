using Microsoft.AspNetCore.Components;

namespace MovieLibrary.Web.Services;

/// <summary>
/// One definition of "which page is this address", for everything that has to compare two URLs.
/// </summary>
/// <remarks>
/// Two readers today -- <c>RouteAnnouncer</c> asking whether the PAGE changed or only a filter, and
/// <c>NavMenu</c> asking which link is current and whether the user followed a link to the page already
/// showing -- and Part B adds a third. They had identical copies of this, which is the kind of duplication that
/// drifts: a reader that trims differently decides a different page changed, and the nav and the announcement
/// then disagree about where the user is.
/// </remarks>
internal static class PagePath
{
    private static readonly char[] QueryOrFragment = { '?', '#' };

    /// <summary>
    /// <paramref name="uri"/> as an address relative to the app's base, without query string, fragment or
    /// trailing slash. The site's root is the empty string, never "/": callers compare these for equality and
    /// the two are the same page. Case is left alone, so callers compare case-insensitively as the router does.
    /// </summary>
    public static string Of(NavigationManager navigation, string uri)
    {
        var path = navigation.ToBaseRelativePath(uri);
        var end = path.IndexOfAny(QueryOrFragment);
        if (end >= 0) path = path[..end];
        return path.TrimEnd('/');
    }
}

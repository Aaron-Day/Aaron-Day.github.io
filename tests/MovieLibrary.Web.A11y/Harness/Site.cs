using Microsoft.AspNetCore.Components;
using MovieLibrary.Web.Pages;

namespace MovieLibrary.Web.A11y.Harness;

/// <summary>
/// The addresses a visitor can land on, read from the app rather than typed here.
/// </summary>
/// <remarks>
/// <para>
/// Reflection over <c>RouteAttribute</c> yields six templates today - <c>/</c>, <c>/movies</c>,
/// <c>/movies/{Key}</c>, <c>/not-found</c>, <c>/tv</c> and <c>/tv/{Slug}</c>, all six real reachable URLs -
/// and <see cref="UnmatchedUrl"/> adds the router's own not-found path, which reaches the not-found page by
/// a different render path. Seven addresses over five page types. Adding a page extends every route scan in
/// this project with no edit here, as long as any route parameter it carries is one <see cref="Filled"/>
/// knows how to fill.
/// </para>
/// <para>
/// <b>This is not <c>MovieLibrary.Web.Tests.AppUnderTest</c> and cannot be.</b> That helper is internal to the
/// bUnit test assembly, which this project does not reference and should not: it carries bUnit, Jint and a
/// renderer that has no business in a browser gate. The duplication is one LINQ query, and both copies read
/// the same attribute off the same assembly, so a new page cannot be picked up by one and missed by the other.
/// </para>
/// </remarks>
internal static class Site
{
    /// <summary>An address no <c>RouteAttribute</c> matches, so the router renders its not-found page.</summary>
    public const string UnmatchedUrl = "/zzz";

    /// <summary>Every route template in the web assembly, in a stable order.</summary>
    public static IReadOnlyList<string> RouteTemplates() =>
        typeof(Movies).Assembly.GetTypes()
            .SelectMany(t => t.GetCustomAttributes(typeof(RouteAttribute), false).Cast<RouteAttribute>())
            .Select(r => r.Template)
            .Distinct()
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Every address worth scanning: one per route template, with any route parameter filled in, plus the
    /// unmatched URL.
    /// </summary>
    /// <remarks>
    /// <b>This used to DROP every parameterised template</b> (<c>.Where(t =&gt; !t.Contains('{'))</c>), which
    /// was right while there were none and became a silent hole the day <c>/movies/{Key}</c> shipped: the film
    /// page would have been absent from every scan in this project, and no test here would have gone red to
    /// say so. A dropped address cannot be noticed; a wrong one can, so the parameter is filled instead and
    /// <c>RouteScanTests.TheScanHasRealAddressesToScan</c> fails on any template this cannot fill.
    /// </remarks>
    public static IReadOnlyList<string> Addresses() =>
        RouteTemplates()
            .Select(Filled)
            .Append(UnmatchedUrl)
            .ToList();

    /// <summary>
    /// Fills a route parameter with a value the page can actually answer. A key and a slug the gate's own
    /// catalogue carries, not typed ones: <c>/movies/1</c> reaches the film page's "no such film" state and
    /// <c>/tv/1</c> the series page's, which are states worth scanning somewhere (DomStateTests scans them)
    /// but not the ones an arrival scan should land on. The slug's record is one of the three
    /// <see cref="Catalogue.PublishedShows"/> publishes, and it is the furnished one.
    /// </summary>
    private static string Filled(string template) =>
        template.Replace("{Key}", Catalogue.FirstKey, StringComparison.Ordinal)
                .Replace("{Slug}", Catalogue.FirstSlug, StringComparison.Ordinal);

    public static IEnumerable<object[]> AddressData() => Addresses().Select(a => new object[] { a });
}

using Microsoft.AspNetCore.Components;
using MovieLibrary.Web.Pages;

namespace MovieLibrary.Web.A11y.Harness;

/// <summary>
/// The addresses a visitor can land on, read from the app rather than typed here.
/// </summary>
/// <remarks>
/// <para>
/// Reflection over <c>RouteAttribute</c> yields three templates today - <c>/</c>, <c>/movies</c> and
/// <c>/not-found</c>, all three real reachable URLs - and <see cref="UnmatchedUrl"/> adds the router's own
/// not-found path, which reaches the same page by a different render path. Four addresses over two page types.
/// Tasks 6 and 8 extend every route scan in this project by adding a page, with no edit here.
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
    /// Every address worth scanning: one per route template, plus the unmatched URL. A route parameter becomes
    /// "1"; there are none in Part A, and Part B's <c>movies/{key}</c> would need a real key, so when one
    /// appears this list is where it gets dealt with rather than silently scanning <c>/movies/1</c>.
    /// </summary>
    public static IReadOnlyList<string> Addresses() =>
        RouteTemplates()
            .Where(t => !t.Contains('{', StringComparison.Ordinal))
            .Append(UnmatchedUrl)
            .ToList();

    public static IEnumerable<object[]> AddressData() => Addresses().Select(a => new object[] { a });
}

using System.Text.RegularExpressions;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MovieLibrary.Web.Pages;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// The one place that knows which addresses this site serves and how to render the real app at one of them.
/// It exists so that there is exactly one copy: Task 5 adds a CatalogService and an HttpClient, and a second
/// private copy of either the route walk or the render call means that registration has to be made in two
/// places, which is the fragility a shared helper is for. RouteAnnouncerTests and MarkupContractTests both
/// come through here.
/// </summary>
internal static class AppUnderTest
{
    /// <summary>
    /// An address no RouteAttribute matches, so the router renders NotFoundPage. It is a fourth address and
    /// not a duplicate of "/not-found": that page is reached by two different render paths, and
    /// RouteAnnouncerTests already depends on the difference between them.
    /// </summary>
    public const string UnmatchedUrl = "/zzz";

    /// <summary>
    /// Every route template in the web assembly, read from RouteAttribute. The table in RouteAnnouncer and
    /// the contracts in MarkupContractTests are both driven from this, so a page added without a heading, a
    /// title or an announcer entry fails the build instead of shipping.
    /// </summary>
    public static IReadOnlyList<string> RouteTemplates() =>
        typeof(Movies).Assembly.GetTypes()
            .SelectMany(t => t.GetCustomAttributes(typeof(RouteAttribute), false).Cast<RouteAttribute>())
            .Select(r => r.Template)
            .Distinct()
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();

    public static IEnumerable<object[]> RouteTemplateData() =>
        RouteTemplates().Select(t => new object[] { t });

    /// <summary>A concrete address for a route template: any route parameter becomes "1".</summary>
    public static string AddressOf(string template) => Regex.Replace(template, @"\{[^}]*\}", "1");

    /// <summary>Every address a visitor can land on: one per route template, plus an unmatched URL.</summary>
    public static IReadOnlyList<string> Addresses() =>
        RouteTemplates().Select(AddressOf).Append(UnmatchedUrl).ToList();

    /// <summary>
    /// Renders the real app at an address, through the real router and the real layout -- the house pattern,
    /// because a routable component rendered on its own contains almost nothing: the skip link, the drawer,
    /// the landmarks, the live regions and the real aria-controls all live in MainLayout and NavMenu.
    /// </summary>
    public static IRenderedComponent<MovieLibrary.Web.App> RenderAppAt(this BunitContext context, string address)
    {
        context.Services.GetRequiredService<NavigationManager>().NavigateTo(address);

        try
        {
            return context.Render<MovieLibrary.Web.App>();
        }
        catch (Exception ex)
        {
            throw RenderFailed(address, ex);
        }
    }

    // Without this, the first person to add a page that needs a service gets one unexplained DI stack trace
    // per contract -- eleven of them, all identical, none saying where the registration belongs.
    private static Exception RenderFailed(string address, Exception ex) =>
        new InvalidOperationException(
            $"Rendering the app at \"{address}\" threw, so none of the markup contracts could be checked "
            + $"there. The component that failed is {FailingComponent(ex)}. If this is a missing service, "
            + $"register it in {nameof(AnnouncerTestExtensions)}."
            + $"{nameof(AnnouncerTestExtensions.AddAnnouncer)} and nowhere else: that single helper is what "
            + "keeps a new page from costing every test class in this project its own copy of the "
            + "registration. Do not add a private copy here. See the inner exception for the real failure.",
            ex);

    // Blazor's property-injection failure reads "Cannot provide a value for property 'X' on type 'Y'", and
    // Y is the component a reader needs. Anything else falls back to saying so rather than guessing.
    private static string FailingComponent(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            var named = Regex.Match(e.Message, @"on type '(?<type>[^']+)'");
            if (named.Success) return named.Groups["type"].Value;
        }

        return "not named in the exception";
    }
}

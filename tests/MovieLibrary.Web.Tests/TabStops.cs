using AngleSharp.Dom;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// Counts the places a keyboard visitor stops in a piece of markup, in document order. bUnit has no layout and no
/// focus, so this reads the rules instead: it is the arithmetic the browser's Tab key does, for the elements this
/// project uses. The real walk is the browser gate's; this is what lets a unit test see a change in the count.
/// </summary>
/// <remarks>
/// What it knows: native controls and links, <c>summary</c> as the one stop a closed <c>details</c> keeps, a radio
/// group as ONE stop however many radios it has, and that <c>tabindex="-1"</c>, <c>disabled</c>,
/// <c>type="hidden"</c> and the inside of a closed <c>details</c> are not stops. What it does not know: CSS
/// (<c>display: none</c> is invisible to it), so it must not be pointed at markup whose stops depend on a stylesheet.
/// </remarks>
internal static class TabStops
{
    private const string Focusable = "input, select, textarea, button, a[href], summary, [tabindex]";

    /// <summary>The stops inside <paramref name="scope"/>, optionally only those before <paramref name="boundary"/>.</summary>
    public static int Count(IElement scope, IElement? boundary = null) => Stops(scope, boundary).Count;

    public static IReadOnlyList<IElement> Stops(IElement scope, IElement? boundary = null)
    {
        var stops = new List<IElement>();
        var radioGroups = new HashSet<string>(StringComparer.Ordinal);

        foreach (var e in scope.QuerySelectorAll(Focusable))
        {
            if (boundary is not null && (boundary.Contains(e) || (boundary.CompareDocumentPosition(e) & DocumentPositions.Following) != 0))
                break;
            if (!IsStop(e)) continue;
            if (e.LocalName == "input" && e.GetAttribute("type") == "radio"
                && !radioGroups.Add(e.GetAttribute("name") ?? "\0" + stops.Count))
                continue;
            stops.Add(e);
        }

        return stops;
    }

    private static bool IsStop(IElement e)
    {
        if (e.HasAttribute("disabled") || e.GetAttribute("tabindex") is { } t && int.TryParse(t, out var n) && n < 0)
            return false;
        if (e.LocalName == "input" && e.GetAttribute("type") == "hidden") return false;
        if (e.LocalName == "summary" && e.ParentElement?.LocalName != "details") return false;

        // Inside a closed disclosure only its own summary is reachable.
        for (var p = e.ParentElement; p is not null; p = p.ParentElement)
        {
            if (p.LocalName != "details" || p.HasAttribute("open")) continue;
            var isItsSummary = e.LocalName == "summary" && e.ParentElement == p;
            if (!isItsSummary) return false;
        }

        return true;
    }
}

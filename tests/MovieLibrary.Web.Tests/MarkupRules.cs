using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// The markup rules of the accessibility gate, as pure functions over a parsed subtree. Each returns one
/// message per violation, naming the element and saying why the rule exists.
/// <para>
/// They are separated from the discovery that feeds them on purpose. The app has two pages, one image and a
/// handful of controls, so several of these rules would otherwise loop over an empty or near-empty set --
/// and a loop over an empty collection PASSES, reading in the output exactly like a rule that checked
/// something. Breaking an assertion to watch it fail does not detect that: the mutation passes too, and the
/// exercise then certifies the vacuous test as proven. Pure functions can be driven against bad markup
/// written by hand (MarkupRuleTests), which proves the logic whatever the app happens to contain today;
/// MarkupContractTests then applies the proven rule to the real app and says, per rule, whether it had
/// anything to look at.
/// </para>
/// <para>
/// Where a rule's shape is a judgement call, it is settled by what axe does, because Task 4b runs axe over
/// the same markup. A rule stricter here than there is a rule that cries wolf, and a gate that cries wolf
/// gets switched off.
/// </para>
/// </summary>
internal static class MarkupRules
{
    private static readonly HtmlParser Parser = new();

    /// <summary>Parses a markup fragment into a subtree the rules can be run over.</summary>
    public static IElement Parse(string markup) =>
        Parser.ParseDocument("<!DOCTYPE html><html><body>" + markup + "</body></html>").Body!;

    // ---------------------------------------------------------------- shared vocabulary

    private static IEnumerable<IElement> SelfAndAncestors(IElement element)
    {
        for (IElement? e = element; e is not null; e = e.ParentElement) yield return e;
    }

    private static bool IsHiddenFromEveryone(IElement e) =>
        e.HasAttribute("hidden") || e.GetAttribute("aria-hidden") == "true";

    private static bool InHiddenSubtree(IElement e) => SelfAndAncestors(e).Any(IsHiddenFromEveryone);

    /// <summary>
    /// Text a sighted reader cannot see, which the accessibility tree still carries. Bootstrap's
    /// .visually-hidden-focusable only hides while nothing inside it has focus; parsed markup has no focus
    /// at all, so it counts as hidden here. A browser can tell the two states apart and Task 4b's pass is
    /// where that distinction can actually be made.
    /// </summary>
    private static bool IsVisuallyHidden(IElement e) =>
        e.ClassList.Contains("visually-hidden")
        || e.ClassList.Contains("visually-hidden-focusable")
        || Regex.IsMatch(e.GetAttribute("style") ?? "", @"display\s*:\s*none", RegexOptions.IgnoreCase);

    private static IElement? ById(IElement scope, string id) =>
        scope.QuerySelectorAll("[id]").FirstOrDefault(e => e.Id == id);

    /// <summary>Collapsed whitespace. What a name is compared on before punctuation and case are dropped.</summary>
    private static string Flatten(string s) => Regex.Replace(s, @"\s+", " ").Trim();

    /// <summary>
    /// Case, punctuation and whitespace removed. SC 2.5.3 is about the words a speech user says, so
    /// "Dismiss error" against "Dismiss error." or "DISMISS ERROR" is not a mismatch worth a red build.
    /// </summary>
    public static string Comparable(string s) =>
        Flatten(Regex.Replace(s.ToLowerInvariant(), @"[^\p{L}\p{N}\s]+", " "));

    /// <summary>A short, unambiguous way to point at an element in a failure message.</summary>
    public static string Describe(IElement e)
    {
        var id = string.IsNullOrEmpty(e.Id) ? "" : "#" + e.Id;
        var classes = e.ClassList.Length == 0 ? "" : "." + string.Join(".", e.ClassList);
        var html = Flatten(e.OuterHtml);
        if (html.Length > 140) html = html[..140] + " ...";
        return $"<{e.LocalName}{id}{classes}>  {html}";
    }

    // ---------------------------------------------------------------- focus and roles

    /// <summary>
    /// Whether an element is in the tab order. tabindex="-1" counts as NOT in it: such an element can be
    /// focused programmatically but is not a keyboard stop, so it is not a nesting trap and not a control
    /// the user can reach by tabbing. Using tab-order semantics rather than "focusable at all" is what
    /// keeps the nested-widget rule from flagging MainLayout's own main[tabindex="-1"] skip target.
    /// </summary>
    public static bool IsTabbable(IElement e)
    {
        if (InHiddenSubtree(e) || e.HasAttribute("disabled") || e.HasAttribute("inert")) return false;

        if (e.GetAttribute("tabindex") is { } tabindex)
            return int.TryParse(tabindex, out var value) && value >= 0;

        return e.LocalName switch
        {
            "a" or "area" => e.HasAttribute("href"),
            "button" or "select" or "textarea" or "summary" or "iframe" => true,
            "input" => !string.Equals(e.GetAttribute("type"), "hidden", StringComparison.OrdinalIgnoreCase),
            "audio" or "video" => e.HasAttribute("controls"),
            _ => e.GetAttribute("contenteditable") is { } editable
                 && !string.Equals(editable, "false", StringComparison.OrdinalIgnoreCase),
        };
    }

    /// <summary>
    /// The widget roles that are leaves: they are operated as one thing, and a second focusable control
    /// inside one is the nesting SC 4.1.2 breaks on. Composite widgets are deliberately absent -- a
    /// combobox, a listbox, a grid, a tablist and a menu all legitimately own focusable children, and
    /// including them would make this rule fail on correct markup.
    /// </summary>
    private static readonly HashSet<string> LeafWidgetRoles = new(StringComparer.Ordinal)
    {
        "button", "link", "checkbox", "radio", "switch", "tab", "option",
        "menuitem", "menuitemcheckbox", "menuitemradio",
        "textbox", "searchbox", "slider", "spinbutton", "scrollbar",
    };

    /// <summary>
    /// An element's role: the explicit one if it has it, otherwise the implicit one for the handful of HTML
    /// elements this app and the tasks after it use. Not a full HTML-AAM mapping; an element whose role is
    /// not known returns null and is simply not a widget for these rules' purposes.
    /// </summary>
    public static string? RoleOf(IElement e)
    {
        if (e.GetAttribute("role") is { } declared && !string.IsNullOrWhiteSpace(declared))
            return declared.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

        return e.LocalName switch
        {
            "a" or "area" => e.HasAttribute("href") ? "link" : null,
            "button" => "button",
            "textarea" => "textbox",
            "select" => "combobox",
            "input" => InputRole(e),
            _ => null,
        };
    }

    private static string? InputRole(IElement e) =>
        (e.GetAttribute("type") ?? "text").ToLowerInvariant() switch
        {
            "button" or "submit" or "reset" or "image" => "button",
            "checkbox" => "checkbox",
            "radio" => "radio",
            "range" => "slider",
            "number" => "spinbutton",
            "search" => "searchbox",
            "text" or "email" or "tel" or "url" or "password" => "textbox",
            // date, time, colour and file have no stable ARIA role mapping, so they claim none.
            _ => null,
        };

    private static bool IsLeafWidget(IElement e) =>
        RoleOf(e) is { } role && LeafWidgetRoles.Contains(role) && !InHiddenSubtree(e);

    // ---------------------------------------------------------------- names

    /// <summary>
    /// The text an element's subtree contributes. <paramref name="visibleOnly"/> additionally drops what a
    /// sighted reader cannot see, which is the whole difference between SC 2.5.3's "label" and its "visible
    /// text": NavMenu's &lt;span class="visually-hidden"&gt;Menu&lt;/span&gt; is in the accessible name and
    /// is not visible, so a naive rule comparing one against the other fails on a correct pattern.
    /// </summary>
    public static string TextOf(IElement element, bool visibleOnly)
    {
        // The element's own hiding counts, not only its descendants'. MainLayout's skip link is itself a
        // .visually-hidden-focusable, so reading its children alone would report "Skip to main content" as
        // text on the screen when it is off-screen until focused.
        if (IsHiddenFromEveryone(element)) return "";
        if (visibleOnly && IsVisuallyHidden(element)) return "";

        var text = new StringBuilder();
        Walk(element);
        return Flatten(text.ToString());

        void Walk(INode node)
        {
            foreach (var child in node.ChildNodes)
            {
                if (child is IText words)
                {
                    text.Append(words.Data).Append(' ');
                    continue;
                }

                if (child is not IElement e) continue;
                if (e.LocalName is "script" or "style" or "template" or "noscript") continue;
                if (IsHiddenFromEveryone(e)) continue;
                if (visibleOnly && IsVisuallyHidden(e)) continue;

                // An image inside a control contributes its alt text to the control's name.
                if (e.LocalName == "img" && e.GetAttribute("alt") is { Length: > 0 } alt)
                    text.Append(alt).Append(' ');

                Walk(e);
            }
        }
    }

    /// <summary>
    /// An element's accessible name, by the part of the accname algorithm this app can reach:
    /// aria-labelledby, then aria-label, then the native name, then title. aria-hidden subtrees are
    /// excluded throughout; .visually-hidden text is NOT, because it is in the accessibility tree.
    /// Not a conforming accname implementation -- it has no CSS, so it cannot see generated content or a
    /// display:none set by a stylesheet rather than inline. Task 4b computes the real one from the browser.
    /// </summary>
    public static string AccessibleName(IElement e)
    {
        var root = SelfAndAncestors(e).Last();

        if (e.GetAttribute("aria-labelledby") is { } labelledBy && !string.IsNullOrWhiteSpace(labelledBy))
        {
            var referenced = Ids(labelledBy)
                .Select(id => ById(root, id))
                .Where(target => target is not null)
                .Select(target => TextOf(target!, visibleOnly: false));

            var name = Flatten(string.Join(" ", referenced));
            if (name.Length > 0) return name;
        }

        if (e.GetAttribute("aria-label") is { } label && !string.IsNullOrWhiteSpace(label))
            return Flatten(label);

        var native = NativeName(e, root);
        if (native.Length > 0) return native;

        return Flatten(e.GetAttribute("title") ?? "");
    }

    private static string NativeName(IElement e, IElement root)
    {
        switch (e.LocalName)
        {
            case "input":
                var type = (e.GetAttribute("type") ?? "text").ToLowerInvariant();
                if (type is "button" or "submit" or "reset")
                {
                    var value = Flatten(e.GetAttribute("value") ?? "");
                    // A bare submit or reset button is named by the user agent, not by the page.
                    return value.Length > 0 ? value : type switch { "submit" => "Submit", "reset" => "Reset", _ => "" };
                }

                if (type == "image") return Flatten(e.GetAttribute("alt") ?? "");

                var labelled = LabelTextFor(e, root);
                // placeholder is a legitimate last resort in the HTML accessible name computation, and axe
                // accepts it. Accepting it here too keeps this half from disagreeing with Task 4b's half.
                return labelled.Length > 0 ? labelled : Flatten(e.GetAttribute("placeholder") ?? "");

            case "select":
            case "textarea":
                return LabelTextFor(e, root);

            default:
                return TextOf(e, visibleOnly: false);
        }
    }

    /// <summary>The text of a form control's own label: a label[for] pointing at it, or one wrapping it.</summary>
    private static string LabelTextFor(IElement control, IElement root)
    {
        var byFor = string.IsNullOrEmpty(control.Id)
            ? null
            : root.QuerySelectorAll("label[for]").FirstOrDefault(l => l.GetAttribute("for") == control.Id);

        var label = byFor ?? control.Closest("label");
        return label is null ? "" : TextOf(label, visibleOnly: false);
    }

    private static IEnumerable<string> Ids(string value) =>
        value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    // ---------------------------------------------------------------- headings

    public static IReadOnlyList<IElement> Headings(IElement scope) =>
        scope.QuerySelectorAll("h1, h2, h3, h4, h5, h6").Where(e => !InHiddenSubtree(e)).ToList();

    public static IReadOnlyList<IElement> TopLevelHeadings(IElement scope) =>
        scope.QuerySelectorAll("h1").ToList();

    /// <summary>
    /// House rule. One h1 per page is a convention, not a criterion: SC 2.4.10 Section Headings is Level
    /// AAA and does not demand it, and SC 1.3.1 asks that headings be marked up, not counted. What makes it
    /// worth enforcing here is that FocusOnNavigate's selector is "#app h1" -- two of them and the focus
    /// move after a navigation silently lands on whichever comes first in the DOM.
    /// </summary>
    public static IReadOnlyList<string> ExactlyOneTopLevelHeading(IElement scope)
    {
        var headings = TopLevelHeadings(scope);
        if (headings.Count == 1) return Array.Empty<string>();

        return new[]
        {
            headings.Count == 0
                ? "no <h1> at all. FocusOnNavigate's selector is \"#app h1\", so a page without one takes no "
                  + "focus after a navigation and a screen reader user is left wherever the last page put them."
                : $"{headings.Count} <h1> elements, so FocusOnNavigate lands on whichever comes first in the "
                  + "DOM rather than on the page's name: "
                  + string.Join(" | ", headings.Select(Describe)),
        };
    }

    public static IReadOnlyList<(IElement Previous, IElement Next)> AdjacentHeadingPairs(IElement scope)
    {
        var headings = Headings(scope);
        return Enumerable.Range(1, Math.Max(0, headings.Count - 1))
            .Select(i => (headings[i - 1], headings[i]))
            .ToList();
    }

    /// <summary>
    /// SC 1.3.1 Info and Relationships, Level A. A heading level skipped on the way down -- h1 then h3 --
    /// tells a reader navigating by heading that a level exists which does not, so the outline they build
    /// of the page is wrong. Only downward skips are a defect: coming back up from h3 to h2 closes a
    /// section and is correct.
    /// </summary>
    public static IReadOnlyList<string> HeadingLevelsNeverSkipALevel(IElement scope) =>
        AdjacentHeadingPairs(scope)
            .Where(pair => Level(pair.Next) > Level(pair.Previous) + 1)
            .Select(pair =>
                $"<{pair.Previous.LocalName}> \"{TextOf(pair.Previous, visibleOnly: false)}\" is followed by "
                + $"<{pair.Next.LocalName}> \"{TextOf(pair.Next, visibleOnly: false)}\", skipping "
                + $"<h{Level(pair.Previous) + 1}>. Someone navigating by heading is told a level exists that "
                + "does not, so the outline they build of this page is wrong.")
            .ToList();

    private static int Level(IElement heading) => heading.LocalName[1] - '0';

    /// <summary>
    /// House rule, and the reason written down is not the one the plan gave. It is NOT for
    /// FocusOnNavigate: focusBySelector reads "if (!element.hasAttribute('tabindex')) element.tabIndex =
    /// -1" and adds it itself, so for FocusOnNavigate this attribute is redundant. The defensible reason is
    /// ElementReference.FocusAsync(), which does not add it and fails SILENTLY on an element that cannot
    /// take focus -- no exception, no moved focus, nothing red. Anything that replaces FocusOnNavigate
    /// later, and MainLayout's skip link already uses FocusAsync, would break with no test to catch it.
    /// </summary>
    public static IReadOnlyList<string> TopLevelHeadingIsExplicitlyFocusable(IElement scope) =>
        TopLevelHeadings(scope)
            .Where(h => h.GetAttribute("tabindex") != "-1")
            .Select(h =>
                $"{Describe(h)} has no tabindex=\"-1\". FocusOnNavigate adds one at runtime, so nothing looks "
                + "wrong today; ElementReference.FocusAsync() does not, and on an element that cannot take "
                + "focus it fails silently. Declare it so the heading is reachable by any focus mechanism.")
            .ToList();

    // ---------------------------------------------------------------- the document title

    public const string TitleSuffix = " - Film and TV library";

    /// <summary>
    /// SC 2.4.2 Page Titled, Level A. Deliberately not a presence check: index.html carries a static
    /// &lt;title&gt;Film and TV library&lt;/title&gt;, so a page that forgets PageTitle entirely still has
    /// a title and a presence check goes green over a page that does not describe itself at all.
    /// </summary>
    public static IReadOnlyList<string> TitleDescribesTheRouteItself(string title, string hostPageTitle)
    {
        if (string.IsNullOrWhiteSpace(title))
            return new[] { "the document title is empty." };

        var problems = new List<string>();

        if (!title.EndsWith(TitleSuffix, StringComparison.Ordinal))
            problems.Add($"the title \"{title}\" does not end with \"{TitleSuffix}\", so the site it belongs "
                         + "to is not named in the one string a tab, a window list and a history entry show.");

        if (string.Equals(title.Trim(), hostPageTitle.Trim(), StringComparison.Ordinal))
            problems.Add($"the title is still index.html's static \"{hostPageTitle}\", which means this route "
                         + "sets no PageTitle of its own. A presence check would have passed here.");

        return problems;
    }

    // ---------------------------------------------------------------- images

    public static IReadOnlyList<IElement> Images(IElement scope) => scope.QuerySelectorAll("img").ToList();

    /// <summary>
    /// SC 1.1.1 Non-text Content, Level A. alt="" is correct and required for a decorative image: it prunes
    /// it from the accessibility tree, which is exactly what the TMDB logo wants since the sentence beside
    /// it already names TMDB. A MISSING alt is the defect -- a reader then falls back to announcing the file
    /// name. The two cases look identical in a browser and read as opposites to a screen reader.
    /// </summary>
    public static IReadOnlyList<string> EveryImageHasAnAltAttribute(IElement scope) =>
        Images(scope)
            .Where(img => !img.HasAttribute("alt"))
            .Select(img =>
                $"{Describe(img)} has no alt attribute. If the image carries meaning, describe it; if it is "
                + "decorative, write alt=\"\" -- which is not the same as leaving it out, because a reader "
                + "with no alt at all announces the file name instead.")
            .ToList();

    /// <summary>
    /// House rule, and about layout shift rather than accessibility: with valid integer width and height
    /// attributes the user agent derives an aspect ratio and reserves the box before the image loads. It is
    /// in this suite because app.css's .attribution img comment depends on those attributes existing -- it
    /// sets exactly one axis and relies on the UA deriving the other. Not a WCAG criterion; a reflow during
    /// load is unpleasant for everyone and worst for someone tracking a focus ring, but no criterion says so.
    /// </summary>
    public static IReadOnlyList<string> EveryImageHasExplicitWidthAndHeight(IElement scope) =>
        Images(scope)
            .Where(img => !IsNonNegativeInteger(img.GetAttribute("width"))
                          || !IsNonNegativeInteger(img.GetAttribute("height")))
            .Select(img =>
                $"{Describe(img)} is missing a valid integer width or height attribute. Without both, the user "
                + "agent cannot derive an aspect ratio and the page reflows when the image arrives.")
            .ToList();

    private static bool IsNonNegativeInteger(string? value) =>
        value is not null && int.TryParse(value, out var number) && number >= 0;

    // ---------------------------------------------------------------- nested widgets

    public static IReadOnlyList<IElement> LeafWidgets(IElement scope) =>
        scope.QuerySelectorAll("*").Where(IsLeafWidget).ToList();

    /// <summary>
    /// SC 4.1.2 Name, Role, Value, Level A. A focusable control inside another is not reliably announced,
    /// and the inner one is often unreachable or unactivatable; axe calls this nested-interactive.
    /// <para>
    /// Three nestings are correct and are carved out by name. The first matters most:
    /// &lt;label&gt;&lt;input type="checkbox"&gt; Watched&lt;/label&gt; is canonical, HTML permits a
    /// label's own labeled control inside it, and Task 8's filters will be full of them. It would not be
    /// flagged anyway, because label has no widget role and so is never an outer element here -- the carve
    /// out is explicit so that the rule stays correct if the outer set is ever widened.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> NoFocusableWidgetIsNestedInsideAnother(IElement scope)
    {
        var violations = new List<string>();

        foreach (var outer in LeafWidgets(scope))
        foreach (var inner in outer.QuerySelectorAll("*").Where(IsTabbable))
        {
            if (IsAllowedNesting(outer, inner)) continue;

            violations.Add(
                $"{Describe(inner)} is in the tab order and sits inside {Describe(outer)}, whose role is "
                + $"\"{RoleOf(outer)}\". A control nested inside another control is not reliably announced, "
                + "and the inner one is often unreachable or unactivatable by keyboard.");
        }

        return violations;
    }

    private static bool IsAllowedNesting(IElement outer, IElement inner) =>
        // <label><input type="checkbox"> Watched</label>: the label's own labeled control.
        IsTheLabelsOwnControl(outer, inner)
        // <details><summary>..</summary></details>: summary IS the disclosure's control.
        || (inner.LocalName == "summary" && outer.LocalName == "details")
        // <video controls><a href="..">Download</a></video>: fallback content, only reachable when the
        // element itself is not supported, so the two are never in the tab order together.
        || (inner.LocalName == "a" && outer.LocalName is "audio" or "video");

    private static bool IsTheLabelsOwnControl(IElement outer, IElement inner)
    {
        var label = outer.LocalName == "label" ? outer : inner.Closest("label");
        if (label is null || !(label == outer || outer.Contains(label))) return false;

        var forId = label.GetAttribute("for");
        var owned = string.IsNullOrEmpty(forId)
            ? label.QuerySelectorAll("*").FirstOrDefault(IsLabelable)
            : SelfAndAncestors(label).Last() is { } root ? ById(root, forId) : null;

        return owned == inner;
    }

    private static bool IsLabelable(IElement e) =>
        e.LocalName is "button" or "meter" or "output" or "progress" or "select" or "textarea"
        || (e.LocalName == "input"
            && !string.Equals(e.GetAttribute("type"), "hidden", StringComparison.OrdinalIgnoreCase));

    // ---------------------------------------------------------------- names on controls

    /// <summary>
    /// Everything the user can operate: anything in the tab order, plus anything carrying a leaf widget
    /// role. aria-hidden subtrees are excluded -- a focusable element inside one is its own defect (axe
    /// calls it aria-hidden-focus) and belongs to a rule of its own rather than being reported here as a
    /// nameless control.
    /// </summary>
    public static IReadOnlyList<IElement> InteractiveElements(IElement scope) =>
        scope.QuerySelectorAll("*")
            .Where(e => !InHiddenSubtree(e) && (IsTabbable(e) || IsLeafWidget(e)))
            .ToList();

    /// <summary>
    /// SC 4.1.2 Name, Role, Value, Level A. A control with no accessible name is announced by its role
    /// alone, as "button" or "link" and nothing else, which tells the user nothing about what it does. The
    /// commonest way to produce one is an icon-only control, which is why the NavMenu toggle carries a
    /// visually-hidden "Menu" and the dismiss button in index.html carries aria-label="Dismiss error".
    /// </summary>
    public static IReadOnlyList<string> EveryInteractiveElementHasAnAccessibleName(IElement scope) =>
        InteractiveElements(scope)
            .Where(e => AccessibleName(e).Length == 0)
            .Select(e =>
                Describe(e) + " has no accessible name, so it is announced as \""
                + (RoleOf(e) ?? "an element") + "\" and nothing else. Give it text, an aria-label, a "
                + "label, or an alt on the image inside it.")
            .ToList();

    /// <summary>
    /// The elements SC 2.5.3 Label in Name actually applies to: a control whose accessible name comes from
    /// an author label AND which shows visible text. A control with no visible text is outside the
    /// criterion entirely, because there is nothing a speech user could be reading off the screen.
    /// </summary>
    public static IReadOnlyList<IElement> ElementsWhoseLabelReplacesVisibleText(IElement scope) =>
        InteractiveElements(scope)
            .Where(e => HasAuthorLabel(e) && TextOf(e, visibleOnly: true).Length > 0)
            .ToList();

    private static bool HasAuthorLabel(IElement e) =>
        !string.IsNullOrWhiteSpace(e.GetAttribute("aria-label"))
        || !string.IsNullOrWhiteSpace(e.GetAttribute("aria-labelledby"));

    /// <summary>
    /// SC 2.5.3 Label in Name, Level A -- Level A, not AA. Someone driving the page by voice says the words
    /// they can see; if the accessible name does not contain them, the command does not match the control.
    /// <para>
    /// The implementation is where this rule goes wrong if written naively. 2.5.3 concerns text presented
    /// VISUALLY, and visually-hidden text is in the accessible name while not being visible. Comparing the
    /// full text content against the label therefore fails on the visually-hidden "Menu" span inside the
    /// NavMenu toggle the moment that button gains a label, and that pattern is correct. Visible text here
    /// excludes .visually-hidden, unfocused .visually-hidden-focusable and aria-hidden subtrees, while the
    /// accessible name excludes only aria-hidden.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> AnAriaLabelContainsTheVisibleTextItReplaces(IElement scope) =>
        ElementsWhoseLabelReplacesVisibleText(scope)
            .Where(e => !Comparable(AccessibleName(e))
                         .Contains(Comparable(TextOf(e, visibleOnly: true)), StringComparison.Ordinal))
            .Select(e =>
                Describe(e) + " shows \"" + TextOf(e, visibleOnly: true) + "\" but its accessible name is \""
                + AccessibleName(e) + "\", which does not contain it. Someone operating the page by voice "
                + "says the words on the screen, and this control does not answer to them.")
            .ToList();

    // ---------------------------------------------------------------- tab order and id references

    public static IReadOnlyList<IElement> ElementsWithATabIndex(IElement scope) =>
        scope.QuerySelectorAll("[tabindex]").ToList();

    /// <summary>
    /// House rule, and axe classifies it a best practice rather than a violation: no WCAG criterion forbids
    /// a positive tabindex. SC 2.4.3 Focus Order asks only that the order preserve meaning, which a
    /// positive tabindex can technically do. It is banned here because it does not scale: a positive value
    /// jumps the element ahead of everything in document order for the whole page, so the next person to
    /// add a control has to renumber against it, and the drawer, the skip link and the page content each
    /// end up with their own idea of what comes first.
    /// </summary>
    public static IReadOnlyList<string> NoElementHasAPositiveTabIndex(IElement scope) =>
        ElementsWithATabIndex(scope)
            .Where(e => int.TryParse(e.GetAttribute("tabindex"), out var value) && value > 0)
            .Select(e =>
                Describe(e) + " has tabindex=\"" + e.GetAttribute("tabindex") + "\". A positive tabindex "
                + "jumps the element ahead of document order for the whole page, so every control added "
                + "afterwards has to be renumbered against it. Use 0, or -1 for a programmatic focus target.")
            .ToList();

    private static readonly string[] IdReferenceAttributes =
    {
        "aria-labelledby", "aria-describedby", "aria-owns", "aria-controls", "for",
    };

    /// <summary>
    /// Every id an attribute points at, one entry per id, because all of these attributes take
    /// SPACE-SEPARATED LISTS. Reading such an attribute as a single id makes the first multi-id reference
    /// anyone writes a false failure. "for" is read only on label and output, where it means a control.
    /// </summary>
    public static IReadOnlyList<(IElement Element, string Attribute, string Id)> IdReferences(IElement scope) =>
        scope.QuerySelectorAll("*")
            .SelectMany(e => IdReferenceAttributes
                .Where(a => a != "for" || e.LocalName is "label" or "output")
                .Select(a => (Element: e, Attribute: a, Value: e.GetAttribute(a)))
                .Where(x => !string.IsNullOrWhiteSpace(x.Value))
                .SelectMany(x => Ids(x.Value!).Select(id => (x.Element, x.Attribute, Id: id))))
            .ToList();

    /// <summary>
    /// Not a criterion of its own: a dangling reference breaks whichever criterion the attribute was
    /// serving -- 1.3.1 for a label or a description, 4.1.2 for a name -- and it breaks it silently,
    /// because an unresolved IDREF is an error nowhere. The plan named only aria-controls; aria-labelledby,
    /// aria-describedby, aria-owns and for are the ones that cost a control its name or its description.
    /// <para>
    /// One exception, named: axe deliberately does NOT fail an aria-controls pointing at an absent element
    /// when the control is aria-expanded="false", because the controlled element may legitimately not be in
    /// the DOM yet. Task 4b runs axe over this same markup, so carving the same case out here is what keeps
    /// the two halves of the gate from disagreeing about correct code.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> EveryIdReferenceResolves(IElement scope) =>
        IdReferences(scope)
            .Where(r => ById(scope, r.Id) is null)
            .Where(r => !(r.Attribute == "aria-controls"
                          && r.Element.GetAttribute("aria-expanded") == "false"))
            .Select(r =>
                Describe(r.Element) + " has " + r.Attribute + " pointing at \"" + r.Id + "\", and no "
                + "element with that id is in this render. The attribute is silently ignored, so whatever "
                + "it was providing -- a name, a description, a relationship -- is simply absent.")
            .ToList();
}

using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// The bUnit half of the accessibility gate: the proven rules in <see cref="MarkupRules"/> applied to the
/// real app, at every address a visitor can reach, and to the host page.
/// <para>
/// Rendered through <c>App</c>, not component by component. A routable component on its own contains
/// almost nothing -- the skip link, the drawer, the landmarks, the live regions and the real aria-controls
/// all live in MainLayout and NavMenu -- and that is the difference between five contracts with something
/// to check and two. index.html is scanned as well, because it holds the app's only image, the Dismiss
/// button, the Reload control and the static title, and until now no contract touched any of it.
/// </para>
/// <para>
/// WHY A GREEN TEST HERE IS NOT AUTOMATICALLY A COVERED ONE. Even through App, several of these rules loop
/// over element sets that are empty or nearly empty in an app with two pages: one image, twenty controls,
/// no positive tabindex anywhere, four id references. A loop over an empty collection PASSES and reads in
/// the output exactly like a rule that checked something, and the usual proof -- break an assertion, watch
/// it fail, restore it -- cannot tell the difference, because the mutation passes too. So: every rule here
/// asserts its own subject set is non-empty, and the two rules whose subjects do not exist yet are marked
/// Skip by hand with the task that will create them, each with a live guard beside it that fails the day
/// they appear. A skipped test reads as not covered. A green one reads as covered. The logic of every rule,
/// including the two skipped ones, is proven in MarkupRuleTests against markup written for the purpose.
/// </para>
/// <para>
/// xunit 2.9.3 has no Assert.Skip. It is a v3 API; Xunit.Sdk.SkipException ships in the shared assert
/// library but the v2 engine reports a throw of it as a failure, which was verified rather than assumed.
/// Hence Skip on the attribute plus a guard, which is strictly better in one way: the guard says in words
/// when the skip has expired.
/// </para>
/// <para>
/// What none of this can prove: anything that needs layout, focus or a real accessibility tree -- contrast,
/// focus order as experienced, whether .visually-hidden-focusable really becomes visible on focus, whether
/// a reader announces any of it. That is Task 4b, over the same markup, with axe and a browser.
/// </para>
/// </summary>
public class MarkupContractTests
{
    private sealed record Scope(string Where, IElement Root);

    /// <summary>
    /// Rendered once for the whole class. Each address gets its own context so that four live routers do
    /// not react to each other's navigation, and the markup is re-parsed into a standalone document so
    /// nothing here outlives the context it came from.
    /// </summary>
    private static readonly Lazy<IReadOnlyList<Scope>> RouteScopes = new(() =>
        AppUnderTest.Addresses().Select(address =>
        {
            using var context = new BunitContext();
            context.JSInterop.Mode = JSRuntimeMode.Loose;
            context.AddAnnouncer();
            return new Scope("the app at " + address, MarkupRules.Parse(context.RenderAppAt(address).Markup));
        }).ToList());

    /// <summary>
    /// The host page is a separate scope rather than being spliced into #app, and that is a real limit
    /// rather than laziness: at runtime Blazor REPLACES the children of #app, so the boot status and the
    /// spinner never coexist with the app's markup -- but #blazor-error-ui and footer.attribution do.
    /// Treating them separately therefore slightly under-approximates the live page. Checking the two
    /// together needs the page as the browser assembles it, which is Task 4b.
    /// </summary>
    private static IReadOnlyList<Scope> EveryScope() =>
        RouteScopes.Value.Append(new Scope("the host page index.html", HostPage.Doc.Body!)).ToList();

    private static IReadOnlyList<Scope> RouteOnlyScopes() => RouteScopes.Value;

    /// <summary>
    /// Every reflective contract runs this first. A rule driven from a route set that silently shrank to
    /// nothing -- a renamed attribute, a reflection walk that stopped matching -- would pass while
    /// visiting no page at all.
    /// </summary>
    private static void TheRouteSetIsWhatItShouldBe()
    {
        var addresses = AppUnderTest.Addresses();

        Assert.Equal(
            new[] { "/", "/movies", "/not-found", AppUnderTest.UnmatchedUrl },
            addresses);
        Assert.True(addresses.Count >= 4,
            "The contracts discovered " + addresses.Count + " addresses. Four exist: \"/\" and \"/movies\" "
            + "(both Movies.razor), \"/not-found\", and an unmatched URL, which reaches NotFound by the "
            + "router's NotFoundPage path rather than by its route. Fewer than four means the reflection "
            + "walk in AppUnderTest stopped finding routes, not that the site shrank.");
    }

    /// <summary>
    /// Asserts a rule had something to look at. The alternative -- a green test over an empty collection --
    /// is indistinguishable in the output from a rule that held, and this project has been bitten by
    /// exactly that before.
    /// </summary>
    private static void Subjects(Func<IElement, int> count, IReadOnlyList<Scope> scopes, string what, string whenItIsGone)
    {
        var total = scopes.Sum(s => count(s.Root));

        Assert.True(total > 0,
            "This rule found no " + what + " in any of " + scopes.Count + " scopes, so it looped over "
            + "nothing and passed without checking anything. " + whenItIsGone);
    }

    private static void NoViolations(
        Func<IElement, IReadOnlyList<string>> rule, IReadOnlyList<Scope> scopes, string why)
    {
        var found = scopes
            .SelectMany(s => rule(s.Root).Select(v => "  in " + s.Where + ": " + v))
            .ToList();

        Assert.True(found.Count == 0,
            why + Environment.NewLine + string.Join(Environment.NewLine, found));
    }

    // ================================================================== the contracts

    [Fact]
    public void TheContractsRunOverEveryAddressTheSiteServes()
    {
        TheRouteSetIsWhatItShouldBe();
        Assert.Equal(AppUnderTest.Addresses().Count, RouteOnlyScopes().Count);
        Assert.Equal(AppUnderTest.Addresses().Count + 1, EveryScope().Count);
    }

    /// <remarks>
    /// House rule. SC 2.4.10 Section Headings is Level AAA and does not require a single h1, and SC 1.3.1
    /// asks that headings be marked up rather than counted. What earns it a place is App.razor's
    /// FocusOnNavigate selector, "#app h1": with two headings the focus move after a navigation lands on
    /// whichever is first in the DOM, and with none it does not happen at all.
    /// </remarks>
    [Fact]
    public void EveryRouteRendersExactlyOneH1()
    {
        TheRouteSetIsWhatItShouldBe();

        NoViolations(MarkupRules.ExactlyOneTopLevelHeading, RouteOnlyScopes(),
            "A route must render exactly one <h1>: FocusOnNavigate's selector is \"#app h1\", so a second "
            + "one steals the focus move after a navigation and none means it never happens.");
    }

    /// <remarks>
    /// SC 1.3.1 Info and Relationships, Level A. Skipped, and this is the clearest example in the suite of
    /// why the distinction matters: every route renders exactly ONE heading today, so there is not a single
    /// adjacent pair of headings anywhere in the app for the rule to compare. It would pass. It would mean
    /// nothing. The rule's logic is proven in MarkupRuleTests, against a skipped level and against the
    /// h1-h2-h3-h2 outline it must not flag.
    /// </remarks>
    [Fact(Skip = "No route renders a second heading, so there is no adjacent heading pair in the whole app "
                 + "to compare -- the rule would pass over an empty set. Task 6 adds the films page's "
                 + "section headings; remove this Skip then. "
                 + "TheHeadingOrderContractIsStillSkippedForTheRightReason fails when that happens.")]
    public void HeadingLevelsNeverSkipALevel()
    {
        TheRouteSetIsWhatItShouldBe();

        Subjects(s => MarkupRules.AdjacentHeadingPairs(s).Count, EveryScope(), "adjacent heading pair",
            "Mark this test Skip again, naming the task that will bring the headings back.");

        NoViolations(MarkupRules.HeadingLevelsNeverSkipALevel, EveryScope(),
            "A heading level skipped on the way down tells someone navigating by heading that a level "
            + "exists which does not, so the outline they build of the page is wrong.");
    }

    [Fact]
    public void TheHeadingOrderContractIsStillSkippedForTheRightReason()
    {
        // The live half of the pair above. The moment any page gains a second heading this fails and says
        // to un-skip the contract, which is the part a bare Skip cannot do for itself.
        var pairs = EveryScope().Sum(s => MarkupRules.AdjacentHeadingPairs(s.Root).Count);

        Assert.True(pairs == 0,
            "There are now " + pairs + " adjacent heading pairs in the app, so HeadingLevelsNeverSkipALevel "
            + "has subjects and is no longer vacuous. Remove the Skip from it and delete this guard.");
    }

    /// <remarks>
    /// SC 2.4.2 Page Titled, Level A. Deliberately NOT a presence check. index.html carries a static
    /// &lt;title&gt;Film and TV library&lt;/title&gt;, so a page that forgets PageTitle entirely still has
    /// a title and a presence check goes green over a route that never describes itself. This asserts the
    /// title is non-empty, names the site, and is not the bare host-page title.
    /// It does not subsume PageTests.NotFoundSetsTheDocumentTitle or
    /// PageTests.MoviesSetsTheDocumentTitleInItsFirstRender, which assert the exact strings. Those stay.
    /// </remarks>
    [Fact]
    public void EveryRouteSetsADocumentTitleOfItsOwn()
    {
        TheRouteSetIsWhatItShouldBe();

        var hostTitle = HostPage.Doc.Title;
        Assert.False(string.IsNullOrWhiteSpace(hostTitle),
            "index.html has no static title, so this contract cannot tell a route's own title from the "
            + "host page's and the check below would be weaker than it reads.");

        foreach (var address in AppUnderTest.Addresses())
        {
            using var context = new BunitContext();
            context.JSInterop.Mode = JSRuntimeMode.Loose;
            context.AddAnnouncer();

            var head = context.Render<HeadOutlet>();
            context.RenderAppAt(address);
            head.WaitForAssertion(() => Assert.NotEmpty(head.FindAll("title")));

            var title = head.Find("title").TextContent;
            var problems = MarkupRules.TitleDescribesTheRouteItself(title, hostTitle);

            Assert.True(problems.Count == 0,
                "At " + address + ": " + string.Join(" ", problems) + " A document title is the first "
                + "thing a screen reader says on arrival and the only label a tab, a window list and a "
                + "history entry get.");
        }
    }

    /// <remarks>
    /// SC 1.1.1 Non-text Content, Level A. Asserted, on one image: the TMDB logo in index.html, which is
    /// the app's only one today. alt="" is correct there and is what the rule accepts -- it prunes a
    /// decorative image from the accessibility tree, which is right because the sentence beside it already
    /// names TMDB. A MISSING alt is the defect, and the two are indistinguishable in a browser. Task 6's
    /// posters will be the first images inside the app itself.
    /// </remarks>
    [Fact]
    public void EveryImgHasAnAltAttributeEvenIfEmpty()
    {
        TheRouteSetIsWhatItShouldBe();

        Subjects(s => MarkupRules.Images(s).Count, EveryScope(), "<img>",
            "Mark this test Skip, naming the task that adds the next image.");

        NoViolations(MarkupRules.EveryImageHasAnAltAttribute, EveryScope(),
            "An <img> with no alt attribute at all makes a screen reader fall back to announcing the file "
            + "name. Decorative images need alt=\"\", which is not the same as leaving it out.");
    }

    /// <remarks>
    /// House rule, and about layout shift rather than accessibility -- no WCAG criterion requires it. It is
    /// here because app.css's .attribution img rule depends on those attributes existing: it fixes exactly
    /// one axis and relies on the user agent deriving the aspect ratio from the attributes, and the comment
    /// there says so. A reflow during load is worst for someone tracking a focus ring, but no criterion
    /// says that either, so the rule says house rule in its own remarks.
    /// </remarks>
    [Fact]
    public void EveryImgHasExplicitWidthAndHeight()
    {
        TheRouteSetIsWhatItShouldBe();

        Subjects(s => MarkupRules.Images(s).Count, EveryScope(), "<img>",
            "Mark this test Skip, naming the task that adds the next image.");

        NoViolations(MarkupRules.EveryImageHasExplicitWidthAndHeight, EveryScope(),
            "Without valid integer width and height attributes the user agent cannot reserve the box "
            + "before the image arrives, and app.css's .attribution img rule sets only one axis because it "
            + "relies on the ratio being derived from them.");
    }

    /// <remarks>
    /// SC 4.1.2 Name, Role, Value, Level A. Asserted over twenty widgets: the skip link, the brand, the
    /// Menu toggle and the Films link on every route, the in-prose link on the two not-found addresses,
    /// and Reload and Dismiss on the host page. Framed as nested focusable WIDGET ROLES, which is how axe
    /// frames it and therefore how Task 4b will behave. The implicit-label pattern, summary inside details
    /// and an anchor in media fallback content are carved out by name and proven in MarkupRuleTests;
    /// composite roles are deliberately outside the rule, because a listbox or a tablist legitimately owns
    /// focusable children and flagging those would make the gate cry wolf from Task 8 onwards.
    /// </remarks>
    [Fact]
    public void NoFocusableWidgetIsNestedInsideAnother()
    {
        TheRouteSetIsWhatItShouldBe();

        Subjects(s => MarkupRules.LeafWidgets(s).Count, EveryScope(), "element with a leaf widget role",
            "Mark this test Skip, naming the task that adds the next control.");

        NoViolations(MarkupRules.NoFocusableWidgetIsNestedInsideAnother, EveryScope(),
            "A control in the tab order inside another control is not reliably announced, and the inner "
            + "one is often unreachable or unactivatable by keyboard.");
    }

    /// <remarks>
    /// SC 4.1.2 Name, Role, Value, Level A. Asserted over the same twenty controls. Two of them have no
    /// visible text at all and are named the two different correct ways -- the Menu toggle by a
    /// visually-hidden span, the Dismiss button by aria-label over an aria-hidden glyph -- so this contract
    /// exercises both the text path and the label path of the name computation on real markup.
    /// What it cannot prove: the name a browser actually computes. This has no CSS, so it cannot see
    /// generated content or a display:none that comes from a stylesheet rather than an inline style. The
    /// real accessible name comes from the accessibility tree, in Task 4b.
    /// </remarks>
    [Fact]
    public void EveryInteractiveElementHasAnAccessibleName()
    {
        TheRouteSetIsWhatItShouldBe();

        Subjects(s => MarkupRules.InteractiveElements(s).Count, EveryScope(), "interactive element",
            "Mark this test Skip, naming the task that adds the next control.");

        NoViolations(MarkupRules.EveryInteractiveElementHasAnAccessibleName, EveryScope(),
            "A control with no accessible name is announced as its role and nothing else, which tells the "
            + "user what it is but not what it does.");
    }

    /// <remarks>
    /// SC 2.5.3 Label in Name, Level A -- Level A, not AA, which an earlier draft of this brief had wrong.
    /// Skipped, because the criterion applies to a control that has an author label AND shows visible
    /// text, and no control in the app is both. The two that carry a label show nothing: the Dismiss
    /// button's only content is an aria-hidden glyph, and the Menu toggle's is a visually-hidden span, so
    /// both are outside 2.5.3 rather than passing it. Task 8's filter controls are the first that will be
    /// labelled and visibly captioned at once.
    /// The rule is proven in MarkupRuleTests, including against both of those patterns, which a naive
    /// "visible text must appear in the label" rule reports as defects.
    /// </remarks>
    [Fact(Skip = "No control in the app has both an author label and visible text, so the criterion does "
                 + "not apply to any element and the rule would pass over an empty set. The Dismiss "
                 + "button's content is an aria-hidden glyph and the Menu toggle's is a visually-hidden "
                 + "span, so both are outside SC 2.5.3 rather than passing it. Task 8's filter controls "
                 + "are the first labelled and visibly captioned controls; remove this Skip then. "
                 + "TheLabelInNameContractIsStillSkippedForTheRightReason fails when that happens.")]
    public void AnAriaLabelContainsTheVisibleTextItReplaces()
    {
        TheRouteSetIsWhatItShouldBe();

        Subjects(s => MarkupRules.ElementsWhoseLabelReplacesVisibleText(s).Count, EveryScope(),
            "control with both an author label and visible text",
            "Mark this test Skip again, naming the task that will bring such a control back.");

        NoViolations(MarkupRules.AnAriaLabelContainsTheVisibleTextItReplaces, EveryScope(),
            "Someone operating the page by voice says the words they can see. If the accessible name does "
            + "not contain the visible text, the spoken command does not match the control.");
    }

    [Fact]
    public void TheLabelInNameContractIsStillSkippedForTheRightReason()
    {
        // The live half of the pair above.
        var subjects = EveryScope()
            .SelectMany(s => MarkupRules.ElementsWhoseLabelReplacesVisibleText(s.Root))
            .ToList();

        Assert.True(subjects.Count == 0,
            "SC 2.5.3 now applies to " + subjects.Count + " control(s), so "
            + "AnAriaLabelContainsTheVisibleTextItReplaces has subjects and is no longer vacuous. Remove "
            + "its Skip and delete this guard. The control(s): "
            + string.Join(" | ", subjects.Select(MarkupRules.Describe)));
    }

    /// <remarks>
    /// House rule, and axe calls it a best practice rather than a violation: no WCAG criterion forbids a
    /// positive tabindex. SC 2.4.3 Focus Order asks only that the order preserve meaning. Asserted over
    /// eight elements -- MainLayout's main[tabindex="-1"] and each route's h1[tabindex="-1"] -- so the
    /// subject set is real even though no positive value exists anywhere to catch.
    /// </remarks>
    [Fact]
    public void NoElementHasAPositiveTabIndex()
    {
        TheRouteSetIsWhatItShouldBe();

        Subjects(s => MarkupRules.ElementsWithATabIndex(s).Count, EveryScope(), "element with a tabindex",
            "Mark this test Skip, naming the task that adds the next focus target.");

        NoViolations(MarkupRules.NoElementHasAPositiveTabIndex, EveryScope(),
            "A positive tabindex jumps an element ahead of document order for the whole page, so every "
            + "control added afterwards has to be renumbered against it. This is a house rule, stricter "
            + "than WCAG: no criterion forbids it, and axe reports it as a best practice.");
    }

    /// <remarks>
    /// House rule, and the rationale is NOT the one the plan gave. It is not for FocusOnNavigate:
    /// focusBySelector reads "if (!element.hasAttribute('tabindex')) { element.tabIndex = -1 }" and adds
    /// the attribute itself, so for FocusOnNavigate it is redundant. The reason worth writing down is
    /// ElementReference.FocusAsync(), which does NOT add it and fails silently on an element that cannot
    /// take focus: no exception, no moved focus, nothing red. MainLayout's skip link already uses
    /// FocusAsync, and anything that replaces FocusOnNavigate later would break with no test to catch it.
    /// </remarks>
    [Fact]
    public void H1IsExplicitlyFocusableSoAnyFocusMechanismCanReachIt()
    {
        TheRouteSetIsWhatItShouldBe();

        Subjects(s => MarkupRules.TopLevelHeadings(s).Count, RouteOnlyScopes(), "<h1>",
            "EveryRouteRendersExactlyOneH1 would have caught this first; fix that one.");

        NoViolations(MarkupRules.TopLevelHeadingIsExplicitlyFocusable, RouteOnlyScopes(),
            "FocusOnNavigate adds tabindex=\"-1\" at runtime, so a bare <h1> looks fine today. "
            + "ElementReference.FocusAsync() does not add it and fails silently without it, so declaring "
            + "it is what keeps the heading reachable by whatever moves focus next.");
    }

    /// <remarks>
    /// Not a criterion of its own: a dangling IDREF breaks whichever criterion the attribute was serving --
    /// 1.3.1 for a label or a description, 4.1.2 for a name -- and it breaks it silently, because an
    /// unresolved reference is an error nowhere. Asserted over four references, all of them NavMenu's
    /// aria-controls="nav-links" pointing at the drawer, one per route.
    /// Two traps, both handled and both proven in MarkupRuleTests: these attributes take space-separated
    /// LISTS, so the first multi-id reference anyone writes would be a false failure if the value were read
    /// as one id; and axe deliberately does not fail an aria-controls pointing at an absent element when
    /// the control is aria-expanded="false", so that exception is carved out here too rather than letting
    /// this half become stricter than Task 4b's.
    /// </remarks>
    [Fact]
    public void EveryIdReferenceResolvesToAnIdInTheSameRender()
    {
        TheRouteSetIsWhatItShouldBe();

        Subjects(s => MarkupRules.IdReferences(s).Count, EveryScope(), "id reference",
            "Mark this test Skip, naming the task that adds the next aria-labelledby or for.");

        NoViolations(MarkupRules.EveryIdReferenceResolves, EveryScope(),
            "An attribute pointing at an id that is not in the render is silently ignored, so whatever it "
            + "was providing -- a name, a description, a relationship -- is simply absent, with nothing "
            + "anywhere reporting an error.");
    }
}

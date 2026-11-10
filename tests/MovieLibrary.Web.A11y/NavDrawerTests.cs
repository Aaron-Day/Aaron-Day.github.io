using MovieLibrary.Web.A11y.Harness;
using Xunit.Abstractions;

namespace MovieLibrary.Web.A11y;

/// <summary>
/// The collapsed nav drawer is out of the keyboard's way, the breakpoint is where it says it is, and the
/// open drawer does not hold the keyboard.
/// </summary>
/// <remarks>
/// <para>
/// <b>The drawer is a DISCLOSURE, not a dialog.</b> A button with <c>aria-expanded</c> and
/// <c>aria-controls</c> over a list that is <c>display: none</c> when collapsed. That matters for what is
/// gated here: a disclosure correctly has no Escape handler and no focus trap, so nothing in this file
/// asserts either, and the last test asserts the OPPOSITE - that the drawer never acquires the dialog
/// semantics that would make a reader's screen reader promise a trap that is not there. Three earlier
/// accessibility reviews of this component asked for a focus trap. Adding one would be the defect.
/// </para>
/// <para>
/// <b>What is a criterion here and what is a house rule.</b> The first test is
/// <b>SC 2.4.3 Focus Order, Level A</b> - the tab order must be one that preserves meaning, and a link a
/// reader cannot see sitting in it is the classic failure. The third is
/// <b>SC 2.1.2 No Keyboard Trap, Level A</b>. The second, the exact breakpoint, is <b>a house rule</b>:
/// no WCAG criterion names a pixel, and the project's own plan is what fixes the nav at 48rem.
/// </para>
/// <para>
/// axe reaches none of this. Its <c>hidden-content</c> and <c>aria-hidden-focus</c> rules look at
/// <c>aria-hidden</c> and at content it cannot evaluate; nothing in it presses Tab, and the collapsed
/// drawer's links are not <c>aria-hidden</c> - they are <c>display: none</c>, which axe correctly treats
/// as absent. bUnit's <c>NavMenuTests</c> has twenty tests on this component's markup and state, and
/// cannot reach any of it either: there is no layout in a bUnit render, so <c>display: none</c> is a
/// class name, the 48rem query never applies, and Tab does nothing.
/// </para>
/// </remarks>
[Collection(BrowserCollection.Name)]
public class NavDrawerTests(BrowserGate gate, ITestOutputHelper output)
{
    private const string Drawer = "#nav-links";

    private const string Toggle = "button.nav-toggle";

    /// <summary>The link inside the drawer. One today; the selector does not assume that.</summary>
    private const string DrawerLinks = "#nav-links li a";

    /// <summary>
    /// SC 2.4.3 Focus Order, Level A: a link the reader cannot see is not in the tab order.
    /// </summary>
    /// <remarks>
    /// The two halves are one test on purpose. "Tab from the Menu button does not land in the drawer" is
    /// satisfied by a drawer with no links in it, which is why the same press is made again with the
    /// drawer open and has to land inside it. Neither half means anything alone.
    /// </remarks>
    [BrowserFact]
    public async Task TheCollapsedDrawersLinksAreNotInTheTabOrderAndTheOpenOnesAre()
    {
        await using var session = await gate.OpenAsync(320, 812);
        await session.GotoAsync("/");
        await session.GridAsync();

        // THE SUBJECT. The links have to BE there for their absence from the tab order to mean anything:
        // a drawer that renders no links at all would satisfy the first assertion trivially, and that is
        // the state a broken Items list would produce.
        Assert.True(await session.Page.Locator(DrawerLinks).CountAsync() >= 1,
            "The drawer renders no links, so there is nothing for the tab order to leave out and the "
            + "first assertion below would pass over an empty list.");

        // The STATE guard is what the component says about itself, not how the stylesheet achieves it.
        // Asserting `display: none` here would be the rule in the wrong place twice over: it would reject
        // visibility: hidden and the hidden attribute, which are both correct for this criterion (rule
        // T5), and - measured - it fires BEFORE the focus assertion on an opacity: 0 mutation, so the
        // thing that actually protects the reader would never be watched failing. The computed display is
        // printed instead, for a failure message to be read beside.
        Assert.Equal("false", await session.Page.Locator(Toggle).GetAttributeAsync("aria-expanded"));

        output.WriteLine($"=== collapsed: {Drawer} computes display "
                         + await session.Page.EvaluateAsync<string>(Scripts.DisplayOfSelector, Drawer)
                         + " ===");

        await session.Page.Locator(Toggle).FocusAsync();
        await session.Page.Keyboard.PressAsync("Tab");

        var landedInside = await session.Page.EvaluateAsync<bool>(Scripts.FocusIsInside, Drawer);

        output.WriteLine("=== collapsed: Tab from the Menu button reaches "
                         + await session.FocusedDescriptionAsync() + " ===");

        Assert.True(!landedInside,
            "With the drawer collapsed, Tab from the Menu button moved focus INTO #nav-links, which is "
            + "SC 2.4.3 Focus Order (Level A): the reader cannot see the drawer, so a stop inside it is a "
            + "stop on nothing, and on a phone it is several. What keeps it out is "
            + "`.nav-links.collapsed { display: none }` in NavMenu.razor.css - a rule that hid the drawer "
            + "with opacity, visibility on a child, or a zero height would leave every link focusable. "
            + "Focus went to " + await session.FocusedDescriptionAsync() + ".");

        await session.Page.Locator(Toggle).ClickAsync();

        await session.Page.Locator(Drawer).WaitForAsync();

        Assert.Equal("true", await session.Page.Locator(Toggle).GetAttributeAsync("aria-expanded"));

        Assert.NotEqual("none", await session.Page.EvaluateAsync<string>(Scripts.DisplayOfSelector, Drawer));

        await session.Page.Locator(Toggle).FocusAsync();
        await session.Page.Keyboard.PressAsync("Tab");

        output.WriteLine("=== expanded: Tab from the Menu button reaches "
                         + await session.FocusedDescriptionAsync() + " ===");

        Assert.True(await session.Page.EvaluateAsync<bool>(Scripts.FocusIsInside, Drawer),
            "With the drawer OPEN, Tab from the Menu button did not reach #nav-links. The drawer's links "
            + "follow the button that controls them in the document, so this is where they should be: "
            + "SC 2.4.3 Focus Order (Level A) is about an order that preserves meaning, and a reader who "
            + "opens a drawer and tabs past its contents has lost it. Focus went to "
            + await session.FocusedDescriptionAsync() + " instead. Without this half, the assertion above "
            + "would be satisfied by a drawer whose links are never focusable at all.");
    }

    /// <summary>
    /// A HOUSE RULE, not a criterion: the nav changes form at exactly 48rem.
    /// </summary>
    /// <remarks>
    /// No WCAG success criterion names a pixel width. This is the project's own plan, and it is gated
    /// because the two forms have different keyboard shapes - below the breakpoint there is a Menu button
    /// and a collapsed drawer, at and above it there is neither and the links are simply there - so a
    /// breakpoint that drifted would leave one of those forms untested by everything else in this file
    /// while a reader at that width got it. 767 and 768 are the two adjacent CSS pixels across the
    /// boundary, which is the only pair that can distinguish <c>min-width: 48rem</c> from a value either
    /// side of it.
    /// <para>
    /// <b>The measurement is HIT-TESTABILITY, not <c>display: none</c>, and that is rule T5.</b> This
    /// theory used to assert <c>drawer == "none"</c>, which bakes one implementation of a conformant
    /// collapse into a breakpoint rule - the very coupling the sibling test sixty lines above explicitly
    /// refuses, calling <c>visibility: hidden</c> and <c>height: 0</c> "equally correct for this
    /// criterion". Measured: switch <c>.nav-links.collapsed</c> to <c>visibility: hidden; height: 0</c>
    /// and SC 2.4.3 still passes while that assertion failed at 767px, on a page with nothing wrong with
    /// it. What the brief actually asked for here is also hit-testability - "at 767 the toggle is
    /// hit-testable and the list is not" - and <see cref="Scripts.HitTestableCentre"/> already answers it,
    /// returning <c>display: none</c> verbatim for this case because at this breakpoint that is the
    /// EXPECTED answer for one of the two controls.
    /// </para>
    /// </remarks>
    [BrowserTheory]
    [InlineData(767, true)]
    [InlineData(768, false)]
    public async Task TheNavSwitchesFormAtExactlyFortyEightRem(int width, bool asADrawer)
    {
        await using var session = await gate.OpenAsync(width, 812);
        await session.GotoAsync("/");
        await session.GridAsync();

        // THE SUBJECT, and it is the premise of the whole test: 48rem is 768px only while the root font
        // size is 16px. At any other root size the boundary is somewhere else and both rows below would be
        // measuring the same side of it.
        var root = await session.Page.EvaluateAsync<string[]>(Scripts.TextMetrics, ":root");

        Assert.True(root[1] == "16px",
            $"The root font size is {root[1]}, not 16px, so 48rem is not 768px and both rows of this "
            + "theory may be on the same side of the breakpoint. Fix the premise before reading the "
            + "result.");

        var toggle = await session.Page.EvaluateAsync<string>(Scripts.HitTestableCentre, Toggle);
        var drawer = await session.Page.EvaluateAsync<string>(Scripts.HitTestableCentre, Drawer);

        output.WriteLine($"=== {width}px: {Toggle} is \"{toggle}\", {Drawer} is \"{drawer}\" ===");

        // Both have to EXIST for either answer to mean anything. A hit test against a selector that
        // stopped matching is a pass that means nothing, which is why "(no element matches ...)" is a
        // distinct answer from the script rather than a false.
        Assert.DoesNotContain("no element matches", toggle);
        Assert.DoesNotContain("no element matches", drawer);

        var expected = asADrawer
            ? "a Menu button a pointer can land on, with the link list not reachable"
            : "no reachable Menu button and the links laid out";

        var explain = $"At {width}px the nav should be {expected}, and {Toggle} is \"{toggle}\" while "
                      + $"{Drawer} is \"{drawer}\". HOUSE RULE, not a criterion: NavMenu.razor.css "
                      + "switches at @media (min-width: 48rem), which is 768px at a 16px root - so 767 is "
                      + "the last drawer pixel and 768 the first laid-out one. A boundary that moved would "
                      + "leave one of the two keyboard shapes in this file untested at the width a reader "
                      + "actually has. HOW the collapsed list is taken out of reach is deliberately not "
                      + "asserted: display: none, visibility: hidden and a zero-height box are all "
                      + "correct, and all of them answer something other than \"ok\" here.";

        Assert.True(asADrawer == (toggle == "ok"), explain);

        Assert.True(asADrawer == (drawer != "ok"), explain);
    }

    /// <summary>
    /// SC 2.1.2 No Keyboard Trap, Level A - and the house rule that the drawer never claims to be a modal.
    /// </summary>
    /// <remarks>
    /// Both directions, because a trap in one is a trap: Tab from the last link has to reach the page
    /// content after the nav, and Shift+Tab from the first has to reach the button that opened the drawer.
    /// <para>
    /// The second assertion is <b>a house rule</b> and not a criterion, and it runs the other way from
    /// what a reviewer expects. <c>role="dialog"</c> or <c>aria-modal="true"</c> on this list would make
    /// a screen reader announce a dialog and confine its reading to the drawer, and nothing here
    /// implements either promise - no trap, no Escape, no inert background, by design, because a
    /// disclosure needs none of them. The failure mode is not a missing trap; it is a claimed one.
    /// </para>
    /// </remarks>
    [BrowserFact]
    public async Task TheOpenDrawerLetsTheKeyboardOutAndNeverClaimsToBeADialog()
    {
        await using var session = await gate.OpenAsync(320, 812);
        await session.GotoAsync("/");
        await session.GridAsync();

        await session.Page.Locator(Toggle).ClickAsync();
        await session.Page.Locator(Drawer).WaitForAsync();

        var links = session.Page.Locator(DrawerLinks);

        // THE SUBJECT. With no links there is no trap to escape from, and both presses below would be
        // measuring the Menu button's own neighbours.
        Assert.True(await links.CountAsync() >= 1,
            "The open drawer renders no links, so there is nothing to tab out of.");

        Assert.NotEqual("none", await session.Page.EvaluateAsync<string>(Scripts.DisplayOfSelector, Drawer));

        await links.Last.FocusAsync();
        Assert.True(await session.Page.EvaluateAsync<bool>(Scripts.FocusIsInside, Drawer),
            "Focusing the drawer's last link did not put focus inside #nav-links, so the press below "
            + "would not be leaving the drawer at all.");

        await session.Page.Keyboard.PressAsync("Tab");

        output.WriteLine("=== Tab from the last drawer link reaches "
                         + await session.FocusedDescriptionAsync() + " ===");

        Assert.True(await session.Page.EvaluateAsync<bool>(Scripts.FocusIsInside, "main#main-content"),
            "Tab from the drawer's last link did not reach the page content: focus is now on "
            + await session.FocusedDescriptionAsync() + ". SC 2.1.2 No Keyboard Trap (Level A). This "
            + "drawer is a disclosure and must not hold the keyboard - if a focus trap has been added to "
            + "it, that is the defect, not the fix. Three reviews of this component have asked for one.");

        await links.First.FocusAsync();
        await session.Page.Keyboard.PressAsync("Shift+Tab");

        output.WriteLine("=== Shift+Tab from the first drawer link reaches "
                         + await session.FocusedDescriptionAsync() + " ===");

        Assert.True(await session.Page.EvaluateAsync<bool>(Scripts.FocusIsInside, Toggle),
            "Shift+Tab from the drawer's first link did not reach the Menu button that opened it: focus "
            + "is now on " + await session.FocusedDescriptionAsync() + ". SC 2.1.2 No Keyboard Trap "
            + "(Level A) is about both directions, and a reader who opened the drawer by mistake gets out "
            + "backwards.");

        var claims = await session.Page.EvaluateAsync<string[]>(Scripts.ModalClaimsInside, "nav");

        Assert.True(claims.Length == 0,
            string.Join("; ", claims) + " inside <nav> claims dialog or modal semantics. HOUSE RULE, not "
            + "a criterion: this drawer is a disclosure - a button with aria-expanded over a list - and it "
            + "implements none of what a dialog promises. role=\"dialog\" or aria-modal=\"true\" tells a "
            + "screen reader to announce a dialog and confine its reading to this subtree, so the two "
            + "assertions above would still pass while a reader was told the keyboard was held and found "
            + "the rest of the page missing instead. If a real modal is ever wanted here, it needs a focus "
            + "trap, an Escape handler, focus return and an inert background, and those are what to gate.");
    }
}

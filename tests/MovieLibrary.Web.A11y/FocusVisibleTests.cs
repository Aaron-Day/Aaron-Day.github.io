using MovieLibrary.Web.A11y.Harness;
using Xunit.Abstractions;

namespace MovieLibrary.Web.A11y;

/// <summary>
/// Every control the keyboard can reach paints a focus ring, read off the browser rather than reasoned
/// about from the cascade.
/// </summary>
/// <remarks>
/// <para>
/// <b>SC 2.4.7 Focus Visible, Level AA.</b> <c>StyleContractTests.TheFocusRingOutSpecifiesBootstrap</c>
/// greps app.css for <c>:root :focus-visible { outline: 3px solid var(--accent) }</c> and reasons about
/// specificity in a comment: (0,2,0) beats Bootstrap's <c>.btn:focus-visible</c> on load order, where a
/// bare <c>:focus-visible</c> at (0,1,0) would lose. A browser is the only place that reasoning can be
/// proved, and a reset killing the focus ring is the most common accessibility regression in a
/// Bootstrap-based app.
/// </para>
/// <para>
/// <b>The ring is driven by real keypresses, which is the whole point.</b> The declaration is on
/// <c>:focus-visible</c>, and that pseudo-class only matches when the browser decides the focus came from
/// the keyboard. A programmatic <c>.focus()</c> or a mouse click on some elements does not match it, so a
/// test that moved focus by script could read <c>outline-style: none</c> on a page that is perfectly
/// conformant - and, worse, could read a ring on a page that had lost it.
/// </para>
/// <para>
/// <b>SC 2.4.13 Focus Appearance is Level AAA and is deliberately not gated.</b> Its 2 CSS px minimum
/// thickness and 3:1 contrast against the unfocused colours are not asserted anywhere here, and the
/// measured <c>3px solid #f5c518</c> would comfortably meet them - which is a reason not to need the gate,
/// not a reason to write one at the wrong level.
/// </para>
/// <para>
/// <b>SC 1.4.13 Content on Hover or Focus is a DIFFERENT criterion, it is Level AA, and it has no subject
/// in Part A.</b> The paragraph above carried the number 1.4.13 where it meant 2.4.13, which read as this
/// project declining to gate an <b>AA</b> criterion on AAA grounds - the exact inversion of what it says.
/// Both numbers are written out here so they cannot be transposed again. What 1.4.13 governs is additional
/// content revealed BESIDE a trigger by hover or focus, and Part A has none: no <c>title</c> attribute
/// anywhere in the markup, no tooltip, no popover, no <c>details</c>/<c>summary</c> and no hover-revealed
/// panel, and the project's one <c>:hover</c> rule is
/// <c>.try-again:hover { border-color: var(--accent) }</c> at app.css:373, which recolours a border and
/// reveals nothing. The skip link is the near miss and is not a subject either: focus changes the
/// presentation of the <b>trigger itself</b> rather than revealing content in addition to it. So 1.4.13's
/// Dismissable, Hoverable and Persistent requirements have nothing here to apply to - and the day a
/// control gains a tooltip they do, which is a real Level AA criterion coming into scope with nothing
/// gating it. That is what this paragraph exists to tell the next reader.
/// </para>
/// <para>
/// <b>What satisfies this is a VISIBLE INDICATOR, not an outline.</b> SC 2.4.7 names no mechanism, and
/// <c>box-shadow</c> is the other common way to draw a ring - which is why
/// <see cref="Scripts.FocusRing"/> has always read and printed it. So a non-transparent outline OR a
/// non-transparent box-shadow passes, and a control with neither fails. Gating "an outline specifically"
/// would be a house rule stricter than the criterion while citing the criterion, which is rule T5 and the
/// same class of error as getting a criterion number wrong. This app draws its ring with an outline and
/// reads <c>box-shadow: none</c> everywhere; the alternative is accepted so that moving the ring to a
/// shadow is a refactor and not a gate failure.
/// </para>
/// <para>
/// <b>Transparency disqualifies either mechanism</b>, because <c>outline: 3px solid transparent</c>
/// satisfies both of the obvious checks and is invisible. It is a real shape: it is how authors keep an
/// outline in the box model while removing it from the page, and <c>box-shadow</c> has the same shape.
/// <b>A limit, recorded rather than hidden:</b> the shadow test reads the computed value as text, so a
/// shadow LIST mixing a transparent layer with a visible one would read as not drawing. No control here
/// has any box-shadow at all, so there is no such shape in this project, and the computed value is
/// printed beside every stop for whoever meets one.
/// </para>
/// <para>
/// <b>The subject set covers the two extra states that are known tab stops.</b> Walking <c>/</c> loaded
/// and <c>/zzz</c> leaves out three controls that other tests prove ARE focusable: <c>a.reload</c> and
/// <c>button.dismiss</c> when the error bar is up, which <c>ObscuredFocusTests</c> counts, and
/// <c>.try-again</c> on the failed catalogue, which <c>ControlBoundaryTests</c> renders. None of them was
/// ever read for a ring. Two more theory rows cover all three, and each names its own subject afterwards
/// rather than trusting the walk to have reached it.
/// </para>
/// </remarks>
[Collection(BrowserCollection.Name)]
public class FocusVisibleTests(BrowserGate gate, ITestOutputHelper output)
{
    /// <summary>
    /// The two colours a fully transparent outline computes to. <c>transparent</c> is defined as
    /// <c>rgba(0, 0, 0, 0)</c>, and the keyword survives in some computed-style readings.
    /// </summary>
    private static readonly string[] Invisible = ["rgba(0, 0, 0, 0)", "transparent"];

    /// <summary>
    /// Both nav layouts, both page types, and the two states that add controls of their own.
    /// </summary>
    /// <remarks>
    /// Below 48rem the nav control is the toggle button; at or above it, the link list - two different
    /// sets of controls, so one viewport would leave one set unmeasured. The not-found page adds the only
    /// anchor in Part A that sits inside running prose, which is the control whose ring is most likely to
    /// differ because it is an inline box rather than a padded one. The last two rows are the states this
    /// file used to miss: the failed catalogue, whose Try again button is a tab stop, and the revealed
    /// error bar, whose Reload link and Dismiss button are two more. One viewport each is enough - what is
    /// new in them is the control, not the layout.
    /// </remarks>
    public static TheoryData<string, int, int, bool, bool> Pages => new()
    {
        { "/", 320, 812, false, false },
        { "/", 1280, 1024, false, false },
        { Site.UnmatchedUrl, 320, 812, false, false },
        { Site.UnmatchedUrl, 1280, 1024, false, false },
        { "/", 1280, 1024, true, false },
        { "/", 1280, 1024, false, true },
    };

    [BrowserTheory]
    [MemberData(nameof(Pages))]
    public async Task EveryKeyboardStopPaintsAFocusRing(
        string address, int width, int height, bool failed, bool barUp)
    {
        await using var session = await gate.OpenAsync(width, height);

        if (failed)
            await session.FailTheCatalogueAsync();

        await session.GotoAsync(address);

        if (failed)
            await session.StateSentenceAsync(PageSession.CouldNotLoad);
        else if (await session.Page.Locator(".catalog-state, .show-state").CountAsync() > 0)
            await session.GridAsync();   // both tile walls page at Catalogue.PageSize

        // The bar is revealed with the same inline display: block the Blazor runtime writes.
        // ObscuredFocusTests' remarks set out why that is a simulation and why it is the honest one
        // available here: on a booted page nothing reveals #blazor-error-ui by itself.
        if (barUp)
        {
            Assert.True(await session.Page.EvaluateAsync<bool>(Scripts.RevealErrorBar));
            await session.Page.Locator("#blazor-error-ui .dismiss").WaitForAsync();
        }

        var state = failed ? ", catalogue failed" : barUp ? ", error bar up" : "";

        for (var press = 0; press < TabWalk.Presses; press++)
        {
            if (await OutsideAsync(session)) break;
            await session.Page.Keyboard.PressAsync("Shift+Tab");
        }

        Assert.True(await OutsideAsync(session),
            $"{TabWalk.Presses} Shift+Tab presses did not walk focus out of the document, so the forward walk would not "
            + "have started at the first stop. Focus is at " + await session.FocusedDescriptionAsync() + ".");

        var seen = new List<string>();
        output.WriteLine($"=== focus rings at {address} {width}x{height}{state} ===");

        var leftTheDocument = false;

        for (var press = 0; press < TabWalk.Presses; press++)
        {
            await session.Page.Keyboard.PressAsync("Tab");

            var ring = await session.Page.EvaluateAsync<string[]>(Scripts.FocusRing);
            var (what, style, thickness, colour, offset, shadow) =
                (ring[0], ring[1], ring[2], ring[3], ring[4], ring[5]);

            if (what == "(nothing is focused)")
            {
                leftTheDocument = true;
                break;
            }

            seen.Add(what);
            output.WriteLine($"  {what}: outline {style} {thickness} {colour}, offset {offset}, "
                             + $"box-shadow {shadow}");

            // ONE assertion over both mechanisms, for the reason the class remarks give: 2.4.7 asks for a
            // visible indicator by ANY means. It replaced three, of which the first was a bare
            // Assert.NotEqual("none", style) - so the mutation this gate is most likely to meet, and the
            // one the header calls the commonest regression in a Bootstrap-based app, failed with
            // `Expected: Not "none" / Actual: "none"` and no context, while the two exotic shapes got full
            // prose. Which of the three shapes was found is now said in the message.
            var outlineDraws = style != "none" && thickness != "0px" && !Invisible.Contains(colour);

            // A shadow counts when it is drawn in something. The computed value serialises its colour, so
            // `box-shadow: 0 0 0 3px transparent` - the exact mirror of the transparent outline - reads as
            // rgba(0, 0, 0, 0) and does not count. See the class remarks for the shadow-list limit.
            var shadowDraws = shadow != "none"
                              && !shadow.Contains("rgba(0, 0, 0, 0)", StringComparison.Ordinal);

            var found = style == "none" ? "outline-style is none"
                : thickness == "0px" ? $"its {style} outline in {colour} is 0px wide"
                : $"its {thickness} {style} outline is drawn in {colour}, which is invisible";

            Assert.True(outlineDraws || shadowDraws,
                $"{what} on {address} at {width}x{height}{state} paints no visible focus indicator when it "
                + $"takes keyboard focus: {found}, and box-shadow reads {shadow}. SC 2.4.7 Focus Visible, "
                + "Level AA, which asks for a visible indicator by ANY means - a non-transparent outline "
                + "or a non-transparent box-shadow each satisfy it, and this control has neither. "
                + "app.css's :root :focus-visible rule is (0,2,0) so that it beats Bootstrap's "
                + ".btn:focus-visible and .form-control:focus on load order; a reset that outranks it, or "
                + "a bare :focus-visible at (0,1,0), loses. The three shapes this catches are a reset "
                + "landing outline-style: none, which is the commonest accessibility regression in a "
                + "Bootstrap-based app; an outline of zero width; and outline: 3px solid transparent, "
                + "which is how an author keeps an outline in the box model while taking it off the page.");
        }

        // A walk that ran out of presses has checked a PREFIX of the tab order and says nothing about the rest, and
        // until this assertion existed it read as a pass: the loop above simply ended. ObscuredFocusTests fails
        // the same way for the same reason. The bound is TabWalk.Presses, sized for the real library.
        Assert.True(leftTheDocument,
            $"{TabWalk.Presses} Tab presses at {address} {width}x{height}{state} never left the document, so the "
            + "walk never completed a cycle and only these stops were checked for a focus ring, a prefix of the "
            + "tab order: " + string.Join(", ", seen));

        // THE SUBJECTS each extra state exists for, named and not counted (rule T1): a count survives one
        // subject being swapped for another, and a walk that never reached the state's own control would
        // leave the loop above reporting on the ordinary page while reading as though it had covered this
        // one. Both sets are proved to be tab stops elsewhere - ObscuredFocusTests counts seven stops with
        // the bar up, ControlBoundaryTests renders Try again on the failed page - so their absence here is
        // a broken walk, not a conformant page.
        if (failed)
            Assert.Contains(seen, s => s.Contains(".try-again", StringComparison.Ordinal));

        if (barUp)
        {
            Assert.Contains(seen, s => s.Contains(".reload", StringComparison.Ordinal));
            Assert.Contains(seen, s => s.Contains(".dismiss", StringComparison.Ordinal));
        }

        // Rule T1. Four stops is the floor on every one of these cases: the skip link, the brand link, one
        // nav control, and then at least one more - the pager's two buttons on the films page, the
        // in-prose link on the not-found page, Try again on the failed one. A walk that stopped early
        // would leave the loop asserting nothing while reading as though it had covered the page.
        Assert.True(seen.Count >= 4,
            $"The tab walk at {address} {width}x{height}{state} found {seen.Count} focusable stop(s) and "
            + "so proved nothing about the focus ring. Every booted page offers at least the skip link, "
            + "the brand link, one nav control and one page control. Stops: " + string.Join(", ", seen));
    }

    private static Task<bool> OutsideAsync(PageSession session) =>
        session.Page.EvaluateAsync<bool>(
            "() => !document.activeElement || document.activeElement === document.body"
            + " || document.activeElement === document.documentElement");
}

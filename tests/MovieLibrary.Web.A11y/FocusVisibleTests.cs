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
/// <b>SC 1.4.13 Focus Appearance is Level AAA and is deliberately not gated.</b> Its 2 CSS px minimum
/// thickness and 3:1 contrast against the unfocused colours are not asserted anywhere here, and the
/// measured <c>3px solid #f5c518</c> would comfortably meet them - which is a reason not to need the gate,
/// not a reason to write one at the wrong level.
/// </para>
/// <para>
/// <b>Transparency is asserted as well as style and width</b>, because <c>outline: 3px solid transparent</c>
/// satisfies both of the obvious checks and is invisible. It is a real shape: it is how authors keep an
/// outline in the box model while removing it from the page.
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
    /// Both nav layouts and both page types. Below 48rem the nav control is the toggle button; at or above
    /// it, the link list - two different sets of controls, so one viewport would leave one set unmeasured.
    /// The not-found page adds the only anchor in Part A that sits inside running prose, which is the
    /// control whose ring is most likely to differ because it is an inline box rather than a padded one.
    /// </summary>
    public static TheoryData<string, int, int> Pages => new()
    {
        { "/", 320, 812 },
        { "/", 1280, 1024 },
        { Site.UnmatchedUrl, 320, 812 },
        { Site.UnmatchedUrl, 1280, 1024 },
    };

    [BrowserTheory]
    [MemberData(nameof(Pages))]
    public async Task EveryKeyboardStopPaintsAFocusRing(string address, int width, int height)
    {
        await using var session = await gate.OpenAsync(width, height);
        await session.GotoAsync(address);
        if (await session.Page.Locator(".catalog-state").CountAsync() > 0) await session.GridAsync();

        for (var press = 0; press < 40; press++)
        {
            if (await OutsideAsync(session)) break;
            await session.Page.Keyboard.PressAsync("Shift+Tab");
        }

        Assert.True(await OutsideAsync(session),
            "Forty Shift+Tab presses did not walk focus out of the document, so the forward walk would not "
            + "have started at the first stop. Focus is at " + await session.FocusedDescriptionAsync() + ".");

        var stops = 0;
        output.WriteLine($"=== focus rings at {address} {width}x{height} ===");

        for (var press = 0; press < 40; press++)
        {
            await session.Page.Keyboard.PressAsync("Tab");

            var ring = await session.Page.EvaluateAsync<string[]>(Scripts.FocusRing);
            var (what, style, thickness, colour, offset, shadow) =
                (ring[0], ring[1], ring[2], ring[3], ring[4], ring[5]);

            if (what == "(nothing is focused)") break;

            stops++;
            output.WriteLine($"  {what}: outline {style} {thickness} {colour}, offset {offset}, "
                             + $"box-shadow {shadow}");

            Assert.NotEqual("none", style);

            Assert.True(thickness != "0px",
                $"{what} on {address} at {width}x{height} paints an outline of zero width when it takes "
                + "keyboard focus. SC 2.4.7 Focus Visible, Level AA. app.css's :root :focus-visible rule is "
                + "(0,2,0) so that it beats Bootstrap's .btn:focus-visible and .form-control:focus on load "
                + $"order; a reset that outranks it, or a bare :focus-visible at (0,1,0), loses. box-shadow "
                + $"reads {shadow}, in case the ring was moved there.");

            Assert.True(!Invisible.Contains(colour),
                $"{what} on {address} at {width}x{height} paints a {thickness} {style} outline in {colour}, "
                + "which is invisible. SC 2.4.7 Focus Visible, Level AA: an outline kept in the box model "
                + "and taken off the page is the same defect as no outline, and it passes the style and "
                + "width checks above.");
        }

        // Rule T1. Four stops is the floor on every one of these four cases: the skip link, the brand link,
        // one nav control, and then at least one more - the pager's two buttons on the films page, the
        // in-prose link on the not-found page. A walk that stopped early would leave this loop asserting
        // nothing while reading as though it had covered the page.
        Assert.True(stops >= 4,
            $"The tab walk at {address} {width}x{height} found {stops} focusable stop(s) and so proved "
            + "nothing about the focus ring. Every booted page offers at least the skip link, the brand "
            + "link, one nav control and one page control.");
    }

    private static Task<bool> OutsideAsync(PageSession session) =>
        session.Page.EvaluateAsync<bool>(
            "() => !document.activeElement || document.activeElement === document.body"
            + " || document.activeElement === document.documentElement");
}

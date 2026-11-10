using Microsoft.Playwright;
using MovieLibrary.Web.A11y.Harness;
using Xunit.Abstractions;

namespace MovieLibrary.Web.A11y;

/// <summary>
/// Nothing the keyboard can reach is hidden under the fixed error bar, at the viewport where that bar is
/// worst.
/// </summary>
/// <remarks>
/// <para>
/// <b>SC 2.4.11 Focus Not Obscured (Minimum), Level AA, new in WCAG 2.2.</b> Its words are "the component
/// is not ENTIRELY hidden due to author-created content", so the conformance assertion below is that AT
/// LEAST ONE probe point on the focused element resolves to it. SC 2.4.12 Focus Not Obscured (Enhanced) is
/// the one that says "no part of", and it is <b>Level AAA</b>: this project does not gate on it, and that
/// is one of the two reasons the five-point tally below is printed rather than asserted.
/// </para>
/// <para>
/// <b>The second reason, MEASURED: an all-five-points rule rejects correct layout, which is rule T5.</b>
/// The four corners are taken one pixel inside the bounding rect, and three of this app's controls do not
/// fill their own rect at the corners. <c>.pager button</c> and <c>.nav-toggle</c> carry a
/// <c>border-radius</c>, so a corner probe lands on the parent; the not-found page's in-prose link is an
/// inline box spanning two line boxes, so two corners of its rect are in the surrounding paragraph.
/// Measured tallies on a correct page run 2/5 and 3/5, never 5/5. A gate asserting five would report four
/// conformant controls as obscured, which is the cry-wolf failure that gets a gate deleted.
/// </para>
/// <para>
/// <b>What this proves that <c>ReflowTests.TheScrollReserveNeverFallsBelowTheBarsOwnCap</c> cannot.</b>
/// That test reads <c>scroll-padding-bottom</c> and checks the arithmetic resolves to
/// <c>max(6rem, var(--bar-max))</c>. It says nothing about whether a browser ACTS on the reserve. This
/// walks the real tab order with the bar up and hit-tests where each control comes to rest, which is the
/// only place that can be seen - and it is seen: at 320x256 the pager row lays out at y 162 with the bar's
/// top edge at y 154, and the focus move scrolls it up to y 56.
/// </para>
/// <para>
/// <b>The bar is revealed from here, and that is a SIMULATION of the runtime's own reveal.</b> On a booted
/// page nothing reveals <c>#blazor-error-ui</c> by itself: index.html's watchdog reveals it only on a boot
/// failure, which is a different state with no app in it, and the Blazor runtime reveals it on an unhandled
/// exception, which this gate has no way to provoke. <see cref="Scripts.RevealErrorBar"/> writes the same
/// inline <c>display: block</c> the runtime writes. A full-height bar over a page that works is the
/// combination nothing in this project had measured.
/// </para>
/// </remarks>
[Collection(BrowserCollection.Name)]
public class ObscuredFocusTests(BrowserGate gate, ITestOutputHelper output)
{
    /// <summary>
    /// Reflow's normative viewport and the unzoomed desktop baseline, each with the bar down and up.
    /// </summary>
    /// <remarks>
    /// 320x256 is where the bar is worst: <c>--bar-max</c> is 40dvh, so it can take 102.4px of a 256px
    /// viewport - two fifths of the screen - while at 1280x1024 the same cap is 409.6px of a much taller
    /// page and no control sits near it. Both are here because the second is the state a reader is most
    /// likely to be in and the first is the state that can actually fail.
    /// </remarks>
    public static TheoryData<int, int, bool> Viewports => new()
    {
        { 320, 256, false },
        { 320, 256, true },
        { 1280, 1024, false },
        { 1280, 1024, true },
    };

    [BrowserTheory]
    [MemberData(nameof(Viewports))]
    public async Task NoFocusableControlIsEntirelyHiddenByTheErrorBar(int width, int height, bool barUp)
    {
        await using var session = await gate.OpenAsync(width, height);
        await session.GotoAsync("/");
        await session.GridAsync();

        Assert.Equal("none", await session.Page.EvaluateAsync<string>(Scripts.DisplayOf, "blazor-error-ui"));

        if (barUp)
        {
            Assert.True(await session.Page.EvaluateAsync<bool>(Scripts.RevealErrorBar));
            await session.Page.Locator("#blazor-error-ui .dismiss").WaitForAsync();
            Assert.Equal("block",
                await session.Page.EvaluateAsync<string>(Scripts.DisplayOf, "blazor-error-ui"));
        }

        var stops = await WalkAsync(session, width, height, barUp);

        // Rule T1, and the floor that matters most here: a hit test over an empty walk is precisely the
        // trivially-passing assertion this project keeps catching. Every booted page offers the skip link,
        // the brand link, one nav control and the two pager buttons; the revealed bar adds Reload and
        // Dismiss, which are the two stops that only exist in the state under test.
        Assert.True(stops.Count >= (barUp ? 7 : 5),
            $"The tab walk at {width}x{height} found {stops.Count} focusable stop(s). The films page offers "
            + "the skip link, the brand link, one nav control and two pager buttons, and the revealed bar "
            + "adds Reload and Dismiss. Fewer means the walk stopped early and every assertion below passed "
            + "over nothing. Stops: " + string.Join(", ", stops.Select(s => s.What)));

        foreach (var stop in stops)
        {
            // The subject, before anything is said about it. A hit test against an element with no visible
            // box passes or throws for reasons that have nothing to do with obscuration.
            Assert.True(stop.Visible,
                $"{stop.What} took keyboard focus at {width}x{height} while rendering a box of "
                + $"{stop.Width}x{stop.Height} that is not visible, so the hit test below would have passed "
                + "over it rather than measured it.");

            Assert.True(stop.Clear >= 1,
                $"{stop.What} is ENTIRELY hidden when it receives keyboard focus at {width}x{height} with "
                + $"the error bar {(barUp ? "up" : "down")}: none of its {stop.Points} probe points resolve "
                + "to it or to a descendant. SC 2.4.11 Focus Not Obscured (Minimum), Level AA. Blocked by "
                + string.Join("; ", stop.Blockers)
                + ". What is supposed to prevent this is app.css's "
                + "html { scroll-padding-bottom: max(6rem, var(--bar-max)) }, and it only moves a control "
                + "the browser decides to scroll at all - so the thing to check is whether the control is "
                + "outside the optimal viewing region, not merely whether it is under the bar.");
        }

        // PRINT ONLY, and it must stay that way. Five of five is the shape of SC 2.4.12 Focus Not Obscured
        // (Enhanced), which is Level AAA and is not gated here - and on this app it is unreachable anyway,
        // because a border-radius or an inline box spanning two lines puts a rect corner outside its own
        // element. See the class remarks before turning any of this into an assertion.
        output.WriteLine($"=== focus obscuration at {width}x{height}, bar {(barUp ? "up" : "down")} ===");
        foreach (var stop in stops)
            output.WriteLine($"  {stop.What} {stop.Width}x{stop.Height} clear {stop.Clear}/{stop.Points}"
                             + (stop.Offscreen > 0 ? $" ({stop.Offscreen} probe(s) off-viewport)" : "")
                             + (stop.Blockers.Length > 0 ? " over " + string.Join("; ", stop.Blockers) : ""));
    }

    /// <summary>
    /// Walks the whole forward tab order once, from outside the document, hit-testing each stop after its
    /// scroll has settled.
    /// </summary>
    /// <remarks>
    /// It starts by walking BACK out of the document, for the reason
    /// <c>FocusTests.TheSkipLinkMovesFocusIntoMainAndKeepsTheQueryString</c> records: on arrival
    /// FocusOnNavigate has already put focus on the h1 inside main, so a forward Tab from there skips every
    /// control above it - the skip link included, which is the one most worth measuring, because it is
    /// <c>position: fixed</c> and so is the one control the page cannot scroll out of the bar's way.
    /// </remarks>
    private async Task<List<Stop>> WalkAsync(PageSession session, int width, int height, bool barUp)
    {
        for (var press = 0; press < 40 && !await OutsideAsync(session); press++)
            await session.Page.Keyboard.PressAsync("Shift+Tab");

        Assert.True(await OutsideAsync(session),
            "Forty Shift+Tab presses did not walk focus out of the document, so the forward walk would not "
            + "have started at the first stop. Focus is at " + await session.FocusedDescriptionAsync()
            + ", which suggests a focus trap.");

        var stops = new List<Stop>();

        for (var press = 0; press < 40; press++)
        {
            await session.Page.Keyboard.PressAsync("Tab");
            await SettledAsync(session);

            var stop = await session.Page.EvaluateAsync<Stop>(Scripts.FocusObscuration);
            if (!stop.Exists) return stops;

            stops.Add(stop);
        }

        Assert.Fail($"Forty Tab presses at {width}x{height} (bar {(barUp ? "up" : "down")}) never left the "
                    + "document, so the walk never completed a cycle and these stops are only a prefix of "
                    + "it: " + string.Join(", ", stops.Select(s => s.What)));
        return stops;
    }

    private static Task<bool> OutsideAsync(PageSession session) =>
        session.Page.EvaluateAsync<bool>(
            "() => !document.activeElement || document.activeElement === document.body"
            + " || document.activeElement === document.documentElement");

    /// <summary>
    /// Waits for the focus move's scroll to come to rest. See <see cref="Scripts.ScrollSettled"/> for why
    /// this is not optional: without it the pager buttons measure as entirely hidden at 320x256 when they
    /// are not, because Bootstrap's reboot makes the scroll smooth and the probe lands mid-animation.
    /// </summary>
    private static async Task SettledAsync(PageSession session)
    {
        await session.Page.EvaluateAsync(Scripts.ForgetScroll);
        await session.Page.WaitForFunctionAsync(Scripts.ScrollSettled, null,
            new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    /// <summary>
    /// One tab stop, as <see cref="Scripts.FocusObscuration"/> returns it. A class with settable properties
    /// rather than a record, for the reason <c>TouchTargetReportTests.Control</c> gives: Playwright's .NET
    /// binding constructs the type with no arguments and fills it by property.
    /// </summary>
    public sealed class Stop
    {
        public string What { get; set; } = string.Empty;

        public bool Exists { get; set; }

        public bool Visible { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }

        public int Points { get; set; }

        public int Clear { get; set; }

        public int Offscreen { get; set; }

        public string[] Blockers { get; set; } = [];
    }
}

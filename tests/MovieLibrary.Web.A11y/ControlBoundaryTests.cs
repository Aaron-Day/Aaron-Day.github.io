using MovieLibrary.Web.A11y.Harness;
using Xunit.Abstractions;

namespace MovieLibrary.Web.A11y;

/// <summary>
/// No control the page actually renders draws its boundary in the decoration-only token.
/// </summary>
/// <remarks>
/// <para>
/// <b>SC 1.4.11 Non-text Contrast, Level AA</b> - and the reason it is here rather than in Task 4b's scan
/// is that <b>axe has no non-text-contrast rule at all</b>, at any tag. Nothing in the axe run can see
/// this, now or when the rule list grows. app.css states the measurement it rests on: <c>--line</c>
/// (#262b38) is <b>1.37:1</b> against <c>--bg</c> and fails, so it is reserved for decoration, and
/// <c>--line-strong</c> (#657084) is <b>3.89:1</b> and is what a control's own boundary uses.
/// </para>
/// <para>
/// <b>What this adds to the source scan.</b>
/// <c>StyleContractTests.NoControlsBoundaryIsDrawnInTheDecorationOnlyToken</c> greps the stylesheets, and
/// its own remarks name what reading a selector cannot see: <c>.try-again</c>, <c>.nav-toggle</c> and
/// <c>#blazor-error-ui .dismiss</c> are all buttons selected by class, so no scan for a control's element
/// name reaches them - and the one real regression this project had was exactly that shape, the Try again
/// button dropped to <c>--line</c> while it carried <c>aria-disabled</c> and
/// <c>background: transparent</c>, which left it with no boundary a low-vision reader could find for the
/// whole of a retry. Reading the rendered page does not care how a control was selected. It also sees
/// through a Bootstrap utility class and an inline style, neither of which is in any stylesheet this
/// repository owns.
/// </para>
/// <para>
/// <b>The token colour is read off the page, not retyped.</b> <see cref="Scripts.DecorationBorderedControls"/>
/// gives a hidden probe <c>color: var(--line)</c> and compares against its computed colour, so retuning
/// the token moves this check with it rather than leaving a stale hex here. The hex figures in these
/// remarks are quoted from app.css's own header, which is where the contrast ratios are recorded.
/// </para>
/// </remarks>
[Collection(BrowserCollection.Name)]
public class ControlBoundaryTests(BrowserGate gate, ITestOutputHelper output)
{
    /// <summary>
    /// The two widths that render different controls, each over the loaded and the failed catalogue.
    /// </summary>
    /// <remarks>
    /// Below 48rem the nav collapses to <c>.nav-toggle</c> and the links are <c>display: none</c>; at or
    /// above it the toggle is <c>display: none</c> and the links lay out. The failed catalogue is what
    /// renders <c>.try-again</c>. Across the four cases every control in Part A that carries a border is a
    /// subject at least once, and the three selected by class - which the source scan cannot see - are
    /// covered here and only here.
    /// </remarks>
    public static TheoryData<int, bool> Cases => new()
    {
        { 320, false },
        { 320, true },
        { 1280, false },
        { 1280, true },
    };

    [BrowserTheory]
    [MemberData(nameof(Cases))]
    public async Task NoRenderedControlDrawsItsBoundaryInTheDecorationOnlyToken(int width, bool failed)
    {
        await using var session = await gate.OpenAsync(width, 812);

        if (failed)
            await session.FailTheCatalogueAsync();

        await session.GotoAsync("/");

        if (failed)
            await session.StateSentenceAsync(PageSession.CouldNotLoad);
        else
            await session.GridAsync();

        // The error bar is revealed so that #blazor-error-ui .dismiss is a subject: it is one of the three
        // class-selected controls, and on a page that works nothing reveals the bar by itself. This writes
        // the same inline display the Blazor runtime writes; ObscuredFocusTests' remarks set out why that
        // is a simulation and why it is the honest one available here.
        Assert.True(await session.Page.EvaluateAsync<bool>(Scripts.RevealErrorBar));
        await session.Page.Locator("#blazor-error-ui .dismiss").WaitForAsync();

        if (width >= 768)
            await session.Page.Locator("ul#nav-links a").First.WaitForAsync();

        var bordered = await session.Page.EvaluateAsync<string[]>(Scripts.BorderedControls);

        output.WriteLine($"=== bordered controls at {width}x812, catalogue "
                         + $"{(failed ? "failed" : "loaded")}: {bordered.Length} ===");
        foreach (var control in bordered.OrderBy(c => c, StringComparer.Ordinal))
            output.WriteLine("  " + control);

        // THE SUBJECTS, before anything is said about them, and rule T1's floor. The assertion below
        // returns only the WRONG controls, so an empty answer reads identically whether every boundary is
        // right or no control has a boundary at all. These name what must be there rather than counting:
        // a count survives one subject being swapped for another, and a vanished subject is the failure
        // this project keeps finding.
        Assert.Contains(bordered, c => c.StartsWith("button.dismiss", StringComparison.Ordinal));

        if (failed)
            Assert.Contains(bordered, c => c.Contains(".try-again", StringComparison.Ordinal));

        if (width < 768)
            Assert.Contains(bordered, c => c.Contains(".nav-toggle", StringComparison.Ordinal));

        Assert.True(bordered.Length >= (failed ? 2 : 3),
            $"Only {bordered.Length} visible control(s) paint a border at {width}x812 with the catalogue "
            + $"{(failed ? "failed" : "loaded")}. The loaded page has the two pager buttons and Dismiss, "
            + "and the failed page has Try again and Dismiss. Fewer means either the enumeration broke or "
            + "a control lost its border - and for .try-again and .nav-toggle, which both set "
            + "background: transparent, the border IS the control, so losing it is an SC 1.4.11 failure in "
            + "its own right rather than a broken test. Found: " + string.Join("; ", bordered));

        var drawnInDecoration =
            await session.Page.EvaluateAsync<string[]>(Scripts.DecorationBorderedControls);

        Assert.True(drawnInDecoration.Length == 0,
            $"At {width}x812 with the catalogue {(failed ? "failed" : "loaded")}, "
            + $"{drawnInDecoration.Length} rendered control(s) draw a border in var(--line): "
            + string.Join("; ", drawnInDecoration)
            + ". app.css measures that token at 1.37:1 against --bg and records it as FAILING SC 1.4.11 "
            + "Non-text Contrast (Level AA); --line-strong is the 3.89:1 token reserved for a control's own "
            + "boundary. axe has no non-text-contrast rule, so nothing else in this repository catches it. "
            + "This has happened once already, to the Try again button, whose transparent background makes "
            + "its border the only thing there is to find.");
    }
}

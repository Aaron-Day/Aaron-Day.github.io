using MovieLibrary.Web.A11y.Harness;
using Xunit.Abstractions;

namespace MovieLibrary.Web.A11y;

/// <summary>
/// No two-dimensional scrolling, at the one viewport a criterion names and at four this project chose.
/// </summary>
/// <remarks>
/// <para>
/// <b>ONLY 320x256 IS NORMATIVE.</b> SC 1.4.10 Reflow, Level AA, is content at 1280x1024 zoomed to 400%,
/// which for vertically-scrolling content is 320 CSS px wide <b>and 256 CSS px tall</b>. The height is half
/// the criterion and is the half that gets forgotten. The other four sizes below are a HOUSE RULE - "it must
/// work on phones and tablets" - and a failure at 768x1024 must never be reported as a conformance failure.
/// Each case says which it is, and so does the failure message.
/// </para>
/// </remarks>
[Collection(BrowserCollection.Name)]
public class ReflowTests(BrowserGate gate, ITestOutputHelper output)
{
    /// <summary>The normative Reflow viewport: 1280x1024 at 400% zoom.</summary>
    private const int NormativeWidth = 320;

    private const int NormativeHeight = 256;

    public static TheoryData<int, int, string> Viewports => new()
    {
        { NormativeWidth, NormativeHeight, "SC 1.4.10 Reflow, Level AA (1280x1024 at 400%)" },
        { 320, 812, "house rule: the narrowest phone in portrait" },
        { 375, 812, "house rule: a common phone in portrait" },
        { 768, 1024, "house rule: a tablet in portrait" },
        { 1280, 1024, "house rule: the unzoomed desktop baseline" },
    };

    [BrowserTheory]
    [MemberData(nameof(Viewports))]
    public async Task NoHorizontalScrollAtAnyOfTheseViewports(int width, int height, string why)
    {
        await using var session = await gate.OpenAsync(width, height);
        await session.GotoAsync("/");
        await session.GridAsync();

        var widths = await session.Page.EvaluateAsync<int[]>(Scripts.DocumentWidths);
        var overflowing = await session.Page.EvaluateAsync<string[]>(Scripts.Overflowing);

        output.WriteLine($"{width}x{height} ({why}): scrollWidth {widths[0]}, clientWidth {widths[1]}, "
                         + $"{overflowing.Length} element(s) past the viewport");
        foreach (var element in overflowing) output.WriteLine("  " + element);

        Assert.True(widths[0] <= widths[1],
            $"The document scrolls horizontally at {width}x{height}: scrollWidth {widths[0]} against "
            + $"clientWidth {widths[1]}. This viewport is {why}.");

        // The document metric alone misses a child overflowing inside an overflow: hidden ancestor, which
        // the reader still cannot see. Both halves, with a 1 px tolerance for subpixel rounding.
        Assert.True(overflowing.Length == 0,
            $"{overflowing.Length} element(s) extend past the viewport at {width}x{height}, which is {why}: "
            + string.Join("; ", overflowing)
            + ". The document's own scrollWidth did not catch this, which means the overflow is inside an "
            + "ancestor that clips it - invisible to the usual check and just as unreadable.");
    }

    /// <summary>
    /// The grid falls to exactly two columns below 22.5rem, which is a density HOUSE RULE and not a
    /// criterion: one column is the canonical Reflow outcome and is fully conformant.
    /// </summary>
    /// <remarks>
    /// This is where PosterGrid's arithmetic gets checked for real. minmax(7rem, 1fr) with auto-fill would
    /// put a third cramped column on a narrow phone, so an explicit two-column rule takes over below
    /// 22.5rem; the join between the two was checked twice on paper, and the DOM-level tests cannot see
    /// either number because neither is in the markup.
    /// </remarks>
    [BrowserFact]
    public async Task TheGridFallsToTwoColumnsAtTheNarrowestWidth()
    {
        await using var session = await gate.OpenAsync(NormativeWidth, NormativeHeight);
        await session.GotoAsync("/");
        await session.GridAsync();

        var columns = await session.Page.EvaluateAsync<int>(Scripts.GridColumns);
        output.WriteLine($"grid columns at {NormativeWidth}px: {columns}");

        Assert.Equal(2, columns);
    }

    /// <summary>
    /// The scroll reserve under the fixed error bar resolves to the LARGER of 6rem and the bar's own cap, at
    /// every tested viewport height.
    /// </summary>
    /// <remarks>
    /// SC 2.4.11 Focus Not Obscured (Minimum), Level AA, new in WCAG 2.2. The bar is position: fixed at
    /// bottom: 0, so a control scrolled into view lands under it unless the scrollport reserves at least as
    /// much room as the bar can occupy. HEIGHT is the dimension that governs.
    /// <para>
    /// <b>A correction to how this gets described.</b> The 40dvh branch wins at EVERY viewport taller than
    /// 240px, not only at 256px: 40% of 256 is 102.4px against 6rem's 96px, and the gap only widens as the
    /// viewport grows. What is special about 256px is that it is the normative Reflow height, which is where
    /// the old flat 6rem was measured and found 6.4px short - so it is the viewport the fix was made for,
    /// not the only one where the two branches differ. app.css says this correctly; a summary of it did not.
    /// </para>
    /// </remarks>
    [BrowserTheory]
    [MemberData(nameof(Viewports))]
    public async Task TheScrollReserveNeverFallsBelowTheBarsOwnCap(int width, int height, string why)
    {
        await using var session = await gate.OpenAsync(width, height);
        await session.GotoAsync("/");
        await session.GridAsync();

        var reserve = await session.Page.EvaluateAsync<double[]>(Scripts.ScrollReserve);
        var (resolved, sixRem, barMax, viewport) = (reserve[0], reserve[1], reserve[2], reserve[3]);
        var expected = Math.Max(sixRem, barMax);

        output.WriteLine(
            $"{width}x{height} ({why}): scroll-padding-bottom {resolved}px = "
            + $"max(6rem {sixRem}px, --bar-max {barMax}px) over a {viewport}px viewport");

        Assert.True(Math.Abs(resolved - expected) < 0.5,
            $"html reserves {resolved}px at the bottom of the scrollport at {width}x{height}, but the error "
            + $"bar can occupy up to {barMax}px there (--bar-max is 40dvh) and the 6rem floor is {sixRem}px. "
            + "A reserve below the larger of the two leaves a control that was scrolled into view sitting "
            + "under the bar.");
    }
}

using MovieLibrary.Web.A11y.Harness;
using Xunit.Abstractions;

namespace MovieLibrary.Web.A11y;

/// <summary>
/// A PRINT-ONLY inventory of every control smaller than the project's own <c>--touch: 44px</c> goal.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here asserts a size, and nothing here may ever report a number as a conformance failure.</b>
/// 44x44 is <b>SC 2.5.5 Target Size (Enhanced), Level AAA</b>, which is the figure this project's token aims
/// at as a HOUSE RULE. The criterion the project is held to is <b>SC 2.5.8 Target Size (Minimum), Level AA</b>,
/// and that is <b>24x24</b>. Every pixel above 24 is house rule, not requirement. Do not cite 2.5.8 for the
/// number 44, and do not let anything below turn into a failure message that reads like a criterion.
/// </para>
/// <para>
/// <b>SC 2.5.8 belongs to axe and not to anything here.</b> axe's target-size rule carries the wcag22aa tag,
/// so the tag list in AxeScan already enables it, it implements the spacing exception correctly, and it is
/// maintained. target-size is also in the fail-on-incomplete list, so an undecided overlap is a failure.
/// A second, hand-rolled 24px check would simply fight it: this project's exceptions would have to cover the
/// in-prose link on the not-found page and the 1x1 unfocused skip link, and a hand-rolled exemption cannot
/// suppress an axe finding anyway. There is deliberately no 24px test in this project.
/// </para>
/// </remarks>
[Collection(BrowserCollection.Name)]
public class TouchTargetReportTests(BrowserGate gate, ITestOutputHelper output)
{
    /// <summary>The project's own goal, which is SC 2.5.5's figure at Level AAA. A house rule here.</summary>
    private const double HouseGoal = 44;

    public static TheoryData<int, int> Viewports => new() { { 375, 812 }, { 1280, 1024 } };

    [BrowserTheory]
    [MemberData(nameof(Viewports))]
    public async Task ReportEveryControlBelowTheHouseTouchGoal(int width, int height)
    {
        await using var session = await gate.OpenAsync(width, height);
        await session.GotoAsync("/");
        await session.GridAsync();

        // At 320-375px the drawer holds the only nav links there are, so a report taken with it shut would
        // leave them out entirely - and they are the controls most likely to be short.
        if (await session.Page.Locator("button.nav-toggle").IsVisibleAsync())
            await session.Page.Locator("button.nav-toggle").ClickAsync();

        var controls = await session.Page.EvaluateAsync<Control[]>(Scripts.Controls);

        // Rule T1. A report over an empty set reads exactly like a report of nothing to say. The films page
        // at either viewport has at least the brand link, the Films link and the two pager buttons.
        Assert.True(controls.Length >= 4,
            $"The touch-target report found {controls.Length} visible control(s) at {width}x{height}. The "
            + "films page has at least four - the brand link, the Films link and the two pager buttons - so "
            + "fewer means the enumeration is broken and the report below says nothing.");

        output.WriteLine($"=== controls at {width}x{height}: {controls.Length} ===");

        var short_ = controls
            .Where(c => c.Width < HouseGoal || c.Height < HouseGoal)
            .OrderBy(c => Math.Min(c.Width, c.Height))
            .ToList();

        output.WriteLine(
            $"below the --touch: {HouseGoal}px HOUSE GOAL (SC 2.5.5 Target Size (Enhanced) is this figure "
            + "and is Level AAA; the Level AA criterion is SC 2.5.8 Target Size (Minimum) at 24x24, which "
            + $"axe owns): {(short_.Count == 0 ? "none" : short_.Count.ToString())}");

        foreach (var control in short_)
            output.WriteLine($"  {control.What} [{control.Name}] {control.Width}x{control.Height}");

        output.WriteLine("every control, for the record:");
        foreach (var control in controls.OrderBy(c => c.What, StringComparer.Ordinal))
            output.WriteLine($"  {control.What} [{control.Name}] {control.Width}x{control.Height}");
    }

    /// <summary>
    /// One control, as Scripts.Controls returns it. A class with settable properties and not a record:
    /// Playwright's .NET binding constructs the type with no arguments and fills it by property, and
    /// rejects a record with "Return type mismatch ... got Object".
    /// </summary>
    public sealed class Control
    {
        public string What { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public double Width { get; set; }

        public double Height { get; set; }
    }
}

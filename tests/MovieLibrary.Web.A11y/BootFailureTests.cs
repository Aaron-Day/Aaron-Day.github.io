using MovieLibrary.Web.A11y.Harness;
using Xunit.Abstractions;

namespace MovieLibrary.Web.A11y;

/// <summary>
/// The boot failure path: the runtime script never arrives, so index.html's watchdog is the only thing left
/// to tell the user anything.
/// </summary>
/// <remarks>
/// <para>
/// This is the one path where NOTHING of the app exists - no Blazor, no layout, no announcer - so every
/// assertion here is about the host page, and none of it can be reached by any other kind of test.
/// </para>
/// <para>
/// <b>What the design actually promises, which is not what an earlier brief asserted.</b> index.html's own
/// comment says all three failure paths end in ONE state: the spinner and its text hidden, #boot-status turned
/// into plain visible text that says what happened, <b>and the bar visible</b>. The bar is role="alert" and is
/// the only announcer, so #boot-status loses its role first and is never a second live region. The defect that
/// was fixed was TWO LIVE REGIONS, not two visible sentences - there are deliberately two visible texts, the
/// bar's and #boot-status's. Asserting the bar is hidden would assert the opposite of the design.
/// </para>
/// </remarks>
[Collection(BrowserCollection.Name)]
public class BootFailureTests(BrowserGate gate, ITestOutputHelper output)
{
    /// <summary>The published script name carries a fingerprint, hence the wildcard.</summary>
    private const string RuntimeScript = "**/_framework/blazor.webassembly*.js";

    /// <summary>The sentence the watchdog writes. Hard-coded here because index.html is not C#.</summary>
    private const string FailureSentence =
        "The library could not start. Reload the page to try again. If that does not help, try again in a "
        + "few minutes.";

    [BrowserFact]
    public async Task TheWatchdogLeavesExactlyOneAnnouncerAndTwoVisibleTexts()
    {
        await using var session = await FailedBootAsync();
        var page = session.Page;

        // 1. The fix itself. #boot-status is role="status" while the page is loading, and the watchdog
        // removes the role BEFORE writing the text, so the sentence is never announced twice.
        Assert.False(await page.EvaluateAsync<bool>(Scripts.HasAttribute, new[] { "boot-status", "role" }),
            "#boot-status still carries a role after the boot failure, so the failure sentence is announced "
            + "by the status region AND by the error bar: two live regions for one event, which is the "
            + "defect this path was fixed for.");

        // 2. The bar is up, and it is the ONLY assertive channel in the document.
        Assert.Equal("block", await page.EvaluateAsync<string>(Scripts.DisplayOf, "blazor-error-ui"));

        var channels = await page.EvaluateAsync<string[]>(Scripts.AssertiveChannels);
        output.WriteLine("assertive channels: " + string.Join(", ", channels));
        Assert.Equal(["div#blazor-error-ui"], channels);

        // 3. The loading decoration is gone, both halves of it.
        Assert.Equal("none", await page.EvaluateAsync<string>(Scripts.DisplayOfSelector, ".loading-progress"));
        Assert.Equal("none",
            await page.EvaluateAsync<string>(Scripts.DisplayOfSelector, ".loading-progress-text"));

        // 4. The explanation is visible, focusable, and says the one thing.
        var status = page.Locator("#boot-status");
        await status.WaitForAsync();
        Assert.True(await status.IsVisibleAsync());
        Assert.Equal("-1", await status.GetAttributeAsync("tabindex"));
        Assert.Equal(FailureSentence, (await status.InnerTextAsync()).Trim());
    }

    /// <summary>
    /// Dismiss hides the bar and moves focus to #boot-status, which is the most valuable assertion on this
    /// path: it guards against the runtime's own dismiss handler, which drops focus to the document body and
    /// would leave a keyboard user with no focus indicator anywhere and the explanation unread.
    /// </summary>
    /// <remarks>
    /// SC 2.4.3 Focus Order, Level A, is the criterion the move answers - focus has to go somewhere a reader
    /// can make sense of, and the only thing left on the page is the sentence saying what happened.
    /// </remarks>
    [BrowserFact]
    public async Task DismissHidesTheBarAndMovesFocusToTheExplanation()
    {
        await using var session = await FailedBootAsync();

        await session.Page.Locator("#blazor-error-ui .dismiss").ClickAsync();

        Assert.Equal("none",
            await session.Page.EvaluateAsync<string>(Scripts.DisplayOf, "blazor-error-ui"));

        Assert.Equal("boot-status", await session.FocusedIdAsync());
    }

    /// <summary>Reload really reloads: the document is replaced, and the failure state comes back.</summary>
    [BrowserFact]
    public async Task ReloadReloadsTheDocument()
    {
        await using var session = await FailedBootAsync();
        var page = session.Page;

        await page.EvaluateAsync(Scripts.Mark);
        Assert.True(await page.EvaluateAsync<bool>(Scripts.MarkSurvives));

        await page.Locator("#blazor-error-ui .reload").ClickAsync();

        // The mark cannot survive a reload, and nothing else on this page removes it. Waiting on the mark
        // rather than on a navigation event means this cannot pass because of a navigation that was not one.
        await page.WaitForFunctionAsync("() => window.a11yReloadMark !== true");

        await page.Locator("#boot-status.boot-failure").WaitForAsync();
        Assert.Equal("block", await page.EvaluateAsync<string>(Scripts.DisplayOf, "blazor-error-ui"));
    }

    /// <summary>
    /// The boot-failure shell, scanned. The error bar is only ever visible on this path and on a runtime
    /// exception, so this and DomStateTests.TheRevealedErrorBarIsClean are the only two places axe sees the
    /// Dismiss button's name, the Reload control or the bar's own contrast at all.
    /// </summary>
    [BrowserFact]
    public async Task TheBootFailureShellIsClean()
    {
        await using var session = await FailedBootAsync();

        var scan = await AxeScan.OfAsync(session.Page, "boot failure, error bar visible", output);

        // NO EXEMPTION HERE, AND NO MECHANISM FOR ONE ANY MORE. This state used to violate three of the
        // eight best-practice rules this project promotes to hard failures - landmark-one-main,
        // page-has-heading-one and region - because Blazor never started, so MainLayout's <main>, its skip
        // link and the page's h1 did not exist and index.html's host document was three loose elements.
        // index.html now gives the shell a main landmark and an h1 of its own, inside #app so that Blazor's
        // render removes them again, and this state measures 0 violations and 0 incomplete against exactly
        // the rule set every other scan is held to. The named, self-expiring exemption that covered those
        // three expired on its own the day that markup landed, which is what it was written to do, and
        // AxeScan.MustBeCleanExcept went with it: a general-purpose exemption API, in a project whose brief
        // said to stop and ask rather than invent one, should not outlive the single case it was for.
        scan.MustBeClean();
    }

    /// <summary>
    /// The boot gate cannot be satisfied by the boot-failure shell. A LIVE GUARD, in the sense of rule T2,
    /// under the one change that would quietly turn every other scan in this project green.
    /// </summary>
    /// <remarks>
    /// <c>PageSession.BootedAsync</c> waits on <c>PageSession.BootedSelector</c> before anything is scanned.
    /// If the shell can ever match it, every <c>GotoAsync</c> returns on the parsed document, the settle
    /// waits find nothing to wait for, and axe scans an un-booted page - fifteen times, green, reporting on
    /// states it never reached. The gate was <c>#app h1</c>, which any shell carrying a heading of its own
    /// satisfies - and giving this shell a heading is a real improvement for a reader navigating a failed
    /// boot, so that was a gate resting on something the project had a reason to change. The selector and
    /// this guard are a pair and have to stay one.
    /// </remarks>
    [BrowserFact]
    public async Task TheBootGateSelectorCannotMatchTheBootFailureShell()
    {
        await using var session = await FailedBootAsync();

        output.WriteLine("boot gate selector: " + PageSession.BootedSelector);

        Assert.Equal(0, await session.Page.Locator(PageSession.BootedSelector).CountAsync());

        // Said again in halves, so a failure says which half moved: MainLayout's landmark is the part the
        // shell may never reproduce, whatever else it gains.
        Assert.Equal(0, await session.Page.Locator("#app main#main-content").CountAsync());

        // And the other half of the pair, which is what makes this guard self-expiring rather than
        // decorative: the shell really does carry a landmark and a heading of its own now, so the old
        // "#app h1" gate WOULD match it. If these two ever go back to zero the guard above has stopped
        // guarding anything and the selector can be simplified again - deliberately, not by accident.
        Assert.Equal(1, await session.Page.Locator("#app main").CountAsync());
        Assert.Equal(1, await session.Page.Locator("#app h1").CountAsync());
    }

    /// <summary>
    /// A page whose runtime script never arrived. The abort produces a resource error on the script element,
    /// which does not bubble and is one of the three failure shapes the watchdog listens for.
    /// </summary>
    private async Task<PageSession> FailedBootAsync()
    {
        var session = await gate.OpenAsync();

        await session.Page.Context.RouteAsync(RuntimeScript, async route => await route.AbortAsync("failed"));
        await session.Page.GotoAsync(session.UrlFor("/"));

        // The failure class is what the watchdog writes, so this waits for the state and not for a timer.
        await session.Page.Locator("#boot-status.boot-failure").WaitForAsync();
        return session;
    }
}

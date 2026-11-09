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

        // THE THREE EXEMPTIONS, AND WHY THIS STATE IS THE ONLY ONE THAT GETS ANY. Three of the eight
        // best-practice rules this project promotes to hard failures are about an APPLICATION's document
        // structure: one main landmark, one level-one heading, and every piece of content inside a landmark.
        // The boot-failure page is not the application. Blazor never started, so MainLayout's <main>, its
        // skip link and the page's h1 do not exist, and index.html's host document is three elements: the
        // explanation, the error bar, and the TMDB attribution footer. None of the three rules carries a
        // WCAG tag - MustBeCleanExcept refuses one that does - so nothing is being exempted from a
        // conformance claim, and every one of the nine booted states still asserts all eight.
        // Whether the boot shell SHOULD gain a <main> and an <h1> is a real question and a real
        // improvement for someone navigating a failed boot by landmark. It is a change to Task 2's host
        // page, not this task's, and the exemption below expires by itself the day it is made.
        scan.MustBeCleanExcept(
            "the boot shell is a host document that never became an application: no main landmark, no h1, "
            + "and the one sentence it has sits outside any landmark. All three are best-practice advice "
            + "about an application's structure and none carries a WCAG tag.",
            "landmark-one-main",
            "page-has-heading-one",
            "region");
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

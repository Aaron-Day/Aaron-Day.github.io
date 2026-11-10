using MovieLibrary.Web.A11y.Harness;
using Xunit.Abstractions;

namespace MovieLibrary.Web.A11y;

/// <summary>
/// How many live regions a booted page has, and that the assertive one delivers on the real clock.
/// </summary>
/// <remarks>
/// <para>
/// <b>House rule R2 - "no other live regions" - is the one this file mostly exists for, and it is a house
/// rule and not a criterion.</b> WCAG says nothing about how many live regions a page may have. The
/// project's rule does, because the cost of a spare one is paid by a screen reader user and by nobody
/// else: two regions holding the same sentence read it twice, and a region nobody writes to is invisible
/// until the day something does. <c>LiveRegionTests</c> checks the four regions' markup at the bUnit
/// level, one component at a time; the question "how many are in the DOM at once" only exists in an
/// assembled, booted page, which is here.
/// </para>
/// <para>
/// <b>SC 4.1.3 Status Messages, Level AA</b> is the last test, and what it adds is the real clock.
/// <c>AnnouncerTests</c> drives the settle delay with a <c>ManualTimeProvider</c> and
/// <c>DomStateTests</c> already exercises the real delay on <c>#route-announcer</c> - so what is NOT yet
/// covered anywhere is the <b>assertive</b> channel actually arriving: <c>#alert-region</c> only ever
/// delivers on the second consecutive failure, because on the first the focus move takes priority and
/// the region is deliberately left empty.
/// </para>
/// </remarks>
[Collection(BrowserCollection.Name)]
public class LiveRegionContractTests(BrowserGate gate, ITestOutputHelper output)
{
    /// <summary>The four regions this project has, and the only four it may have.</summary>
    private static readonly string[] Expected =
        ["route-announcer", "status-announcer", "alert-region", "blazor-error-ui"];

    /// <summary>
    /// HOUSE RULE R2: a booted page holds exactly the four live regions, and every one of them has an id.
    /// </summary>
    /// <remarks>
    /// Both halves matter. The count catches a fifth region being added; the id requirement catches the
    /// shape that is harder to see, a region with no id, which nothing in this project can address by
    /// name and which no later test can assert anything about. The scan finds regions by
    /// <c>aria-live</c> AND by the four roles that carry an implicit one, because three of this project's
    /// four regions declare only a role.
    /// </remarks>
    [BrowserTheory]
    [InlineData("/")]
    [InlineData(Site.UnmatchedUrl)]
    public async Task ABootedPageHoldsExactlyTheFourLiveRegions(string address)
    {
        await using var session = await gate.OpenAsync();
        await session.GotoAsync(address);
        await session.BootedAsync();

        var regions = await session.Page.EvaluateAsync<string[]>(Scripts.LiveRegions);

        output.WriteLine($"=== live regions on {address}: {regions.Length} ===");
        foreach (var region in regions)
            output.WriteLine("  " + region);

        foreach (var id in Expected)
            Assert.True(regions.Any(r => r.Contains("#" + id + " ", StringComparison.Ordinal)),
                $"#{id} is not a live region on {address}. HOUSE RULE R2 names four, and this is one of "
                + "them: either it has lost the role or aria-live that made it one, in which case nothing "
                + "it holds is ever spoken, or it is no longer in the DOM. Found: "
                + string.Join("; ", regions));

        Assert.True(regions.All(r => r.Contains('#', StringComparison.Ordinal)),
            "A live region on " + address + " has no id: " + string.Join("; ", regions)
            + ". HOUSE RULE: every live region in this project is addressed by id - that is how the "
            + "announcer writes to it and how every test in this suite names it - so one without an id "
            + "can be written to by nothing and asserted about by nothing, while still taking a turn in "
            + "whatever a screen reader decides to read.");

        Assert.True(regions.Length == Expected.Length,
            $"{address} holds {regions.Length} live regions, not {Expected.Length}. HOUSE RULE R2, \"no "
            + "other live regions\": the four are " + string.Join(", ", Expected) + ". A fifth is not a "
            + "WCAG failure in itself - this is the project's rule, not a criterion - but two regions "
            + "holding one sentence read it twice, and a region nothing writes to is invisible until the "
            + "day something does. Found: " + string.Join("; ", regions));
    }

    /// <summary>
    /// HOUSE RULE: #boot-status is gone once the app has booted, so it is never a fifth live region.
    /// </summary>
    /// <remarks>
    /// index.html's loading paragraph is <c>role="status"</c> while the payload downloads, and it sits
    /// inside <c>#app</c> - which Blazor empties when it renders. That is the whole mechanism, and it is
    /// implicit: nothing says "remove the boot status", it simply stops existing. The boot watchdog relies
    /// on the same fact in the other direction, using <c>#boot-status</c> still being in the DOM as its
    /// signal that Blazor never started, and <c>BootFailureTests</c> covers that path, where the role is
    /// stripped by hand so the failure sentence is not announced twice. Neither covers the ordinary case:
    /// the app boots, and the region has to have left.
    /// <para>
    /// This is why the test above can assert FOUR. If the loading paragraph survived a successful boot it
    /// would be a fifth region, holding "Loading library" for the rest of the session.
    /// </para>
    /// </remarks>
    [BrowserFact]
    public async Task TheBootStatusRegionIsGoneOnceTheAppHasBooted()
    {
        await using var session = await gate.OpenAsync();

        // THE SUBJECT, before its absence can mean anything: the paragraph has to be in the host page in
        // the first place. Asserted against the served document rather than remembered, because an
        // absence that was always an absence proves nothing about booting.
        var host = await session.Page.APIRequest.GetAsync(session.UrlFor("/"));

        Assert.Contains("id=\"boot-status\"", await host.TextAsync(), StringComparison.Ordinal);

        await session.GotoAsync("/");
        await session.BootedAsync();

        var left = await session.Page.Locator("#boot-status").CountAsync();

        Assert.True(left == 0,
            $"#boot-status is still in the DOM after a successful boot ({left} found). HOUSE RULE: it is "
            + "role=\"status\" holding \"Loading library\", so a copy that survives the boot is a fifth "
            + "live region for the rest of the session - and it would make the four-region count in this "
            + "file wrong rather than merely untidy. It leaves because it sits inside #app, which Blazor "
            + "empties when it renders; if it has been moved out of #app, it needs removing explicitly "
            + "instead, and the boot watchdog's test for it needs rethinking at the same time.");
    }

    /// <summary>
    /// SC 4.1.3 Status Messages, Level AA: the assertive region delivers, on the browser's own clock.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why the retry is needed to see this at all.</b> Movies.razor's Failed arm moves focus first and
    /// raises the alert only <c>if (!movedOnThisPass)</c>. On the first failure the focus move happens -
    /// #main-content is a FocusKeeper - so the error is delivered by the focus move and
    /// <c>#alert-region</c> is deliberately left EMPTY, which is what
    /// <c>DomStateTests</c> asserts. The Try again button is deliberately NOT a keeper, so on the second
    /// failure there is no move to make, and the assertive region is the only thing that will tell a
    /// screen reader user the retry failed. That second failure is the only path on which this channel
    /// ever speaks, and nothing in this repository had taken it.
    /// </para>
    /// <para>
    /// <b>What the real clock adds.</b> <c>AnnouncerTests</c> drives the settle delay with a
    /// <c>ManualTimeProvider</c>, which proves the arithmetic and not the arrival.
    /// <c>PageSession.AnnouncedAsync</c> polls for up to the boot timeout plus ten settle delays, so a
    /// delay that had been left un-awaited, or a <c>StateHasChanged</c> that never came, shows up here as
    /// a region that stays empty.
    /// </para>
    /// </remarks>
    [BrowserFact]
    public async Task TheAssertiveRegionDeliversTheSecondFailureOnTheRealClock()
    {
        await using var session = await gate.OpenAsync();
        await session.FailTheCatalogueAsync();
        await session.GotoAsync("/");

        await session.StateSentenceAsync(PageSession.CouldNotLoad);

        // The first failure is delivered by the focus move, so the region is empty - the premise of the
        // whole test, and the state DomStateTests pins. If this were ever not empty the retry below would
        // be asserting against a sentence that was already there.
        Assert.Equal(string.Empty, await session.Page.Locator("#alert-region").InnerTextAsync());

        var tryAgain = session.Page.Locator("button.try-again");

        await tryAgain.WaitForAsync();

        await tryAgain.ClickAsync();

        // THE SUBJECT. A second catalogue call has to have happened for there to be a second failure; a
        // click that did nothing would leave the region empty for an entirely different reason, and the
        // wait below would time out saying the announcer is broken.
        // The wait is wrapped because a live region that never fills otherwise reports itself as a bare
        // 61-second Playwright timeout, which says nothing about what went wrong. What the region holds
        // and how many requests were made is the whole diagnosis.
        try
        {
            await session.AnnouncedAsync("alert-region", PageSession.CouldNotLoad);
        }
        catch (Exception e) when (e is TimeoutException or Microsoft.Playwright.PlaywrightException)
        {
            Assert.Fail(
                "#alert-region never received the retry's failure sentence. It holds \""
                + await session.Page.Locator("#alert-region").InnerTextAsync()
                + $"\" and the catalogue was called {session.CatalogueCalls} time(s). SC 4.1.3 Status "
                + "Messages (Level AA): on the SECOND consecutive failure there is no focus move to make "
                + "- the Try again button is deliberately not a FocusKeeper - so this region is the only "
                + "thing that tells a screen reader user the retry failed. Movies.razor's Failed arm "
                + "raises it with `if (!movedOnThisPass) Announcer.Alert(CouldNotLoad)`; "
                + "Announcer.SettleDelay is awaited on the real clock here, so an un-awaited delay or a "
                + "missing StateHasChanged leaves the region empty exactly like this.");
        }

        Assert.True(session.CatalogueCalls >= 2,
            $"The catalogue was called {session.CatalogueCalls} time(s), so the sentence in #alert-region "
            + "is not the second failure's. Clicking Try again has to produce a second request for this "
            + "test to be about SC 4.1.3 Status Messages at all.");

        output.WriteLine($"=== #alert-region delivered after {session.CatalogueCalls} catalogue calls: "
                         + "\"" + await session.Page.Locator("#alert-region").InnerTextAsync() + "\" ===");
    }
}

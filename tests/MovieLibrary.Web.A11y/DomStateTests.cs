using System.Globalization;
using Microsoft.Playwright;
using MovieLibrary.Web.A11y.Harness;
using Xunit.Abstractions;

namespace MovieLibrary.Web.A11y;

/// <summary>
/// The DOM states a route scan never reaches, each scanned where it is the only place something can be seen.
/// </summary>
/// <remarks>
/// <para>
/// A plain load with the drawer closed, the live regions empty and the error bar display: none is ONE state.
/// Tasks 5 and 6 gave the films page three of its own and filled the third with sixty tiles and a pager, and the
/// film page brings three more - loading, failed, and an address no record answers - so there are now twelve
/// states worth scanning, and each entry below says what it is the only place to catch. A film that LOADED is
/// not among them: that one is an arrival state and RouteScanTests scans it at /movies/{key}.
/// </para>
/// <para>
/// <b>Every scan here runs with no poster path on any record.</b> See Catalogue for why that is a budget rule
/// and what it costs - briefly, the img branch of a tile is covered at the DOM level by
/// MovieLibrary.Web.Tests.MarkupContractTests and not by any scan here except
/// <see cref="OneRealPosterGivesAxeAnImgToLookAt"/>, whose single request is answered inside this process.
/// </para>
/// </remarks>
[Collection(BrowserCollection.Name)]
public class DomStateTests(BrowserGate gate, ITestOutputHelper output)
{
    /// <summary>
    /// The rules every state below has a subject for beyond AxeScan's own always-applicable set: the skip
    /// link, which only a booted page has, and the nav drawer's own li elements. Rule T1 from the
    /// applicability side - a rule that went inapplicable stops covering anything while the scan stays
    /// green. The boot-failure shell has neither and so names neither.
    /// </summary>
    private static readonly string[] Booted = ["skip-link", "listitem"];

    /// <summary>
    /// State 1: Loading. The only state with the loading sentence on screen and its spoken form in
    /// #status-announcer, and the only one where the state container holds a message with no control in it.
    /// </summary>
    [BrowserFact]
    public async Task LoadingIsClean()
    {
        await using var session = await gate.OpenAsync();
        await session.HangTheCatalogueAsync();

        await session.Page.GotoAsync(session.UrlFor("/"));
        await session.BootedAsync();
        await session.StateSentenceAsync(PageSession.LoadingVisible);
        await session.AnnouncedAsync("status-announcer", PageSession.LoadingSpoken);

        Assert.Equal(0, await session.Page.Locator("button.try-again").CountAsync());

        var scan = await AxeScan.OfAsync(session.Page, "loading", output);
        scan.MustBeClean(Booted);
    }

    /// <summary>
    /// State 2: Failed. The only state where the Try again button exists, so the only place axe can look at
    /// its name, its boundary and its contrast; and the only place the focus ring on a programmatically
    /// focused container can be seen at all.
    /// </summary>
    /// <remarks>
    /// The focus move and the alert are MUTUALLY EXCLUSIVE by Task 5's design, and this is where that gets
    /// checked in a browser. On a first load the page has just put focus on #app h1, which the focus helper
    /// treats as a keeper, so the move happens and #alert-region stays EMPTY. Asserting the region holds the
    /// sentence here would be asserting the defect.
    /// </remarks>
    [BrowserFact]
    public async Task FailedIsCleanAndHoldsFocusWithoutAlsoAlerting()
    {
        await using var session = await gate.OpenAsync();
        await session.FailTheCatalogueAsync();

        await session.Page.GotoAsync(session.UrlFor("/"));
        await session.BootedAsync();
        await session.StateSentenceAsync(PageSession.CouldNotLoad);
        await session.Page.Locator("button.try-again").WaitForAsync();

        var focused = await session.Page.EvaluateAsync<bool>(
            "() => document.activeElement === document.querySelector(\"div.catalog-state\")");

        Assert.True(focused,
            "The failure did not leave focus on div.catalog-state[tabindex=-1]. It holds " +
            await session.FocusedDescriptionAsync() + ". The focus move is the PRIMARY failure channel on "
            + "this page and the alert is only the fallback, so losing the move means the failure is reported "
            + "by the less reliable route.");

        Assert.Equal(string.Empty, await session.Page.Locator("#alert-region").InnerTextAsync());

        var scan = await AxeScan.OfAsync(session.Page, "failed, focus on the state container", output);
        scan.MustBeClean(Booted);
    }

    /// <summary>
    /// State 3: Failed, with a retry in flight. The button carries aria-disabled="true", keeps its boundary
    /// and keeps its place in the tab order, and the sentence has gone back to the loading copy.
    /// </summary>
    /// <remarks>
    /// axe does not object to aria-disabled on a button - it is the mechanism WAI-ARIA defines for
    /// "unavailable but still perceivable and focusable" - so a rule firing in this state is a real finding
    /// rather than a false positive. The one that would be meaningful is color-contrast: choosing
    /// aria-disabled over the disabled attribute forfeits SC 1.4.3's exemption for an inactive component,
    /// because the control is still focusable and still receives events.
    /// </remarks>
    [BrowserFact]
    public async Task FailedWithARetryInFlightIsClean()
    {
        await using var session = await gate.OpenAsync();
        await session.FailThenHangTheCatalogueAsync();

        await session.Page.GotoAsync(session.UrlFor("/"));
        await session.BootedAsync();
        await session.StateSentenceAsync(PageSession.CouldNotLoad);

        var button = session.Page.Locator("button.try-again");
        await button.ClickAsync();

        await session.Page.WaitForFunctionAsync(
            "() => document.querySelector(\"button.try-again\")?.getAttribute(\"aria-disabled\") === \"true\"");
        await session.StateSentenceAsync(PageSession.LoadingVisible);

        Assert.True(session.CatalogueCalls >= 2,
            "The retry never reached the catalogue, so this is still the Failed state and not the "
            + "retry-in-flight one. The scan would have been of the wrong DOM.");

        // The boundary, which is what a sighted user has instead of aria-disabled. Not a size assertion.
        var border = await button.EvaluateAsync<string>("el => getComputedStyle(el).borderTopWidth");
        output.WriteLine("try-again border while retrying: " + border);
        Assert.NotEqual("0px", border);

        var scan = await AxeScan.OfAsync(session.Page, "failed, retry in flight", output);
        scan.MustBeClean(Booted);
    }

    /// <summary>
    /// State 4: Ready, page 1. The grid - ul.grid[role=list] with sixty li - the count sentence, and the
    /// pager with Previous at a bound and Next live. The first state in this project's history with sixty of
    /// anything, and the first with target-size subjects that are not the nav toggle.
    /// </summary>
    [BrowserFact]
    public async Task ReadyPageOneIsClean()
    {
        await using var session = await gate.OpenAsync();
        await session.GotoAsync("/");
        await session.GridAsync();

        await ExpectPagerAsync(session, previousAtBound: true, nextAtBound: false);
        Assert.Equal(PageLabel(1, Catalogue.Pages), await PageLabelAsync(session));

        var scan = await AxeScan.OfAsync(session.Page, "ready, page 1, 60 tiles", output);
        scan.MustBeClean(Booted);
    }

    /// <summary>
    /// State 5: Ready, a middle page. Both pager buttons live, and the only way in is to press Next - which
    /// also exercises the polite announcement the grid raises for a page change.
    /// </summary>
    [BrowserFact]
    public async Task ReadyOnAMiddlePageIsClean()
    {
        await using var session = await gate.OpenAsync();
        await session.GotoAsync("/");
        await session.GridAsync();

        await session.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Next" }).ClickAsync();
        await session.Page.WaitForFunctionAsync(
            "() => document.querySelector(\".pager span\")?.textContent?.startsWith(\"Page 2 \") === true");
        // Built from the fixture's own numbers, and formatted in en-US ON BOTH SIDES. BrowserGate pins the
        // context's locale to en-US, so the app groups 1,456 that way whatever machine this runs on - but
        // an interpolated {Catalogue.Records:N0} formats in the RUNNER's culture, which on ubuntu-latest
        // happens to agree and on a de-DE machine would expect "1.456" and fail a correct app. The app
        // taking its culture from the browser is deliberate behaviour that MoviesGridTests covers on its
        // own; the gate pins the browser and must then speak the same language back.
        //
        // EVERY placeholder carries :N0, because PosterGrid formats every number it speaks through
        // Number(v) => v.ToString("N0"). With 1,456 records none of the other three has a thousands separator,
        // so a bare {0} agrees today - and at 60,000 records the app would say "Page 2 of 1,000" while a bare
        // {0} expected "Page 2 of 1000". The comparison is a === inside WaitForFunctionAsync, so that would
        // surface as a 61 second timeout (AnnouncedAsync allows the boot timeout plus ten settle delays) that
        // says nothing about the text, against an app that was correct.
        var spoken = CultureInfo.GetCultureInfo("en-US");

        await session.AnnouncedAsync("status-announcer", string.Format(
            spoken,
            "Page 2 of {0:N0}. Showing {1:N0} to {2:N0} of {3:N0} titles.",
            Catalogue.Pages, Catalogue.PageSize + 1, Catalogue.PageSize * 2, Catalogue.Records));

        await ExpectPagerAsync(session, previousAtBound: false, nextAtBound: false);
        await session.GridAsync();

        var scan = await AxeScan.OfAsync(session.Page, "ready, a middle page, both pager buttons live", output);
        scan.MustBeClean(Booted);
    }

    /// <summary>
    /// State 6: Ready, the last page. Next at a bound, and a SHORT page - sixteen tiles of 1,456 at a page
    /// size of sixty - which is the only state where the grid is not full.
    /// </summary>
    /// <remarks>
    /// Reached by pressing Next until it stops, because that is the only way in: the page number lives in a
    /// field on Movies.razor and Task 8 is what lifts it into the query string. Twenty-four presses is the
    /// honest cost of testing the path a user takes.
    /// </remarks>
    [BrowserFact]
    public async Task ReadyOnTheLastPageIsClean()
    {
        await using var session = await gate.OpenAsync();
        await session.GotoAsync("/");
        await session.GridAsync();

        var next = session.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Next" });

        for (var page = 2; page <= Catalogue.Pages; page++)
        {
            await next.ClickAsync();
            await session.Page.WaitForFunctionAsync(
                "label => document.querySelector(\".pager span\")?.textContent === label",
                PageLabel(page, Catalogue.Pages));
        }

        await session.GridAsync(Catalogue.OnTheLastPage);
        await ExpectPagerAsync(session, previousAtBound: false, nextAtBound: true);

        var scan = await AxeScan.OfAsync(
            session.Page, $"ready, the last page, {Catalogue.OnTheLastPage} tiles", output);
        scan.MustBeClean(Booted);
    }

    /// <summary>
    /// State 7: the drawer open at 320px. aria-expanded="true" and a visible list of links: genuinely
    /// different markup, and the only state where the drawer's own links are in the layout at all.
    /// </summary>
    [BrowserFact]
    public async Task TheDrawerOpenAt320IsClean()
    {
        await using var session = await gate.OpenAsync(320, 812);
        await session.GotoAsync("/");
        await session.GridAsync();

        var toggle = session.Page.Locator("button.nav-toggle");
        Assert.Equal("false", await toggle.GetAttributeAsync("aria-expanded"));

        await toggle.ClickAsync();
        Assert.Equal("true", await toggle.GetAttributeAsync("aria-expanded"));
        await session.Page.Locator("ul#nav-links a").First.WaitForAsync();

        var scan = await AxeScan.OfAsync(session.Page, "drawer open at 320x812", output);
        scan.MustBeClean(Booted);
    }

    /// <summary>
    /// State 8: after a client-side navigation, with #route-announcer holding the new page's name.
    /// </summary>
    /// <remarks>
    /// What this state has that a cold load does not is text in #route-announcer when axe runs: the test
    /// asserts the announcer is empty before it navigates. That text is NOT outside every landmark, which
    /// this remark used to say. The div is a child of .page and not of the main, but it carries
    /// aria-live="polite", and axe's isRegion helper counts a polite or assertive live region (as well as
    /// role alert, log or status, and the landmarks) as a region - so the region rule finds the text already
    /// inside one. Nor is this a state where the rule has a subject and the others do not: region is
    /// <c>{selector: "body *"}</c> with no matcher, so it has subjects in EVERY state, which is why it is in
    /// AxeScan.AlwaysApplicable. Both facts are read from the axe-core 4.10.2 bundle.
    /// <para>
    /// <b>This state got harder after Task 5.</b> Movies.razor answers both "/" and "/movies", so navigating
    /// between them changes no page type and the route announcer stays empty. The way in is a page-type
    /// change, and the only real link that makes one is the in-prose "Back to the library" on the not-found
    /// page - so the navigation runs in that direction. Deep-linking to /not-found first is itself a path a
    /// user takes.
    /// </para>
    /// </remarks>
    [BrowserFact]
    public async Task TheRouteAnnouncementStateIsClean()
    {
        await using var session = await gate.OpenAsync();
        await session.GotoAsync("/not-found");

        Assert.Equal(string.Empty, await session.Page.Locator("#route-announcer").InnerTextAsync());

        await session.Page.Locator("main a[href=\"movies\"]").ClickAsync();
        await session.GridAsync();
        await session.AnnouncedAsync("route-announcer", "Films");

        var scan = await AxeScan.OfAsync(session.Page, "after a client-side navigation", output);
        scan.MustBeClean(Booted);
    }

    /// <summary>
    /// State 9: the error bar revealed. Because #blazor-error-ui is display: none, axe skips it on every
    /// other scan, so the Dismiss button's aria-label, the Reload control and the bar's own contrast are
    /// otherwise never checked at all.
    /// </summary>
    /// <remarks>
    /// Revealed with an inline display: block, which is exactly how the Blazor runtime reveals it - and the
    /// reason app.css puts the bar's layout rules on .error-ui-inner, since a stylesheet cannot override an
    /// inline style. The bar is also scanned on the boot-failure path in BootFailureTests, where it is
    /// revealed by a real failure; this state is the booted page with the bar up, which is what a runtime
    /// exception produces.
    /// </remarks>
    [BrowserFact]
    public async Task TheRevealedErrorBarIsClean()
    {
        await using var session = await gate.OpenAsync();
        await session.GotoAsync("/");
        await session.GridAsync();

        Assert.Equal("none",
            await session.Page.EvaluateAsync<string>(Scripts.DisplayOf, "blazor-error-ui"));

        Assert.True(await session.Page.EvaluateAsync<bool>(Scripts.RevealErrorBar));
        await session.Page.Locator("#blazor-error-ui .dismiss").WaitForAsync();

        Assert.Equal("block",
            await session.Page.EvaluateAsync<string>(Scripts.DisplayOf, "blazor-error-ui"));

        var scan = await AxeScan.OfAsync(session.Page, "error bar revealed on a booted page", output);
        scan.MustBeClean(Booted);
    }

    /// <summary>
    /// The one scan with a real img element in it, so that the tile's poster branch is not entirely
    /// unscanned in a browser.
    /// </summary>
    /// <remarks>
    /// ONE record carries a poster path, not sixty, and the request it makes is answered from inside this
    /// process with a 1x1 PNG by the route BrowserGate registers on every context. Dozens of live requests
    /// to image.tmdb.org per scanned state, from CI on a public repository, would make this gate red for
    /// CDN reasons rather than accessibility ones - and answering them locally means this project makes no
    /// external request of any kind, on any path, including the A11Y_BASE_URL path where the catalogue
    /// under test is not one this harness wrote. That route is NOT this test's to arrange any more: it used
    /// to be a per-test call made only from here, which is exactly how the hole stayed open.
    /// </remarks>
    [BrowserFact]
    public async Task OneRealPosterGivesAxeAnImgToLookAt()
    {
        await using var session = await gate.OpenAsync();
        await session.ServeCatalogueAsync(Catalogue.OnePosterAmong(Catalogue.PageSize));

        await session.GotoAsync("/");
        await session.GridAsync();

        var images = session.Page.Locator("ul.grid img");
        Assert.Equal(1, await images.CountAsync());
        Assert.Equal(string.Empty, await images.First.GetAttributeAsync("alt"));

        var scan = await AxeScan.OfAsync(session.Page, "ready, one real poster among sixty tiles", output);
        scan.MustBeClean(Booted);
    }

    // ======================================================== the film page's own states

    /// <summary>
    /// State 10: a film still loading. The only film-page state with the loading sentence on screen, and the
    /// only place a browser can check that the page says NOTHING while it loads.
    /// </summary>
    /// <remarks>
    /// The silence is the contract, not an omission. FocusOnNavigate has just put focus on the h1, which in
    /// this state reads "Loading film…", so a reader has already heard those words; publishing them to
    /// #status-announcer as well would be the same sentence twice for one event. The films page announces its
    /// own loading state because its heading says "Films" and not "Loading films", which is why these two
    /// pages differ here and why both are worth a scan.
    /// </remarks>
    [BrowserFact]
    public async Task AFilmStillLoadingIsCleanAndSaysNothing()
    {
        await using var session = await gate.OpenAsync();
        await session.HangTheCatalogueAsync();

        await session.Page.GotoAsync(session.UrlFor(FilmAddress));
        await session.BootedAsync();
        await session.FilmSentenceAsync(PageSession.FilmLoadingVisible);

        Assert.Equal(0, await session.Page.Locator("button.try-again").CountAsync());
        Assert.Equal(string.Empty, await session.Page.Locator("#status-announcer").InnerTextAsync());

        var scan = await AxeScan.OfAsync(session.Page, "a film loading", output);
        scan.MustBeClean(Booted);
    }

    /// <summary>
    /// State 11: a film that could not be loaded. The film page's Try again button, and its failure focus
    /// move onto its own state container - a different element with a different class from the films page's,
    /// so neither scan covers the other.
    /// </summary>
    [BrowserFact]
    public async Task AFilmThatFailedIsCleanAndHoldsFocusWithoutAlsoAlerting()
    {
        await using var session = await gate.OpenAsync();
        await session.FailTheCatalogueAsync();

        await session.Page.GotoAsync(session.UrlFor(FilmAddress));
        await session.BootedAsync();
        await session.FilmSentenceAsync(PageSession.FilmCouldNotLoad);
        await session.Page.Locator("button.try-again").WaitForAsync();

        var focused = await session.Page.EvaluateAsync<bool>(
            "() => document.activeElement === document.querySelector(\"div.film-state\")");

        Assert.True(focused,
            "The failure did not leave focus on div.film-state[tabindex=-1]. It holds " +
            await session.FocusedDescriptionAsync() + ". The focus move is the PRIMARY failure channel and the "
            + "alert is only the fallback, so losing the move means the failure is reported by the less "
            + "reliable route.");

        // Mutually exclusive, exactly as on the films page: the move happened, so nothing is alerted.
        Assert.Equal(string.Empty, await session.Page.Locator("#alert-region").InnerTextAsync());

        var scan = await AxeScan.OfAsync(session.Page, "a film that failed, focus on the state container", output);
        scan.MustBeClean(Booted);
    }

    /// <summary>
    /// State 12: an address with a key no record carries. The catalogue loaded and the page is not broken -
    /// there is simply no such film - so there is no Try again button and no failure: a different state from
    /// State 11 and the one a stale bookmark or a hand-typed URL reaches.
    /// </summary>
    [BrowserFact]
    public async Task AnAddressNoFilmAnswersIsClean()
    {
        await using var session = await gate.OpenAsync();

        await session.GotoAsync("/movies/no-such-key");
        await session.FilmSentenceAsync(PageSession.NoSuchFilm);

        Assert.Equal(0, await session.Page.Locator("button.try-again").CountAsync());
        Assert.Equal(0, await session.Page.Locator("dl.facts").CountAsync());

        var scan = await AxeScan.OfAsync(session.Page, "an address no film answers", output);
        scan.MustBeClean(Booted);
    }

    /// <summary>The film page address these states use: the one record Catalogue furnishes for it.</summary>
    private static string FilmAddress => "/movies/" + Catalogue.FirstKey;

    /// <summary>
    /// The pager label exactly as PosterGrid speaks it, and the reason this is a helper rather than a
    /// concatenation at each call site. PosterGrid.PageLabelFor is
    /// <c>$"Page {Number(page)} of {Number(PageCount)}"</c> and Number is <c>ToString("N0")</c>, so BOTH
    /// numbers carry group separators. A bare <c>"Page 1 of " + Catalogue.Pages</c> agrees only while the
    /// page count stays under a thousand: raise Catalogue.Records to 60,000 and the app says
    /// "Page 1 of 1,000" while the concatenation expects "Page 1 of 1000", so the test fails an app that is
    /// correct. en-US because the browser context is pinned to it, as the announcement expectation above is.
    /// </summary>
    private static string PageLabel(int page, int of) =>
        string.Format(CultureInfo.GetCultureInfo("en-US"), "Page {0:N0} of {1:N0}", page, of);

    private static Task<string> PageLabelAsync(PageSession session) =>
        session.Page.Locator(".pager span").InnerTextAsync();

    /// <summary>
    /// Both pager buttons, their aria-disabled values, and a floor under the whole thing: the pager has to
    /// have TWO buttons for any of this to mean anything.
    /// </summary>
    private static async Task ExpectPagerAsync(PageSession session, bool previousAtBound, bool nextAtBound)
    {
        var buttons = session.Page.Locator("nav.pager button");
        Assert.Equal(2, await buttons.CountAsync());

        var previous = session.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Previous" });
        var next = session.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Next" });

        // Rendered from a string and never from a bool: a bool attribute is present-and-empty when true and
        // absent when false, and ARIA reads BOTH of those as undefined. So the absent case is asserted as
        // null rather than as "false", which is what the markup actually does.
        Assert.Equal(previousAtBound ? "true" : null, await previous.GetAttributeAsync("aria-disabled"));
        Assert.Equal(nextAtBound ? "true" : null, await next.GetAttributeAsync("aria-disabled"));

        // NEITHER BUTTON IS EVER REALLY DISABLED, whatever bound it is at, and it is still focusable. That
        // is the whole of PosterGrid's house rule: the disabled ATTRIBUTE on the focused element takes it out
        // of the tab order, focus falls back to the document body, and the user's next Tab restarts from
        // above the skip link - twice a visit for someone paging through 25 pages by keyboard.
        // Asserted against the DOM and NOT with Playwright's Locator.IsDisabledAsync, which reports an
        // aria-disabled button as disabled. That is Playwright's actionability heuristic rather than the
        // DOM, so the obvious assertion was testing the test framework; it is also why nothing in this
        // project may call ClickAsync on a button sitting at its bound - the call would wait for the button
        // to become "enabled" and time out on markup that is correct.
        Assert.True(await StaysFocusableAsync(session, "Previous"));
        Assert.True(await StaysFocusableAsync(session, "Next"));
    }

    /// <summary>
    /// Whether a pager button carries no real disabled state and can still take focus.
    /// </summary>
    private static Task<bool> StaysFocusableAsync(PageSession session, string label) =>
        session.Page.EvaluateAsync<bool>(Scripts.TakesFocusAndIsNotDisabled, label);
}

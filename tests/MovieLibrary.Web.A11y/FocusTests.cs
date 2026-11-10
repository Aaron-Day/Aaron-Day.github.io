using MovieLibrary.Web.A11y.Harness;
using Xunit.Abstractions;

namespace MovieLibrary.Web.A11y;

/// <summary>
/// Where focus goes when the user moves between pages, and the three outcomes that are deliberate.
/// </summary>
/// <remarks>
/// <para>
/// <b>No AA criterion requires moving focus to the h1 on a single-page navigation.</b> It is a house rule.
/// SC 2.4.3 Focus Order is about a sequence being meaningful, not about where a route change leaves you, and
/// citing it for the h1 jump would be a miscitation. What the tests below pin is that all three outcomes are
/// CHOSEN, so that nobody later "fixes" either same-type case into a focus jump it should not make.
/// </para>
/// <para>
/// <b>Why one test became three.</b> FocusOnNavigate compares RouteData.PageType and fires only when the page
/// type changes, and Movies.razor answers both "/" and "/movies" - so navigating between those two moves no
/// focus, correctly. But NavMenu DOES move focus in exactly that case: same page, drawer open, link activated,
/// focus back to the Menu button. A single test asserting "same-type navigation does not move focus"
/// therefore passes at 1280px and fails at 320px - the same test, opposite results by viewport. It only
/// passes unconditionally if it navigates by pushState, which is to say by never testing the path a user
/// takes.
/// </para>
/// </remarks>
[Collection(BrowserCollection.Name)]
public class FocusTests(BrowserGate gate, ITestOutputHelper output)
{
    /// <summary>
    /// A page-type change moves focus to the new page's h1.
    /// </summary>
    /// <remarks>
    /// Driven from the not-found page's own in-prose link, which is the only real link in Part A that crosses
    /// a page type: there is no link anywhere to an unmatched address, so the brief's "/movies to /zzz and
    /// back" cannot be walked in that direction without inventing a control the app does not have. Arriving
    /// at an unmatched address by deep link and then following the link back is a path users genuinely take,
    /// and it exercises the same FocusOnNavigate branch.
    /// </remarks>
    [BrowserFact]
    public async Task CrossTypeNavigationMovesFocusToTheNewPagesH1()
    {
        await using var session = await gate.OpenAsync();
        await session.GotoAsync(Site.UnmatchedUrl);

        Assert.Equal("Page not found", await session.Page.Locator("#app h1").InnerTextAsync());

        await session.Page.Locator("main a[href=\"movies\"]").ClickAsync();
        await session.GridAsync();

        var onTheHeading = await session.Page.EvaluateAsync<bool>(
            "() => document.activeElement === document.querySelector(\"#app h1\")");

        Assert.True(onTheHeading,
            "A page-type change left focus at " + await session.FocusedDescriptionAsync()
            + " rather than on the new page's h1. FocusOnNavigate's selector is \"#app h1\"; both pages "
            + "declare tabindex=\"-1\" on their heading so that the move can land.");

        Assert.Equal("Films", await session.Page.Locator("#app h1").InnerTextAsync());
    }

    /// <summary>
    /// Same page type, drawer open: closing the drawer takes the activated link out of the tab order, so
    /// NavMenu puts focus back on the Menu button rather than letting it fall to the document body.
    /// </summary>
    /// <remarks>
    /// This is the case that makes a single "same-type navigation moves nothing" test impossible. The drawer
    /// only exists below 48rem, which is why the viewport is 320px and not a parameter.
    /// </remarks>
    [BrowserFact]
    public async Task SameTypeNavigationFromTheDrawerReturnsFocusToTheMenuButton()
    {
        await using var session = await gate.OpenAsync(320, 812);
        await session.GotoAsync("/");
        await session.GridAsync();

        var toggle = session.Page.Locator("button.nav-toggle");
        await toggle.ClickAsync();
        Assert.Equal("true", await toggle.GetAttributeAsync("aria-expanded"));

        await session.Page.Locator("ul#nav-links a[href=\"movies\"]").ClickAsync();
        await session.Page.WaitForURLAsync("**/movies");

        // The drawer closes on navigation, which is display: none on the list the activated link lives in.
        // That is the removal from the tab order NavMenu's focus move exists to repair, so waiting for it is
        // waiting for the state under test rather than for the focus call.
        await session.Page.WaitForFunctionAsync(
            "() => document.querySelector(\"button.nav-toggle\")?.getAttribute(\"aria-expanded\") === \"false\"");

        var onTheToggle = await session.Page.EvaluateAsync<bool>(
            "() => document.activeElement === document.querySelector(\"button.nav-toggle\")");

        Assert.True(onTheToggle,
            "Following a drawer link to the page already showing left focus at "
            + await session.FocusedDescriptionAsync() + ". FocusOnNavigate does not fire - the page type did "
            + "not change - and closing the drawer removes the activated link from the tab order, so without "
            + "NavMenu's own move focus drops to the document body and the next Tab restarts above the skip "
            + "link.");
    }

    /// <summary>
    /// Same page type, no drawer: focus stays exactly where the user put it, on the link they activated.
    /// </summary>
    /// <remarks>
    /// At 1280px the Menu button is display: none and the link list is always laid out, so NavMenu's guard -
    /// which needs the drawer to have been OPEN - does not fire, and neither does FocusOnNavigate. Nothing
    /// moves, and that is correct: a navigation that changes no page has no new place to send a reader.
    /// </remarks>
    [BrowserFact]
    public async Task SameTypeNavigationWithTheDrawerClosedLeavesFocusWhereItWas()
    {
        await using var session = await gate.OpenAsync(1280, 1024);
        await session.GotoAsync("/");
        await session.GridAsync();

        Assert.Equal("none",
            await session.Page.EvaluateAsync<string>(Scripts.DisplayOfSelector, "button.nav-toggle"));

        var link = session.Page.Locator("ul#nav-links a[href=\"movies\"]");
        await link.ClickAsync();
        await session.Page.WaitForURLAsync("**/movies");
        await session.GridAsync();

        var onTheLink = await session.Page.EvaluateAsync<bool>(
            "() => document.activeElement === document.querySelector(\"ul#nav-links a[href='movies']\")");

        Assert.True(onTheLink,
            "A same-type navigation with no drawer moved focus to " + await session.FocusedDescriptionAsync()
            + ". It must move nothing: the page did not change, so there is nowhere new to send a reader, and "
            + "a jump to the h1 here would be a focus move the user did not ask for. Pinned deliberately.");
    }

    /// <summary>
    /// The skip link is the first tab stop in the document, it moves focus into main, and it keeps the
    /// query string.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The query string is the whole reason the link has an onclick that prevents the default:
    /// href="#main-content" resolves against &lt;base href="/"&gt;, so Blazor turns the activation into an
    /// in-app navigation to "/" and the query is lost. Task 8 puts the filter state in that query, so losing
    /// it would discard a reader's filters every time they used the skip link.
    /// </para>
    /// <para>
    /// <b>MEASURED, and worth knowing: on arrival the skip link is not reachable by a forward Tab at all.</b>
    /// FocusOnNavigate has already moved focus to the h1, which is INSIDE main, and blurring it does not move
    /// the sequential focus navigation starting point - so the next Tab goes to the first control after the
    /// heading, which on the films page is the pager's Previous button. That is not a defect: a reader who
    /// lands on the h1 is already past the blocks the link exists to skip. But it does mean the only honest
    /// way to test the link is to walk focus back out of the document first, which is the state a reader is
    /// in after tabbing from the browser's own UI into the page - the case the link is really for.
    /// </para>
    /// </remarks>
    [BrowserFact]
    public async Task TheSkipLinkMovesFocusIntoMainAndKeepsTheQueryString()
    {
        await using var session = await gate.OpenAsync();
        // A query the gate catalogue MATCHES: every title it generates contains "Fixture" (see Harness/Catalogue.cs),
        // so the grid still holds its sixty tiles and this stays a test of the skip link. It used to be "test",
        // which was a stand-in while nothing read the query; now that the page filters on it, a query matching
        // nothing would render the no-match state and GridAsync would wait for tiles that never come.
        await session.GotoAsync("/movies?q=fixture");
        await session.GridAsync();

        await WalkBackToTheTopAsync(session);
        await session.Page.Keyboard.PressAsync("Tab");

        var first = await session.FocusedDescriptionAsync();
        output.WriteLine("first tab stop from the top of the document: " + first);
        Assert.Contains("skip-link", first);

        await session.Page.Keyboard.PressAsync("Enter");

        Assert.Equal("main-content", await session.FocusedIdAsync());
        Assert.Equal("?q=fixture", await session.Page.EvaluateAsync<string>("() => location.search"));
    }

    /// <summary>
    /// Shift-Tabs until focus has left the page's tab order, so that the next Tab enters the document from
    /// the top. A count rather than a fixed number of presses, because the number of controls differs by
    /// viewport and by page and a hard-coded count would be overfitted to today's markup.
    /// </summary>
    private static async Task WalkBackToTheTopAsync(PageSession session)
    {
        for (var press = 0; press < 40; press++)
        {
            if (await session.Page.EvaluateAsync<bool>(
                    "() => !document.activeElement || document.activeElement === document.body "
                    + "|| document.activeElement === document.documentElement"))
                return;

            await session.Page.Keyboard.PressAsync("Shift+Tab");
        }

        Assert.Fail(
            "Forty Shift+Tab presses did not walk focus out of the document, so the next Tab would not have "
            + "entered it from the top and the skip link would not have been the stop under test. Focus is at "
            + await session.FocusedDescriptionAsync() + ", which suggests a focus trap.");
    }
}

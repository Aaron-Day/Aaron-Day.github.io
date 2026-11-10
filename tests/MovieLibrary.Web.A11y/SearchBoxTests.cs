using Microsoft.Playwright;
using MovieLibrary.Web.A11y.Harness;
using MovieLibrary.Web.Pages;
using Xunit.Abstractions;

namespace MovieLibrary.Web.A11y;

/// <summary>
/// The search box as a person uses it: real key presses into the real control, with the address changing under
/// them. This is the acceptance criterion the filter work could not settle with a DOM-level test, because the
/// thing in question is what a browser does to focus and the caret while the app navigates.
/// </summary>
/// <remarks>
/// <para>
/// <b>What was already known, and what these add.</b> A throwaway probe page in a real Chromium showed that
/// <c>NavigateTo(..., replace: true)</c> does not re-trigger <c>FocusOnNavigate</c>: five key presses 8 to 60 ms
/// apart, then five more, left focus in the input, advanced the caret 1 to 5 and then to 10 with no reset, left
/// <c>history.length</c> alone and left <c>#route-announcer</c> empty. That was a probe. These are the same
/// presses on the real search box, through the real debounce, the real <c>[SupplyParameterFromQuery]</c> round
/// trip and the real re-render, and they add the two cases a probe could not have: the caret in the MIDDLE of
/// the text when the address comes back, and an empty result while the visitor is still typing.
/// </para>
/// <para>
/// <b>Only Chromium is driven.</b> Whether Safari or Firefox move focus on a replace navigation is not
/// established by anything here, and is on the manual list.
/// </para>
/// </remarks>
[Collection(BrowserCollection.Name)]
public class SearchBoxTests(BrowserGate gate, ITestOutputHelper output)
{
    /// <summary>Whether the address's q parameter is exactly this text.</summary>
    private const string AddressHolds =
        "q => new URLSearchParams(location.search).get(\"q\") === q";

    /// <summary>Whether focus is on the paragraph the no-match state puts it on.</summary>
    private const string FocusIsOnTheNoMatchParagraph =
        "() => !!document.activeElement && document.activeElement.matches(\".no-match p\")";

    private static readonly KeyboardTypeOptions RealTyping = new() { Delay = 30 };

    private async Task ExpectStillTypingAsync(PageSession session, string text, int historyBefore)
    {
        var box = session.Page.Locator("#q");

        var state = await box.EvaluateAsync<int[]>("el => [el.selectionStart, el.selectionEnd, el.value.length]");
        var value = await box.InputValueAsync();
        var history = await session.Page.EvaluateAsync<int>("() => history.length");
        var route = await session.Page.Locator("#route-announcer").TextContentAsync();

        output.WriteLine($"  value \"{value}\", caret {state[0]}-{state[1]} of {state[2]}, history {history}, "
                         + $"focus {await session.FocusedDescriptionAsync()}");

        Assert.Equal("q", await session.FocusedIdAsync());
        Assert.Equal(text, value);
        Assert.Equal(new[] { text.Length, text.Length, text.Length }, state);
        Assert.Equal(historyBefore, history);
        Assert.Equal(string.Empty, route);
    }

    [BrowserFact]
    public async Task FiveKeysThenFiveMoreKeepFocusInTheBoxAndNeverResetTheCaret()
    {
        await using var session = await gate.OpenAsync();
        await session.GotoAsync("/movies");
        await session.GridAsync();

        await session.Page.Locator("#q").ClickAsync();
        var history = await session.Page.EvaluateAsync<int>("() => history.length");

        await session.Page.Keyboard.TypeAsync("fixtu", RealTyping);
        await ExpectStillTypingAsync(session, "fixtu", history);

        // Past the filter debounce and the router's report of the address: the moment a re-render could have
        // moved the caret or taken focus, and the one the probe exercised.
        await session.Page.WaitForFunctionAsync(AddressHolds, "fixtu");
        await ExpectStillTypingAsync(session, "fixtu", history);

        await session.Page.Keyboard.TypeAsync("re 1", RealTyping);
        await ExpectStillTypingAsync(session, "fixture 1", history);

        await session.Page.WaitForFunctionAsync(AddressHolds, "fixture 1");
        await ExpectStillTypingAsync(session, "fixture 1", history);
    }

    /// <remarks>
    /// The caret in the middle. A box whose value is rewritten when the address comes back would put the caret
    /// at the end, and the visitor typing a correction into the first word would find the second letter landing
    /// in the wrong place.
    /// </remarks>
    [BrowserFact]
    public async Task ACorrectionTypedInTheMiddleKeepsTheCaretWhereItIsAfterTheAddressComesBack()
    {
        await using var session = await gate.OpenAsync();
        await session.GotoAsync("/movies");
        await session.GridAsync();

        await session.Page.Locator("#q").ClickAsync();
        await session.Page.Keyboard.TypeAsync("fixtu", RealTyping);
        await session.Page.WaitForFunctionAsync(AddressHolds, "fixtu");

        await session.Page.Keyboard.PressAsync("Home");
        await session.Page.Keyboard.TypeAsync("A", RealTyping);
        await session.Page.WaitForFunctionAsync(AddressHolds, "Afixtu");

        var caret = await session.Page.Locator("#q").EvaluateAsync<int>("el => el.selectionStart");

        Assert.Equal("q", await session.FocusedIdAsync());
        Assert.Equal("Afixtu", await session.Page.Locator("#q").InputValueAsync());
        Assert.Equal(1, caret);
    }

    /// <remarks>
    /// Stealing focus from a field mid-word is worse than the thing it would fix. The no-match state moves focus
    /// only when nobody has it, so a visitor who types their way to nothing stays in the box, and hears the
    /// sentence instead.
    /// </remarks>
    [BrowserFact]
    public async Task TypingYourWayToNothingLeavesFocusInTheBoxAndSpeaksTheSentence()
    {
        await using var session = await gate.OpenAsync();
        await session.GotoAsync("/movies");
        await session.GridAsync();

        await session.Page.Locator("#q").ClickAsync();
        await session.Page.Keyboard.TypeAsync("zzzz", RealTyping);

        await session.Page.Locator(".no-match p").WaitForAsync();
        await session.AnnouncedAsync("status-announcer", Movies.NoMatch);

        Assert.Equal("q", await session.FocusedIdAsync());
        Assert.Equal("zzzz", await session.Page.Locator("#q").InputValueAsync());
    }

    /// <remarks>
    /// The other side, in a real browser: focus on a pager button, then the filter leaves nothing. The pager goes
    /// with the grid and focus falls to the body, which the helper in index.html reads as "nobody has moved it".
    /// It lands on the paragraph instead of leaving the next Tab to restart above the skip link.
    /// </remarks>
    [BrowserFact]
    public async Task FocusOnTheRemovedPagerLandsOnTheNoMatchParagraph()
    {
        await using var session = await gate.OpenAsync();
        await session.GotoAsync("/movies");
        await session.GridAsync();

        await session.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Next" }).FocusAsync();
        Assert.Contains("Next", await session.FocusedDescriptionAsync());

        await session.Page.EvaluateAsync("() => Blazor.navigateTo(\"/movies?q=zzzz\")");

        await session.Page.WaitForFunctionAsync(FocusIsOnTheNoMatchParagraph);
        Assert.Contains(Movies.NoMatch, await session.FocusedDescriptionAsync());
    }
}

using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MovieLibrary.Web.Services;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// The real router, the real layout and the real nav, with only the clock replaced. What these pin: when the route
/// text is produced, when it is not, and that it is never written inside the synchronous navigation chain -- so the
/// new page's render and the focus move that accompanies it always precede it, whatever order the LocationChanged
/// handlers ran in. What they cannot pin: how a screen reader orders a focus change against a live-region update.
/// That is the device pass.
/// </summary>
public class RouteAnnouncerTests : BunitContext
{
    private const string FocusBySelector = "Blazor._internal.domWrapper.focusBySelector";

    private readonly ManualTimeProvider clock;
    private readonly Announcer announcer;
    private readonly NavigationManager nav;

    public RouteAnnouncerTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        clock = this.AddAnnouncer();
        announcer = Services.GetRequiredService<Announcer>();
        nav = Services.GetRequiredService<NavigationManager>();
    }

    // Through AppUnderTest, not a local copy: MarkupContractTests renders the same way, and a service a
    // later page needs has to be registerable in one helper rather than in each test class that renders.
    private IRenderedComponent<App> RenderAt(string path) => this.RenderAppAt(path);

    private void Settle() => clock.Advance(TimeSpan.FromHours(1));

    private static string RouteText(IRenderedComponent<App> cut) => cut.Find("#route-announcer").TextContent;

    [Theory]
    [InlineData("/")]
    [InlineData("/movies")]
    [InlineData("/zzz")]
    public void TheFirstLoadIsSilentBecauseTheBrowserAlreadyAnnouncedIt(string path)
    {
        var cut = RenderAt(path);
        Settle();

        Assert.Equal(string.Empty, RouteText(cut));
        Assert.Equal(string.Empty, announcer.RouteText);
    }

    [Fact]
    public void ArrivingOnAnotherPageAnnouncesItsName()
    {
        var cut = RenderAt("/zzz");

        nav.NavigateTo("/movies");
        Settle();

        cut.WaitForAssertion(() => Assert.Equal("Films", RouteText(cut)));
    }

    [Fact]
    public void AnUnknownUrlIsAnnouncedAsNotFoundWhichAlsoProvesTheLayoutSurvivesThatNavigation()
    {
        var cut = RenderAt("/movies");

        nav.NavigateTo("/zzz");
        Settle();

        // If the not-found page were rendered through a fresh layout, the announcer would be rebuilt, treat
        // /zzz as a first load and say nothing.
        cut.WaitForAssertion(() => Assert.Equal("Page not found", RouteText(cut)));
    }

    [Fact]
    public void AChangeOfQueryStringOrFragmentAloneAnnouncesNothing()
    {
        var cut = RenderAt("/movies");

        nav.NavigateTo("/movies?q=a", replace: true);
        nav.NavigateTo("/movies?q=ab", replace: true);
        nav.NavigateTo("/movies?q=ab&genre=Drama&page=2", replace: true);
        nav.NavigateTo("/movies?q=ab#results");
        Settle();
        Assert.Equal(string.Empty, RouteText(cut));

        // Positive control, so the silence above cannot be the harness not working.
        nav.NavigateTo("/zzz");
        Settle();
        cut.WaitForAssertion(() => Assert.Equal("Page not found", RouteText(cut)));
    }

    [Theory]
    [InlineData("/Movies")]
    [InlineData("/movies/")]
    [InlineData("/MOVIES/?q=a")]
    public void TheSamePageUnderADifferentCaseOrTrailingSlashIsNotANewPage(string path)
    {
        var cut = RenderAt("/movies");

        nav.NavigateTo(path);
        Settle();

        Assert.Equal(string.Empty, RouteText(cut));
    }

    [Fact]
    public void TheTwoAddressesOfTheFilmsPageEachAnnounceWhenYouMoveBetweenThem()
    {
        // No redirect exists, so "/" is where a first visit lands and "/movies" is where the nav link goes. The
        // user did navigate and the URL did change, so they hear the name; FocusOnNavigate does not fire for it.
        var cut = RenderAt("/");

        nav.NavigateTo("/movies");
        Settle();

        cut.WaitForAssertion(() => Assert.Equal("Films", RouteText(cut)));
    }

    [Fact]
    public void MovingToAnotherPageCommitsTheRegionEmptyBeforeTheNewName()
    {
        // The old name said "even though the text is the same as before", which this does not test: the text
        // immediately before the final leg is "Page not found" and the new text is "Films", so Contains(empty)
        // holds for any change at all. What it does prove is that the region is committed EMPTY in a render of
        // its own before the new name, which is the gap a reader needs. Identical-consecutive coverage belongs
        // at Announcer level, where ARepeatedMessageIsClearedFirst... has it; do not try to recreate it here.
        var cut = RenderAt("/zzz");
        var committed = new List<string>();
        cut.OnMarkupUpdated += (_, _) => committed.Add(RouteText(cut));

        nav.NavigateTo("/movies"); Settle();
        cut.WaitForAssertion(() => Assert.Equal("Films", RouteText(cut)));
        nav.NavigateTo("/zzz"); Settle();
        cut.WaitForAssertion(() => Assert.Equal("Page not found", RouteText(cut)));
        committed.Clear();
        nav.NavigateTo("/movies"); Settle();
        cut.WaitForAssertion(() => Assert.Equal("Films", RouteText(cut)));

        Assert.Contains(string.Empty, committed);   // cleared between, never Films straight over Films
    }

    // The walk itself lives in AppUnderTest so that MarkupContractTests drives its contracts from the same
    // route set. This stays only as the MemberData hook xunit needs on the test class.
    public static IEnumerable<object[]> RouteTemplates() => AppUnderTest.RouteTemplateData();

    // The table in RouteAnnouncer is typed by hand. A page added without an entry would be announced as
    // "Page not found" on a page that exists, which is a false statement. This turns that into a failing build.
    [Theory]
    [MemberData(nameof(RouteTemplates))]
    public void EveryRouteTheSiteServesIsAnnouncedByTheNameItsOwnHeadingGives(string template)
    {
        var path = AppUnderTest.AddressOf(template);
        var cut = RenderAt(template == "/not-found" ? "/movies" : "/zzz");

        nav.NavigateTo(path);
        Settle();

        var heading = cut.Find("main h1").TextContent.Trim();
        cut.WaitForAssertion(() => Assert.Equal(heading, RouteText(cut)));
    }

    [Fact]
    public void TheNameIsNotWrittenSynchronouslyWithTheNavigationThatAlreadyAskedForTheFocusMove()
    {
        // What this proves is asynchrony, not an ordering between speech events: by the time the navigation call
        // returns the router has rendered and FocusOnNavigate has asked for the h1, and the route text is still
        // empty, so the write cannot be part of that synchronous chain. Whether a reader then speaks the focus
        // change before the region is a device question these tests cannot reach.
        var cut = RenderAt("/zzz");
        Assert.Single(JSInterop.Invocations[FocusBySelector]);

        nav.NavigateTo("/movies");

        // By now the router has rendered the new page and FocusOnNavigate has asked for the h1. The text has
        // not appeared yet; it follows the settle delay, so a speech queue sees the focus change first.
        Assert.Equal(2, JSInterop.Invocations[FocusBySelector].Count);
        Assert.Equal(string.Empty, RouteText(cut));

        Settle();
        cut.WaitForAssertion(() => Assert.Equal("Films", RouteText(cut)));
    }

    [Fact]
    public void ActivatingTheNavLinkOfThePageYouAreOnMovesFocusToTheMenuButtonAndTheNameIsStillNotWritten()
    {
        // Same shape as above, for the one navigation FocusOnNavigate does not fire for: NavMenu puts focus back
        // on the Menu button, and the route text is still empty when that has happened. Asynchrony, not speech
        // order.
        var cut = RenderAt("/");
        cut.Find("button.nav-toggle").Click();
        cut.Find("#nav-links a").Click();

        nav.NavigateTo("/movies");

        cut.WaitForAssertion(() => JSInterop.VerifyFocusAsyncInvoke());
        Assert.Equal(string.Empty, RouteText(cut));

        Settle();
        cut.WaitForAssertion(() => Assert.Equal("Films", RouteText(cut)));
    }

#if DEBUG
    /// <summary>
    /// The announcer's "editor" entry is typed by hand in RouteAnnouncer, whose name table is private, so the
    /// equality with the page's heading is asserted the way every other name here is: by arriving at the
    /// route and reading what the region says. Debug only, because in Release the web project does not
    /// reference the editor, "/editor" is not routed and the announcer has no entry for it -- the test not
    /// compiling is the correct outcome there.
    /// </summary>
    [Fact]
    public async Task TheEditorIsAnnouncedByTheNameOfItsHeadingCharacterForCharacter()
    {
        var cut = RenderAt("/zzz");

        nav.NavigateTo("/editor");
        Settle();

        cut.WaitForAssertion(() => Assert.Equal(MovieLibrary.Web.Editor.Pages.RecordEditor.Heading, RouteText(cut)));
        Assert.Equal("Record editor", RouteText(cut));

        // The editor's LocalDirectory is async-disposable only, and this class is disposed synchronously by
        // xunit, which the container refuses. Disposed here, asynchronously, so the refusal never arises.
        await DisposeAsync();
    }
#endif

    [Fact]
    public async Task TheRouteAnnouncerStopsListeningWhenTheLayoutGoes()
    {
        RenderAt("/movies");
        await DisposeComponentsAsync();

        nav.NavigateTo("/zzz");
        Settle();

        Assert.Equal(string.Empty, announcer.RouteText);
    }
}

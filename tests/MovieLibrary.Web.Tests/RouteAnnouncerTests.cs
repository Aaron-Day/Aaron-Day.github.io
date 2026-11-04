using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MovieLibrary.Web.Pages;
using MovieLibrary.Web.Services;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// The real router, the real layout and the real nav, with only the clock replaced. What these pin: when the route
/// text is produced, when it is not, and that it follows the focus move rather than racing it. What they cannot
/// pin: how a screen reader orders a focus change against a live-region update. That is the device pass.
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

    private IRenderedComponent<App> RenderAt(string path)
    {
        nav.NavigateTo(path);
        return Render<App>();
    }

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
    public void ReturningToAPageAnnouncesItAgainEvenThoughTheTextIsTheSameAsBefore()
    {
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

    public static IEnumerable<object[]> RouteTemplates() =>
        typeof(Movies).Assembly.GetTypes()
            .SelectMany(t => t.GetCustomAttributes(typeof(RouteAttribute), false).Cast<RouteAttribute>())
            .Select(r => r.Template)
            .Distinct()
            .Select(t => new object[] { t });

    // The table in RouteAnnouncer is typed by hand. A page added without an entry would be announced as
    // "Page not found" on a page that exists, which is a false statement. This turns that into a failing build.
    [Theory]
    [MemberData(nameof(RouteTemplates))]
    public void EveryRouteTheSiteServesIsAnnouncedByTheNameItsOwnHeadingGives(string template)
    {
        var path = System.Text.RegularExpressions.Regex.Replace(template, @"\{[^}]*\}", "1");
        var cut = RenderAt(template == "/not-found" ? "/movies" : "/zzz");

        nav.NavigateTo(path);
        Settle();

        var heading = cut.Find("main h1").TextContent.Trim();
        cut.WaitForAssertion(() => Assert.Equal(heading, RouteText(cut)));
    }

    [Fact]
    public void TheNameIsPublishedOnlyAfterTheFocusMoveForTheSameNavigation()
    {
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
    public void ActivatingTheNavLinkOfThePageYouAreOnMovesFocusToTheMenuButtonBeforeTheNameIsPublished()
    {
        var cut = RenderAt("/");
        cut.Find("button.nav-toggle").Click();
        cut.Find("#nav-links a").Click();

        nav.NavigateTo("/movies");

        cut.WaitForAssertion(() => JSInterop.VerifyFocusAsyncInvoke());
        Assert.Equal(string.Empty, RouteText(cut));

        Settle();
        cut.WaitForAssertion(() => Assert.Equal("Films", RouteText(cut)));
    }

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

using System.Reflection;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MovieLibrary.Web.Layout;
using MovieLibrary.Web.Services;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// The three live regions in MainLayout. What this file CAN prove: the markup a screen reader needs is present,
/// is not hidden, starts empty, receives the right text, and the DOM really does change twice for a repeated
/// message. What it CANNOT prove: that any screen reader speaks the text. That depends on the browser's
/// accessibility tree and the reader's own debouncing, and only a device pass can settle it (see the report).
/// </summary>
public class LiveRegionTests : BunitContext
{
    private readonly ManualTimeProvider clock;
    private readonly Announcer announcer;

    public LiveRegionTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        clock = this.AddAnnouncer();
        announcer = Services.GetRequiredService<Announcer>();
    }

    private IRenderedComponent<MainLayout> RenderLayout() =>
        Render<MainLayout>(p => p.Add<AnnounceButtons>(x => x.Body));

    [Theory]
    [InlineData("route-announcer", null, "polite")]
    [InlineData("status-announcer", "status", null)]
    [InlineData("alert-region", "alert", null)]
    public void EachRegionIsPresentAndEmptyOnTheFirstRender(string id, string? role, string? live)
    {
        var region = RenderLayout().Find("#" + id);

        // Present and empty from the first render: a live region injected together with its text is not announced.
        Assert.Equal(string.Empty, region.TextContent);
        Assert.Equal(role, region.GetAttribute("role"));
        Assert.Equal(live, region.GetAttribute("aria-live"));
        Assert.Equal("true", region.GetAttribute("aria-atomic"));
        // Visually hidden, not hidden: display:none, hidden and aria-hidden all remove a region from the tree.
        Assert.Contains("visually-hidden", region.ClassList);
        Assert.Null(region.GetAttribute("hidden"));
        Assert.Null(region.GetAttribute("aria-hidden"));
        Assert.Null(region.GetAttribute("style"));
    }

    [Fact]
    public void ThereAreExactlyThreeRegionsAndNeverTwoFeaturesInOne()
    {
        var cut = RenderLayout();

        var regions = cut.FindAll("[aria-live], [role='status'], [role='alert'], [role='log']");
        Assert.Equal(new[] { "alert-region", "route-announcer", "status-announcer" },
            regions.Select(r => r.Id).OrderBy(i => i, StringComparer.Ordinal));
    }

    [Fact]
    public void OnlyTheAlertRegionIsAssertiveInTheLayoutAndTheErrorBarIsTheOnlyOtherSourceOnTheHostPage()
    {
        // Two sources of assertive speech exist on the page and this is the whole inventory. Announcer.Alert is for
        // failures the app HANDLED; an unhandled exception speaks through #blazor-error-ui. A code path does one
        // or the other, never both, so the two are never heard together. Adding a third source fails here.
        var layout = RenderLayout().FindAll("[role='alert'], [aria-live='assertive']");
        Assert.Equal("alert-region", Assert.Single(layout).Id);

        var host = HostPage.Doc.QuerySelectorAll("[role='alert'], [aria-live='assertive']");
        Assert.Equal("blazor-error-ui", Assert.Single(host).Id);
    }

    [Theory]
    [InlineData("say-route", "route-announcer")]
    [InlineData("say-status", "status-announcer")]
    [InlineData("say-alert", "alert-region")]
    public void EachMessageLandsInItsOwnRegionAndNoOther(string button, string expectedRegion)
    {
        var cut = RenderLayout();

        cut.Find("#" + button).Click();
        clock.Advance(TimeSpan.FromHours(1));

        cut.WaitForAssertion(() =>
        {
            foreach (var id in new[] { "route-announcer", "status-announcer", "alert-region" })
            {
                var text = cut.Find("#" + id).TextContent;
                if (id == expectedRegion) Assert.NotEqual(string.Empty, text);
                else Assert.Equal(string.Empty, text);
            }
        });
    }

    [Fact]
    public void ARepeatedMessageFromAnEventHandlerReallyChangesTheDomTwice()
    {
        var cut = RenderLayout();
        var committed = new List<string>();
        cut.OnMarkupUpdated += (_, _) => committed.Add(cut.Find("#status-announcer").TextContent);

        cut.Find("#say-status").Click();
        clock.Advance(TimeSpan.FromHours(1));
        cut.WaitForAssertion(() => Assert.Equal("12 films", cut.Find("#status-announcer").TextContent));
        committed.Clear();

        cut.Find("#say-status").Click();
        clock.Advance(TimeSpan.FromHours(1));
        cut.WaitForAssertion(() => Assert.Equal("12 films", cut.Find("#status-announcer").TextContent));

        // The renderer merges every render requested inside one event handler, so a clear and a re-set made in
        // the same handler arrive as a single batch whose result equals what the DOM already held: no change.
        // The empty render here is a batch of its own. Whether a screen reader speaks the second one is a
        // device question; that the DOM changed at all is what this proves.
        Assert.Contains(string.Empty, committed);
        Assert.Equal("12 films", committed.Last());
    }

    [Fact]
    public void TwoIdenticalMessagesInOneHandlerAreOneAnnouncement()
    {
        var cut = RenderLayout();
        var committed = new List<string>();
        cut.OnMarkupUpdated += (_, _) => committed.Add(cut.Find("#status-announcer").TextContent);

        cut.Find("#say-twice").Click();
        clock.Advance(TimeSpan.FromHours(1));
        cut.WaitForAssertion(() => Assert.Equal("12 films", cut.Find("#status-announcer").TextContent));

        Assert.Single(committed, c => c.Length > 0);
    }

    [Fact]
    public async Task TheLayoutStopsListeningWhenItIsDisposed()
    {
        var cut = RenderLayout();
        Assert.Equal(1, Listeners(announcer));

        await DisposeComponentsAsync();

        // The announcer lives for the whole session; a layout it still points at is a leak and a late render.
        Assert.Equal(0, Listeners(announcer));
        announcer.Status("12 films");
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal("12 films", announcer.StatusText);
        Assert.True(cut.IsDisposed);
    }

    // Counts the subscribers of the field-like event Changed through its compiler-generated backing field.
    private static int Listeners(Announcer a) =>
        ((Delegate?)typeof(Announcer).GetField("Changed", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(a))?.GetInvocationList().Length ?? 0;
}

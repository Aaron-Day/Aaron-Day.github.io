using MovieLibrary.Web.Services;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// The service on its own, against a clock the test drives. These pin the model: what each region holds and when.
/// They cannot say whether a screen reader speaks anything; that is a device question (see LiveRegionTests).
/// </summary>
public class AnnouncerTests
{
    private readonly ManualTimeProvider clock = new();
    private readonly Announcer announcer;
    private readonly List<(string Route, string Status, string Alert)> seen = new();

    public AnnouncerTests()
    {
        announcer = new Announcer(clock);
        announcer.Changed += () => seen.Add((announcer.RouteText, announcer.StatusText, announcer.AlertText));
    }

    private static readonly TimeSpan Settle = Announcer.SettleDelay;
    private static readonly TimeSpan Plenty = TimeSpan.FromHours(1);

    [Fact]
    public void EveryRegionStartsEmpty()
    {
        Assert.Equal(string.Empty, announcer.RouteText);
        Assert.Equal(string.Empty, announcer.StatusText);
        Assert.Equal(string.Empty, announcer.AlertText);
    }

    [Fact]
    public void TheSettleDelayIsARealDelayNotJustAHopToTheNextTick()
    {
        // The fake clock only fires on Advance, so it cannot tell 100 ms from 0. With a zero delay the real
        // clock would fire at once, the clear and the re-set would share a task, and a browser would be free to
        // coalesce them into no change. At least a few frames, and well short of anything a user would notice.
        // This is a guard against 0, not a measurement. If the device pass settles on a value above 500 ms,
        // widen the bound and record the measurement -- the failure is this test being out of date, not a regression.
        Assert.InRange(Announcer.SettleDelay, TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public void AMessageIsHeldBackForTheSettleDelayThenPublished()
    {
        announcer.Status("12 films");
        Assert.Equal(string.Empty, announcer.StatusText);

        clock.Advance(Settle - TimeSpan.FromMilliseconds(1));
        Assert.Equal(string.Empty, announcer.StatusText);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal("12 films", announcer.StatusText);
    }

    [Fact]
    public void ARepeatedMessageIsClearedFirstSoTheRegionChangesTwiceInSeparateNotifications()
    {
        announcer.Status("12 films");
        clock.Advance(Plenty);
        seen.Clear();

        announcer.Status("12 films");
        Assert.Equal(string.Empty, announcer.StatusText);   // cleared at once, by itself
        clock.Advance(Plenty);

        // Two Changed notifications, one empty and one full, with the settle delay between them. A region set to
        // the text it already holds is not a change, so the empty notification is what lets the second one count.
        Assert.Equal(new[] { string.Empty, "12 films" }, seen.Select(s => s.Status));
    }

    [Fact]
    public void WhenTheLatestMessageIsNewThereIsNoNeedToClearAnEmptyRegion()
    {
        announcer.Status("12 films");
        clock.Advance(Plenty);

        Assert.Equal(new[] { "12 films" }, seen.Select(s => s.Status));
    }

    [Fact]
    public void TheLatestMessageWinsAndASupersededOneIsNeverPublished()
    {
        announcer.Status("12 films");
        clock.Advance(TimeSpan.FromMilliseconds(50));
        announcer.Status("13 films");
        clock.Advance(Plenty);

        Assert.Equal("13 films", announcer.StatusText);
        Assert.DoesNotContain(seen, s => s.Status == "12 films");
    }

    [Fact]
    public void WritingOneRegionNeverDisturbsTheOthers()
    {
        announcer.Alert("Could not load the film list.");
        announcer.Route("Films");
        clock.Advance(Plenty);
        seen.Clear();

        announcer.Status("12 films");
        clock.Advance(Plenty);

        Assert.Equal("Could not load the film list.", announcer.AlertText);
        Assert.Equal("Films", announcer.RouteText);
        Assert.NotEmpty(seen);          // Assert.All on an empty list proves nothing
        Assert.All(seen, s =>
        {
            Assert.Equal("Could not load the film list.", s.Alert);
            Assert.Equal("Films", s.Route);
        });
    }

    [Fact]
    public void EachPurposeGoesToItsOwnRegion()
    {
        announcer.Route("Films");
        announcer.Status("12 films");
        announcer.Alert("Could not load the film list.");
        clock.Advance(Plenty);

        Assert.Equal("Films", announcer.RouteText);
        Assert.Equal("12 films", announcer.StatusText);
        Assert.Equal("Could not load the film list.", announcer.AlertText);
    }

    [Fact]
    public void EmptyTextClearsTheRegionAtOnceAndCancelsAMessageStillWaiting()
    {
        announcer.Status("12 films");
        clock.Advance(Plenty);

        announcer.Status("13 films");
        announcer.Status(string.Empty);
        Assert.Equal(string.Empty, announcer.StatusText);

        clock.Advance(Plenty);
        Assert.Equal(string.Empty, announcer.StatusText);
    }

    [Fact]
    public void NullIsAProgrammingErrorNotAnAnnouncement()
    {
        Assert.Throws<ArgumentNullException>(() => announcer.Status(null!));
        Assert.Throws<ArgumentNullException>(() => announcer.Alert(null!));
    }

    [Fact]
    public async Task OnTheRealClockTheMessageArrivesWithoutAnyoneAdvancingAnything()
    {
        var real = new Announcer();
        real.Status("12 films");

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (real.StatusText.Length == 0 && DateTime.UtcNow < deadline) await Task.Delay(10);

        Assert.Equal("12 films", real.StatusText);
    }

    // --- The settle delay is anchored to the render that carries the clear (RenderCommitted) ---

    [Fact]
    public void ARenderThatCommitsAfterAClearRestartsTheSettleDelayFromThere()
    {
        // The defect this guards: Blazor defers a render requested inside a synchronous handler until the handler
        // returns, so on a slow device the clear and the text can reach the DOM in the same millisecond and a
        // repeated message is no change at all. MainLayout calls RenderCommitted from every OnAfterRender; here
        // the test plays that part, because bUnit's renderer commits in the same instant as the call.
        announcer.Status("12 films");
        clock.Advance(Plenty);

        announcer.Status("12 films");                        // a repeat: the region is cleared first
        clock.Advance(TimeSpan.FromMilliseconds(99));
        announcer.RenderCommitted();                         // the clear reaches the DOM only now

        clock.Advance(TimeSpan.FromMilliseconds(99));
        Assert.Equal(string.Empty, announcer.StatusText);    // 198 ms since the call, 99 ms since the clear

        clock.Advance(TimeSpan.FromMilliseconds(2));
        Assert.Equal("12 films", announcer.StatusText);
    }

    [Fact]
    public void AMessageIntoAnEmptyRegionIsNotHeldForARenderBecauseThereIsNoClearToWaitFor()
    {
        // Empty-to-text is itself a change, so there is no gap to anchor to. A region that is never non-empty
        // would otherwise wait for a render that has nothing to do with it, and on a page that never re-renders
        // it would starve.
        announcer.Status("12 films");
        clock.Advance(TimeSpan.FromMilliseconds(99));
        announcer.RenderCommitted();

        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal("12 films", announcer.StatusText);
    }

    [Fact]
    public void ARenderStormCannotStarveDeliveryBecauseAMessageIsReArmedAtMostOnce()
    {
        announcer.Status("12 films");
        clock.Advance(Plenty);
        announcer.Status("12 films");

        for (var i = 0; i < 50; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(10));
            announcer.RenderCommitted();
        }

        Assert.Equal("12 films", announcer.StatusText);
    }

    [Fact]
    public void ARenderWithNothingPendingChangesNothing()
    {
        announcer.Status("12 films");
        clock.Advance(Plenty);
        seen.Clear();

        announcer.RenderCommitted();
        clock.Advance(Plenty);

        Assert.Equal("12 films", announcer.StatusText);
        Assert.Empty(seen);
    }

    // --- Alert accumulates within its pending window; Status and Route stay latest-wins ---

    [Fact]
    public void TwoFailuresRaisedInsideOneWindowAreBothSpokenRatherThanOneWinningARace()
    {
        // Part B loads movies.json and meta.json concurrently and each catch raises an alert. Latest-wins would
        // tell a user whose connection dropped about one of the two failures, chosen by whichever lost the race.
        announcer.Alert("Could not load the film list.");
        announcer.Alert("Could not load the library details.");
        clock.Advance(Plenty);

        Assert.Equal("Could not load the film list. Could not load the library details.", announcer.AlertText);
    }

    [Fact]
    public void BothFailuresArriveTogetherAfterASingleSettleDelay()
    {
        announcer.Alert("Could not load the film list.");
        announcer.Alert("Could not load the library details.");

        clock.Advance(Settle - TimeSpan.FromMilliseconds(1));
        Assert.Equal(string.Empty, announcer.AlertText);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(new[] { "Could not load the film list. Could not load the library details." },
            seen.Select(s => s.Alert).Where(a => a.Length > 0));
    }

    [Fact]
    public void TheSameFailureRaisedTwiceInOneWindowIsSaidOnce()
    {
        announcer.Alert("Could not load the film list.");
        announcer.Alert("Could not load the film list.");
        clock.Advance(Plenty);

        Assert.Equal("Could not load the film list.", announcer.AlertText);
    }

    [Fact]
    public void AFailureAfterTheWindowClosedIsANewEventAndReplacesTheOldText()
    {
        // Deliberately bounded. Accumulating for the whole session would read back a list of everything that has
        // ever gone wrong, most of it no longer true.
        announcer.Alert("Could not load the film list.");
        clock.Advance(Plenty);

        announcer.Alert("Could not save your filter.");
        clock.Advance(Plenty);

        Assert.Equal("Could not save your filter.", announcer.AlertText);
    }

    [Fact]
    public void AnEmptyAlertClearsTheAccumulationAsWellAsTheText()
    {
        announcer.Alert("Could not load the film list.");
        announcer.Alert("Could not load the library details.");
        announcer.Alert(string.Empty);
        clock.Advance(Plenty);
        Assert.Equal(string.Empty, announcer.AlertText);

        announcer.Alert("Could not save your filter.");
        clock.Advance(Plenty);
        Assert.Equal("Could not save your filter.", announcer.AlertText);
    }

    [Fact]
    public void StatusAndRouteStayLatestWinsBecauseAStaleCountOrPageNameIsWorseThanNone()
    {
        announcer.Status("12 films");
        announcer.Status("13 films");
        announcer.Route("Films");
        announcer.Route("Page not found");
        clock.Advance(Plenty);

        Assert.Equal("13 films", announcer.StatusText);
        Assert.Equal("Page not found", announcer.RouteText);
    }
}

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
}

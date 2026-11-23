#if DEBUG
using MovieLibrary.Web.Services;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// The editor reaches the page's live regions through a seam, and the seam is where a failure could be routed to
/// the wrong one. Only the announcer's own text proves where a message went.
/// </summary>
public sealed class EditorStatusRegionTests
{
    /// <summary>
    /// A failure on the polite region can be dropped unspoken inside its settle delay by a newer message, which is
    /// a failure the owner is never told about. Checking only that the assertive text is non-empty would pass
    /// against a delegation wired to both, so the polite region is asserted empty as well.
    /// </summary>
    [Fact]
    public void AnAlertReachesTheAssertiveRegionAndNeverThePoliteOne()
    {
        var clock = new ManualTimeProvider();
        var announcer = new Announcer(clock);

        new EditorStatusRegion(announcer).Alert("Could not save Se7en.");
        clock.Advance(Announcer.SettleDelay);

        Assert.Equal("Could not save Se7en.", announcer.AlertText);
        Assert.Equal(string.Empty, announcer.StatusText);
    }

    [Fact]
    public void AStatusReachesThePoliteRegionAndNeverTheAssertiveOne()
    {
        var clock = new ManualTimeProvider();
        var announcer = new Announcer(clock);

        new EditorStatusRegion(announcer).Status("12 titles match.");
        clock.Advance(Announcer.SettleDelay);

        Assert.Equal("12 titles match.", announcer.StatusText);
        Assert.Equal(string.Empty, announcer.AlertText);
    }
}
#endif

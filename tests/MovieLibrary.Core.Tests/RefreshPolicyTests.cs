using MovieLibrary.Core.Caching;

namespace MovieLibrary.Core.Tests;

public class RefreshPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FreshEntryIsNotRefreshed()
        => Assert.False(RefreshPolicy.NeedsRefresh(Now.AddDays(-30), Now));

    [Fact]
    public void EntryJustInsideTheWindowIsNotRefreshed()
        => Assert.False(RefreshPolicy.NeedsRefresh(Now.AddDays(-149), Now));

    [Fact]
    public void EntryPastFiveMonthsIsRefreshed()
        => Assert.True(RefreshPolicy.NeedsRefresh(Now.AddDays(-151), Now));

    [Fact]
    public void WindowLeavesABufferBeforeTmdbSixMonthLimit()
        => Assert.True(RefreshPolicy.Window < TimeSpan.FromDays(180));

    [Fact]
    public void NegativeResultsRetryAfterThirtyDays()
    {
        Assert.False(RefreshPolicy.NegativeExpired(Now.AddDays(-29), Now));
        Assert.True(RefreshPolicy.NegativeExpired(Now.AddDays(-31), Now));
    }
}

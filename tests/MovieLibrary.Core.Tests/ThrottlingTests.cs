using MovieLibrary.Core.Throttling;
using MovieLibrary.Core.Tmdb;

namespace MovieLibrary.Core.Tests;

public class ThrottlingTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DetectsV4TokenByJwtPrefix()
        => Assert.Equal(TmdbAuthMode.BearerToken, TmdbAuth.Detect("eyJhbGciOiJIUzI1NiJ9.abc.def"));

    [Fact]
    public void DetectsV3KeyAsHex()
        => Assert.Equal(TmdbAuthMode.QueryKey, TmdbAuth.Detect("0123456789abcdef0123456789abcdef"));

    [Fact]
    public void RejectsEmptyToken()
        => Assert.Throws<ArgumentException>(() => TmdbAuth.Detect("   "));

    [Fact]
    public void FirstFortyRequestsAreNotDelayed()
    {
        var limiter = new SlidingWindowRateLimiter(40, TimeSpan.FromSeconds(10));
        for (var i = 0; i < 40; i++)
        {
            Assert.Equal(TimeSpan.Zero, limiter.DelayBefore(T0));
            limiter.Record(T0);
        }
    }

    [Fact]
    public void FortyFirstRequestWaitsForTheWindowToSlide()
    {
        var limiter = new SlidingWindowRateLimiter(40, TimeSpan.FromSeconds(10));
        for (var i = 0; i < 40; i++) limiter.Record(T0);

        Assert.Equal(TimeSpan.FromSeconds(10), limiter.DelayBefore(T0));
        Assert.Equal(TimeSpan.FromSeconds(4), limiter.DelayBefore(T0.AddSeconds(6)));
        Assert.Equal(TimeSpan.Zero, limiter.DelayBefore(T0.AddSeconds(10)));
    }

    [Fact]
    public void BurstsAreLegalWhileTheAverageHolds()
    {
        var limiter = new SlidingWindowRateLimiter(40, TimeSpan.FromSeconds(10));
        for (var i = 0; i < 40; i++) limiter.Record(T0);
        for (var i = 0; i < 40; i++) limiter.Record(T0.AddSeconds(11));

        Assert.Equal(TimeSpan.FromSeconds(10), limiter.DelayBefore(T0.AddSeconds(11)));
    }

    [Fact]
    public void BudgetStopsAtItsCeiling()
    {
        var budget = new RequestBudget(3);
        Assert.True(budget.TryConsume());
        Assert.True(budget.TryConsume());
        Assert.True(budget.TryConsume());
        Assert.False(budget.TryConsume());
        Assert.True(budget.IsExhausted);
        Assert.Equal(3, budget.Used);
        Assert.Equal(0, budget.Remaining);
    }

    [Fact]
    public void ZeroMeansUnlimited()
    {
        var budget = new RequestBudget(0);
        for (var i = 0; i < 10_000; i++) Assert.True(budget.TryConsume());
        Assert.False(budget.IsExhausted);
    }
}

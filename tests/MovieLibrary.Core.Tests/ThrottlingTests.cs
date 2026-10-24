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

    [Fact]
    public void WindowSlidesPerTimestampNotPerBatch()
    {
        // Pins Evict's boundary comparison. With every request recorded at one instant the
        // clamp hides the difference between >= and >; staggering exposes it.
        var limiter = new SlidingWindowRateLimiter(40, TimeSpan.FromSeconds(10));
        limiter.Record(T0);
        for (var i = 0; i < 39; i++) limiter.Record(T0.AddSeconds(5));

        // At T0+10s the first request has just aged out, leaving 39 in the window...
        limiter.Record(T0.AddSeconds(10));

        // ...so the window is full again and the next slot frees 5s later, when the
        // T0+5s batch ages out.
        Assert.Equal(TimeSpan.FromSeconds(5), limiter.DelayBefore(T0.AddSeconds(10)));
    }

    [Fact]
    public void RejectsNonPositiveRequestLimit()
    {
        var window = TimeSpan.FromSeconds(10);
        Assert.Equal("maxRequests",
            Assert.Throws<ArgumentOutOfRangeException>(() => new SlidingWindowRateLimiter(0, window)).ParamName);
        Assert.Equal("maxRequests",
            Assert.Throws<ArgumentOutOfRangeException>(() => new SlidingWindowRateLimiter(-1, window)).ParamName);
    }

    [Fact]
    public void RejectsNonPositiveWindow()
    {
        Assert.Equal("window",
            Assert.Throws<ArgumentOutOfRangeException>(() => new SlidingWindowRateLimiter(40, TimeSpan.Zero)).ParamName);
        Assert.Equal("window",
            Assert.Throws<ArgumentOutOfRangeException>(() => new SlidingWindowRateLimiter(40, TimeSpan.FromSeconds(-1))).ParamName);
    }

    [Fact]
    public void NegativeAlsoMeansUnlimited()
    {
        var budget = new RequestBudget(-1);
        for (var i = 0; i < 10_000; i++) Assert.True(budget.TryConsume());
        Assert.False(budget.IsExhausted);
    }

    /// <remarks>
    /// Pins the `while` in Evict. With `if` (evict at most one per call) every other limiter
    /// test still passes: BurstsAreLegal... has 40 stale entries meeting 40 Records that each
    /// evict one, so the end state coincides. This sequence deliberately exceeds the limit
    /// (43 records) so more than one entry must age out at once.
    /// </remarks>
    [Fact]
    public void EvictDrainsEveryExpiredEntryNotJustOne()
    {
        var limiter = new SlidingWindowRateLimiter(40, TimeSpan.FromSeconds(10));
        for (var i = 0; i < 3; i++) limiter.Record(T0);
        for (var i = 0; i < 40; i++) limiter.Record(T0.AddSeconds(5));

        // At T0+10 all three T0 entries age out together, leaving exactly 40 at T0+5,
        // whose oldest frees at T0+15.
        Assert.Equal(TimeSpan.FromSeconds(5), limiter.DelayBefore(T0.AddSeconds(10)));
    }
}

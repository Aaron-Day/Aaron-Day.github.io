namespace MovieLibrary.Core.Caching;

public static class RefreshPolicy
{
    /// <summary>
    /// Five months. TMDB's API Terms forbid caching their content beyond six,
    /// so the extra month is slack for a lapsed cron.
    /// </summary>
    public static TimeSpan Window { get; } = TimeSpan.FromDays(150);

    public static TimeSpan NegativeRetry { get; } = TimeSpan.FromDays(30);

    public static bool NeedsRefresh(DateTimeOffset fetchedUtc, DateTimeOffset now)
        => now - fetchedUtc > Window;

    public static bool NegativeExpired(DateTimeOffset fetchedUtc, DateTimeOffset now)
        => now - fetchedUtc > NegativeRetry;
}

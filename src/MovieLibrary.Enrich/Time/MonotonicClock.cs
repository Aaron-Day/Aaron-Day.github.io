using System.Diagnostics;

namespace MovieLibrary.Enrich.Time;

/// <summary>
/// A non-decreasing clock. SlidingWindowRateLimiter requires one: a wall-clock step
/// backwards can stall it for the size of the step, and a seed run takes ~18 minutes.
/// </summary>
public sealed class MonotonicClock
{
    private readonly DateTimeOffset _base = DateTimeOffset.UtcNow;
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();

    public DateTimeOffset Now() => _base + _elapsed.Elapsed;
}

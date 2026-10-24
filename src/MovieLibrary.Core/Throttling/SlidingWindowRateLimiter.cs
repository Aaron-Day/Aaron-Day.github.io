namespace MovieLibrary.Core.Throttling;

/// <summary>
/// Permits <paramref name="maxRequests"/> within any trailing window. Bursts are
/// legal so long as the windowed average holds — unlike a fixed sleep per call.
/// </summary>
public sealed class SlidingWindowRateLimiter(int maxRequests, TimeSpan window)
{
    private readonly Queue<DateTimeOffset> _timestamps = new();

    public TimeSpan DelayBefore(DateTimeOffset now)
    {
        Evict(now);
        if (_timestamps.Count < maxRequests) return TimeSpan.Zero;

        var wait = _timestamps.Peek() + window - now;
        return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
    }

    public void Record(DateTimeOffset now)
    {
        Evict(now);
        _timestamps.Enqueue(now);
    }

    private void Evict(DateTimeOffset now)
    {
        while (_timestamps.Count > 0 && now - _timestamps.Peek() >= window)
            _timestamps.Dequeue();
    }
}

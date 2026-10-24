namespace MovieLibrary.Core.Throttling;

/// <summary>
/// Permits at most <c>maxRequests</c> within any trailing window. Bursts are
/// legal so long as the windowed average holds — unlike a fixed sleep per call.
/// </summary>
/// <remarks>
/// <para>
/// Caller contract: call <see cref="DelayBefore"/>, wait that long, then call
/// <see cref="Record"/> with the time <em>after</em> the wait. <see cref="Record"/> does not
/// itself enforce the limit; recording the pre-wait time would under-count and let the next
/// request through early.
/// </para>
/// <para>
/// Not thread-safe. That is fine for the single-threaded enrich tool; a parallel caller
/// must serialise access itself.
/// </para>
/// </remarks>
public sealed class SlidingWindowRateLimiter
{
    private readonly Queue<DateTimeOffset> _timestamps = new();
    private readonly int _maxRequests;
    private readonly TimeSpan _window;

    public SlidingWindowRateLimiter(int maxRequests, TimeSpan window)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRequests);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(window.Ticks, nameof(window));
        _maxRequests = maxRequests;
        _window = window;
    }

    public TimeSpan DelayBefore(DateTimeOffset now)
    {
        Evict(now);
        if (_timestamps.Count < _maxRequests) return TimeSpan.Zero;

        var wait = _timestamps.Peek() + _window - now;
        return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
    }

    public void Record(DateTimeOffset now)
    {
        Evict(now);
        _timestamps.Enqueue(now);
    }

    private void Evict(DateTimeOffset now)
    {
        while (_timestamps.Count > 0 && now - _timestamps.Peek() >= _window)
            _timestamps.Dequeue();
    }
}

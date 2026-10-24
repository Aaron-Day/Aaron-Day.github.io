namespace MovieLibrary.Core.Throttling;

/// <summary>
/// Advises how long to wait so that no more than <c>maxRequests</c> fall within any
/// trailing half-open window <c>(now - window, now]</c>. Bursts are legal so long as the
/// windowed average holds — unlike a fixed sleep per call. This type advises; it does not
/// enforce. A request exactly one window later is permitted, so a closed interval of
/// <c>window</c> length can contain <c>maxRequests + 1</c>.
/// </summary>
/// <remarks>
/// <para>
/// Caller contract: call <see cref="DelayBefore"/>, wait that long, then call
/// <see cref="Record"/> with the time <em>after</em> the wait. <see cref="Record"/> does not
/// itself enforce the limit; recording the pre-wait time would under-count and let the next
/// request through early.
/// </para>
/// <para>
/// <b><c>now</c> must never move backwards.</b> Every correctness property here assumes a
/// non-decreasing clock. A backwards step produces absurd waits — after a full window,
/// a <c>now</c> one hour earlier yields a delay of over an hour — and can hide entries
/// behind a larger front entry, since eviction only inspects the head of the queue.
/// Derive <c>now</c> from a monotonic source (a fixed UTC base plus <c>Stopwatch</c>
/// elapsed), not from a raw wall clock that NTP can step.
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

        // Evict has already dropped everything at or beyond the window, so the head
        // strictly satisfies now - ts < window and this subtraction is always positive.
        // The clamp is therefore unreachable; it is kept only as a guard, and it is what
        // previously hid an off-by-one in Evict's comparison from every test.
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

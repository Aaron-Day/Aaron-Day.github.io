namespace MovieLibrary.Web.Tests;

/// <summary>
/// A clock that only moves when a test says so. The announcer arms its settle delay by calling
/// <see cref="CreateTimer"/> directly, so the delay can be stepped across exactly, with no sleeping and no flaky
/// "wait long enough to prove nothing happened". <see cref="ITimer.Change"/> really does reset the due time here,
/// which is what lets a test observe the announcer re-arming a pending message when a render commits.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly object gate = new();
    private readonly List<ManualTimer> timers = new();
    private TimeSpan now;

    public override long GetTimestamp() => now.Ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>Moves the clock forward, firing every timer that falls due on the way, in order.</summary>
    public void Advance(TimeSpan by)
    {
        var target = now + by;
        while (true)
        {
            ManualTimer? next;
            lock (gate)
            {
                next = timers.Where(t => t.Due <= target).OrderBy(t => t.Due).FirstOrDefault();
                if (next is null) { now = target; return; }
                now = next.Due > now ? next.Due : now;
                next.Reschedule(now);
            }
            next.Fire();
        }
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        private TimeSpan period = Timeout.InfiniteTimeSpan;
        public TimeSpan Due { get; private set; } = TimeSpan.MaxValue;

        public bool Change(TimeSpan dueTime, TimeSpan newPeriod)
        {
            lock (owner.gate)
            {
                period = newPeriod;
                Due = dueTime == Timeout.InfiniteTimeSpan ? TimeSpan.MaxValue : owner.now + dueTime;
                if (!owner.timers.Contains(this)) owner.timers.Add(this);
            }
            return true;
        }

        // Called under the gate: a one-shot timer leaves the list, a periodic one moves to its next tick.
        public void Reschedule(TimeSpan firedAt)
        {
            if (period == Timeout.InfiniteTimeSpan || period == TimeSpan.Zero) { Due = TimeSpan.MaxValue; owner.timers.Remove(this); }
            else Due = firedAt + period;
        }

        public void Fire() => callback(state);

        public void Dispose() { lock (owner.gate) { Due = TimeSpan.MaxValue; owner.timers.Remove(this); } }

        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}

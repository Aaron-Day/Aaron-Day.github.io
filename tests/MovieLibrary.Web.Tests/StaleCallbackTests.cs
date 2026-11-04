using MovieLibrary.Web.Services;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// The version guard in <c>Deliver</c>, against an interleaving no ordinary clock can produce.
/// </summary>
/// <remarks>
/// <see cref="ManualTimeProvider"/> cannot reach this: it fires a timer and then takes it off its list, so a
/// disposed timer never calls back. <c>System.Threading.Timer</c> documents the opposite as possible -- a callback
/// already queued to the thread pool runs even though <c>Dispose</c> has since been called -- so the clock here
/// keeps each callback and lets a test deliver one on purpose. Without the version check both of these fail:
/// the superseded text is published, and <c>Deliver</c> then disposes the newer message's timer so the newer text
/// is lost outright, which breaks "latest wins" and "empty cancels" together.
/// </remarks>
public class StaleCallbackTests
{
    private sealed class CapturingTime : TimeProvider
    {
        public readonly List<CapturedTimer> Timers = new();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var t = new CapturedTimer(callback, state);
            Timers.Add(t);
            return t;
        }
    }

    private sealed class CapturedTimer(TimerCallback callback, object? state) : ITimer
    {
        public bool Disposed;

        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose() => Disposed = true;

        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }

        /// <summary>Normal expiry: a disposed timer does not fire.</summary>
        public void Fire() { if (!Disposed) callback(state); }

        /// <summary>Already queued to the pool before <see cref="Dispose"/> ran, so it arrives anyway.</summary>
        public void FireAlreadyQueued() => callback(state);
    }

    [Fact]
    public void ACallbackThatWasAlreadyQueuedWhenItsMessageWasSupersededDoesNotPublishNorCancelTheNewerOne()
    {
        var time = new CapturingTime();
        var a = new Announcer(time);

        a.Status("old");
        a.Status("new");
        Assert.True(time.Timers[0].Disposed);

        time.Timers[0].FireAlreadyQueued();          // the stale callback arrives anyway
        Assert.Equal(string.Empty, a.StatusText);    // latest wins: the superseded text must not appear

        time.Timers[1].Fire();                       // and the newer message must still be delivered
        Assert.Equal("new", a.StatusText);
    }

    [Fact]
    public void ACallbackThatWasAlreadyQueuedWhenTheRegionWasClearedDoesNotPublish()
    {
        var time = new CapturingTime();
        var a = new Announcer(time);

        a.Status("old");
        a.Status(string.Empty);
        time.Timers[0].FireAlreadyQueued();

        Assert.Equal(string.Empty, a.StatusText);
    }
}

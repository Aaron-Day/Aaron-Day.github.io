namespace MovieLibrary.Web.Services;

/// <summary>
/// Owns the text of the three live regions in <c>MainLayout</c>. One place, so two features cannot quietly share a
/// region and overwrite each other's message.
/// </summary>
/// <remarks>
/// <para>
/// <b>Delivery.</b> A message is not written the moment it is requested. The region is emptied first (if it holds
/// anything), and the new text arrives <see cref="SettleDelay"/> later in a separate render. Two things depend on
/// that gap. A live region set to the text it already holds is not a change, so a REPEATED message (filtering
/// back to the same count) only speaks if the region visibly went empty in between. And the renderer merges every
/// render requested inside one event handler, so clearing and re-setting in the same call would reach the DOM as
/// "no change at all". The gap also lets the focus move that accompanies a navigation land first, so a speech queue
/// sees the focus change before the route name, not the other way round.
/// </para>
/// <para>
/// <b>Regions are independent.</b> Writing one never touches another. Within a region the latest message wins: a
/// message still waiting out the delay when a newer one arrives is dropped, never spoken. An empty string clears
/// the region at once and cancels anything waiting.
/// </para>
/// <para>
/// <b>Text is encoded</b> by Razor, so a film title is safe to include.
/// </para>
/// <para>
/// <b>The alert region is not the error bar.</b> The host page already has <c>#blazor-error-ui</c> with
/// <c>role="alert"</c>, which the runtime reveals for an UNHANDLED exception. <see cref="Alert"/> is for a failure the
/// app has handled (a fetch that was caught). A code path does one or the other, never both: catching, calling
/// <see cref="Alert"/> and rethrowing would make two assertive regions speak at once, in an order nobody controls.
/// </para>
/// <para>
/// <b>The regions are visually hidden.</b> Anything that matters to a sighted user must also be on screen as text.
/// <see cref="Alert"/> in particular is not a substitute for showing the failure.
/// </para>
/// </remarks>
public sealed class Announcer
{
    /// <summary>
    /// How long a region stays empty before its new text arrives. 100 ms is the figure the Angular CDK's
    /// LiveAnnouncer settled on for the same job; it has not been measured on this project's devices.
    /// </summary>
    internal static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(100);

    private readonly TimeProvider time;
    private readonly object gate = new();
    private readonly Region route = new(), status = new(), alert = new();

    /// <param name="time">The clock the settle delay runs on. Tests supply their own; the app uses the system's.</param>
    public Announcer(TimeProvider? time = null) => this.time = time ?? TimeProvider.System;

    /// <summary>The current text of <c>#route-announcer</c>.</summary>
    public string RouteText => Read(route);

    /// <summary>The current text of <c>#status-announcer</c>.</summary>
    public string StatusText => Read(status);

    /// <summary>The current text of <c>#alert-region</c>.</summary>
    public string AlertText => Read(alert);

    /// <summary>Raised whenever any region's text changes. MainLayout re-renders on it.</summary>
    public event Action? Changed;

    /// <summary>Result counts, load completions, state toggles. Polite: never interrupts.</summary>
    public void Status(string text) => Publish(status, text);

    /// <summary>
    /// A failure the app HANDLED, such as a fetch it caught. Assertive, so it must stay rare. Never call this for
    /// something that is also thrown: an unhandled exception already speaks through the host page's error bar.
    /// </summary>
    public void Alert(string text) => Publish(alert, text);

    /// <summary>Called by <c>RouteAnnouncer</c> only.</summary>
    internal void Route(string text) => Publish(route, text);

    private string Read(Region region) { lock (gate) return region.Text; }

    private void Publish(Region region, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        bool cleared;
        lock (gate)
        {
            var version = ++region.Version;               // supersedes anything still waiting out the delay
            region.Pending?.Dispose();
            region.Pending = null;

            cleared = region.Text.Length > 0;
            region.Text = string.Empty;

            if (text.Length > 0)
            {
                // A timer, not Task.Delay: the callback runs where the clock fires it, so a test that advances a
                // fake clock sees the result at once, and a superseded message is cancelled outright.
                region.Pending = time.CreateTimer(_ => Deliver(region, version, text), null, SettleDelay, Timeout.InfiniteTimeSpan);
            }
        }

        if (cleared) Changed?.Invoke();                   // its own notification, so its own render
    }

    private void Deliver(Region region, int version, string text)
    {
        lock (gate)
        {
            if (region.Version != version) return;        // a newer message, or a clear, got here first
            region.Text = text;
            region.Pending?.Dispose();
            region.Pending = null;
        }

        Changed?.Invoke();
    }

    private sealed class Region
    {
        public string Text = string.Empty;
        public int Version;
        public ITimer? Pending;     // held so the pending delivery cannot be collected, and can be cancelled
    }
}

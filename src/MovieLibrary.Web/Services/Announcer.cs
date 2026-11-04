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
/// "no change at all".
/// </para>
/// <para>
/// <b>The delay is anchored to the render that carries the clear, not to the call.</b> Blazor defers a render
/// requested inside a synchronous handler until that handler returns, so when <c>Publish</c> returns the clear has
/// not reached the DOM yet; it reaches it when the current batch flushes. On a slow device a handler that
/// announces and then re-renders a large list can flush the empty and the text in the same millisecond, which is
/// exactly the "no change at all" outcome the delay exists to prevent. <c>MainLayout</c> therefore calls
/// <see cref="RenderCommitted"/> from every <c>OnAfterRender</c>, and the first render that commits after a
/// message is armed re-arms that message's timer. The effective delay is whichever is later, the call or that
/// render; at most one re-arm happens per message, so a chatty page cannot starve delivery. Where no layout is
/// mounted (the service on its own, in a test) nothing calls <see cref="RenderCommitted"/> and the call governs.
/// </para>
/// <para>
/// <b>Ordering against the focus move is not what the delay provides.</b> The single-threaded event loop does
/// that: a timer callback cannot run until the whole synchronous navigation chain has finished, so the new page's
/// render and the focus move that accompanies it always land before the route name, whatever order the
/// <c>LocationChanged</c> handlers ran in. The delay adds margin and provides the clear-then-set gap; it is not
/// the ordering mechanism, and nobody should read 100 ms as load-bearing for ordering.
/// </para>
/// <para>
/// <b>Regions are independent.</b> Writing one never touches another. Within a region the latest message wins: a
/// message still waiting out the delay when a newer one arrives is dropped, never spoken. An empty string clears
/// the region at once and cancels anything waiting. <see cref="Alert"/> is the one exception: while a delivery is
/// pending on the alert region, a further non-empty alert JOINS the pending text rather than replacing it (exact
/// duplicates are dropped), so two failures raised inside the same window are both spoken instead of one winning
/// a race. Accumulation stops at delivery -- a failure a minute later is a new event and replaces the old text --
/// and <see cref="Status"/> and <see cref="Route"/> stay latest-wins, because a stale result count or route name
/// is worse than none.
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
/// <para>
/// <b>What the API cannot tell a caller, and a page author needs to know.</b>
/// </para>
/// <list type="number">
/// <item>
/// <b>Delivered text never expires.</b> <c>Status("174 titles match.")</c> is still in <c>#status-announcer</c>
/// after the user has left the page, and a handled <see cref="Alert"/> stays in <c>#alert-region</c> for the rest
/// of the session: the layout survives navigation and this service is a scoped singleton that lives as long as the
/// app. A caller clears with <c>Status("")</c> or <c>Alert("")</c> when the statement stops being true -- on
/// dispose, and on a retry that succeeds.
/// </item>
/// <item>
/// <b>A message still pending when its page is disposed will be spoken on the NEXT page.</b> The window is one
/// <see cref="SettleDelay"/>. Clearing on dispose covers it.
/// </item>
/// <item>
/// <b>Calls arriving faster than the settle delay never deliver anything</b>, because each one re-arms the timer.
/// A caller that would announce per keystroke has to debounce; nothing in this API hints at that.
/// </item>
/// <item>
/// <b>The regions are not coordinated with each other.</b> Route and Status are both polite, so a result count
/// will often land in the same window as a route name. The DOM cannot clobber -- they are separate elements --
/// but which gets spoken, and in what order, is unknown and untested here. It is on the device list.
/// </item>
/// </list>
/// <para>
/// There is deliberately no duration parameter and no expiry mechanism. Nothing needs one yet, and a caller
/// clearing its own text is both simpler and more honest about who owns the statement.
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
    private readonly Region[] regions;

    /// <param name="time">The clock the settle delay runs on. Tests supply their own; the app uses the system's.</param>
    public Announcer(TimeProvider? time = null)
    {
        this.time = time ?? TimeProvider.System;
        regions = new[] { route, status, alert };
    }

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
    /// A second alert raised while the first is still pending joins it rather than replacing it; see the remarks.
    /// </summary>
    public void Alert(string text) => Publish(alert, text, accumulate: true);

    /// <summary>Called by <c>RouteAnnouncer</c> only.</summary>
    internal void Route(string text) => Publish(route, text);

    /// <summary>
    /// Called by <c>MainLayout</c> from EVERY <c>OnAfterRender</c>, not just the first. A render that commits
    /// after a message was armed necessarily carries that message's clear, because <c>Publish</c> empties
    /// <c>Region.Text</c> inside the lock before it returns -- so there is no window in which this marks a clear
    /// observed that is not on screen. Restarting the timer from here makes the gap the user actually gets
    /// "whichever is later, the call or the render", which is what matters on a device slow enough to flush the
    /// clear and the text together. <c>ClearObserved</c> caps it at one re-arm per message.
    /// </summary>
    internal void RenderCommitted()
    {
        lock (gate)
        {
            foreach (var region in regions)
            {
                if (region.Pending is null || region.ClearObserved) continue;

                region.ClearObserved = true;
                region.Pending.Change(SettleDelay, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private string Read(Region region) { lock (gate) return region.Text; }

    /// <param name="accumulate">
    /// When true, a non-empty message that arrives while a delivery is still pending joins the pending text
    /// instead of replacing it. Only <see cref="Alert"/> passes this: two concurrent loads that both fail would
    /// otherwise land in one settle window and a race would pick which failure the user hears about.
    /// </param>
    private void Publish(Region region, string text, bool accumulate = false)
    {
        ArgumentNullException.ThrowIfNull(text);

        bool cleared;
        lock (gate)
        {
            var joining = accumulate && text.Length > 0 && region.Pending is not null;
            if (joining)
            {
                if (!region.PendingParts.Contains(text, StringComparer.Ordinal)) region.PendingParts.Add(text);
            }
            else
            {
                region.PendingParts.Clear();
                if (text.Length > 0) region.PendingParts.Add(text);
            }

            var pending = string.Join(" ", region.PendingParts);

            var version = ++region.Version;               // supersedes anything still waiting out the delay
            // Not load-bearing: the version check in Deliver already makes a stale callback harmless. Kept so a
            // burst of calls does not leave a queue of callbacks that each wake up and take the lock for nothing.
            region.Pending?.Dispose();
            region.Pending = null;

            cleared = region.Text.Length > 0;
            region.Text = string.Empty;

            if (pending.Length > 0)
            {
                // Where the region was already empty there is no empty gap to wait for -- empty-to-text is itself
                // a change -- so the message counts as observed and RenderCommitted must not re-arm it, or a
                // region that is never non-empty would starve. A message that JOINS one already pending inherits
                // the flag: whether that clear has rendered is a fact about the region, not about this call.
                if (!joining) region.ClearObserved = !cleared;

                // A timer, not Task.Delay: the callback runs where the clock fires it, so a test that advances a
                // fake clock sees the result at once, and a superseded message is cancelled outright.
                region.Pending = time.CreateTimer(_ => Deliver(region, version, pending), null, SettleDelay, Timeout.InfiniteTimeSpan);
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
            // Hygiene, not behaviour: the next non-joining Publish resets PendingParts anyway, so a mutant that
            // removes this line survives the suite. Kept so the list never holds text that has already been
            // delivered, which is what anything reading it later would reasonably assume. Not worth a test.
            region.PendingParts.Clear();
        }

        Changed?.Invoke();
    }

    private sealed class Region
    {
        public string Text = string.Empty;
        public int Version;
        public ITimer? Pending;     // held so the pending delivery cannot be collected, and can be cancelled

        // False while this region's clear has been written but not yet rendered, so RenderCommitted knows there
        // is still a clear to anchor the delay to. True once that render has happened, or where no clear was due.
        public bool ClearObserved;

        // The messages making up the text still pending, in the order they were raised. Only Alert ever holds
        // more than one; cleared at delivery, so accumulation never outlives its own settle window.
        public readonly List<string> PendingParts = new();
    }
}

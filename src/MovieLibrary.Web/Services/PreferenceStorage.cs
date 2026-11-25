using Microsoft.JSInterop;

namespace MovieLibrary.Web.Services;

/// <summary>Where the owner's saved state is kept. One seam, so the store above it needs no browser.</summary>
/// <remarks>
/// <b>An interface for ONE implementation, and the reason is the gate rather than the design.</b> The store's
/// rules -- an empty entry is deleted, a moved key is recovered by TMDB id, an import merges -- are worth
/// dozens of assertions, and every one of them would otherwise need a JS runtime, a browser and a real
/// <c>localStorage</c> to run at all. bUnit's loose interop mode would answer every read with null, which
/// tests the default and nothing else.
/// </remarks>
public interface IPreferenceStorage
{
    /// <summary>The saved document, or null when this browser holds none.</summary>
    Task<string?> ReadAsync();

    /// <summary>
    /// The saved document read WITHOUT awaiting, or null where this runtime cannot do that.
    /// </summary>
    /// <remarks>
    /// <b>Sixty controls may not change state under the owner's eye.</b> A wall renders 60 tiles, each with
    /// a favourite toggle carrying <c>aria-pressed</c>, so a document that arrives after the first paint
    /// flips sixty hearts and sixty announced states with no user action behind it -- which is a control
    /// reporting something that was false a frame ago (SC 4.1.2), and sixty shapes changing for a reader
    /// who did nothing. Standalone WebAssembly has <c>IJSInProcessRuntime</c>, so the read can happen in
    /// <c>OnInitialized</c> before the first await, which is where <c>Movies.razor</c> already adopts the
    /// address and for the same stated reason.
    /// <para>
    /// Null means "ask <see cref="ReadAsync"/> instead" and never "nothing saved": a renderer without an
    /// in-process runtime -- bUnit, or a prerendering host this app does not use today -- must still get
    /// the document, one frame late, rather than silently showing every title as unstarred.
    /// </para>
    /// </remarks>
    string? ReadNow();

    Task WriteAsync(string json);
}

/// <summary>
/// <see cref="IPreferenceStorage"/> over the browser's own <c>localStorage</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every call can throw and none of them may escape.</b> <c>localStorage</c> is unavailable in a private
/// window in some browsers, blocked outright by a site-data setting, and throws <c>QuotaExceededError</c>
/// when it is full -- and this app has no server to fall back to, so a throw that reached the renderer would
/// reveal <c>#blazor-error-ui</c> over a page that is otherwise working. The failures are reported to the
/// caller as false rather than swallowed here, because the caller is the only thing that knows whether the
/// owner was watching when it happened.
/// </para>
/// <para>
/// <c>localStorage</c> and not <c>sessionStorage</c> or IndexedDB: a favourite has to survive closing the
/// tab (which rules the first out), and the whole document is a few kilobytes for 1,457 films and 122
/// series, which is nowhere near the 5 MB a browser gives one origin (which makes the third's asynchronous
/// machinery a cost with nothing bought).
/// </para>
/// </remarks>
public sealed class LocalPreferenceStorage(IJSRuntime js) : IPreferenceStorage
{
    /// <summary>
    /// The one key this app writes, namespaced and versioned in the NAME as well as in the document.
    /// </summary>
    /// <remarks>
    /// The site is published at the root of a <c>github.io</c> origin, which it shares with anything else
    /// the owner ever publishes there -- <c>localStorage</c> is per ORIGIN, not per path -- so an unprefixed
    /// key would be a collision waiting for his next project.
    /// </remarks>
    public const string Key = "movieLibrary.preferences.v1";

    /// <summary>
    /// <see cref="IPreferenceStorage.ReadNow"/>, or null when the runtime is not in-process.
    /// </summary>
    /// <remarks>
    /// The cast is the whole of the check. <c>IJSInProcessRuntime</c> is what standalone WebAssembly
    /// provides and what bUnit's does not, so the same code is a synchronous read in the browser and a null
    /// in a component test -- which is why the store keeps its asynchronous path rather than treating this
    /// as the only way in.
    /// </remarks>
    public string? ReadNow()
    {
        if (js is not IJSInProcessRuntime inProcess) return null;

        try
        {
            return inProcess.Invoke<string?>("localStorage.getItem", Key);
        }
        catch (Exception ex) when (Recoverable(ex))
        {
            return null;
        }
    }

    public async Task<string?> ReadAsync()
    {
        try
        {
            return await js.InvokeAsync<string?>("localStorage.getItem", Key);
        }
        catch (Exception ex) when (Recoverable(ex))
        {
            return null;
        }
    }

    public async Task WriteAsync(string json)
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", Key, json);
        }
        catch (Exception ex) when (Recoverable(ex))
        {
            throw new PreferenceStorageException(ex);
        }
    }

    /// <summary>
    /// The ways interop fails that are not this app's fault: storage refused or full (JSException), the
    /// circuit going away mid-call, and the renderer disposing while a call is in flight.
    /// </summary>
    private static bool Recoverable(Exception ex) =>
        ex is JSException or TaskCanceledException or ObjectDisposedException;
}

/// <summary>Raised when the browser refused to keep what the owner just did.</summary>
/// <remarks>
/// A type of its own because the caller has to tell this apart from a bug: the owner starred something, the
/// star is on screen, and the browser will not remember it. Only something that says so on screen can stop
/// that from being a silent loss, which is why the write failure is not swallowed where it happens.
/// </remarks>
public sealed class PreferenceStorageException(Exception inner)
    : Exception("The browser would not save this. Storage may be full or blocked for this site.", inner);

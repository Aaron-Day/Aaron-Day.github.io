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
internal interface IPreferenceStorage
{
    /// <summary>The saved document, or null when this browser holds none.</summary>
    Task<string?> ReadAsync();

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
internal sealed class LocalPreferenceStorage(IJSRuntime js) : IPreferenceStorage
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
internal sealed class PreferenceStorageException(Exception inner)
    : Exception("The browser would not save this. Storage may be full or blocked for this site.", inner);

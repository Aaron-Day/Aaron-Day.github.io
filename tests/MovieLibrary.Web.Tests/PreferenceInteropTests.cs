using Bunit;
using Microsoft.JSInterop;
using MovieLibrary.Web.Services;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// The one place the owner's saved state meets the browser: two calls into <c>localStorage</c>, and every
/// way they can fail.
/// </summary>
/// <remarks>
/// <b>Separate from <c>PreferenceStoreTests</c> on purpose.</b> That class pins the document's rules with no
/// JS runtime anywhere near them; this one exists because those two interop calls are the half that a stub
/// cannot prove -- the key they are made with, the arguments, and the fact that a browser refusing storage
/// does not take the page down with it. bUnit's <c>JSInterop</c> is the subject here rather than a
/// workaround.
/// </remarks>
public class PreferenceInteropTests : BunitContext
{
    private const string Get = "localStorage.getItem";
    private const string Set = "localStorage.setItem";

    private LocalPreferenceStorage Storage() => new(JSInterop.JSRuntime);

    [Fact]
    public async Task ReadingAsksLocalStorageForThisAppsOwnKey()
    {
        JSInterop.Setup<string?>(Get, LocalPreferenceStorage.Key).SetResult("{\"v\":1}");

        Assert.Equal("{\"v\":1}", await Storage().ReadAsync());

        var call = Assert.Single(JSInterop.Invocations[Get]);
        Assert.Equal([LocalPreferenceStorage.Key], call.Arguments);
    }

    /// <remarks>
    /// The key is namespaced because <c>localStorage</c> is per ORIGIN and the site shares
    /// <c>aaron-day.github.io</c> with anything else the owner ever publishes there. Pinned as a value, not
    /// just as a shape: a rename silently orphans every favourite already saved under the old name.
    /// </remarks>
    [Fact]
    public void TheStorageKeyIsNamespacedAndVersioned()
    {
        Assert.Equal("movieLibrary.preferences.v1", LocalPreferenceStorage.Key);
    }

    [Fact]
    public async Task WritingHandsLocalStorageTheKeyAndTheDocument()
    {
        JSInterop.SetupVoid(Set, LocalPreferenceStorage.Key, "{\"v\":1}").SetVoidResult();

        await Storage().WriteAsync("{\"v\":1}");

        var call = Assert.Single(JSInterop.Invocations[Set]);
        Assert.Equal([LocalPreferenceStorage.Key, "{\"v\":1}"], call.Arguments);
    }

    /// <remarks>
    /// A browser with site data blocked, or a private window in Safari, throws on the READ -- and this app
    /// has no server to ask instead, so the page has to come up with no saved state rather than not at all.
    /// Null is "nothing saved", which is the same state a first visit is in.
    /// </remarks>
    [Fact]
    public async Task AReadThatThrowsIsTheSameAsNothingSaved()
    {
        JSInterop.Setup<string?>(Get, LocalPreferenceStorage.Key)
            .SetException(new JSException("SecurityError: the operation is insecure"));

        Assert.Null(await Storage().ReadAsync());
    }

    /// <remarks>
    /// A WRITE that fails is the opposite case and must NOT be swallowed: the owner just starred something,
    /// the star is on screen, and the browser will not remember it. Only something that says so on screen
    /// stops that being a silent loss, and only an exception reaching the caller can make it say so.
    /// </remarks>
    [Fact]
    public async Task AWriteThatThrowsIsReportedAsAStorageFailure()
    {
        JSInterop.SetupVoid(Set, _ => true)
            .SetException(new JSException("QuotaExceededError: the quota has been exceeded"));

        var ex = await Assert.ThrowsAsync<PreferenceStorageException>(() => Storage().WriteAsync("{}"));

        Assert.Contains("full or blocked", ex.Message, StringComparison.Ordinal);
        Assert.IsType<JSException>(ex.InnerException);
    }

    /// <remarks>
    /// The disposal race: the owner navigates while a write is in flight. It is the same list of three that
    /// <c>PageFocus</c> catches, for the same reason -- an escape from interop reveals
    /// <c>#blazor-error-ui</c>, which is an assertive region, over a page that is working.
    /// </remarks>
    [Theory]
    [InlineData(typeof(TaskCanceledException))]
    [InlineData(typeof(ObjectDisposedException))]
    public async Task TheWaysInteropDiesMidCallAreNotUnhandled(Type kind)
    {
        Exception thrown = kind == typeof(TaskCanceledException)
            ? new TaskCanceledException()
            : new ObjectDisposedException("renderer");

        JSInterop.Setup<string?>(Get, LocalPreferenceStorage.Key).SetException(thrown);
        JSInterop.SetupVoid(Set, _ => true).SetException(thrown);

        Assert.Null(await Storage().ReadAsync());
        await Assert.ThrowsAsync<PreferenceStorageException>(() => Storage().WriteAsync("{}"));
    }
}

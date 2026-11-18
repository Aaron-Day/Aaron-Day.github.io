using Microsoft.JSInterop;

namespace MovieLibrary.Web.Editor.Store;

/// <summary>
/// The C# side of the one directory grant: a lazily imported JS module, and six operations over it.
/// </summary>
/// <remarks>
/// <para><b>The module reference may be null and that is not an error.</b> Under bUnit's loose JS mode an
/// unimported module comes back as default, and every markup contract renders this page that way. So the import
/// is attempted once, the result is cached as it is, and a null module means "this browser cannot" -- which is
/// also the honest answer on Firefox and Safari. Nothing here dereferences it unguarded.</para>
/// <para><b>Writing is the one operation that refuses rather than no-ops.</b> A read with no grant is a
/// question with no answer, so null is right. A write with no grant is a correction that was supposed to be
/// saved, so it throws and the page says so: a save that silently does nothing is the worst outcome available.
/// The two refusals share one sentence because bUnit's loose mode hands back a module whose every call returns
/// default, so "no module" and "the module reported failure" are indistinguishable in a test and, to the
/// owner, the same thing: the folder is not usable.</para>
/// </remarks>
public sealed class LocalDirectory(IJSRuntime js) : IAsyncDisposable
{
    internal const string ModulePath = "./_content/MovieLibrary.Web.Editor/localDirectory.js";

    private IJSObjectReference? module;
    private bool imported;

    private async ValueTask<IJSObjectReference?> ModuleAsync()
    {
        if (imported) return module;

        imported = true;

        try
        {
            module = await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
        }
        catch (JSException)
        {
            module = null;
        }
        catch (TaskCanceledException)
        {
            module = null;
        }

        return module;
    }

    public async Task<bool> SupportedAsync() =>
        await ModuleAsync() is { } m && await m.InvokeAsync<bool>("supported");

    public async Task<string?> GrantAsync() =>
        await ModuleAsync() is { } m ? await m.InvokeAsync<string?>("grant") : null;

    public async Task<string?> ReconfirmAsync() =>
        await ModuleAsync() is { } m ? await m.InvokeAsync<string?>("reconfirm") : null;

    public async Task<string?> ReadTextAsync(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return await ModuleAsync() is { } m ? await m.InvokeAsync<string?>("readText", name) : null;
    }

    public async Task WriteTextAsync(string name, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(text);

        if (await ModuleAsync() is not { } m)
            throw new InvalidOperationException(
                $"'{name}' was not written. Either no folder has been granted in this browser, or the editor's "
                + "script could not be loaded. Choose a folder first, reload the page, or use the download fallback.");

        if (!await m.InvokeAsync<bool>("writeText", name, text))
            throw new InvalidOperationException(
                $"'{name}' was not written and is unchanged. Either the folder's permission has lapsed, or the "
                + "existing file could not be read -- and what cannot be copied is not overwritten, because the "
                + "save would not be reversible. Choose the folder again, then retry.");
    }

    public async Task DownloadAsync(string name, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(text);

        if (await ModuleAsync() is { } m) await m.InvokeVoidAsync("download", name, text);
    }

    public async ValueTask DisposeAsync()
    {
        if (module is null) return;

        try
        {
            await module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The circuit is already gone, which is the ordinary shape of a page being left.
        }
    }
}

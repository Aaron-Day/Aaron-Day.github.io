using Bunit;
using MovieLibrary.Web.Editor.Store;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// The file-writing layer, driven through bUnit's module interop so no browser is involved. What these pin is
/// the contract the pages rely on: a missing module means "this browser cannot", never an exception.
/// </summary>
public sealed class LocalDirectoryTests
{
    private static BunitContext Context()
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }

    [Fact]
    public async Task WithNoModuleAtAllNothingThrowsAndNothingIsSupported()
    {
        using var context = Context();
        var directory = new LocalDirectory(context.JSInterop.JSRuntime);

        Assert.False(await directory.SupportedAsync());
        Assert.Null(await directory.GrantAsync());
        Assert.Null(await directory.ReconfirmAsync());
        Assert.Null(await directory.ReadTextAsync("x"));
    }

    [Fact]
    public async Task AWriteWithNoModuleIsRefusedRatherThanIgnored()
    {
        using var context = Context();
        var directory = new LocalDirectory(context.JSInterop.JSRuntime);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => directory.WriteTextAsync("record-store.json", "{}"));

        Assert.Contains("no folder has been granted", thrown.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SupportIsWhateverTheModuleSays(bool supported)
    {
        using var context = Context();
        context.JSInterop.SetupModule(LocalDirectory.ModulePath).Setup<bool>("supported").SetResult(supported);
        var directory = new LocalDirectory(context.JSInterop.JSRuntime);

        Assert.Equal(supported, await directory.SupportedAsync());
    }

    [Fact]
    public async Task ARefusedGrantIsNullAndNotAnException()
    {
        using var context = Context();
        context.JSInterop.SetupModule(LocalDirectory.ModulePath).Setup<string?>("grant").SetResult(null);
        var directory = new LocalDirectory(context.JSInterop.JSRuntime);

        Assert.Null(await directory.GrantAsync());
    }

    [Fact]
    public async Task AMissingFileReadsAsNullRatherThanEmpty()
    {
        using var context = Context();
        context.JSInterop.SetupModule(LocalDirectory.ModulePath).Setup<string?>("readText", "record-store.json").SetResult(null);
        var directory = new LocalDirectory(context.JSInterop.JSRuntime);

        Assert.Null(await directory.ReadTextAsync("record-store.json"));
    }

    [Fact]
    public async Task AWriteCrossesTheBoundaryOnceWithBothArguments()
    {
        using var context = Context();
        var module = context.JSInterop.SetupModule(LocalDirectory.ModulePath);
        module.Setup<bool>("writeText", "record-store.json", "{}").SetResult(true);
        var directory = new LocalDirectory(context.JSInterop.JSRuntime);

        await directory.WriteTextAsync("record-store.json", "{}");

        var call = Assert.Single(module.Invocations);
        Assert.Equal("writeText", call.Identifier);
        Assert.Equal(["record-store.json", "{}"], call.Arguments.Cast<string>());
    }

    [Fact]
    public async Task AWriteTheModuleReportsAsFailedIsAnExceptionNotASilentNoOp()
    {
        using var context = Context();
        context.JSInterop.SetupModule(LocalDirectory.ModulePath)
            .Setup<bool>("writeText", "record-store.json", "{}").SetResult(false);
        var directory = new LocalDirectory(context.JSInterop.JSRuntime);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => directory.WriteTextAsync("record-store.json", "{}"));
    }

    /// <summary>
    /// A58. The backup happens inside the module's one writeText call, so no C# invocation order can show it.
    /// What can be shown is the order of the statements: the backup file is opened before the temporary file
    /// is, and the rename is last.
    /// </summary>
    [Fact]
    public void TheScriptBacksUpBeforeItWritesAndRenamesLast()
    {
        var script = WebRoot.ReadSibling("MovieLibrary.Web.Editor", "wwwroot", "localDirectory.js");
        var write = script[script.IndexOf("export async function writeText", StringComparison.Ordinal)..];

        var backup = write.IndexOf("name + \".bak\"", StringComparison.Ordinal);
        var temporary = write.IndexOf("name + \".tmp\"", StringComparison.Ordinal);
        var rename = write.IndexOf(".move(name)", StringComparison.Ordinal);

        Assert.True(backup >= 0 && temporary >= 0 && rename >= 0, "the three steps must all be present");
        Assert.True(backup < temporary && temporary < rename, "backup, then temporary file, then rename");
    }

    // The backup is only a safety net if a write that cannot make one refuses to happen. Reading the file can
    // fail for reasons other than the file being absent, and those two answers must not collapse into one: on
    // absence there is nothing to lose, but on a failed read the write would overwrite a file it could not copy
    // and the save would stop being reversible. Source-order, like the test above -- the suite mocks the module
    // and never runs this script, so a guard removed here would otherwise go unnoticed until it cost real data.
    [Fact]
    public void TheScriptWillNotOverwriteAFileItCouldNotBackUp()
    {
        var script = WebRoot.ReadSibling("MovieLibrary.Web.Editor", "wwwroot", "localDirectory.js");
        var write = script[script.IndexOf("export async function writeText", StringComparison.Ordinal)..];

        var refusal = write.IndexOf("!seen.read && !seen.missing", StringComparison.Ordinal);
        var backup = write.IndexOf("name + \".bak\"", StringComparison.Ordinal);

        Assert.True(refusal >= 0, "writeText must tell a failed read apart from an absent file");
        Assert.True(refusal < backup, "the refusal comes before anything is written");
        Assert.Contains("error?.name === \"NotFoundError\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheDownloadFallbackGoesThroughTheModuleToo()
    {
        using var context = Context();
        var module = context.JSInterop.SetupModule(LocalDirectory.ModulePath);
        module.SetupVoid("download", "record-store.json", "{}").SetVoidResult();
        var directory = new LocalDirectory(context.JSInterop.JSRuntime);

        await directory.DownloadAsync("record-store.json", "{}");

        var call = Assert.Single(module.Invocations);
        Assert.Equal("download", call.Identifier);
        Assert.Equal(["record-store.json", "{}"], call.Arguments.Cast<string>());
    }
}

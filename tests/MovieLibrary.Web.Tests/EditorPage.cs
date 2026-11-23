using Bunit;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MovieLibrary.Core.Store;
using MovieLibrary.Web.Editor;
using MovieLibrary.Web.Editor.Pages;
using MovieLibrary.Web.Editor.Store;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// The one place the editor page's test render is assembled: the scripted folder module and the services the
/// page injects. EditorContractTests, EditorSaveTests and RecordEditorTests each used to carry a private copy
/// of this, so a service the page started injecting would have meant editing every one of them, and the first
/// copy missed would have been an unexplained DI stack trace. Register a new service HERE and nowhere else.
/// (EditorShellTests registers through the product's own <c>AddRecordEditor</c> on purpose: it is the test that
/// the real registration is sufficient, so it must not borrow a test-side one.)
/// <para>
/// What differs per caller is a parameter and not a choice made here: the environment, the catalogue source,
/// the status region, the clock and the JS runtime. The clock matters most. A <see cref="ManualTimeProvider"/>
/// never fires the worklist's debounce timers on its own and <see cref="TimeProvider.System"/> does, and the
/// tests that rely on either are not interchangeable.
/// </para>
/// </summary>
internal static class EditorPage
{
    private sealed class Host(string environment) : IWebAssemblyHostEnvironment
    {
        public string Environment { get; } = environment;
        public string BaseAddress => "http://localhost/";
    }

    /// <summary>The environment the page injects, in a bUnit context that has no WebAssembly host.</summary>
    public static IWebAssemblyHostEnvironment Environment(string environment = "Development") => new Host(environment);

    /// <summary>
    /// Scripts the folder module the way a load asks it three questions: is the API supported, which folder is
    /// granted, and what does the store file read as. Returns the module so a caller can script more on it.
    /// </summary>
    public static BunitJSModuleInterop ScriptFolder(
        BunitContext context, bool supported = true, string? folder = "MovieLibrary", string? storeText = null)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var module = context.JSInterop.SetupModule(LocalDirectory.ModulePath);
        module.Setup<bool>("supported").SetResult(supported);
        module.Setup<string?>("status").SetResult(folder);
        module.Setup<string?>("readText", RecordStoreReader.DefaultFileName).SetResult(storeText);
        return module;
    }

    /// <summary>
    /// Registers what <see cref="RecordEditor"/> injects. <paramref name="runtime"/> is the JS runtime the
    /// directory is built over when a test substitutes its own; without one the container builds it.
    /// </summary>
    public static void Register(
        BunitContext context, IWebAssemblyHostEnvironment environment, ICatalogueSource source,
        IStatusRegion region, TimeProvider clock, IJSRuntime? runtime = null)
    {
        context.Services
            .AddSingleton(environment)
            .AddSingleton(source)
            .AddSingleton(region)
            .AddSingleton(clock);

        if (runtime is null) context.Services.AddScoped<LocalDirectory>();
        else context.Services.AddScoped(_ => new LocalDirectory(runtime));

        context.Services.AddScoped<EditorCatalogue>();
    }

    /// <summary>Renders the page, and says where a missing registration belongs if that is why it threw.</summary>
    public static IRenderedComponent<RecordEditor> Render(BunitContext context)
    {
        try
        {
            return context.Render<RecordEditor>();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Rendering RecordEditor threw, so the test could not look at it. If this is a missing "
                + $"service, register it in {nameof(EditorPage)}.{nameof(Register)} and nowhere else: that "
                + "single helper is what keeps a new service from costing every editor test class its own "
                + "copy of the registration. Do not add a private copy in the test class. See the inner "
                + "exception for the real failure.", ex);
        }
    }
}

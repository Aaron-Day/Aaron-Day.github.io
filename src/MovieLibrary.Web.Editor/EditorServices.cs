using Microsoft.Extensions.DependencyInjection;
using MovieLibrary.Web.Editor.Store;

namespace MovieLibrary.Web.Editor;

/// <summary>
/// The editor's one registration point, called from the web app's Program.cs inside a Debug-only block. It
/// exists so that adding a service to the editor never means editing the web app again -- the web app's single
/// call stays the same, and the Release build does not make it at all.
/// </summary>
public static class EditorServices
{
    public static IServiceCollection AddRecordEditor(this IServiceCollection services) =>
        services.AddScoped<LocalDirectory>();
}

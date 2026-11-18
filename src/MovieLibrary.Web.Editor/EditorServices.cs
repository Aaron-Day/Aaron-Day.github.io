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
    /// <typeparam name="TCatalogue">The web app's implementation of <see cref="ICatalogueSource"/>.</typeparam>
    /// <typeparam name="TStatus">The web app's implementation of <see cref="IStatusRegion"/>.</typeparam>
    /// <remarks>
    /// Generic, so that the two things this project cannot reach -- the catalogue service and the announcer, both
    /// in the web app -- are demanded at compile time rather than discovered as a DI error on the first render.
    /// </remarks>
    public static IServiceCollection AddRecordEditor<TCatalogue, TStatus>(this IServiceCollection services)
        where TCatalogue : class, ICatalogueSource
        where TStatus : class, IStatusRegion =>
        services
            .AddScoped<LocalDirectory>()
            .AddScoped<ICatalogueSource, TCatalogue>()
            .AddScoped<IStatusRegion, TStatus>()
            .AddScoped<EditorCatalogue>();
}

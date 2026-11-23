using MovieLibrary.Core.Models;

namespace MovieLibrary.Web.Editor.Store;

/// <summary>What happened to one fetch of the published catalogue, as far as the editor needs to know.</summary>
/// <remarks>
/// The editor's own enum and not the web app's <c>CatalogState</c>, which is declared in
/// <c>MovieLibrary.Web</c>: this project cannot name a type from an assembly that references it, and moving
/// <c>CatalogResult</c> into Core would change a type three other pages depend on for the benefit of a
/// Debug-only tool. The adapter maps. There is no Loading member because the service never returns one -- the
/// page holds "no state yet" itself, as <c>Movies.razor</c> does.
/// </remarks>
public enum CatalogueState
{
    /// <summary>The fetch produced a catalogue, which may legitimately be empty.</summary>
    Ready,

    /// <summary>The fetch did not produce a catalogue. The value beside it is a safe default and says nothing about the library.</summary>
    Failed,
}

/// <summary>
/// What the editor needs of the published catalogue, declared here because the editor cannot reference the web
/// app -- the reference runs the other way, and only in Debug. The web app implements this over its own
/// CatalogService, so there is still exactly one data path to data/movies.json and one cache behind it.
/// </summary>
public interface ICatalogueSource
{
    Task<(CatalogueState State, IReadOnlyList<MovieRecord> Movies)> MoviesAsync();

    Task<(CatalogueState State, CatalogMeta? Meta)> MetaAsync();
}

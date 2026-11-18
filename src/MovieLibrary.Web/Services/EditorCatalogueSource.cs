#if DEBUG
using MovieLibrary.Core.Models;
using MovieLibrary.Web.Editor;
using MovieLibrary.Web.Editor.Store;
using MovieLibrary.Web.Models;

namespace MovieLibrary.Web.Services;

// Debug only, whole file inside #if DEBUG: the editor project is referenced in Debug alone, so in a Release
// build these types would name an assembly that is not there. Both classes delegate, and that is the point:
// the editor gets the same fetch, the same cached task and the same failure semantics the films page gets,
// because it is literally the same service instance, and the same polite region the shell already owns.

/// <summary>The editor's view of the published catalogue, over the one <see cref="CatalogService"/>.</summary>
internal sealed class EditorCatalogueSource(CatalogService catalogue) : ICatalogueSource
{
    public async Task<(CatalogueState State, IReadOnlyList<MovieRecord> Movies)> MoviesAsync()
    {
        var result = await catalogue.GetMoviesAsync();
        return (Map(result.State), result.Value);
    }

    public async Task<(CatalogueState State, CatalogMeta? Meta)> MetaAsync()
    {
        var result = await catalogue.GetMetaAsync();
        return (Map(result.State), result.Value);
    }

    // Loading is never returned by the service (CatalogState says so), so anything that is not Ready is a fetch
    // that did not produce a catalogue. Mapping the unexpected to Failed is the safe direction: the editor then
    // says the library could not be loaded, where mapping it to Ready would show an empty worklist.
    private static CatalogueState Map(CatalogState state) =>
        state == CatalogState.Ready ? CatalogueState.Ready : CatalogueState.Failed;
}

/// <summary>The editor's polite region, which is the shell's announcer.</summary>
internal sealed class EditorStatusRegion(Announcer announcer) : IStatusRegion
{
    public void Status(string text) => announcer.Status(text);
}
#endif

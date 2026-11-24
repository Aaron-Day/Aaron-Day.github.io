using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using MovieLibrary.Core.Models;
using MovieLibrary.Web.Components;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// The two parameters that make the generic <c>PosterGrid</c> a grid of FILMS, in one place.
/// </summary>
/// <remarks>
/// <c>PosterGrid</c> became generic when the series list needed the same pager, the same clamp and the same
/// announcement: everything below its markup is about paging a list and none of it is about films. What the
/// caller now supplies is the key and the tile, and three harnesses in this project build the component by
/// hand through a <c>RenderTreeBuilder</c>. Spelled once here, because three copies of a render fragment is
/// three chances for one of them to draw a different tile and for a test to pass over markup the app does
/// not ship.
/// </remarks>
internal static class FilmGrid
{
    /// <summary>The published key: distinct across the whole catalogue, where <c>I</c> is 0 for every unmatched film.</summary>
    public static readonly Func<MovieRecord, string> Key = film => film.K;

    /// <summary>One tile, exactly as <c>Movies.razor</c> draws it.</summary>
    public static readonly RenderFragment<MovieRecord> Tile = film => builder =>
    {
        builder.OpenComponent<PosterTile>(0);
        builder.AddComponentParameter(1, nameof(PosterTile.Movie), film);
        builder.CloseComponent();
    };

    /// <summary>
    /// Adds both to a component being built by hand, and returns the next free sequence number.
    /// </summary>
    public static int AddFilmTile(this RenderTreeBuilder builder, int sequence)
    {
        builder.AddComponentParameter(sequence, nameof(PosterGrid<MovieRecord>.KeyOf), Key);
        builder.AddComponentParameter(sequence + 1, nameof(PosterGrid<MovieRecord>.ChildContent), Tile);

        return sequence + 2;
    }
}

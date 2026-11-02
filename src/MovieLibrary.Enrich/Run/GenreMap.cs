using System.Globalization;
using MovieLibrary.Core.Models;

namespace MovieLibrary.Enrich.Run;

/// <summary>
/// The id to name table <c>meta.json</c> carries, and the check that it names every genre a film uses. Each
/// <see cref="MovieRecord.G"/> holds TMDB genre ids; the UI draws the genre facet from this table, so an id without a
/// name is a bare number on screen, and an empty table is a facet of bare numbers.
/// </summary>
internal static class GenreMap
{
    /// <summary>Every genre id some film carries that has no usable name, ascending. Empty when the table is complete.</summary>
    public static IReadOnlyList<int> MissingNames(IReadOnlyDictionary<int, string> names, IReadOnlyList<MovieRecord> movies)
        => [.. movies
            .SelectMany(m => m.G)
            .Distinct()
            .Where(id => string.IsNullOrWhiteSpace(names.GetValueOrDefault(id)))
            .Order()];

    /// <summary>The table as <see cref="CatalogMeta.Genres"/> wants it: string keys, ascending by id.</summary>
    public static Dictionary<string, string> ForMeta(IReadOnlyDictionary<int, string> names)
        => names.OrderBy(kv => kv.Key)
                .ToDictionary(kv => kv.Key.ToString(CultureInfo.InvariantCulture), kv => kv.Value);
}

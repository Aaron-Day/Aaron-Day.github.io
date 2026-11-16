using MovieLibrary.Core.Store;

namespace MovieLibrary.Enrich.Store;

/// <summary>
/// The record store after every entry has been attached to the sheet row it names -- or not attached, and said so.
/// </summary>
/// <remarks>
/// <para><b>Both dictionaries are keyed by the SHEET ROW's identity key, not by the entry's.</b> That is the
/// whole point of binding: a pipeline already computes the row's key (<c>IdentityKey.ForFilm</c> for a film,
/// <c>IdentityKey.ForSeriesName</c> for a series, never
/// <c>ForSeries(TitleNormalizer.Normalize(name))</c>, which omits the trailing-year fold <c>TvPipeline</c> applies) and can look the entry up with it, with no knowledge of drift,
/// orphans or the store's file format.</para>
/// <para><b>Named properties, not positional.</b> The two dictionaries have different value types so they could
/// not be exchanged -- but <see cref="Notes"/> next to them is the shape this project has already shipped a
/// defect with, so the whole type is written the safe way.</para>
/// </remarks>
public sealed record BoundStore
{
    public static BoundStore Empty { get; } = new()
    {
        Films = new Dictionary<string, FilmEntry>(StringComparer.Ordinal),
        Series = new Dictionary<string, SeriesEntry>(StringComparer.Ordinal),
        Notes = [],
    };

    public required IReadOnlyDictionary<string, FilmEntry> Films { get; init; }
    public required IReadOnlyDictionary<string, SeriesEntry> Series { get; init; }

    /// <summary>
    /// What the owner has to be told: an entry that bound by drift, one that bound to nothing, and one that
    /// two entries fought over. These become <c>ReviewQueue.OverrideNotes</c> -- they are not doubtful matches,
    /// they are the owner's own corrections failing, which is why that group prints first.
    /// </summary>
    public required IReadOnlyList<string> Notes { get; init; }
}

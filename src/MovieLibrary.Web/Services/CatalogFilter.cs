using System.Globalization;
using MovieLibrary.Core.Matching;
using MovieLibrary.Core.Models;
using MovieLibrary.Web.Models;

namespace MovieLibrary.Web.Services;

/// <summary>What the films page shows for a filter: the pure half of the facet panel, with no component in it.</summary>
public static class CatalogFilter
{
    /// <summary>
    /// The films that pass every facet, in the order the state asks for. Never reorders or mutates
    /// <paramref name="films"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Search is a plain case-insensitive substring over the title and every director.</b> No fuzzy matching,
    /// no diacritic folding, no splitting into words: <c>OrdinalIgnoreCase</c> is the whole rule.
    /// </para>
    /// <para>
    /// <b>The band is <see cref="ConfidenceBand.Of"/> and is never recomputed from <c>Mc</c> here.</b> That method
    /// reads the id before the score, because the pipeline writes <c>Mc: 0</c> for an unmatched film whatever its
    /// best candidate scored.
    /// </para>
    /// <para>
    /// <b>Sort is by the SORT title -- <c>Srt ?? T</c> -- culture-aware and case-insensitive, with the key as the
    /// last tiebreak</b> so two films that differ only in the thing being sorted by never swap places between
    /// renders. See <see cref="SortTitleOf"/> for what <c>Srt</c> is and what it changes on screen.
    /// </para>
    /// <para>
    /// <b>The year and runtime orders break their ties on the same key</b>, and that is the requirement rather
    /// than consistency for its own sake: a title tiebreak IS title ordering, so a film filed under E in the
    /// default view and under T in the year view would be the worse outcome. There is one title order here.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<MovieRecord> Apply(IReadOnlyList<MovieRecord> films, FilterState state)
    {
        IEnumerable<MovieRecord> shown = films;

        if (state.Query.Length > 0)
            shown = shown.Where(m => Mentions(m, state.Query));

        if (state.Band is { } band)
            shown = shown.Where(m => ConfidenceBand.Of(m) == band);

        if (state.Genres.Count > 0)
            shown = shown.Where(m => m.G is { } genres && genres.Any(state.Genres.Contains));

        if (state.Decades.Count > 0 || state.YearUnknown)
            shown = shown.Where(m => m.Y is { } year ? state.Decades.Contains(DecadeOf(year)) : state.YearUnknown);

        return [.. Order(shown, state.Sort)];
    }

    private static bool Mentions(MovieRecord film, string query) =>
        film.T.Contains(query, StringComparison.OrdinalIgnoreCase)
        || (film.Dir is { } directors && directors.Any(d => d.Contains(query, StringComparison.OrdinalIgnoreCase)));

    private static IEnumerable<MovieRecord> Order(IEnumerable<MovieRecord> films, SortOrder sort) => sort switch
    {
        SortOrder.Year => films
            .OrderByDescending(m => m.Y ?? int.MinValue)   // newest first, and a film with no year after them all
            .ThenBy(SortTitleOf, Titles).ThenBy(m => m.K, StringComparer.Ordinal),
        SortOrder.Runtime => films
            .OrderByDescending(m => m.Rt)
            .ThenBy(SortTitleOf, Titles).ThenBy(m => m.K, StringComparer.Ordinal),
        _ => films
            .OrderBy(SortTitleOf, Titles).ThenBy(m => m.K, StringComparer.Ordinal),
    };

    /// <summary>
    /// Where a film files: its sort title when it has one, its display title when it does not. THE ONE PLACE
    /// this app decides that, so the three orders above cannot drift into two different title orders.
    /// </summary>
    /// <remarks>
    /// <c>MovieRecord.Srt</c> is null unless the title sorts somewhere its display title would not -- the
    /// pipeline strips one leading English article, and <c>titles.sort</c> in the record store overrides what
    /// it derived -- so <c>Srt ?? T</c> is the whole rule. Sorting on <c>Srt</c> alone would file the great
    /// majority of the catalogue under null.
    /// <para>
    /// WHAT THIS CHANGES ON SCREEN, decided by the owner: a leading article stops counting.
    /// <i>The Empire Strikes Back</i> carries <c>Srt</c> "Empire Strikes Back" and files under E, not T. The
    /// DISPLAY title is untouched -- the tile still reads "The Empire Strikes Back" -- and search still reads
    /// <c>T</c>, because a visitor typing "the empire" is typing what they can see.
    /// </para>
    /// </remarks>
    private static string SortTitleOf(MovieRecord film) => film.Srt ?? film.T;

    private static readonly StringComparer Titles = StringComparer.InvariantCultureIgnoreCase;

    /// <summary>
    /// The genres the panel offers, from <c>CatalogMeta.Genres</c>, sorted by name. THE ONE PLACE the map's string
    /// keys become the integers <c>MovieRecord.G</c> carries, so no comparison site converts for itself. A key that
    /// is not a positive integer, or a name that is blank, is dropped: neither can be matched against a film, and a
    /// bare id on screen is exactly what the facet must never show.
    /// </summary>
    public static IReadOnlyList<GenreOption> GenreOptions(CatalogMeta meta) =>
    [
        .. meta.Genres
            .Select(g => (Parsed: int.TryParse(g.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var id), Id: id, g.Value))
            .Where(g => g.Parsed && g.Id > 0 && !string.IsNullOrWhiteSpace(g.Value))
            .Select(g => new GenreOption(g.Id, g.Value.Trim()))
            .OrderBy(g => g.Name, Titles).ThenBy(g => g.Id),
    ];

    /// <summary>The decades some film is in, newest first. A decade nobody is in is not worth a checkbox.</summary>
    public static IReadOnlyList<int> DecadesOf(IReadOnlyList<MovieRecord> films) =>
        [.. films.Where(m => m.Y is not null).Select(m => DecadeOf(m.Y!.Value)).Distinct().OrderDescending()];

    /// <summary>Whether any film has no year, and so whether "Year unknown" is worth offering.</summary>
    public static bool AnyYearUnknown(IReadOnlyList<MovieRecord> films) => films.Any(m => m.Y is null);

    /// <summary>"1990s". The decade is a year, and the year is not a quantity, so it is never grouped (1,990s).</summary>
    public static string DecadeLabel(int decade) => decade.ToString(CultureInfo.InvariantCulture) + "s";

    private static int DecadeOf(int year) => year / 10 * 10;
}

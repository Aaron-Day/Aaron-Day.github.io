using MovieLibrary.Core.Matching;

namespace MovieLibrary.Web.Models;

/// <summary>How the films page orders what the filters leave. Every value has its own direction.</summary>
public enum SortOrder
{
    /// <summary>A to Z. The default, and the one value the URL does not spell out.</summary>
    Title,

    /// <summary>Newest first. A film with no year sorts after every film that has one.</summary>
    Year,

    /// <summary>Longest first.</summary>
    Runtime,
}

/// <summary>One genre the facet offers: TMDB's id and the name the visitor reads.</summary>
public readonly record struct GenreOption(int Id, string Name);

/// <summary>
/// Everything the facet panel can say, and nothing else. The page number is NOT in here: a page is where in
/// the result the visitor is, and every change to this record sends them back to the first one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Within one facet the choices are OR, across facets they are AND.</b> Ticking Drama and Comedy shows films
/// that are either; ticking Drama and the 1990s shows the Dramas of the 1990s. A film cannot be in two decades,
/// so for decades OR is the only reading that works, and genres follow the same rule so that the two checkbox
/// groups behave alike.
/// </para>
/// <para>
/// <b>The query is held trimmed.</b> A search of nothing but spaces is no search, and it must not write
/// <c>?q=%20</c> into the address bar.
/// </para>
/// <para>
/// <b>Genres and Decades are sorted and distinct</b>, which is what lets two states be compared by their
/// canonical text (see <c>QueryState.Canonical</c>) instead of by a hand-written equality over two lists.
/// </para>
/// </remarks>
public sealed record FilterState(
    string Query,
    Confidence? Band,
    IReadOnlyList<int> Genres,
    IReadOnlyList<int> Decades,
    bool YearUnknown,
    SortOrder Sort)
{
    /// <summary>No filter at all, ordered by title: what the page shows with an empty address.</summary>
    public static FilterState Default { get; } = new("", null, [], [], false, SortOrder.Title);

    /// <summary>
    /// Whether anything is narrowing the list. Sort is deliberately not counted: it reorders what is shown and
    /// removes nothing, so "Clear all filters" leaves it alone and the opening announcement is not replaced by a
    /// count merely because the visitor chose a different order.
    /// </summary>
    public bool IsFiltering =>
        Query.Length > 0 || Band is not null || Genres.Count > 0 || Decades.Count > 0 || YearUnknown;

    /// <summary>This state with one genre ticked or unticked.</summary>
    public FilterState WithGenre(int id, bool on) => this with { Genres = Toggle(Genres, id, on) };

    /// <summary>This state with one decade (the first year of it, 1990 for the 1990s) ticked or unticked.</summary>
    public FilterState WithDecade(int decade, bool on) => this with { Decades = Toggle(Decades, decade, on) };

    private static int[] Toggle(IReadOnlyList<int> current, int value, bool on) =>
        [.. (on ? current.Append(value) : current.Where(v => v != value)).Distinct().Order()];

    /// <summary>
    /// The state that can actually be shown: choices the panel has no control for are dropped. A genre the
    /// catalogue's meta does not name, a decade no film is in, and "Year unknown" when every film has a year are
    /// all things an address can ask for and the panel cannot display, and a filter the visitor can neither see
    /// nor untick must not be silently narrowing the list.
    /// </summary>
    public FilterState Within(IEnumerable<int> genres, IEnumerable<int> decades, bool yearUnknownExists)
    {
        var knownGenres = genres.ToHashSet();
        var knownDecades = decades.ToHashSet();

        return this with
        {
            Genres = [.. Genres.Where(knownGenres.Contains)],
            Decades = [.. Decades.Where(knownDecades.Contains)],
            YearUnknown = YearUnknown && yearUnknownExists,
        };
    }
}

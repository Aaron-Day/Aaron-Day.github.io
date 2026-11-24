using MovieLibrary.Core.Matching;
using MovieLibrary.Core.Models;

namespace MovieLibrary.Web.Models;

/// <summary>
/// One line of a tile's caption: the words, and the class that says which line it is.
/// </summary>
/// <remarks>
/// The class is carried rather than derived from position because <c>PosterTileTests</c> reads the caption
/// back by name -- <c>.tile-runtime</c>, <c>.tile-rating</c> -- and a caption addressed by index would let a
/// reordering pass those assertions while changing what a reader meets first. Neither class is styled:
/// <c>PosterTile.razor.css</c> dresses them through <c>.tile-meta &gt; span</c>, so a new kind of fact needs
/// no stylesheet change and cannot arrive unstyled.
/// <para>
/// PUBLIC, and only because the component is. A <c>[Parameter]</c> on a generated component class is a
/// public property, so an internal parameter type is CS0053 Inconsistent accessibility -- not a warning
/// this project could waive, and the Razor SDK gives no way to make the component internal instead.
/// <c>FilmText</c> stays internal: it is called from inside the mapping and never named in a signature.
/// </para>
/// </remarks>
public readonly record struct TileFact(string Class, string Text);

/// <summary>
/// Everything <see cref="Components.PosterTile"/> draws, with nothing in it that knows what a film is.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> A series tile is the same tile: a poster, a title, a year, two terse facts, a
/// confidence badge, one anchor around the body. The alternative was a second component with a second
/// stylesheet, and <c>PosterTile.razor.css</c> is the named subject of eleven assertions across three test
/// files -- the badge variants, the rem font-size, the forced-colors block -- so a copy of it would have
/// been 192 measured lines that nothing held together. The two pages now look alike by construction rather
/// than by inspection, which is what SC 3.2.3 Consistent Navigation and SC 3.2.4 Consistent Identification
/// ask for and what a reader who has learned one wall gets for free on the other.
/// </para>
/// <para>
/// <b>The mapping is where the subject-specific judgement lives</b>, and that is the point of the split:
/// the guard that keeps "0.0/10" off an unmatched film belongs to films and would be nonsense on a series,
/// while a series' own guard -- no episode list, so no episode count -- would be nonsense on a film. The
/// component below renders what it is handed and decides none of it.
/// </para>
/// </remarks>
public sealed record TileView(
    string Href, string? Poster, string Title, int? Year, TileFact[] Meta, Confidence Band)
{
    /// <summary>A film's tile.</summary>
    /// <remarks>
    /// <para>
    /// <b>THE RATING IS GUARDED, AND THE GUARD IS THE WHOLE POINT.</b> <c>Vote</c> is 0d on every unmatched
    /// record -- 30 of the 1,488 published -- and those are precisely the records the page exists to
    /// surface. Rendering "0.0/10" would state as a fact that a film nobody looked up scored zero, so the
    /// fact is ABSENT rather than zero, the same way the year is absent when <c>Y</c> is null. A matched
    /// film TMDB has not rated is the same shape and the same guard covers it. Runtime needs none: no
    /// published film has <c>rt &lt;= 0</c>, and <c>RuntimeParser</c> rejects what it cannot read.
    /// </para>
    /// <para>
    /// Both facts stay TERSE because both are inside the anchor and so inside its accessible name --
    /// "Se7en 1995 1h 50m 8.4/10, link", read on each of 60 tiles. Nothing is added here without weighing
    /// that again.
    /// </para>
    /// <para>
    /// <c>href</c> IS RELATIVE, as <c>NotFound.razor</c>'s is, so it resolves against
    /// <c>&lt;base href="/"&gt;</c> and the one deploy that is not at a domain root keeps working.
    /// </para>
    /// </remarks>
    public static TileView ForFilm(MovieRecord film)
    {
        ArgumentNullException.ThrowIfNull(film);

        var facts = new List<TileFact>(2) { new("tile-runtime", FilmText.Runtime(film.Rt)) };

        if (film.Vote > 0d) facts.Add(new("tile-rating", FilmText.Rating(film.Vote)));

        return new("movies/" + film.K, film.P, film.T, film.Y, [.. facts], ConfidenceBand.Of(film));
    }
}

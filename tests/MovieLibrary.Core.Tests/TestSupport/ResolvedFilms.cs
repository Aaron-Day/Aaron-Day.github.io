using System.Text.Json;
using MovieLibrary.Core.Identity;
using MovieLibrary.Core.Models;
using MovieLibrary.Core.Text;
using MovieLibrary.Enrich.Pipeline;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// Builds the <see cref="ResolvedFilm"/>s the cross-row rules work over, with the key, identity key and folded title
/// derived the way the pipeline derives them, so a rule is exercised on the same shapes a real run hands it.
/// </summary>
internal static class ResolvedFilms
{
    /// <param name="id">The TMDB id the row resolved to; 0 builds an unmatched film.</param>
    /// <param name="sheetTitle">The sheet's own title, which is what <c>T</c> carries.</param>
    /// <param name="tmdbTitle">The matched film's title as TMDB gives it. Null: its details never loaded.</param>
    /// <param name="genre">Apple's own genre, stored as <c>Ag</c>.</param>
    /// <param name="rowRuntime">The sheet's runtime text, when a test needs two rows that differ only in its seconds.</param>
    /// <param name="dir">The published directors, when a test needs them to differ from <paramref name="directors"/> (the pipeline splits on <c>&amp;</c>).</param>
    /// <param name="own">Stored verbatim as <c>Own</c> (the pipeline has already turned blank into null by this point).</param>
    public static ResolvedFilm Make(
        int id, string sheetTitle, int runtime, string? tmdbTitle,
        string? own = null, bool fromOverride = false, string? note = null,
        string directors = "Some Director", double mc = 0.85, string genre = "Drama", string? rowRuntime = null, string[]? dir = null)
    {
        var title = TitleNormalizer.Normalize(sheetTitle);
        var identity = IdentityKey.ForFilm(title, runtime, TitleNormalizer.NormalizeDirectors(directors));
        var row = new SheetRow(sheetTitle, rowRuntime ?? $"{runtime / 60}:{runtime % 60:00}:00", directors, own ?? "", genre, "");

        var record = new MovieRecord(
            I: id, K: RecordKey.ForFilm(id > 0 ? id : null, runtime, identity), T: sheetTitle,
            Y: 2006, Rt: runtime, P: "/p.jpg", B: "/b.jpg", G: [28], Ag: genre, Vote: 7.5, Dir: dir ?? [directors],
            Ov: "Overview.", Tag: "Tagline.", Cast: [new CastMember("An Actor", "A Role", "/a.jpg")],
            Col: "A Collection", Own: own, Mc: id > 0 ? mc : 0d);

        return new ResolvedFilm(record, row, title, identity, tmdbTitle, fromOverride, note);
    }

    // Everything the owner and the published file can see of a reconcile, in a form that is equal only when the output is:
    // each record (as JSON), each film's review line, and the rules' lines. The films are sorted by key because the
    // list keeps the order it was given, and the question here is WHICH row survived, not where it sits.
    public static string Outcome(IEnumerable<ResolvedFilm> input)
    {
        var result = CatalogueReconciler.Reconcile([.. input]);
        var films = result.Films.OrderBy(f => f.Record.K, StringComparer.Ordinal).ThenBy(f => f.Record.T, StringComparer.Ordinal)
            .Select(f => JsonSerializer.Serialize(f.Record) + " | " + f.ReviewLine);
        return string.Join(Environment.NewLine, films.Concat(result.Notes));
    }
}

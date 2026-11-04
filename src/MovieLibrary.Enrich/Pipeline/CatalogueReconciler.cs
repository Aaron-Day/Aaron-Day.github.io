using System.Globalization;
using MovieLibrary.Core.Identity;
using MovieLibrary.Core.Matching;
using MovieLibrary.Core.Models;
using MovieLibrary.Core.Text;

namespace MovieLibrary.Enrich.Pipeline;

/// <summary>
/// One resolved film plus the facts about how it was resolved that <see cref="MovieRecord"/> does not carry: the sheet
/// row it came from, its identity key, and the title TMDB gave the film it matched. The cross-row rules in
/// <see cref="CatalogueReconciler"/> need all of these and the published record needs none of them.
/// </summary>
/// <param name="Record">The record as the row-by-row pass built it.</param>
/// <param name="Row">The sheet row. Its title, runtime and directors head every review line.</param>
/// <param name="SheetTitle">The row's title, folded: what <c>MatchScorer.TitleAgrees</c> compares a TMDB title against.</param>
/// <param name="IdentityKey">Needed to rebuild the <c>u-</c> key if the match is withdrawn.</param>
/// <param name="TmdbTitle">The matched film's title as TMDB gives it; null when unmatched or when its details never loaded.</param>
/// <param name="FromOverride">The owner forced this id from the Overrides tab, so no heuristic may withdraw it.</param>
/// <param name="ReviewNote">Why a human should look at this row, without the row prefix; null when no one should.</param>
internal sealed record ResolvedFilm(
    MovieRecord Record, SheetRow Row, NormalizedTitle SheetTitle, string IdentityKey,
    string? TmdbTitle, bool FromOverride, string? ReviewNote)
{
    /// <summary>The note as the review queue shows it: <c>Title (runtime, directors) → note</c>.</summary>
    public string? ReviewLine => ReviewNote is null ? null : Line(Row, ReviewNote);

    public static string Line(SheetRow row, string note) => $"{row.Title} ({row.Runtime}, {row.Directors}) → {note}";
}

/// <summary>
/// The films after the cross-row rules, and every review line those rules wrote, finished and in rule order. The rules'
/// lines are kept apart from the rows' own <see cref="ResolvedFilm.ReviewNote"/>s so the caller can put them FIRST: the
/// job summary shows only the first <c>JobSummary.MaxReviewLinesPerSection</c> lines of a section, the real film queue is
/// already past that, and lines appended at the end would be the ones cut, which would make a collapse silent.
/// </summary>
internal sealed record Reconciliation(IReadOnlyList<ResolvedFilm> Films, IReadOnlyList<string> Notes);

/// <summary>
/// The rules that need the whole run resolved before they can apply: they compare one row against another, which
/// the row-by-row pass cannot do. A pure post-pass over <see cref="ResolvedFilm"/>s with NO network access, by
/// construction: it holds no client and no budget, because the TMDB request budget is the owner's and a post-pass
/// that spent from it would be a defect.
/// </summary>
/// <remarks>
/// <para>The order is load-bearing, each rule reading the previous one's output. <b>1.</b> A row that took another
/// film's id is rejected, which changes its key. <b>2.</b> Rows that still share a key (decided on the final ids) collapse
/// into one. <b>3.</b> What is left is checked for one film listed twice at different runtimes, which only notes.</para>
/// </remarks>
internal static class CatalogueReconciler
{
    /// <summary>Joins the distinct <c>Own</c> values of a collapsed group.</summary>
    internal const string OwnSeparator = " / ";

    public static Reconciliation Reconcile(IReadOnlyList<ResolvedFilm> films)
    {
        var notes = new List<string>();
        var result = RejectConflictingIds(films, notes);
        result = CollapseDuplicateKeys(result, notes);
        NoteProbableDuplicates(result, notes);
        return new Reconciliation(result, notes);
    }

    private static string Runtimes(IEnumerable<ResolvedFilm> films)
        => string.Join(", ", films.Select(f => f.Record.Rt).Distinct().Order()
            .Select(rt => rt.ToString(CultureInfo.InvariantCulture)));

    private static string Quoted(IEnumerable<string> titles) => string.Join(", ", titles.Select(t => $"\"{t}\""));

    // ---- Rule 1: a row that took another film's id --------------------------------------------------------

    /// <summary>
    /// Groups the films by non-zero TMDB id. In a group of more than one, a row SPEAKS for the id when its sheet title
    /// agrees with the film's TMDB title (<see cref="MatchScorer.TitleAgrees"/>) or it came from the Overrides tab. A row
    /// that does not speak for it is rebuilt as unmatched, but only when some other row does. When no row speaks for the
    /// id nothing in the evidence picks a winner, so nothing is rejected and the group gets one note. When every row
    /// speaks for it the group is fine and says nothing: the owner owns two cuts of a film, which is correct.
    /// </summary>
    /// <remarks>
    /// <para>An Overrides row is the owner saying "this film is TMDB id X", so it is authoritative twice over. It is never
    /// rejected itself: overrides exist precisely where the TMDB title cannot fold to the sheet's (<c>Seven</c> against
    /// <c>Se7en</c>), so an override row would otherwise be the first to go and the owner could never correct a row. And
    /// it speaks for the id even when its own title disagrees, so a non-override row that took the same id and does not
    /// agree on title is rejected because the id is spoken for, even if no other row agrees. Only the override row is
    /// exempt; its group is judged like any other.</para>
    /// <para>The all-agree group is deliberately silent. Every legitimate two-cut pair is one, so a note for it is a permanent
    /// line the owner can do nothing about, in a review section the job summary caps.</para>
    /// </remarks>
    public static IReadOnlyList<ResolvedFilm> RejectConflictingIds(IReadOnlyList<ResolvedFilm> films, List<string> notes)
    {
        var result = films.ToArray();
        var groups = Enumerable.Range(0, films.Count)
            .Where(i => films[i].Record.I > 0)
            .GroupBy(i => films[i].Record.I)
            .Where(g => g.Count() > 1)
            .OrderBy(g => g.Key);

        foreach (var group in groups)
        {
            var id = group.Key;
            var members = group.ToList();
            var runtimes = Runtimes(members.Select(i => films[i]));

            // Every row in the group holds the same film, so one row's copy of its title stands for all of them: a
            // budget-stopped run can leave a row whose payload never loaded.
            var tmdbTitle = members.Select(i => films[i].TmdbTitle).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
            if (tmdbTitle is null)
            {
                notes.Add($"TMDB id {id} → {members.Count} rows share this id, but its title is unavailable " +
                          $"so they could not be checked ({runtimes} min)");
                continue;
            }

            var label = $"{tmdbTitle} ({id})";
            var share = $"{members.Count} rows share this TMDB id";
            var titleAgrees = members.Where(i => MatchScorer.TitleAgrees(tmdbTitle, films[i].SheetTitle)).ToHashSet();
            var speaks = members.Where(i => titleAgrees.Contains(i) || films[i].FromOverride).ToList();
            var rejected = members.Except(speaks).ToList();

            if (speaks.Count == 0)
            {
                notes.Add($"{label} → {share} and none agrees on its title ({runtimes} min); " +
                          "nothing says which is right, so none rejected");
            }
            else if (rejected.Count > 0)
            {
                // The row's own note (Flagged at 0.70, id 9615) is not carried over: it has become false. This one
                // replaces it, and like every line this class writes it goes in Notes, not on the row (see Reconciliation).
                var reason = titleAgrees.Count > 0
                    ? "its title does not match that film and another row's does"
                    : "an Overrides row holds this id and this row's title does not match that film";

                foreach (var i in rejected)
                {
                    result[i] = Unmatched(films[i]);
                    notes.Add(ResolvedFilm.Line(films[i].Row,
                        $"rejected TMDB id {id} (\"{tmdbTitle}\"): {reason}, so it is now unmatched"));
                }
            }
            else
            {
                // Nothing was rejected. If an override is in the group and its title does not agree, say so once: it is
                // the one case where the owner holds two rows on one id and only one of them is the film's own name.
                var kept = members.Where(i => films[i].FromOverride && !titleAgrees.Contains(i))
                    .Select(i => films[i].Row.Title).ToList();
                if (kept.Count > 0)
                    notes.Add($"{label} → {share}; Overrides rows are never rejected, so kept despite a title " +
                              $"that does not agree: {Quoted(kept)}");
            }
        }

        return result;
    }

    /// <summary>
    /// The row as it would have been had it never matched: <c>BuildRecordAsync</c>'s path for a null id. Everything
    /// TMDB supplied is cleared, and everything the sheet supplied (<c>T</c>, <c>Rt</c>, <c>Ag</c>, <c>Dir</c>, <c>Own</c>) stays.
    /// The year falls back to the one the sheet title hints at, as it does there.
    /// </summary>
    private static ResolvedFilm Unmatched(ResolvedFilm film) => film with
    {
        Record = film.Record with
        {
            I = 0,
            K = RecordKey.ForFilm(null, film.Record.Rt, film.IdentityKey),
            Y = film.SheetTitle.YearHint,
            P = null,
            B = null,
            G = [],
            Vote = 0d,
            Ov = null,
            Tag = null,
            Cast = [],
            Col = null,
            Mc = 0d,
        },
        TmdbTitle = null,
        ReviewNote = null,
    };

    // ---- Rule 2: rows that still share a key ----------------------------------------------------------------

    /// <summary>
    /// Groups the films by key. A group of more than one becomes ONE film: the row with the longest sheet title (it
    /// names the edition), ties broken ordinally, with <c>Own</c> replaced by the group's distinct non-blank values,
    /// ordered ordinally and joined with <see cref="OwnSeparator"/>. Nothing the sheet recorded leaves the catalogue.
    /// </summary>
    /// <remarks>
    /// Collapsing rather than disambiguating (a <c>-2</c> on the loser) is deliberate: that would make a record's key
    /// depend on whether a sibling exists, so deleting one sheet row would re-key the other and break any saved link
    /// to it. Here every surviving key stays a pure function of its own row. Every choice reads only the rows' own
    /// fields, never their order, so the same sheet always collapses the same way.
    /// </remarks>
    public static IReadOnlyList<ResolvedFilm> CollapseDuplicateKeys(IReadOnlyList<ResolvedFilm> films, List<string> notes)
    {
        var groups = Enumerable.Range(0, films.Count)
            .GroupBy(i => films[i].Record.K, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToList();
        if (groups.Count == 0) return films;

        var result = films.ToArray();
        var dropped = new HashSet<int>();

        foreach (var group in groups)
        {
            var members = group.Select(i => (Index: i, Film: films[i])).ToList();
            var ranked = members
                .OrderByDescending(m => m.Film.Record.T.Length)
                .ThenBy(m => m.Film.Record.T, StringComparer.Ordinal)
                // Only reachable when the titles are identical; this keeps the choice independent of the sheet's row order.
                .ThenBy(m => m.Film.Record.Ag, StringComparer.Ordinal)
                .ThenBy(m => string.Join("&", m.Film.Record.Dir), StringComparer.Ordinal)
                .ThenByDescending(m => m.Film.Record.Mc)
                .ToList();

            var kept = ranked[0];
            var own = MergeOwn(members.Select(m => m.Film.Record.Own));
            result[kept.Index] = kept.Film with { Record = kept.Film.Record with { Own = own } };

            var gone = ranked.Skip(1).ToList();
            foreach (var m in gone) dropped.Add(m.Index);

            notes.Add(ResolvedFilm.Line(kept.Film.Row,
                $"duplicate key {group.Key}: kept \"{kept.Film.Record.T}\", " +
                $"dropped {Quoted(gone.Select(m => m.Film.Record.T).Order(StringComparer.Ordinal))}" +
                (string.Equals(own, kept.Film.Record.Own, StringComparison.Ordinal) ? "" : "; purchase info merged into Own")));
        }

        return [.. result.Where((_, i) => !dropped.Contains(i))];
    }

    private static string? MergeOwn(IEnumerable<string?> owns)
    {
        var distinct = owns
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        return distinct.Count == 0 ? null : string.Join(OwnSeparator, distinct);
    }

    // ---- Rule 3: one film listed twice at different runtimes ------------------------------------------------

    /// <summary>
    /// Notes, for each TMDB id, every set of two or more rows whose RAW sheet titles are ordinally equal and whose keys
    /// differ. It changes nothing: both rows publish under their own key, and the owner decides whether he owns one copy
    /// or two. The raw title is the discriminator and the folded one cannot be, because <c>TitleNormalizer</c> strips an
    /// edition suffix, so <c>American Pie 2</c> and <c>American Pie 2 (Unrated)</c> fold to one value while every real
    /// two-cut pair differs in its raw title. It is not widened to a runtime tolerance: that would also merge a genuine
    /// pair of cuts a minute apart, and showing one card for two films the owner owns is the worse failure.
    /// </summary>
    public static void NoteProbableDuplicates(IReadOnlyList<ResolvedFilm> films, List<string> notes)
    {
        foreach (var byId in films.Where(f => f.Record.I > 0).GroupBy(f => f.Record.I).OrderBy(g => g.Key))
        foreach (var same in byId.GroupBy(f => f.Row.Title, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var rows = same.ToList();
            if (rows.Select(f => f.Record.K).Distinct(StringComparer.Ordinal).Count() < 2) continue;

            notes.Add($"{same.Key} ({byId.Key}) → same title and id at {Runtimes(rows)} min; probably one film listed twice");
        }
    }
}

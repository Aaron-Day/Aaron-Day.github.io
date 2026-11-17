using System.Globalization;
using MovieLibrary.Core.Identity;
using MovieLibrary.Core.Models;
using MovieLibrary.Core.Parsing;
using MovieLibrary.Core.Store;
using MovieLibrary.Core.Text;

namespace MovieLibrary.Enrich.Store;

/// <summary>
/// Attaches each store entry to the sheet row it names. Pure: no network, no file, no clock.
/// </summary>
/// <remarks>
/// <para><b>By hashing, not by comparing.</b> An entry names a row by its identity key, which is a SHA-1 and so
/// cannot be compared with tolerance. Each row is therefore hashed at its own runtime and at every
/// <see cref="RuntimeDrift.Offsets"/>, with the same frozen <see cref="IdentityKey.ForFilm"/>, and the entry's
/// key is looked up in that index. Offset 0 is an exact bind; anything else is drift. No title is compared
/// loosely anywhere, and the <c>sheet</c> echo is read only to word a note.</para>
/// <para><b>A row claimed by two entries binds to neither.</b> Picking one silently is how a correction lands on
/// the wrong cut of a film. An exact claim does beat a drifted one, because that is not a tie.</para>
/// <para><b>A key two rows carry is reported, not refused.</b> Two rows with one folded title, one truncated
/// runtime and one director hash to ONE key, and the index then holds ONE slot for both of them -- so such a
/// claim is indistinguishable from any other exact hit and the equidistant-tie branch never fires. The entry
/// binds and corrects every row carrying the key. Nothing in the file can separate them, so there is nothing
/// to refuse on the owner's behalf; what there is to do is say so (<see cref="SharedRowNote"/>).</para>
/// <para><b>A series has no drift rule.</b> Its key is a pure function of its folded name
/// (<see cref="IdentityKey.ForSeriesName"/>), so there is no runtime to move and nothing to tolerate.</para>
/// </remarks>
public static class StoreBinder
{
    private const int MaxCandidatesInANote = 3;

    /// <summary>One entry's claim on one row, and how far the runtime had to move to make it.</summary>
    private sealed record Claim(FilmEntry Entry, string RowKey, int Offset);

    public static BoundStore Bind(
        RecordStore store, IReadOnlyList<SheetRow> movieRows, IReadOnlyList<SheetRow> showRows)
    {
        var notes = new List<string>();

        return new BoundStore
        {
            Films = BindFilms(store.Films, movieRows, notes),
            Series = BindSeries(store.Series, showRows, notes),
            Notes = notes,
        };
    }

    // ---- films -------------------------------------------------------------------------------------------

    private static IReadOnlyDictionary<string, FilmEntry> BindFilms(
        IReadOnlyList<FilmEntry> entries, IReadOnlyList<SheetRow> rows, List<string> notes)
    {
        var bound = new Dictionary<string, FilmEntry>(StringComparer.Ordinal);
        if (entries.Count == 0) return bound;

        var (index, sheetTitles, rowInfo) = IndexRows(rows);
        var claims = new List<Claim>();

        foreach (var entry in entries)
        {
            if (index.TryGetValue(entry.IdentityKey, out var hits) && hits.Count > 0)
            {
                // The nearest row wins outright, so an exact hit is never reported as drift. But two rows at the
                // SAME distance (109 and 111 for an entry keyed at 110) are an ambiguity the file cannot settle:
                // picking one would land a correction on a cut the owner may not have meant, and say nothing.
                var nearest = hits.Min(h => Math.Abs(h.Value));
                var closest = hits.Where(h => Math.Abs(h.Value) == nearest).ToList();
                if (closest.Count > 1)
                {
                    notes.Add(TieNote(entry, closest.Select(h => rowInfo[h.Key][0])));
                    continue;
                }

                claims.Add(new Claim(entry, closest[0].Key, closest[0].Value));
                continue;
            }

            notes.Add(OrphanNote(entry, sheetTitles));
        }

        foreach (var contested in claims.GroupBy(c => c.RowKey, StringComparer.Ordinal))
        {
            var exact = contested.Where(c => c.Offset == 0).ToList();

            // At most one claim can be exact: an exact claim means the entry's key IS the row's key, and the
            // reader has already rejected two entries sharing one key.
            if (exact.Count == 1)
            {
                bound[contested.Key] = exact[0].Entry;
                foreach (var loser in contested.Where(c => c.Offset != 0))
                    notes.Add(TakenNote(loser.Entry, exact[0].Entry));

                continue;
            }

            var only = contested.ToList();
            if (only.Count == 1)
            {
                bound[contested.Key] = only[0].Entry;
                notes.Add(DriftNote(only[0]));
                continue;
            }

            foreach (var claim in only) notes.Add(ContestedNote(claim, only));
        }

        // Said here and nowhere else. --print-keys warns at WRITE time, but the collision can arise after an
        // entry was written and bound exactly: Apple re-encodes one of two cuts by a minute, both rows then
        // truncate to the same minutes, and the entry that addressed one of them now addresses both. The
        // binder cannot see that in `hits` -- two rows sharing a key produce ONE slot, so the claim looks
        // exact and single -- and the only other symptom is CatalogueReconciler's duplicate-key line, which
        // names neither this file nor this entry.
        foreach (var (rowKey, entry) in bound.OrderBy(b => b.Key, StringComparer.Ordinal))
        {
            var carriers = rowInfo[rowKey];
            if (carriers.Count > 1) notes.Add(SharedRowNote(entry, carriers));
        }

        return bound;
    }

    /// <summary>
    /// Every row hashed at every offset. The value maps a row key to the SMALLEST offset that reached it, so a
    /// row that two offsets can reach is reported by the truer one.
    /// </summary>
    /// <remarks>
    /// <c>Rows</c> holds EVERY row carrying a key, in sheet order, not just the first. The index cannot: two
    /// rows sharing a key share one slot in it, so the number of carriers is the only record that a key is not
    /// one row's own, and <see cref="BindFilms"/> has nothing else to warn from. Where a note needs a single
    /// row it names the first carrier, which is what the earlier single-value map kept.
    /// </remarks>
    private static (Dictionary<string, Dictionary<string, int>> Index, List<string> SheetTitles,
        Dictionary<string, List<(string Title, int Minutes)>> Rows) IndexRows(
        IReadOnlyList<SheetRow> rows)
    {
        var index = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        var titles = new List<string>();
        var info = new Dictionary<string, List<(string Title, int Minutes)>>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            // EnrichRun rejects an unreadable runtime before the store is bound. Skipping rather than throwing
            // keeps a direct caller (and every later task) out of a stack trace over a data problem.
            if (!RuntimeParser.TryFilmMinutes(row.Runtime, out var minutes)) continue;

            var title = TitleNormalizer.Normalize(row.Title);
            var directors = TitleNormalizer.NormalizeDirectors(row.Directors);
            var rowKey = IdentityKey.ForFilm(title, minutes, directors);
            titles.Add(row.Title);
            if (!info.TryGetValue(rowKey, out var carriers))
                info[rowKey] = carriers = [];

            carriers.Add((row.Title, minutes));

            foreach (var offset in RuntimeDrift.Offsets)
            {
                var at = minutes + offset;

                // Offset 0 is the row's OWN key, and that key exists whatever the runtime is: MoviePipeline
                // looks a row's entry up under IdentityKey.ForFilm over FilmMinutes, which truncates 0:00:30
                // to 0 minutes. Skipping it left such a row the one row on the sheet no entry could ever
                // correct -- while --print-keys printed its key for pasting, unmarked. A DRIFTED key at or
                // below zero is a different thing: it is some other row's own key, so a 1-minute row drifting
                // to 0 would take an entry written against a 0-minute row. That one stays unhashed.
                if (offset != 0 && at <= 0) continue;

                var hash = offset == 0 ? rowKey : IdentityKey.ForFilm(title, at, directors);

                if (!index.TryGetValue(hash, out var hits))
                    index[hash] = hits = new Dictionary<string, int>(StringComparer.Ordinal);

                if (!hits.ContainsKey(rowKey)) hits[rowKey] = offset;
            }
        }

        return (index, titles, info);
    }

    private static string Name(FilmEntry entry)
        => string.IsNullOrWhiteSpace(entry.Sheet.Title) ? $"'{entry.IdentityKey}'" : $"'{entry.Sheet.Title}'";

    private static string DriftNote(Claim claim)
        => $"Record store entry {Name(claim.Entry)} was written against a runtime " +
           $"{Math.Abs(claim.Offset).ToString(CultureInfo.InvariantCulture)} minute(s) " +
           $"{(claim.Offset < 0 ? "shorter" : "longer")} than the sheet now has, and was matched anyway. " +
           "Apple re-encoded the film. Nothing is broken and nothing was changed: the entry goes on working. " +
           "The editor can re-key it when you next open it.";

    private static string TieNote(FilmEntry entry, IEnumerable<(string Title, int Minutes)> rows)
        => $"Record store entry {Name(entry)} is the same distance from more than one sheet row, so it was not " +
           "applied to any of them. Two or more sheet rows are an equal distance from the runtime it was " +
           "written against: " +
           string.Join("; ", rows.OrderBy(r => r.Minutes).ThenBy(r => r.Title, StringComparer.Ordinal)
               .Select(r => $"'{r.Title}' at {r.Minutes.ToString(CultureInfo.InvariantCulture)} minutes")) +
           ". Nothing in the file says which cut you meant. Re-key the entry to the row you mean " +
           "(--print-keys lists each row's key).";

    /// <summary>
    /// One entry bound to a key more than one row carries, so it corrected all of them. The three sentences
    /// after the list are <c>EnrichRun.SayShared</c>'s, word for word on purpose: it is the same fact about
    /// the same key, and two phrasings of it would read as two different problems.
    /// </summary>
    private static string SharedRowNote(FilmEntry entry, IReadOnlyList<(string Title, int Minutes)> carriers)
        => $"Record store entry {Name(entry)} names an identity key that " +
           $"{carriers.Count.ToString(CultureInfo.InvariantCulture)} rows on the Movies tab carry, so it was " +
           "applied to every one of them: " +
           string.Join("; ", carriers.OrderBy(r => r.Minutes).ThenBy(r => r.Title, StringComparer.Ordinal)
               .Select(r => $"'{r.Title}' at {r.Minutes.ToString(CultureInfo.InvariantCulture)} minutes")) +
           ". A record store entry names its row by that key and is applied to EVERY row the key names, so an " +
           "entry on a shared key corrects all the rows listed under it and can never correct one of them on " +
           "its own. Nothing you can write in record-store.json separates them.";

    private static string TakenNote(FilmEntry loser, FilmEntry winner)
        => $"Record store entry {Name(loser)} would have matched the same row as {Name(winner)}, which names it " +
           "exactly, so the drifted one was not applied. If both are meant for one row, merge them into a single " +
           "entry whose \"records\" list holds both records. If one is for a film you no longer own, delete it.";

    private static string ContestedNote(Claim claim, IReadOnlyList<Claim> all)
        => $"Record store entry {Name(claim.Entry)} matches the same row as more than one other entry " +
           $"({string.Join(", ", all.Where(c => !ReferenceEquals(c, claim)).Select(c => Name(c.Entry)))}), " +
           "and none of them names " +
           "it exactly, so none was applied. Nothing in the file says which you meant. Correct one entry's " +
           "\"identityKey\" to the row's own key, or merge them into one entry.";

    private static string OrphanNote(FilmEntry entry, IReadOnlyList<string> sheetTitles)
    {
        // The echo is read HERE and only here: to word the note. Matching never touches it.
        var folded = TitleNormalizer.Normalize(entry.Sheet.Title).Value;
        var near = sheetTitles
            .Where(t => folded.Length > 0 && TitleNormalizer.Normalize(t).Value == folded)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var closest = near.Count == 0
            ? "No film on the sheet has that title."
            : "Closest on the sheet: " + string.Join("; ", near.Take(MaxCandidatesInANote).Select(t => $"'{t}'"))
              + (near.Count > MaxCandidatesInANote
                  ? $"; and {(near.Count - MaxCandidatesInANote).ToString(CultureInfo.InvariantCulture)} more"
                  : "") + ".";

        return $"Record store entry {Name(entry)} matches no film on the Movies tab, so none of its records was " +
               "applied. An entry is matched on its identity key, which folds in the title, the runtime and the " +
               $"directors, with up to {RuntimeDrift.ToleranceMinutes.ToString(CultureInfo.InvariantCulture)} " +
               $"minutes of runtime drift allowed. {closest} A title or director edited on the sheet changes the " +
               "key, so re-key the entry from the row as it now reads. If the film has left your library, delete " +
               "the entry, or this note will come back on every run.";
    }

    // ---- series ------------------------------------------------------------------------------------------

    private static IReadOnlyDictionary<string, SeriesEntry> BindSeries(
        IReadOnlyList<SeriesEntry> entries, IReadOnlyList<SheetRow> rows, List<string> notes)
    {
        var bound = new Dictionary<string, SeriesEntry>(StringComparer.Ordinal);
        if (entries.Count == 0) return bound;

        var names = rows.Select(r => r.Series.Trim()).Where(n => n.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        // TryAdd, not ToDictionary: two distinct names on the sheet can fold to one key ("The Office" and
        // "the office."), and ToDictionary would throw -- turning a sheet problem into a crashed build. The
        // first name wins for the message; which one it is does not matter, because the KEY is what binds.
        var byKey = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in names) byKey.TryAdd(IdentityKey.ForSeriesName(name), name);

        foreach (var entry in entries)
        {
            if (byKey.ContainsKey(entry.IdentityKey))
            {
                bound[entry.IdentityKey] = entry;
                continue;
            }

            var shown = string.IsNullOrWhiteSpace(entry.Sheet.Title) ? $"'{entry.IdentityKey}'" : $"'{entry.Sheet.Title}'";
            var folded = TitleNormalizer.Normalize(entry.Sheet.Title).Value;
            var near = names.Where(n => folded.Length > 0 && TitleNormalizer.Normalize(n).Value == folded).ToList();

            notes.Add(
                $"Record store entry {shown} matches no series on the Shows tab, so its record was not applied. A " +
                "series is matched on its folded name from the Series column, with no runtime and no drift " +
                "allowance. " +
                (near.Count == 0
                    ? "No series on the sheet has that name."
                    : "Closest on the sheet: " + string.Join("; ", near.Select(n => $"'{n}'")) + ".") +
                " If the series has left your library, delete the entry, or this note will come back on every run.");
        }

        return bound;
    }
}

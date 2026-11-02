using MovieLibrary.Core.Models;
using MovieLibrary.Core.Parsing;
using MovieLibrary.Core.Text;
using MovieLibrary.Enrich.Pipeline;

namespace MovieLibrary.Enrich.Sheets;

/// <summary>One Overrides row that is in effect, kept so a later check can name it.</summary>
internal sealed record OverrideEntry(int RowNumber, string Title, string Runtime, bool IsSeries, string Key, int TmdbId);

/// <summary>
/// The Overrides tab, split the way the two pipelines look it up. They use different keys, so one map cannot
/// serve both. <c>Films</c> is keyed by <see cref="MoviePipeline.OverrideKey(string, int)"/> (folded title, trailing
/// year if any, runtime in whole minutes), because films share titles (remakes, editions, re-releases) and a title
/// alone would retarget every one of them at once. <c>Series</c> is keyed by <see cref="TvPipeline.OverrideKey"/>
/// (folded name, trailing year). A film and a series that share one folded title therefore never collide. A row is a
/// series override when its Runtime is blank or zero, a film override when it is a film runtime, and a note when it
/// is neither.
/// </summary>
/// <param name="Films">TMDB ids for films, for <c>MoviePipeline.RunAsync</c>.</param>
/// <param name="Series">TMDB ids for series, for <c>TvPipeline.RunAsync</c>.</param>
/// <param name="Notes">
/// Things the owner should know and that must not pass silently: the tab was not found, so no override was
/// applied, or a row was ignored or overruled. <see cref="WithUnmatchedNotes"/> adds the rows that match nothing.
/// Task 17 prints them in the job summary.
/// </param>
public sealed record SheetOverrides(
    IReadOnlyDictionary<string, int> Films,
    IReadOnlyDictionary<string, int> Series,
    IReadOnlyList<string> Notes)
{
    internal IReadOnlyList<OverrideEntry> Entries { get; init; } = [];

    private const int MaxCandidates = 3;

    /// <summary>
    /// This, with a note appended for every override that matches no row of the sheet. The film key is exact to the
    /// minute, so a re-export that shifts a runtime by one, or a title typed without its year, would otherwise stop an
    /// override applying without a word. Each note names the Overrides row and the closest titles the sheet does have,
    /// says what to change if the row was meant for the other kind (a series with a runtime is read as a film, a film
    /// with none as a series), and says to delete the row if what it names has left the library, which is the only way
    /// to quiet a note that would otherwise come back on every run. Call it once, with the rows the pipelines will be
    /// given. Notes already present keep their place at the front.
    /// </summary>
    public SheetOverrides WithUnmatchedNotes(IReadOnlyList<SheetRow> movieRows, IReadOnlyList<SheetRow> showRows)
    {
        var filmKeys = movieRows.Select(FilmKey).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var seriesNames = showRows.Select(r => r.Series.Trim()).Where(n => n.Length > 0).Distinct().ToList();
        var seriesKeys = seriesNames.Select(TvPipeline.OverrideKey).ToHashSet(StringComparer.Ordinal);

        var unmatched = new List<string>();
        foreach (var e in Entries.OrderBy(e => e.RowNumber))
        {
            if ((e.IsSeries ? seriesKeys : filmKeys).Contains(e.Key)) continue;
            unmatched.Add(e.IsSeries ? SeriesNote(e, seriesNames, movieRows) : FilmNote(e, movieRows, seriesNames));
        }

        return this with { Notes = [.. Notes, .. unmatched] };
    }

    private static string? FilmKey(SheetRow row)
        => Minutes(row.Runtime) is { } minutes ? MoviePipeline.OverrideKey(row.Title, minutes) : null;

    /// <summary>The runtime in whole minutes as a film's sheet row is read, or null when it is not a film runtime.</summary>
    internal static int? Minutes(string runtime)
    {
        try { return RuntimeParser.FilmMinutes(runtime); }
        catch (FormatException) { return null; }
    }

    private static string RuntimeText(string runtime)
        => Minutes(runtime) is { } m ? $"{runtime} ({m} min)" : runtime;

    private static string Describe(string title, string runtime) => $"'{title}' {RuntimeText(runtime)}";

    private static string FilmNote(
        OverrideEntry e, IReadOnlyList<SheetRow> movieRows, IReadOnlyList<string> seriesNames)
    {
        var folded = TitleNormalizer.Normalize(e.Title).Value;
        var near = movieRows
            .Where(r => TitleNormalizer.Normalize(r.Title).Value == folded)
            .Select(r => Describe(r.Title, r.Runtime))
            .Distinct()
            .ToList();

        // A series override pasted with its Shows runtime (h:mm:ss, so it reads as a very long film) lands here, and
        // "copy the runtime from the Movies tab" is the opposite of what would help it. When the Shows tab has a series
        // by that name, say what fits that first. Otherwise nothing says which the owner meant, so give both.
        var isASeriesName = seriesNames.Any(n => TitleNormalizer.Normalize(n).Value == folded);
        var remedy = isASeriesName
            ? "The Shows tab has a series by that name, and a series takes no Runtime: if this row is for the series, " +
              "clear its Runtime cell. If it is for a film, copy the Title and Runtime from the Movies tab exactly."
            : "Copy the Title and Runtime from the Movies tab exactly. " +
              "(A series takes no Runtime: if this row is for one, clear its Runtime cell.)";

        return $"Overrides row {e.RowNumber} ('{e.Title}', {RuntimeText(e.Runtime)}) matches no film on the Movies tab, so it " +
               "was not applied. A film is matched on its title, its year in brackets if it has one, and its runtime " +
               "to the minute. " + Closest(near, "No film on the sheet has that title.") + " " + remedy + " " +
               LeftTheLibrary("film");
    }

    private static string SeriesNote(
        OverrideEntry e, IReadOnlyList<string> seriesNames, IReadOnlyList<SheetRow> movieRows)
    {
        var folded = TitleNormalizer.Normalize(e.Title).Value;
        var near = seriesNames.Where(n => TitleNormalizer.Normalize(n).Value == folded).Select(n => $"'{n}'").ToList();

        // A film override left without its Runtime lands here, because a blank Runtime marks a series, and "copy the
        // name from the Shows tab" is the opposite of what would help it. Mirror of FilmNote: lead with what fits the
        // evidence when the Movies tab has a film by that title, and otherwise give both.
        var isAFilmTitle = movieRows.Any(r => TitleNormalizer.Normalize(r.Title).Value == folded);
        var remedy = isAFilmTitle
            ? "The Movies tab has a film by that title, and a film needs its Runtime: if this row is for the film, " +
              "copy the Runtime from the Movies tab into its Runtime cell. If it is for a series, copy the name from the " +
              "Series column of the Shows tab exactly."
            : "Copy the name from the Series column of the Shows tab exactly. " +
              "(A blank Runtime marks a series; for a film, copy its Title and Runtime from the Movies tab.)";

        return $"Overrides row {e.RowNumber} ('{e.Title}') matches no series on the Shows tab, so it was not applied. " +
               "A series is matched on its name and its year in brackets if it has one. " +
               Closest(near, "No series on the sheet has that name.") + " " + remedy + " " + LeftTheLibrary("series");
    }

    /// <summary>
    /// The one way to quiet a note for a row whose film or series is gone: nothing else removes it, and it returns on
    /// every run, so the owner has to be told the row itself is what to delete.
    /// </summary>
    private static string LeftTheLibrary(string kind)
        => $"If the {kind} has left your library, delete this row, or this note will come back on every run.";

    private static string Closest(IReadOnlyList<string> near, string none)
        => near.Count == 0
            ? none
            : "Closest on the sheet: " + string.Join("; ", near.Take(MaxCandidates))
              + (near.Count > MaxCandidates ? $"; and {near.Count - MaxCandidates} more" : "") + ".";
}

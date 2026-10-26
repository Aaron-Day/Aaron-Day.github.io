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
/// year if any, runtime in whole minutes), because the library has 37 titles shared by 76 films and a title alone
/// retargets all of them. <c>Series</c> is keyed by <see cref="TvPipeline.OverrideKey"/> (folded name, trailing
/// year). A film and a series that share one folded title therefore never collide. A row is a series override when
/// its Runtime is blank or zero, a film override when it is a film runtime, and a note when it is neither.
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
    /// override applying without a word. Each note names the Overrides row and the closest titles the sheet does have.
    /// Call it once, with the rows the pipelines will be given. Notes already present keep their place at the front.
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
            unmatched.Add(e.IsSeries ? SeriesNote(e, seriesNames) : FilmNote(e, movieRows));
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

    private static string FilmNote(OverrideEntry e, IReadOnlyList<SheetRow> movieRows)
    {
        var folded = TitleNormalizer.Normalize(e.Title).Value;
        var near = movieRows
            .Where(r => TitleNormalizer.Normalize(r.Title).Value == folded)
            .Select(r => Describe(r.Title, r.Runtime))
            .Distinct()
            .ToList();

        return $"Overrides row {e.RowNumber} ('{e.Title}', {RuntimeText(e.Runtime)}) matches no film on the Movies tab, so it " +
               "was not applied. A film is matched on its title, its year in brackets if it has one, and its runtime " +
               "to the minute. " + Closest(near, "No film on the sheet has that title.") +
               " Copy the Title and Runtime from the Movies tab exactly.";
    }

    private static string SeriesNote(OverrideEntry e, IReadOnlyList<string> seriesNames)
    {
        var folded = TitleNormalizer.Normalize(e.Title).Value;
        var near = seriesNames.Where(n => TitleNormalizer.Normalize(n).Value == folded).Select(n => $"'{n}'").ToList();

        return $"Overrides row {e.RowNumber} ('{e.Title}') matches no series on the Shows tab, so it was not applied. " +
               "A series is matched on its name and its year in brackets if it has one. " +
               Closest(near, "No series on the sheet has that name.") +
               " Copy the name from the Series column of the Shows tab exactly.";
    }

    private static string Closest(IReadOnlyList<string> near, string none)
        => near.Count == 0
            ? none
            : "Closest on the sheet: " + string.Join("; ", near.Take(MaxCandidates))
              + (near.Count > MaxCandidates ? $"; and {near.Count - MaxCandidates} more" : "") + ".";
}

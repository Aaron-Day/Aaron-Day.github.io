using System.Globalization;
using MovieLibrary.Core.Models;
using MovieLibrary.Core.Sheets;
using MovieLibrary.Core.Text;
using MovieLibrary.Enrich.Pipeline;

namespace MovieLibrary.Enrich.Sheets;

/// <summary>
/// Reads the three tabs of the link-shared Google Sheet over the anonymous CSV endpoint. No credential is
/// involved. Parsing and header validation live in Core (<see cref="CsvParser"/>, <see cref="SheetSchema"/>).
/// </summary>
/// <remarks>
/// <para><b>Google never reports a missing tab.</b> For an unknown tab name it answers HTTP 200 with the FIRST
/// sheet. So a bad status is a real failure (an outage, a sheet that is no longer shared) and propagates as
/// <see cref="HttpRequestException"/>, and a wrong tab is recognised by its header: <see cref="SheetSchemaException"/>.</para>
/// <para><b>Overrides is optional</b> (the first run has to succeed before there is anything to override), so
/// what comes back decides: its own header is parsed; an empty body, or the header of the Movies or Shows tab
/// (Google's fallback), means the tab was not found and yields zero overrides plus a note, because dropping
/// the owner's corrections without a word is the one outcome to avoid; any other header throws.</para>
/// </remarks>
public sealed class GoogleSheetClient(HttpClient http)
{
    private const string MoviesTab = "Movies";
    private const string ShowsTab = "Shows";
    private const string OverridesTab = "Overrides";

    private const string TabNotFoundNote =
        "The 'Overrides' tab was not found (or was renamed), so no overrides were applied. " +
        "Add a tab named 'Overrides' with the columns Title, Runtime, TmdbId, Note.";

    private static string Url(string sheetId, string tab)
        => $"spreadsheets/d/{sheetId}/gviz/tq?tqx=out:csv&sheet={Uri.EscapeDataString(tab)}";

    public async Task<IReadOnlyList<SheetRow>> FetchMoviesAsync(string sheetId, CancellationToken ct)
    {
        var rows = await FetchAsync(sheetId, MoviesTab, SheetSchema.Movies, ct);
        return [.. rows.Select(r => new SheetRow(
            Title: At(r, 0), Runtime: At(r, 1), Directors: At(r, 2),
            PurchaseInfo: At(r, 3), Genre: At(r, 4), Series: string.Empty))];
    }

    public async Task<IReadOnlyList<SheetRow>> FetchShowsAsync(string sheetId, CancellationToken ct)
    {
        var rows = await FetchAsync(sheetId, ShowsTab, SheetSchema.Shows, ct);
        return [.. rows.Select(r => new SheetRow(
            Title: At(r, 0), Runtime: At(r, 1), Directors: string.Empty,
            PurchaseInfo: At(r, 3), Genre: At(r, 4), Series: At(r, 2)))];
    }

    /// <summary>
    /// The Overrides tab as two maps, because the pipelines look an override up under different keys:
    /// films by the normalized title without its year, series by <see cref="TvPipeline.OverrideKey"/>.
    /// A blank or zero Runtime marks a series override, anything else a film.
    /// </summary>
    public async Task<SheetOverrides> FetchOverridesAsync(string sheetId, CancellationToken ct)
    {
        var rows = await GetRowsAsync(sheetId, OverridesTab, ct);

        if (rows.Count == 0
            || Conforms(MoviesTab, SheetSchema.Movies, rows[0])
            || Conforms(ShowsTab, SheetSchema.Shows, rows[0]))
            return new SheetOverrides(new Dictionary<string, int>(), new Dictionary<string, int>(), [TabNotFoundNote]);

        SheetSchema.Validate(OverridesTab, SheetSchema.Overrides, rows[0]); // anything else unknown throws here
        return ParseOverrides(rows);
    }

    private static SheetOverrides ParseOverrides(IReadOnlyList<string[]> rows)
    {
        var films = new Dictionary<string, int>(StringComparer.Ordinal);
        var series = new Dictionary<string, int>(StringComparer.Ordinal);
        var notes = new List<string>();

        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            var (title, runtime, idText, note) = (At(row, 0), At(row, 1), At(row, 2), At(row, 3));
            var number = i + 1; // the header is sheet row 1

            if (title.Length + runtime.Length + idText.Length + note.Length == 0) continue; // padding, not a mistake

            var normalized = TitleNormalizer.Normalize(title);
            if (normalized.Value.Length == 0)
            {
                notes.Add($"Overrides row {number} was ignored: '{title}' is not a usable title.");
                continue;
            }

            if (!int.TryParse(idText, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
            {
                notes.Add($"Overrides row {number} ('{title}') was ignored: TmdbId '{idText}' is not a positive whole number.");
                continue;
            }

            var isSeries = IsBlankOrZero(runtime);
            var map = isSeries ? series : films;
            var key = isSeries ? TvPipeline.OverrideKey(title) : normalized.Value;

            if (map.TryGetValue(key, out var earlier) && earlier != id)
                notes.Add($"Overrides row {number} ('{title}') repeats the {(isSeries ? "series" : "film")} " +
                          $"'{key}' with a different TmdbId ({earlier}, then {id}); {id} was used.");
            map[key] = id;
        }

        return new SheetOverrides(films, series, notes);
    }

    /// <summary>
    /// Blank, or every part of it zero ("0", "0:00", "0:00:00"). A series has no single runtime, so the
    /// Overrides tab leaves it blank; every film row in the export carries one.
    /// </summary>
    private static bool IsBlankOrZero(string runtime)
        => runtime.Length == 0
           || runtime.Split(':').All(part =>
                  long.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var v) && v == 0);

    private async Task<IReadOnlyList<string[]>> FetchAsync(
        string sheetId, string tab, IReadOnlyList<string> expected, CancellationToken ct)
    {
        var rows = await GetRowsAsync(sheetId, tab, ct);
        if (rows.Count == 0) throw new SheetSchemaException($"Tab '{tab}' returned no rows.");

        SheetSchema.Validate(tab, expected, rows[0]);
        return [.. rows.Skip(1).Where(r => r.Length > 0 && r[0].Trim().Length > 0)];
    }

    /// <summary>The tab's CSV rows, header first. A body with nothing in any cell comes back as no rows at all.</summary>
    private async Task<IReadOnlyList<string[]>> GetRowsAsync(string sheetId, string tab, CancellationToken ct)
    {
        using var res = await http.GetAsync(Url(sheetId, tab), ct);
        res.EnsureSuccessStatusCode();

        var rows = CsvParser.Parse(await res.Content.ReadAsStringAsync(ct));
        return rows.All(r => r.All(string.IsNullOrWhiteSpace)) ? [] : rows;
    }

    /// <summary>Whether the header is that tab's own. <see cref="SheetSchema"/> only offers a throwing check, and
    /// using it keeps this in step with its rules (case, BOM, trailing unnamed columns).</summary>
    private static bool Conforms(string tab, IReadOnlyList<string> expected, string[] header)
    {
        try { SheetSchema.Validate(tab, expected, header); return true; }
        catch (SheetSchemaException) { return false; }
    }

    private static string At(string[] row, int i) => i < row.Length ? row[i].Trim() : string.Empty;
}

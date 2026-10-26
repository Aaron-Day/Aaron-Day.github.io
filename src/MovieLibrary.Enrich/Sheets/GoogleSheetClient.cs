using System.Globalization;
using System.Text.RegularExpressions;
using MovieLibrary.Core.Models;
using MovieLibrary.Core.Parsing;
using MovieLibrary.Core.Sheets;
using MovieLibrary.Core.Text;
using MovieLibrary.Enrich.Pipeline;

namespace MovieLibrary.Enrich.Sheets;

/// <summary>
/// Reads the three tabs of the link-shared Google Sheet over the anonymous CSV endpoint. No credential is
/// involved, and none is sent. Parsing and header validation live in Core (<see cref="CsvParser"/>, <see cref="SheetSchema"/>).
/// </summary>
/// <remarks>
/// <para><b>Google never reports a missing tab.</b> For an unknown tab name it answers HTTP 200 with the FIRST
/// sheet. So a bad status is a real failure (an outage, a sheet that is no longer shared) and propagates as
/// <see cref="HttpRequestException"/>, and a wrong tab is recognised by its header: <see cref="SheetSchemaException"/>.</para>
/// <para><b>A web page is not a tab.</b> If the Sheet stops being link-shared, Google answers HTTP 200 with a sign-in
/// page. That is recognised before any header is read, and is a <see cref="SheetAccessException"/> saying the Sheet may
/// no longer be shared, not a header mismatch blaming the tab name.</para>
/// <para><b>Overrides is optional</b> (the first run has to succeed before there is anything to override), so
/// what comes back decides: its own header is parsed; an empty body, or the header of the Movies or Shows tab
/// (Google's fallback), means the tab was not found and yields zero overrides plus a note, because dropping
/// the owner's corrections without a word is the one outcome to avoid; any other header throws. A tab that is
/// present but has only its header is healthy: zero overrides, no note.</para>
/// </remarks>
public sealed partial class GoogleSheetClient(HttpClient http)
{
    private const string MoviesTab = "Movies";
    private const string ShowsTab = "Shows";
    private const string OverridesTab = "Overrides";

    private const string TabNotFoundNote =
        "The 'Overrides' tab was not found (or was renamed), so no overrides were applied. " +
        "Add a tab named 'Overrides' with the columns Title, Runtime, TmdbId, Note.";

    /// <summary>Digits, or digits in groups of three with commas: a Number-formatted TmdbId column arrives as "1,234".</summary>
    [GeneratedRegex("^(?:[0-9]+|[0-9]{1,3}(?:,[0-9]{3})+)$", RegexOptions.CultureInvariant)]
    private static partial Regex WholeNumber();

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
    /// films by <see cref="MoviePipeline.OverrideKey(string, int)"/>, series by <see cref="TvPipeline.OverrideKey"/>.
    /// A blank or zero Runtime marks a series override, a film runtime (h:mm:ss) a film. Follow with
    /// <see cref="SheetOverrides.WithUnmatchedNotes"/> once the Movies and Shows rows are in hand.
    /// </summary>
    public async Task<SheetOverrides> FetchOverridesAsync(string sheetId, CancellationToken ct)
    {
        var rows = await GetRowsAsync(sheetId, OverridesTab, ct);

        if (rows.Count == 0
            || SheetSchema.Matches(SheetSchema.Movies, rows[0])
            || SheetSchema.Matches(SheetSchema.Shows, rows[0]))
            return new SheetOverrides(new Dictionary<string, int>(), new Dictionary<string, int>(), [TabNotFoundNote]);

        SheetSchema.Validate(OverridesTab, SheetSchema.Overrides, rows[0]); // anything else unknown throws here
        return ParseOverrides(rows);
    }

    private static SheetOverrides ParseOverrides(IReadOnlyList<string[]> rows)
    {
        var films = new Dictionary<string, OverrideEntry>(StringComparer.Ordinal);
        var series = new Dictionary<string, OverrideEntry>(StringComparer.Ordinal);
        var notes = new List<string>();

        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            var (title, runtime, idText, note) = (At(row, 0), At(row, 1), At(row, 2), At(row, 3));
            var number = i + 1; // the header is sheet row 1

            if (title.Length + runtime.Length + idText.Length + note.Length == 0) continue; // padding, not a mistake

            // One note per row: the first thing wrong. Each says what was read and what to do about it.
            var normalized = TitleNormalizer.Normalize(title);
            if (normalized.Value.Length == 0)
            {
                notes.Add($"Overrides row {number} was ignored: its Title '{title}' has no name left once any year or " +
                          "edition in brackets is removed. Type the film or series name from the Movies or Shows tab " +
                          "in the Title column.");
                continue;
            }

            if (!TryParseTmdbId(idText, out var id))
            {
                notes.Add($"Overrides row {number} ('{title}') was ignored: TmdbId '{idText}' is not a whole number " +
                          "above zero. Paste only the number from the TMDB page address, such as 948.");
                continue;
            }

            var isSeries = IsBlankOrZero(runtime);
            string key;
            if (isSeries)
                key = TvPipeline.OverrideKey(title);
            else if (SheetOverrides.Minutes(runtime) is { } minutes)
                key = MoviePipeline.OverrideKey(normalized, minutes);
            else
            {
                notes.Add($"Overrides row {number} ('{title}') was ignored: Runtime '{runtime}' is not a film runtime " +
                          "written h:mm:ss, such as 1:31:00. Copy it from the Movies tab, or leave it blank for a series.");
                continue;
            }

            var map = isSeries ? series : films;
            if (map.TryGetValue(key, out var earlier) && earlier.TmdbId != id)
                notes.Add($"Overrides rows {earlier.RowNumber} and {number} ('{earlier.Title}' and '{title}') name the same " +
                          $"{(isSeries ? "series" : "film (same title, year and runtime to the minute)")} but give different " +
                          $"TmdbIds ({earlier.TmdbId} and {id}). The later row, row {number}, was used. Delete the wrong one.");
            map[key] = new OverrideEntry(number, title, runtime, isSeries, key, id);
        }

        return new SheetOverrides(
            films.ToDictionary(kv => kv.Key, kv => kv.Value.TmdbId, StringComparer.Ordinal),
            series.ToDictionary(kv => kv.Key, kv => kv.Value.TmdbId, StringComparer.Ordinal),
            notes)
        { Entries = [.. films.Values, .. series.Values] };
    }

    private static bool TryParseTmdbId(string text, out int id)
    {
        id = 0;
        return WholeNumber().IsMatch(text)
               && int.TryParse(text.Replace(",", string.Empty), NumberStyles.None, CultureInfo.InvariantCulture, out id)
               && id > 0;
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

    /// <summary>
    /// The tab's CSV rows, header first. A body with nothing in any cell comes back as no rows at all. A web page
    /// where the CSV should be is a <see cref="SheetAccessException"/>.
    /// </summary>
    private async Task<IReadOnlyList<string[]>> GetRowsAsync(string sheetId, string tab, CancellationToken ct)
    {
        using var res = await http.GetAsync(Url(sheetId, tab), ct);
        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadAsStringAsync(ct);
        if (IsWebPage(res.Content.Headers.ContentType?.MediaType, body))
            throw new SheetAccessException(
                $"Google returned a web page instead of CSV for the '{tab}' tab. The Sheet may no longer be shared as " +
                "'Anyone with the link can view', or Google is asking for a sign-in. Open the Sheet in a private " +
                "browser window to check, then share it again.");

        var rows = CsvParser.Parse(body);
        return rows.All(r => r.All(string.IsNullOrWhiteSpace)) ? [] : rows;
    }

    /// <summary>
    /// HTML by its declared type, or by its first real character: CSV from this sheet starts with a quote or a
    /// letter, never a tag. Whitespace and a BOM before it do not hide it.
    /// </summary>
    private static bool IsWebPage(string? mediaType, string body)
    {
        if (string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase)) return true;

        var i = 0;
        while (i < body.Length && (char.IsWhiteSpace(body[i]) || body[i] == '\uFEFF')) i++;
        return i < body.Length && body[i] == '<';
    }

    private static string At(string[] row, int i) => i < row.Length ? row[i].Trim() : string.Empty;
}

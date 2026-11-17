using MovieLibrary.Core.Models;
using MovieLibrary.Core.Sheets;
using MovieLibrary.Core.Text;

namespace MovieLibrary.Enrich.Sheets;

/// <summary>
/// Reads the two tabs of the link-shared Google Sheet over the anonymous CSV endpoint. No credential is
/// involved, and none is sent. Parsing and header validation live in Core (<see cref="CsvParser"/>, <see cref="SheetSchema"/>).
/// </summary>
/// <remarks>
/// <para><b>Google never reports a missing tab.</b> For an unknown tab name it answers HTTP 200 with the FIRST
/// sheet. So a bad status is a real failure (an outage, a sheet that is no longer shared) and propagates as
/// <see cref="HttpRequestException"/>, and a wrong tab is recognised by its header: <see cref="SheetSchemaException"/>.</para>
/// <para><b>A web page is not a tab.</b> If the Sheet stops being link-shared, Google answers HTTP 200 with a sign-in
/// page. That is recognised before any header is read, and is a <see cref="SheetAccessException"/> saying the Sheet may
/// no longer be shared, not a header mismatch blaming the tab name.</para>
/// </remarks>
public sealed class GoogleSheetClient(HttpClient http)
{
    private const string MoviesTab = "Movies";
    private const string ShowsTab = "Shows";

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

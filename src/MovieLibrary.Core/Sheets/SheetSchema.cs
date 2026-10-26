namespace MovieLibrary.Core.Sheets;

public sealed class SheetSchemaException(string message) : Exception(message);

public static class SheetSchema
{
    public static IReadOnlyList<string> Movies { get; } =
        ["Title", "Runtime", "Directors", "PurchaseInfo", "Genres"];

    public static IReadOnlyList<string> Shows { get; } =
        ["Title", "Runtime", "Series", "PurchaseInfo", "Genre"];

    public static IReadOnlyList<string> Overrides { get; } =
        ["Title", "Runtime", "TmdbId", "Note"];

    // Trim() does not strip U+FEFF, so a BOM on the first header cell would abort every import.
    private static string Clean(string cell) => cell.Trim().TrimStart('\uFEFF').Trim();

    private static bool Same(string expected, string actual)
        => string.Equals(expected, Clean(actual), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether <paramref name="actualHeader"/> is this tab's own header, by exactly the rules of
    /// <see cref="Validate"/>, without an exception per miss. For a caller that expects to be told "no": telling
    /// a tab from the first sheet Google falls back to is a miss on a normal run, not an error.
    /// </summary>
    public static bool Matches(IReadOnlyList<string> expected, string[] actualHeader)
    {
        if (actualHeader.Length < expected.Count) return false;

        for (var i = 0; i < expected.Count; i++)
            if (!Same(expected[i], actualHeader[i])) return false;

        return true;
    }

    /// <summary>
    /// Expected headers must appear as a prefix of the actual row; trailing
    /// unnamed columns are tolerated because the real export has one.
    /// </summary>
    public static void Validate(string tabName, IReadOnlyList<string> expected, string[] actualHeader)
    {
        if (actualHeader.Length < expected.Count)
            throw new SheetSchemaException(
                $"Tab '{tabName}' returned {actualHeader.Length} columns, expected at least " +
                $"{expected.Count}: [{string.Join(", ", expected)}].");

        for (var i = 0; i < expected.Count; i++)
        {
            if (!Same(expected[i], actualHeader[i]))
                throw new SheetSchemaException(
                    $"Tab '{tabName}' header mismatch at column {i + 1}: expected '{expected[i]}', " +
                    $"got '{actualHeader[i]}'. Google silently returns the first sheet when a tab " +
                    $"name is unknown — check the tab is still named '{tabName}'.");
        }
    }
}

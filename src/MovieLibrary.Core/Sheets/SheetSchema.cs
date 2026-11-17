namespace MovieLibrary.Core.Sheets;

public sealed class SheetSchemaException(string message) : Exception(message);

public static class SheetSchema
{
    public static IReadOnlyList<string> Movies { get; } =
        ["Title", "Runtime", "Directors", "PurchaseInfo", "Genres"];

    public static IReadOnlyList<string> Shows { get; } =
        ["Title", "Runtime", "Series", "PurchaseInfo", "Genre"];

    // Trim() does not strip U+FEFF, so a BOM on the first header cell would abort every import.
    private static string Clean(string cell) => cell.Trim().TrimStart('\uFEFF').Trim();

    private static bool Same(string expected, string actual)
        => string.Equals(expected, Clean(actual), StringComparison.OrdinalIgnoreCase);

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

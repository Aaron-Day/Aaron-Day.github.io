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
            if (!string.Equals(expected[i], actualHeader[i].Trim(), StringComparison.OrdinalIgnoreCase))
                throw new SheetSchemaException(
                    $"Tab '{tabName}' header mismatch at column {i + 1}: expected '{expected[i]}', " +
                    $"got '{actualHeader[i]}'. Google silently returns the first sheet when a tab " +
                    $"name is unknown — check the tab is still named '{tabName}'.");
        }
    }
}

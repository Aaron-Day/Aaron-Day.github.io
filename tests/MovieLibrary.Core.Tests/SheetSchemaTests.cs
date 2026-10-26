using MovieLibrary.Core.Sheets;

namespace MovieLibrary.Core.Tests;

public class SheetSchemaTests
{
    [Fact]
    public void AcceptsRealHeaderWithTrailingUnnamedColumn()
    {
        // The live Movies tab really does end with an empty sixth column.
        string[] actual = ["Title", "Runtime", "Directors", "PurchaseInfo", "Genres", ""];
        SheetSchema.Validate("Movies", SheetSchema.Movies, actual);
    }

    [Fact]
    public void RejectsWrongTabSilentlyReturnedByGoogle()
    {
        // Asking for "Shows" but receiving the Movies sheet must fail loudly.
        string[] moviesHeader = ["Title", "Runtime", "Directors", "PurchaseInfo", "Genres", ""];
        var ex = Assert.Throws<SheetSchemaException>(
            () => SheetSchema.Validate("Shows", SheetSchema.Shows, moviesHeader));
        Assert.Contains("Shows", ex.Message);
        Assert.Contains("Series", ex.Message);
    }

    [Fact]
    public void ToleratesAUtf8BomOnTheFirstHeaderCell()
    {
        string[] withBom = ["\uFEFFTitle", "Runtime", "Directors", "PurchaseInfo", "Genres", ""];
        SheetSchema.Validate("Movies", SheetSchema.Movies, withBom);   // must not throw
    }

    [Fact]
    public void RejectsTruncatedHeader()
        => Assert.Throws<SheetSchemaException>(
            () => SheetSchema.Validate("Overrides", SheetSchema.Overrides, ["Title", "Runtime"]));

    [Fact]
    public void WrongTabMessageExplainsTheGoogleFallback()
    {
        // The hint is the whole point of this exception; without this assertion it
        // could be deleted and every existing test would still pass.
        string[] moviesHeader = ["Title", "Runtime", "Directors", "PurchaseInfo", "Genres", ""];
        var ex = Assert.Throws<SheetSchemaException>(
            () => SheetSchema.Validate("Shows", SheetSchema.Shows, moviesHeader));
        Assert.Contains("first sheet", ex.Message);
    }

    // ---- Matches: the same rules as Validate, without an exception per miss ---------------------------

    private static readonly string[][] Headers =
    [
        ["Title", "Runtime", "Directors", "PurchaseInfo", "Genres", ""],
        ["Title", "Runtime", "Series", "PurchaseInfo", "Genre", ""],
        ["Title", "Runtime", "TmdbId", "Note"],
        ["\uFEFFTitle", "Runtime", "TmdbId", "Note"],
        ["title", "RUNTIME", "tmdbid", "note"],
        ["Title", "Runtime", "TmdbId", "Note", "", "stray"],
        ["Title", "Runtime", "TmdbId"],
        ["Title", "Runtime"],
        ["Title", "Runtime", "Id", "Note"],
        ["", "Runtime", "TmdbId", "Note"],
        ["Note", "TmdbId", "Runtime", "Title"],
        [],
    ];

    [Fact]
    public void MatchesAgreesWithValidateForEveryTabAndEveryHeader()
    {
        foreach (var expected in new[] { SheetSchema.Movies, SheetSchema.Shows, SheetSchema.Overrides })
            foreach (var header in Headers)
            {
                var validates = true;
                try { SheetSchema.Validate("tab", expected, header); }
                catch (SheetSchemaException) { validates = false; }

                Assert.Equal(validates, SheetSchema.Matches(expected, header));
            }
    }

    [Fact]
    public void MatchesAcceptsATrailingUnnamedColumnABomAndAnyCase()
    {
        Assert.True(SheetSchema.Matches(SheetSchema.Movies, Headers[0]));
        Assert.True(SheetSchema.Matches(SheetSchema.Overrides, Headers[3]));   // BOM
        Assert.True(SheetSchema.Matches(SheetSchema.Overrides, Headers[4]));   // case
        Assert.True(SheetSchema.Matches(SheetSchema.Overrides, Headers[5]));   // extra columns
    }

    [Fact]
    public void MatchesRejectsAShortHeaderAWrongColumnAMissingFirstCellAndAReorder()
    {
        Assert.False(SheetSchema.Matches(SheetSchema.Overrides, Headers[6]));
        Assert.False(SheetSchema.Matches(SheetSchema.Overrides, Headers[8]));
        Assert.False(SheetSchema.Matches(SheetSchema.Overrides, Headers[9]));
        Assert.False(SheetSchema.Matches(SheetSchema.Overrides, Headers[10]));
        Assert.False(SheetSchema.Matches(SheetSchema.Overrides, Headers[11]));   // no header at all
    }

    [Fact]
    public void MatchesTellsTheTabsApartWithoutThrowing()
    {
        Assert.False(SheetSchema.Matches(SheetSchema.Overrides, Headers[0]));   // Google's fallback to the Movies sheet
        Assert.False(SheetSchema.Matches(SheetSchema.Shows, Headers[0]));
        Assert.False(SheetSchema.Matches(SheetSchema.Movies, Headers[1]));
        Assert.False(SheetSchema.Matches(SheetSchema.Movies, Headers[2]));
    }
}

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
}

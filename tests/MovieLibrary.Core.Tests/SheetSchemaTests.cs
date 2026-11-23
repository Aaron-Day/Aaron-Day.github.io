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
            () => SheetSchema.Validate("Movies", SheetSchema.Movies, ["Title", "Runtime"]));

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

    // ---- shapes that only the retired tab's Matches cases used to reach ------------------------------
    // Matches went with the Overrides fetch that was its only caller. These were real Validate rules all
    // along, asserted only through Matches agreeing with it, so they are stated against Validate here.

    [Fact]
    public void AcceptsAHeaderInAnyCase()
        => SheetSchema.Validate("Movies", SheetSchema.Movies,
                                ["title", "RUNTIME", "directors", "purchaseinfo", "GENRES"]);

    [Fact]
    public void AcceptsStrayColumnsAfterTheExpectedOnes()
        => SheetSchema.Validate("Shows", SheetSchema.Shows,
                                ["Title", "Runtime", "Series", "PurchaseInfo", "Genre", "", "stray"]);

    [Fact]
    public void RejectsAReorderedHeader()
        => Assert.Throws<SheetSchemaException>(
            () => SheetSchema.Validate("Movies", SheetSchema.Movies,
                                       ["Genres", "PurchaseInfo", "Directors", "Runtime", "Title"]));

    [Fact]
    public void RejectsABlankFirstCell()
        => Assert.Throws<SheetSchemaException>(
            () => SheetSchema.Validate("Movies", SheetSchema.Movies,
                                       ["", "Runtime", "Directors", "PurchaseInfo", "Genres"]));

    [Fact]
    public void RejectsAnEmptyHeader()
        => Assert.Throws<SheetSchemaException>(
            () => SheetSchema.Validate("Movies", SheetSchema.Movies, []));

    [Fact]
    public void TheTooFewColumnsMessageCarriesTheRenamedTabHintToo()
    {
        var ex = Assert.Throws<SheetSchemaException>(
            () => SheetSchema.Validate("Movies", SheetSchema.Movies, ["Title", "Runtime"]));

        // The header-mismatch path has always said this. A tab name Google cannot find returns the
        // WRONG sheet, and a wrong sheet is as likely to be too narrow as to be misnamed -- this is
        // the path that fires in the first case, so it needs the same hint.
        Assert.Contains("still named 'Movies'", ex.Message, StringComparison.Ordinal);
    }
}

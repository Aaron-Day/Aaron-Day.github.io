using MovieLibrary.Core.Text;

namespace MovieLibrary.Core.Tests;

public class CsvParserTests
{
    [Fact]
    public void ParsesQuotedFieldContainingCommas()
    {
        var rows = CsvParser.Parse("\"Season 1, Episode 1: Truth Be Told\",\"1:06:19\",\"Alias\"\n");
        Assert.Single(rows);
        Assert.Equal("Season 1, Episode 1: Truth Be Told", rows[0][0]);
        Assert.Equal("Alias", rows[0][2]);
    }

    [Fact]
    public void ParsesDoubledQuotesAsLiteralQuote()
        => Assert.Equal("He said \"hi\"", CsvParser.Parse("\"He said \"\"hi\"\"\"\n")[0][0]);

    [Fact]
    public void ParsesEmbeddedNewlineInsideQuotes()
    {
        var rows = CsvParser.Parse("\"line one\nline two\",\"x\"\n");
        Assert.Single(rows);
        Assert.Equal("line one\nline two", rows[0][0]);
    }

    [Fact]
    public void PreservesEmptyTrailingField()
        => Assert.Equal(["a", "b", ""], CsvParser.Parse("\"a\",\"b\",\"\"\n")[0]);

    [Fact]
    public void HandlesCrLfAndIgnoresTrailingBlankLine()
        => Assert.Equal(2, CsvParser.Parse("\"a\"\r\n\"b\"\r\n").Count);
}

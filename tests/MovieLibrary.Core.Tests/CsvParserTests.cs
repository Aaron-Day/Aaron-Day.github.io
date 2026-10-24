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
    {
        var rows = CsvParser.Parse("\"a\"\r\n\"b\"\r\n");
        Assert.Equal(2, rows.Count);
        Assert.Equal("a", rows[0][0]);   // no stray carriage return
        Assert.Equal("b", rows[1][0]);
    }

    [Fact]
    public void QuotedEmptySingleFieldIsARowWithOrWithoutATrailingNewline()
    {
        Assert.Single(CsvParser.Parse("\"\"\n"));
        Assert.Single(CsvParser.Parse("\"\""));
    }

    [Fact]
    public void ParsesUnquotedFields()
    {
        // The `default` arm had no coverage at all: Google quotes every field today,
        // but a mutation dropping characters here survived the entire suite.
        var rows = CsvParser.Parse("a,b\n");
        Assert.Single(rows);
        Assert.Equal(["a", "b"], rows[0]);
    }

    [Fact]
    public void SkipsBlankLinesBetweenRows()
        => Assert.Equal(2, CsvParser.Parse("a,b\n\nc,d\n").Count);

    [Fact]
    public void ParsesAFinalRowWithNoTrailingNewline()
    {
        Assert.Equal(["a", "b"], CsvParser.Parse("a,b")[0]);
        Assert.Equal(["a", ""], CsvParser.Parse("a,")[0]);
    }

    [Fact]
    public void PreservesCrLfInsideAQuotedField()
        => Assert.Equal("line one\r\nline two",
                        CsvParser.Parse("\"line one\r\nline two\",\"x\"\n")[0][0]);
}

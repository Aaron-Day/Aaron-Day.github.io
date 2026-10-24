using MovieLibrary.Core.Parsing;
using MovieLibrary.Core.Text;

namespace MovieLibrary.Core.Tests;

public class EpisodeTitleParserTests
{
    [Fact]
    public void ParsesNumberedEpisode()
    {
        var r = EpisodeTitleParser.Parse("Season 3, Episode 7: Prelude");
        Assert.Equal(3, r.Season);
        Assert.Equal(7, r.Episode);
        Assert.Equal("Prelude", r.Name);
    }

    [Fact]
    public void ParsesMultiDigitNumbers()
    {
        var r = EpisodeTitleParser.Parse("Season 12, Episode 104: Almost Thirty Years");
        Assert.Equal(12, r.Season);
        Assert.Equal(104, r.Episode);
    }

    [Fact]
    public void ParsesNameContainingColons()
        => Assert.Equal("The Box Pt. 1: Reprise",
            EpisodeTitleParser.Parse("Season 1, Episode 12: The Box Pt. 1: Reprise").Name);

    [Theory]
    [InlineData("King Me")]
    [InlineData("Poll Position")]
    [InlineData("Enter the Fanaloka")]
    public void BareNamesHaveNoNumbers(string raw)
    {
        var r = EpisodeTitleParser.Parse(raw);
        Assert.Null(r.Season);
        Assert.Null(r.Episode);
        Assert.Equal(raw, r.Name);
    }

    [Theory]
    [InlineData("The Simpsons", "the-simpsons")]
    [InlineData("NCIS: Los Angeles", "ncis-los-angeles")]
    [InlineData("Marvel's Agents of S.H.I.E.L.D.", "marvel-s-agents-of-s-h-i-e-l-d")]
    public void SlugsAreFilenameSafe(string raw, string expected)
        => Assert.Equal(expected, Slug.From(raw));
}

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
    [InlineData("Season 1, Episode 1, Pilot", 1, 1, "Pilot")]              // comma, not colon
    [InlineData("Season 2 , Episode 1 : Fear and Loathing", 2, 1, "Fear and Loathing")]
    public void ParsesCommaSeparatedAndLooselySpacedForms(
        string raw, int season, int episode, string name)
    {
        var r = EpisodeTitleParser.Parse(raw);
        Assert.Equal(season, r.Season);
        Assert.Equal(episode, r.Episode);
        Assert.Equal(name, r.Name);
    }

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
    [InlineData("Amélie", "amelie")]
    [InlineData("Law & Order", "law-and-order")]
    public void SlugsAreFilenameSafe(string raw, string expected)
        => Assert.Equal(expected, Slug.From(raw));

    [Fact]
    public void YearDisambiguatesOtherwiseIdenticalSeriesSlugs()
    {
        Assert.Equal("the-biggest-loser", Slug.From("The Biggest Loser"));
        Assert.Equal("the-biggest-loser-2020", Slug.From("The Biggest Loser (2020)"));
    }

    /// <remarks>
    /// Real rows from the export. They are the cases most likely to break if the regex is
    /// loosened: dropping the ^ anchor, or allowing "Season N: Episode M". Both must stay bare.
    /// </remarks>
    [Theory]
    [InlineData("Bonus: Deleted Scene: Choose to Live")]
    [InlineData("Season 4: DC's Legends of Tomorrow: Trailer")]
    [InlineData("Season 1, Episode 1")]
    public void NearMissesStayBare(string raw)
    {
        var r = EpisodeTitleParser.Parse(raw);
        Assert.Null(r.Season);
        Assert.Null(r.Episode);
    }

    [Fact]
    public void PrefixMatchingIsCaseInsensitive()
    {
        var r = EpisodeTitleParser.Parse("SEASON 1, EPISODE 1: Pilot");
        Assert.Equal(1, r.Season);
        Assert.Equal("Pilot", r.Name);
    }
}

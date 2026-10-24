using MovieLibrary.Core.Text;

namespace MovieLibrary.Core.Tests;

public class TitleNormalizerTests
{
    [Theory]
    [InlineData("The A-Team", "the a team")]
    [InlineData("Airplane!", "airplane")]
    [InlineData("Angels & Demons", "angels and demons")]
    [InlineData("Façade", "facade")]
    [InlineData("American Pie 2 (Unrated)", "american pie 2")]
    [InlineData("The Abyss (Special Edition)", "the abyss")]
    [InlineData("American Psycho (Uncut Version)", "american psycho")]
    [InlineData("Anchorman 2: The Legend Continues (Unrated)", "anchorman 2 the legend continues")]
    public void StripsEditionsAndFoldsPunctuation(string raw, string expected)
        => Assert.Equal(expected, TitleNormalizer.Normalize(raw).Value);

    [Theory]
    [InlineData("The Accountant (2016)", "the accountant", 2016)]
    [InlineData("Anastasia (1997)", "anastasia", 1997)]
    [InlineData("Apollo 11 (2019)", "apollo 11", 2019)]
    public void ExtractsYearHint(string raw, string value, int year)
    {
        var r = TitleNormalizer.Normalize(raw);
        Assert.Equal(value, r.Value);
        Assert.Equal(year, r.YearHint);
    }

    [Fact]
    public void NoYearHintWhenAbsent()
        => Assert.Null(TitleNormalizer.Normalize("Alien").YearHint);

    [Fact]
    public void DirectorsAreOrderIndependentAndCommaTolerant()
    {
        // Real row: "Kevin Allen & Allen, Kevin" is one person written twice.
        Assert.Equal("allen kevin", TitleNormalizer.NormalizeDirectors("Kevin Allen & Allen, Kevin"));
        Assert.Equal(
            TitleNormalizer.NormalizeDirectors("Joel Coen & Ethan Coen"),
            TitleNormalizer.NormalizeDirectors("Ethan Coen & Joel Coen"));
    }

    [Fact]
    public void NameTokensSupportSubsetMatching()
    {
        var bag = TitleNormalizer.NameTokens("David Zucker, Jerry Zucker & Jim Abrahams");
        Assert.True(TitleNormalizer.NameTokens("Jim Abrahams").IsSubsetOf(bag));
        Assert.False(TitleNormalizer.NameTokens("Ridley Scott").IsSubsetOf(bag));
    }
}

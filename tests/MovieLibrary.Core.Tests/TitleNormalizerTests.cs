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
    [InlineData("Foo (Extended-Cut)", "foo")]     // hyphen-joined: pins Fold() inside IsEdition
    [InlineData("Foo (Unrated, Uncut)", "foo")]   // comma-joined
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

    [Theory]
    [InlineData("Foo (2016) (Unrated)", "foo", 2016)]   // peel loop runs more than once
    [InlineData("Foo (Unrated) (2016)", "foo", 2016)]   // either order
    public void PeelsMultipleTrailingParentheticals(string raw, string value, int year)
    {
        var r = TitleNormalizer.Normalize(raw);
        Assert.Equal(value, r.Value);
        Assert.Equal(year, r.YearHint);
    }

    [Fact]
    public void KeepsAParentheticalThatIsNeitherYearNorEdition()
    {
        var r = TitleNormalizer.Normalize("Pi (Sequel)");
        Assert.Equal("pi sequel", r.Value);
        Assert.Equal("Pi (Sequel)", r.Cleaned);
        Assert.Null(r.YearHint);
    }

    [Fact]
    public void CleanedKeepsPunctuationAndCaseForSearching()
    {
        var r = TitleNormalizer.Normalize("Ocean's Eleven (Unrated)");
        Assert.Equal("Ocean's Eleven", r.Cleaned);
        Assert.Equal("ocean s eleven", r.Value);
    }

    [Theory]
    [InlineData("Foo (1869)", "foo 1869", null)]   // below the 1870 bound: not a year
    [InlineData("Foo (1870)", "foo", 1870)]        // on the bound: is a year
    [InlineData("Foo (2200)", "foo", 2200)]        // upper bound: is a year
    [InlineData("Foo (2201)", "foo 2201", null)]   // above it: not a year
    [InlineData("Foo (02016)", "foo 02016", null)] // 5 chars: pins the Length == 4 guard
    public void YearBoundsArePinned(string raw, string value, int? year)
    {
        var r = TitleNormalizer.Normalize(raw);
        Assert.Equal(value, r.Value);
        Assert.Equal(year, r.YearHint);
    }

    [Theory]
    [InlineData("Foo (Shortcut)", "foo shortcut")]   // "cut" is a substring, not a word
    [InlineData("Foo (Calcutta)", "foo calcutta")]   // likewise
    [InlineData("Foo (Subversion)", "foo subversion")] // "version" is a substring, not a word
    public void DoesNotStripParentheticalsThatMerelyContainAnEditionWord(string raw, string expected)
        => Assert.Equal(expected, TitleNormalizer.Normalize(raw).Value);
}

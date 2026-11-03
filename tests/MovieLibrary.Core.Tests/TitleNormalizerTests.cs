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
    [InlineData("I Am Legend (Alternate Ending)", "i am legend")]
    [InlineData("Clerks (Uncensored)", "clerks")]
    public void StripsEditionsAndFoldsPunctuation(string raw, string expected)
        => Assert.Equal(expected, TitleNormalizer.Normalize(raw).Value);

    /// <remarks>
    /// Apple writes an edition after a colon as readily as in brackets, and the query and the identity key
    /// carried the whole of it: "Scary Movie 5: Unrated Version" was searched for verbatim, found nothing, and
    /// was cached as a film TMDB does not have. All five of these are the owner's own rows.
    /// </remarks>
    [Theory]
    [InlineData("Scary Movie 5: Unrated Version", "scary movie 5")]
    [InlineData("Donnie Darko: Anniversary Special Edition", "donnie darko")]
    [InlineData("The Exorcist: Extended Director's Cut", "the exorcist")]
    [InlineData("How the Grinch Stole Christmas: The Ultimate Edition", "how the grinch stole christmas")]
    [InlineData("National Lampoon's Van Wilder: The Unrated Version", "national lampoon s van wilder")]
    public void StripsAnEditionWrittenAfterAColon(string raw, string expected)
        => Assert.Equal(expected, TitleNormalizer.Normalize(raw).Value);

    /// <remarks>
    /// The suffix peel is the loose one - a parenthetical is already marked off by its brackets, a subtitle is
    /// not - so it needs EVERY word to be an edition word or filler. One ordinary word keeps the subtitle.
    /// Blade Runner is the case that matters: "The Final Cut" reads like an edition and is how TMDB titles the
    /// film, so stripping it would lose the match it looks like it would win.
    /// </remarks>
    [Theory]
    [InlineData("Star Wars: A New Hope", "star wars a new hope")]
    [InlineData("Captain America: The First Avenger", "captain america the first avenger")]
    [InlineData("Blade Runner: The Final Cut", "blade runner the final cut")]
    [InlineData("Dracula II: Ascension", "dracula ii ascension")]
    [InlineData("Halo 4: Forward Unto Dawn", "halo 4 forward unto dawn")]
    public void KeepsASubtitleThatIsNotAnEditionPhrase(string raw, string expected)
        => Assert.Equal(expected, TitleNormalizer.Normalize(raw).Value);

    /// <remarks>
    /// Apple's export brackets a re-release year with square brackets and the edition with round ones, in that
    /// order. Reading only the round pair left "friday the 13th uncut version 1980" as the search query and as
    /// the identity key, so the film could not be found and the miss was cached.
    /// </remarks>
    [Theory]
    [InlineData("Friday the 13th (Uncut Version) [1980]", "friday the 13th", 1980)]
    [InlineData("Dawn of the Dead (Unrated) [2004]", "dawn of the dead", 2004)]
    [InlineData("Total Recall (Director's Cut) [2012]", "total recall", 2012)]
    [InlineData("Foo [2016]", "foo", 2016)]
    [InlineData("Foo [Unrated]", "foo", null)]
    public void PeelsSquareBracketsAsWellAsRoundOnes(string raw, string value, int? year)
    {
        var r = TitleNormalizer.Normalize(raw);
        Assert.Equal(value, r.Value);
        Assert.Equal(year, r.YearHint);
    }

    [Fact]
    public void KeepsASquareBracketThatIsNeitherYearNorEdition()
        => Assert.Equal("foo sequel", TitleNormalizer.Normalize("Foo [Sequel]").Value);

    [Fact]
    public void CleanedDropsTheEditionSuffixSoTheSearchQueryDoesToo()
    {
        var r = TitleNormalizer.Normalize("Scary Movie 5: Unrated Version");
        Assert.Equal("Scary Movie 5", r.Cleaned);
    }

    [Fact]
    public void NamePartsKeepTheOrderTheNameWasWrittenIn()
    {
        Assert.Equal(["juan", "antonio", "bayona"], TitleNormalizer.NameParts("Juan Antonio Bayona"));
        Assert.Equal(["j", "a", "bayona"], TitleNormalizer.NameParts("J. A. Bayona"));
    }

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

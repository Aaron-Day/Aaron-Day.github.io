using MovieLibrary.Core.Identity;
using MovieLibrary.Core.Text;

namespace MovieLibrary.Core.Tests;

public class IdentityKeyTests
{
    private static string Film(string title, int minutes, string directors)
        => IdentityKey.ForFilm(TitleNormalizer.Normalize(title), minutes,
                               TitleNormalizer.NormalizeDirectors(directors));

    [Fact]
    public void SameContentYieldsSameKeyRegardlessOfPosition()
        => Assert.Equal(Film("Alien", 117, "Ridley Scott"), Film("Alien", 117, "Ridley Scott"));

    [Fact]
    public void EditionSuffixDoesNotChangeTheKey()
        => Assert.Equal(Film("American Pie", 95, "Paul Weitz"),
                        Film("American Pie (Unrated)", 95, "Paul Weitz"));

    [Fact]
    public void CollidingTitlesStayDistinctByRuntime()
    {
        // The library's two Aladdins differ by 38 minutes.
        Assert.NotEqual(Film("Aladdin", 129, "Guy Ritchie"),
                        Film("Aladdin", 91, "Ron Clements & John Musker"));
    }

    [Fact]
    public void DirectorOrderDoesNotChangeTheKey()
        => Assert.Equal(Film("Airplane!", 87, "David Zucker, Jerry Zucker & Jim Abrahams"),
                        Film("Airplane!", 87, "Jim Abrahams & Jerry Zucker, David Zucker"));

    [Fact]
    public void KeyIsFortyCharLowercaseHex()
    {
        var key = Film("Alien", 117, "Ridley Scott");
        Assert.Equal(40, key.Length);
        Assert.Matches("^[0-9a-f]{40}$", key);
    }

    [Fact]
    public void SeriesKeyIgnoresCaseAndPunctuation()
        => Assert.Equal(IdentityKey.ForSeries(TitleNormalizer.Normalize("NCIS: Los Angeles")),
                        IdentityKey.ForSeries(TitleNormalizer.Normalize("ncis los angeles")));
}

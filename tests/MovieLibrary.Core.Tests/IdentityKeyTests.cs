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

    // Golden values were derived INDEPENDENTLY of this code, by hand-writing the documented
    // hash input and hashing it with two other tools:
    //   printf 'film|alien|117|ridley scott' | sha1sum
    //   python -c "import hashlib; print(hashlib.sha1(b'film|alien|117|ridley scott').hexdigest())"
    // If either golden test fails, the cache contract changed and every cached entry is
    // invalidated. Re-derive the expected value independently; do NOT paste in whatever the
    // code now produces, or this test pins nothing.
    private const string ExpectedAlienKey = "87670b0ff5ec8209179b5a6ca174aa38ea7a61e5";
    private const string ExpectedNcisLaKey = "1c1cd2a6dc513639168a1f895a50792236ae37e5";

    [Fact]
    public void FilmKeyIsAStableGoldenValue()
    {
        // Pins the exact hash input format: "film|{Value}|{runtime}|{directors}".
        var key = IdentityKey.ForFilm(
            TitleNormalizer.Normalize("Alien"), 117,
            TitleNormalizer.NormalizeDirectors("Ridley Scott"));

        Assert.Equal(40, key.Length);
        Assert.Matches("^[0-9a-f]{40}$", key);
        Assert.Equal(ExpectedAlienKey, key);
    }

    [Fact]
    public void SeriesKeyIsAStableGoldenValue()
        // Pins the exact hash input format: "series|{Value}".
        => Assert.Equal(ExpectedNcisLaKey,
                        IdentityKey.ForSeries(TitleNormalizer.Normalize("NCIS: Los Angeles")));

    [Fact]
    public void RuntimeAloneChangesTheKey()
        => Assert.NotEqual(Film("Aladdin", 129, "Guy Ritchie"), Film("Aladdin", 91, "Guy Ritchie"));

    [Fact]
    public void DirectorsAloneChangeTheKey()
        => Assert.NotEqual(Film("Aladdin", 100, "Guy Ritchie"), Film("Aladdin", 100, "Ron Clements"));

    [Fact]
    public void TitleAloneChangesTheKey()
        => Assert.NotEqual(Film("Alien", 117, "Ridley Scott"), Film("Aliens", 117, "Ridley Scott"));

    [Fact]
    public void FilmAndSeriesKeysNeverCollide()
    {
        // Identical normalized names.
        var name = TitleNormalizer.Normalize("Alien");
        Assert.NotEqual(
            IdentityKey.ForFilm(name, 117, TitleNormalizer.NormalizeDirectors("Ridley Scott")),
            IdentityKey.ForSeries(name));

        // Worst case: a series whose Value equals a film's whole hash tail. Only the
        // "film|" / "series|" prefixes keep these apart.
        var crafted = new NormalizedTitle("alien|117|ridley scott", "alien|117|ridley scott", null);
        Assert.NotEqual(
            IdentityKey.ForFilm(name, 117, TitleNormalizer.NormalizeDirectors("Ridley Scott")),
            IdentityKey.ForSeries(crafted));
    }

    [Theory]
    [InlineData("Alien", "ALIEN")]
    [InlineData("Amélie", "Amelie")]
    [InlineData("Airplane!", "Airplane")]
    [InlineData("Se7en", "Se7en.")]
    public void FilmKeyFoldsCaseAccentsAndPunctuation(string plain, string variant)
        => Assert.Equal(Film(plain, 100, "Some Director"), Film(variant, 100, "Some Director"));

    /// <remarks>
    /// Pins Encoding.UTF8 in IdentityKey. Every other golden folds to ASCII, so swapping
    /// UTF8 for ASCII or Latin1 survived the whole suite. Under ASCII a non-Latin title
    /// becomes "?" characters, and two different foreign titles would hash identically and
    /// collide in the cache. No such title exists in the current export; this is insurance.
    /// Derived independently via SHA-1 of: film|千と千尋の神隠し|125|hayao miyazaki
    /// </remarks>
    [Fact]
    public void NonLatinTitleKeyIsAStableGoldenValue()
    {
        var key = IdentityKey.ForFilm(
            new NormalizedTitle("千と千尋の神隠し", "千と千尋の神隠し", null), 125,
            TitleNormalizer.NormalizeDirectors("Hayao Miyazaki"));

        Assert.Equal("c9dd6b21e7c65d0443958432b79105ebb86c5a5a", key);
    }
}

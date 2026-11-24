using MovieLibrary.Web.Models;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// What a series' numbers turn into on screen, under the cultures this site actually renders in.
/// </summary>
/// <remarks>
/// <b>THE SEPARATOR IS THE SUBJECT.</b> A WebAssembly app formats on the visitor's own machine, so every
/// count here is rendered under whatever culture the browser reports -- and the published site's own gate
/// runs under the INVARIANT culture while this box is en-US, which is how a stray space before a percent
/// sign once failed a deploy that passed locally. The counts are quantities and follow the culture; a season
/// NUMBER is an identifier and must not, because "Season 1.001" is nonsense in any language.
/// <para>
/// The numbers are the library's real extremes, measured on 2026-11-24 rather than invented: 122 series,
/// 18,208 episodes between them, and 24 seasons on the longest-running one. The grouped cases are the ones
/// that exist to fail -- below a thousand, every culture agrees and the test would prove nothing.
/// </para>
/// </remarks>
public class ShowTextTests
{
    /// <remarks>
    /// Three cultures, in the three shapes that matter: a comma group, a full-stop group, and the invariant
    /// culture the gate and the build summary run under.
    /// </remarks>
    [Theory]
    [InlineData("en-US", "18,208 episodes", "17,901 of 18,208 episodes")]
    [InlineData("de-DE", "18.208 episodes", "17.901 of 18.208 episodes")]
    [InlineData("", "18,208 episodes", "17,901 of 18,208 episodes")]
    public void AnEpisodeCountIsGroupedTheWayTheVisitorsOwnCultureGroupsIt(
        string culture, string owned, string withTotal)
    {
        using var scope = new CultureScope(culture);

        Assert.Equal(owned, ShowText.Owned(18_208));
        Assert.Equal(withTotal, ShowText.Episodes(17_901, 18_208));
    }

    /// <remarks>
    /// A season COUNT is a quantity like any other, and no published series comes near a separator -- 24 is
    /// the longest run in the library. The grouped case is here so the rule is visible rather than
    /// accidental: if a future library ever did hold a thousand seasons, the count would group and the
    /// season NUMBER below still would not.
    /// </remarks>
    [Theory]
    [InlineData("en-US", "24 seasons", "1,024 seasons")]
    [InlineData("de-DE", "24 seasons", "1.024 seasons")]
    [InlineData("", "24 seasons", "1,024 seasons")]
    public void ASeasonCountIsGroupedAndASingleSeasonIsSingular(string culture, string real, string grouped)
    {
        using var scope = new CultureScope(culture);

        Assert.Equal(real, ShowText.Seasons(24));
        Assert.Equal(grouped, ShowText.Seasons(1_024));
        Assert.Equal("1 season", ShowText.Seasons(1));
    }

    /// <remarks>
    /// <b>The one that would be nonsense.</b> A season number is an identifier: "Season 1.024" names no
    /// season in any culture, and the same mistake is "Season 1,024" in en-US. No library will hold a
    /// four-figure season, which is exactly why a test is the only thing standing between this format and a
    /// well-meant change to N0 for consistency with the counts beside it.
    /// </remarks>
    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("")]
    public void ASeasonNumberIsAnIdentifierAndIsNeverGrouped(string culture)
    {
        using var scope = new CultureScope(culture);

        Assert.StartsWith("Season 1024,", ShowText.Season(1_024, 1, 1, withTotal: true), StringComparison.Ordinal);
        Assert.StartsWith("Season 7,", ShowText.Season(7, 1, 1, withTotal: true), StringComparison.Ordinal);
    }

    /// <remarks>
    /// The whole name a reader meets on a collapsed disclosure, in both halves of the trust condition: the
    /// totals where the match is trusted and the owner's own count where it is not.
    /// </remarks>
    [Theory]
    [InlineData(true, "Season 3, 9 of 12 episodes")]
    [InlineData(false, "Season 3, 9 episodes")]
    public void ASeasonsNameCarriesItsCountsAndDropsTheTotalWhereTheMatchIsNotTrusted(
        bool trusted, string expected)
    {
        Assert.Equal(expected, ShowText.Season(3, 9, 12, trusted));
    }

    /// <remarks>
    /// <b>Season 0 is not a season and not the specials.</b> It is TvPipeline's bucket for episodes it could
    /// not place, so it is named rather than numbered -- and the name is the one place a reader learns that
    /// those episodes are in the library but nowhere in particular.
    /// </remarks>
    [Theory]
    [InlineData(true, "Season unknown, 5 of 5 episodes")]
    [InlineData(false, "Season unknown, 5 episodes")]
    public void TheUnplacedBucketIsNamedAndNotNumbered(bool trusted, string expected)
    {
        Assert.Equal(expected, ShowText.Season(0, 5, 5, trusted));
        Assert.Equal("Season unknown", ShowText.Unplaced);
    }

    /// <remarks>
    /// The singulars, which a count of one reaches in three different helpers. "1 of 1 episode" and not
    /// "1 of 1 episodes": the noun agrees with the TOTAL, because that is the quantity it names.
    /// </remarks>
    [Fact]
    public void OneEpisodeIsSingularWhereverItIsCounted()
    {
        Assert.Equal("1 episode", ShowText.Owned(1));
        Assert.Equal("1 of 1 episode", ShowText.Episodes(1, 1));
        Assert.Equal("0 of 1 episode", ShowText.Episodes(0, 1));
        Assert.Equal("1 of 2 episodes", ShowText.Episodes(1, 2));
    }
}

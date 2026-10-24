using MovieLibrary.Core.Parsing;

namespace MovieLibrary.Core.Tests;

public class RuntimeParserTests
{
    [Theory]
    [InlineData("1:59:16", 119)]   // The A-Team
    [InlineData("2:51:03", 171)]   // The Abyss
    [InlineData("1:25:44", 85)]    // Ace Ventura
    public void FilmRuntimesAreStraightHoursMinutesSeconds(string raw, int minutes)
        => Assert.Equal(minutes, RuntimeParser.FilmMinutes(raw));

    [Theory]
    [InlineData("45:37:00", 45)]   // really 45m37s, coerced to 45 hours by Sheets
    [InlineData("42:09:00", 42)]
    [InlineData("44:50:59", 44)]   // Alias S1E17 — seconds is 59, NOT 0
    [InlineData("41:22:00", 41)]
    [InlineData("23:04:00", 23)]   // pins the lower edge: most real episodes live here
    [InlineData("10:00:00", 10)]   // exactly on the >= 10 boundary
    public void CoercedEpisodeRuntimesAreMinutesAndSeconds(string raw, int minutes)
        => Assert.Equal(minutes, RuntimeParser.EpisodeMinutes(raw));

    [Fact]
    public void GenuineLongEpisodeIsNotMisread()
        => Assert.Equal(66, RuntimeParser.EpisodeMinutes("1:06:19")); // Alias pilot, a real 66 minutes

    [Theory]
    [InlineData("")]
    [InlineData("n/a")]
    [InlineData("12")]
    public void RejectsUnparseableInput(string raw)
        => Assert.Throws<FormatException>(() => RuntimeParser.FilmMinutes(raw));

    [Theory]
    [InlineData("23:04", 23)]   // All Hail King Julien - never coerced, plain mm:ss
    [InlineData("21:09", 21)]
    [InlineData("0:44", 0)]     // under a minute
    public void AcceptsTwoPartMinutesSeconds(string raw, int minutes)
        => Assert.Equal(minutes, RuntimeParser.EpisodeMinutes(raw));

    [Fact]
    public void FilmRuntimesStillRequireThreeParts()
        => Assert.Throws<FormatException>(() => RuntimeParser.FilmMinutes("23:04"));

    /// <remarks>
    /// Characterization, not an endorsement. "6:10:00" is really 6m10s ("Inside Breaking
    /// Bad"), but the 1-9 hour band is ambiguous and no heuristic is applied, so it reads
    /// as 370 minutes. If someone adds a seconds-based rule this test fails, which is the
    /// point: it forces the re-audit the EpisodeMinutes remarks ask for.
    /// </remarks>
    [Fact]
    public void AmbiguousLowHourBandIsLeftUnresolvedDeliberately()
        => Assert.Equal(370, RuntimeParser.EpisodeMinutes("6:10:00"));

    [Theory]
    [InlineData("")]
    [InlineData("n/a")]
    [InlineData("12")]
    [InlineData("a:b")]
    [InlineData("1:2:3:4")]
    public void EpisodeMinutesRejectsUnparseableInput(string raw)
        => Assert.Throws<FormatException>(() => RuntimeParser.EpisodeMinutes(raw));
}

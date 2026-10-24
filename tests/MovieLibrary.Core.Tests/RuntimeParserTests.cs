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
}

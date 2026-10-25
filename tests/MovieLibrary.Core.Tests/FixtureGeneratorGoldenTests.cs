using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MovieLibrary.Core.Models;
using MovieLibrary.Enrich.Emit;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// Byte-identical output is a project-wide constraint (the dev-data diff, and anything that compares two runs), so
/// these pin LITERAL values. Every other determinism test compares two builds inside ONE process, and a hash that
/// .NET randomises per process (string.GetHashCode) agrees with itself inside a process: a Seed built on it passes
/// all of them while the output changes on every run. Only a literal worked out beforehand can see that.
/// The per-field literals below were derived by hand from the Seed and Pick formulas (a separate Python model, not
/// by running the generator), so they also say what the formulas are; the final test pins the lot.
/// </summary>
public class FixtureGeneratorGoldenTests
{
    private static SheetRow Film(string title)
        => new(title, "1:30:00", "Some Director", "", "Comedy", "");

    [Theory]
    [InlineData("Alien", 967_770, 1981, 7.6, "Everything changes after a single, unremarkable decision.")]
    [InlineData("Airplane!", 957_275, 1994, 9.6, "The past refuses to stay buried.")]
    [InlineData("iqelaitl", 986_808, 2019, 6.5, "Everything changes after a single, unremarkable decision.")]
    [InlineData("Amélie", 957_389, 1994, 8.9, "An ordinary evening turns into the longest night of their lives.")]
    [InlineData("Ocean's Twelve", 901_215, 1982, 9.4, "An ordinary evening turns into the longest night of their lives.")]
    public void AFilmsFieldsAreTheseLiteralsInEveryProcess(string title, int id, int year, double vote, string overview)
    {
        var film = FixtureGenerator.Build([Film(title)], []).Item1.Single();

        Assert.Equal(id, film.I);
        Assert.Equal(year, film.Y);
        Assert.Equal(vote, film.Vote);
        Assert.Equal(overview, film.Ov);
    }

    [Theory]
    [InlineData("Alias", 830_777, 1999, "1999-08-05")]
    [InlineData("Frasier", 879_423, 2005, "2005-03-03")]
    [InlineData("Zulu", 829_675, 2004, "2004-10-07")]
    public void ASeriesIdFirstYearAndFirstAirDateAreTheseLiteralsInEveryProcess(string name, int id, int firstYear, string airDate)
    {
        var show = FixtureGenerator.Build([], [new SheetRow("Season 1, Episode 1: Pilot", "44:00:00", "", "", "Drama", name)])
            .Item2.Single();

        Assert.Equal(id, show.I);
        Assert.Equal(firstYear, show.FirstYear);
        Assert.Equal(airDate, show.Seasons.Single(s => s.N == 1).Eps.Single(e => e.O).Air);
    }

    private static List<SheetRow> FixedFilms()
    {
        string[] genres = ["Action & Adventure", "Comedy", "Drama", "Sci-Fi & Fantasy", "Kids & Family",
                           "Horror", "Thriller", "Documentary", "Holiday"];
        return [.. new[] { "Alien", "Airplane!", "iqelaitl", "Amélie", "Ocean's Twelve", "Aladdin", "Aladdin" }
            .Concat(Enumerable.Range(0, 80).Select(i => $"Film {i * 7 % 80}"))
            .Select((title, i) => new SheetRow(
                title, $"{1 + i % 2}:{10 + i % 50:D2}:{i % 60:D2}", i % 3 == 0 ? "A Director & B Director" : "C Director",
                i % 4 == 0 ? "Bundle" : "", genres[i % genres.Length], ""))];
    }

    private static List<SheetRow> FixedShows()
    {
        var rows = new List<SheetRow>();
        void Add(string series, string title, string runtime = "44:50:59")
            => rows.Add(new SheetRow(title, runtime, "", "", "Drama", series));

        Add("Alias", "Season 1, Episode 1: Truth Be Told");
        Add("Alias", "Season 1, Episode 2: So It Begins");
        Add("Alias", "Season 2, Episode 1: The Telling");
        Add("Zulu", "A Standalone Special", "23:04");
        Add("Zulu", "Another Special", "23:04");
        Add("Zulu", "Another Special", "23:04");
        Add("Zulu", "Season 1, Episode 1: Pilot", "23:04");
        for (var i = 0; i < 40; i++)
            for (var season = 1; season <= 1 + i % 4; season++)
                for (var e = 1; e <= 5 + i % 4; e++)
                    if ((e + season + i) % 7 != 0) Add($"Series {i}", $"Season {season}, Episode {e}: Name {i}-{season}-{e}");
        for (var i = 0; i < 40; i += 4) Add($"Series {i}", $"A Bare Extra {i}");
        for (var i = 40; i < 46; i++) Add($"Series {i}", $"Bare only {i} b");     // series with no numbered row at all
        for (var i = 40; i < 46; i++) Add($"Series {i}", $"Bare only {i} a");

        return rows;
    }

    private static string HashOfTheFixedOutput()
    {
        var (movies, shows) = FixtureGenerator.Build(FixedFilms(), FixedShows());
        var json = JsonSerializer.Serialize(movies) + "\n" + JsonSerializer.Serialize(shows);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    // Measured over 286,871 characters of JSON (48 series: numbered, bare, mixed, padded and fully owned); the same in separate processes and under every culture below.
    private const string Golden = "8b802f1a492d3ece8999be925bb0ddb891ec8e3a6497e05350be520a4a412170";

    [Fact]
    public void TheWholeOutputOfAFixedInputHasThisSha256InEveryProcess()
        => Assert.Equal(Golden, HashOfTheFixedOutput());

    [Theory]
    [InlineData("th-TH")]       // Buddhist calendar: a year is 2543 for 2000
    [InlineData("ar-SA")]       // Hijri calendar
    [InlineData("tr-TR")]       // dotless i
    [InlineData("fa-IR")]       // Persian calendar and digits
    public void TheOutputIsTheSameUnderAnyCulture(string culture)
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);

            Assert.Equal(Golden, HashOfTheFixedOutput());
        }
        finally { CultureInfo.CurrentCulture = before; }
    }
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MovieLibrary.Core.Matching;
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
        Assert.Equal(id.ToString(CultureInfo.InvariantCulture) + "-90", film.K);   // its id, then Film()'s 1:30:00 in minutes
        Assert.Equal(year, film.Y);
        Assert.Equal(vote, film.Vote);
        Assert.Equal(overview, film.Ov);
    }

    // Worked out beforehand in a separate Python model, not by running the generator: Seed("Empty Kingdom 65") puts
    // Pick(seed, SaltUnmatched, 80) at 0, and SHA-1("film|empty kingdom 65|90|director some") begins d2f1c6e2.
    [Fact]
    public void AnUnmatchedFilmHasNoIdNoConfidenceAndThisLiteralKeyInEveryProcess()
    {
        var film = FixtureGenerator.Build([Film("Empty Kingdom 65")], []).Item1.Single();

        Assert.Equal(0, film.I);
        Assert.Equal(0d, film.Mc);
        Assert.Equal("u-d2f1c6e2", film.K);

        // And nothing TMDB would have supplied, which is the shape the pipeline really publishes. Pinned here
        // as well as in FixtureGeneratorCatalogueTests because this is the one film in the project whose
        // unmatched-ness is a LITERAL rather than a draw over a catalogue: if the generator ever synthesised
        // these again, this is the test that says so without depending on a 1,457-row library.
        Assert.Null(film.Y);
        Assert.Empty(film.G);
        Assert.Empty(film.Cast);
        Assert.Null(film.Ov);
        Assert.Null(film.Tag);
        Assert.Null(film.Col);
        Assert.Equal(0d, film.Vote);

        // The Sheet's own data is untouched: Film() gives it 1:30:00, "Comedy" and "Some Director".
        Assert.Equal(90, film.Rt);
        Assert.Equal("Comedy", film.Ag);
        Assert.Equal(["Some Director"], film.Dir);
    }

    // Seed("Film 48") puts Pick(seed, SaltUncertain, 8) at 0 and Pick(seed, SaltUncertainScore, 5) at 4, so the score is
    // AcceptThreshold - 0.05 * 5 = 0.70. Worked out in the same separate model, and the band is read back out of
    // ConfidenceBand rather than compared with a literal threshold.
    [Fact]
    public void AMatchedFilmDrawnIntoTheUncertainBandScoresThisLiteralValueInEveryProcess()
    {
        var film = FixtureGenerator.Build([Film("Film 48")], []).Item1.Single();

        Assert.Equal(0.7, film.Mc);
        Assert.Equal(Confidence.Uncertain, ConfidenceBand.Of(film));
        Assert.NotEqual(0, film.I);          // a real id: the only thing separating this band from Unmatched
        Assert.NotNull(film.Y);              // it matched, so it kept every other TMDB field
        Assert.Equal(4, film.Cast.Length);
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
        return [.. new[] { "Alien", "Airplane!", "iqelaitl", "Amélie", "Ocean's Twelve", "Aladdin", "Aladdin", "Empty Kingdom 65" }
            .Concat(Enumerable.Range(0, 80).Select(i => $"Film {i * 7 % 80}"))
            .Select((title, i) => new SheetRow(
                title, $"{1 + i % 2}:{10 + i % 50:D2}:{i % 60:D2}", i % 3 == 0 ? "A Director & B Director" : "C Director",
                i % 4 == 0 ? "Bundle" : "", genres[i % genres.Length], ""))];
    }

    private static List<SheetRow> FixedShows()
    {
        var rows = new List<SheetRow>();
        void Add(string series, string title, string runtime = "44:50:59", string genre = "Drama")
            => rows.Add(new SheetRow(title, runtime, "", "", genre, series));

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

        // Bare extras beside numbered rows, in series of one to four seasons, so extras claim slots of earlier seasons
        // (ClaimSlots) and, in a one-season series, of the latest.
        for (var i = 0; i < 40; i += 3)
        {
            Add($"Series {i}", $"Extra {i} A");
            Add($"Series {i}", $"Extra {i} B");
            if (i % 2 == 0) Add($"Series {i}", $"Extra {i} C");
        }

        // Series with no numbered row at all: every row is bare (ClaimedSeasons), twelve rows each.
        for (var i = 40; i < 52; i++)
            for (var k = 0; k < 12; k++) Add($"Series {i}", $"Bare only {i} {k:D2}");

        // Rows that disagree about the series: spelling, genre and runtime. The pipeline takes the most common, ties ordinal.
        Add(" mixed signals", "Season 1, Episode 2: Two", "30:00:00", "Comedy");     // the first row is the minority view
        Add("Mixed Signals", "Season 1, Episode 1: One", "44:00:00", "Drama");
        Add("Mixed Signals", "Season 1, Episode 3: Three", "44:00:00", "Drama");
        Add("Mixed Signals", "Season 1, Episode 4: Four", "30:00:00", "Comedy");
        Add("Mixed Signals", "Season 1, Episode 5: Five", "44:00:00", "Drama");

        return rows;
    }

    private static string HashOfTheFixedOutput()
    {
        var (movies, shows) = FixtureGenerator.Build(FixedFilms(), FixedShows());
        var json = JsonSerializer.Serialize(movies) + "\n" + JsonSerializer.Serialize(shows);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    // 336,532 characters of JSON (88 films: one unmatched and six uncertain; 61 series: numbered, extras that claim slots,
    // bare-only, unlisted, disagreeing, padded, fully owned).
    // Derived, NEVER read off the generator -- read it off and this test asserts only that the generator agrees with itself.
    // Each golden has been reached by transforming a dump of the one before it:
    //   030e8dbe...f44f, 336,644 characters, K = the bare id.
    //   7c9cbfef...f481, 336,962 characters: that dump with every matched film's "K":"{id}" rewritten to "K":"{id}-{Rt}" from
    //     that film's own Rt. 87 substrings, hence +318 characters.
    //   3aaffeb4...dc55, this one: THAT dump (its SHA-256 checked against 7c9cbfef... first, so the input was proved to be the
    //     old output) with two edits applied in Python from a model of Seed and Pick, not by calling the generator.
    //     (a) the one film with Pick(seed, SaltUnmatched, 80) == 0 -- "Empty Kingdom 65" -- loses the seven fields TMDB would
    //         have supplied: "Y":null, "G":[], "Vote":0, "Ov":null, "Tag":null, "Cast":[], "Col":null.
    //     (b) the six matched films with Pick(seed, SaltUncertain, 8) == 0 get "Mc":<UncertainScore(seed)> instead of 0.95:
    //         Film 0 and Film 5 at 0.5, Film 4 and Film 72 at 0.55, Film 48 and Film 52 at 0.7.
    //     Only those substrings differ, hence -430 characters. The model was checked three ways before it was trusted: the
    //     dump hashes to the old golden, its 88 titles are the model's own reconstruction of FixedFilms(), and every "Col" in
    //     the dump is what the model's copy of the Collections rule computes -- which exercises Seed, Pick and the ordinal
    //     sort. The generator was then run and produced this byte for byte, two independent derivations agreeing.
    //   a0a91973...7af22, 339,535 characters: Task 2 added two trailing members to both records, and this hash is of the default-options
    //     serialisation, which writes nulls. Derived from a dump of the generator's output: stripping the 143 appended
    //     ",\"Srt\":null,\"Ex\":null" suffixes recovers a string that hashes to 3aaffeb4... (so the dump was proved to be the old output),
    //     and the unstripped dump hashes to this. +3,003 characters = 143 x 21.
    // The same in separate processes and under every culture below.
    private const string Golden = "a0a9197362d47ba26b5db2703bf13e518c1c5d943b027517b8804235b667af22";

    // A golden pins only what its input reaches. A hash that .NET randomises per process, used as a sort key in a branch
    // the input never takes, would pass everything: so this fails if the input stops reaching a branch.
    [Fact]
    public void TheFixedInputReachesEveryBranchTheHashIsMeantToPin()
    {
        var (films, shows) = FixtureGenerator.Build(FixedFilms(), FixedShows());
        bool IsExtra(EpisodeRecord e) => e.O && e.T.StartsWith("Extra ", StringComparison.Ordinal);
        bool IsNumbered(EpisodeRecord e) => e.O && e.T.StartsWith("Name ", StringComparison.Ordinal);
        bool IsInvented(EpisodeRecord e) => e.O && e.T.StartsWith("Bare only ", StringComparison.Ordinal);
        int Latest(ShowRecord s) => s.Seasons.Where(x => x.Eps.Any(IsNumbered)).Select(x => x.N).DefaultIfEmpty(-1).Max();

        // a bare extra claims a slot in a series that has numbered rows: in an earlier season, and in the latest one
        Assert.Contains(shows, s => s.Seasons.Any(x => x.N > 0 && x.N < Latest(s) && x.Eps.Any(IsExtra)));
        Assert.Contains(shows, s => s.Seasons.Any(x => x.N == Latest(s) && x.Eps.Any(IsExtra)));
        // ...and an extra that finds no slot stays in season 0
        Assert.Contains(shows, s => s.Seasons.Any(x => x.N == 0 && x.Eps.Any(IsExtra)));
        // an invented TMDB list holds claimed rows of a series with no numbered row, and some of its rows are unmatched
        Assert.Contains(shows, s => s.Seasons.Any(x => x.N > 0 && x.Eps.Any(IsInvented)));
        Assert.Contains(shows, s => s.Seasons.Any(x => x.N == 0 && x.Eps.Any(IsInvented)));
        // an owned numbered episode TMDB does not list, with nothing but a title
        Assert.Contains(shows, s => s.Seasons.Any(x => x.N > 0 && x.Eps.Any(e => IsNumbered(e) && e.Air is null && e.Rt is null)));
        // a film no TMDB search matched: id 0, keyed by its content hash instead, and degraded with it
        Assert.Contains(films, m => m.I == 0 && m.K.StartsWith("u-", StringComparison.Ordinal)
                                             && m.Y is null && m.Cast.Length == 0 && m.G.Length == 0);
        // ...and a matched film drawn into the uncertain band, which is a SEPARATE branch of Mc: without one
        // in the input the hash would pin nothing about UncertainScore, and the branch could be changed freely.
        Assert.Contains(films, m => ConfidenceBand.Of(m) == Confidence.Uncertain);
        Assert.Contains(films, m => ConfidenceBand.Of(m) == Confidence.Confident);
        // a series whose rows disagree
        var signals = shows.Single(s => s.T == "Mixed Signals");
        Assert.Equal("Drama", signals.Ag);
        Assert.Equal(44, signals.Seasons.Single(x => x.N == 1).Eps.First(e => e.O && e.Air is not null).Rt);
    }

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

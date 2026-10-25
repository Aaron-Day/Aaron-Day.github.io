using System.Globalization;
using System.Text.Json;
using MovieLibrary.Core.Models;
using MovieLibrary.Enrich.Emit;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// The fixtures exist so the UI can be built against a full-size catalogue before a real run, so these tests
/// work at catalogue scale (1,457 films) and pin what the UI depends on: cast, genres, collections, unique
/// ids, and no image path that could ever trigger a request.
/// </summary>
public class FixtureGeneratorCatalogueTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ml-fixtures-" + Guid.NewGuid());
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private static readonly string[] AppleGenres =
        ["Action & Adventure", "Comedy", "Drama", "Sci-Fi & Fantasy", "Kids & Family",
         "Horror", "Thriller", "Documentary", "Holiday"];

    private static readonly string[] Adjectives =
        ["Dark", "Last", "Silent", "Red", "Golden", "Broken", "Hidden", "Wild", "Iron", "Paper", "Glass",
         "Winter", "Burning", "Lost", "Secret", "Midnight", "Empty", "Crimson", "Distant", "Hollow"];

    private static readonly string[] Nouns =
        ["Road", "City", "Garden", "Letter", "Harbor", "Mirror", "Season", "Promise", "Station",
         "Crown", "Orchard", "Bridge", "Kingdom", "Echo", "Witness", "Machine", "Island", "Tower"];

    internal static SheetRow Film(string title, string genre = "Comedy", string runtime = "1:30:00",
                                 string directors = "Some Director", string purchase = "")
        => new(title, runtime, directors, purchase, genre, "");

    /// <summary>1,457 distinct, plausible titles from a fixed seed, with the Sheet's nine Apple genres.</summary>
    internal static List<SheetRow> Library(int count = 1457)
    {
        var rng = new Random(15);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<SheetRow>(count);

        while (rows.Count < count)
        {
            var title = $"{Adjectives[rng.Next(Adjectives.Length)]} {Nouns[rng.Next(Nouns.Length)]} {rng.Next(1, 500)}";
            if (!seen.Add(title)) continue;
            rows.Add(Film(title, AppleGenres[rng.Next(AppleGenres.Length)],
                          purchase: rows.Count % 3 == 0 ? "Some Bundle" : ""));
        }

        return rows;
    }

    private static IReadOnlyList<MovieRecord> Films(IReadOnlyList<SheetRow>? rows = null)
        => FixtureGenerator.Build(rows ?? Library(), []).Item1;

    // ---- real data survives --------------------------------------------------------------

    [Fact]
    public void KeepsEverySheetRowInOrderWithItsRealTitleRuntimeGenreDirectorsAndBundle()
    {
        var rows = Library();
        rows[1] = Film("Ocean's Twelve", "Thriller", "2:05:30", "Steven Soderbergh & Another Person", "  ");
        var movies = Films(rows);

        Assert.Equal(rows.Count, movies.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            Assert.Equal(rows[i].Title, movies[i].T);
            Assert.Equal(rows[i].Genre, movies[i].Ag);
            Assert.Equal(i == 1 ? 125 : 90, movies[i].Rt);
        }
        Assert.Equal(["Steven Soderbergh", "Another Person"], movies[1].Dir);
        Assert.Null(movies[1].Own);                              // whitespace-only bundle is no bundle
        Assert.Equal("Some Bundle", movies[0].Own);
    }

    [Fact]
    public void SyntheticFieldsStayInTheRangesTheUiWasDesignedFor()
    {
        Assert.All(Films(), m =>
        {
            Assert.InRange(m.Y!.Value, 1970, 2024);
            Assert.InRange(m.Vote, 4.0, 9.9);
            Assert.Equal(Math.Round(m.Vote, 1), m.Vote);
            Assert.InRange(m.I, 900_000, 999_999);
            Assert.False(string.IsNullOrWhiteSpace(m.Ov));
        });
    }

    // ---- the four corrections to the brief -----------------------------------------------

    [Fact]
    public void MatchConfidenceIsConfidentButNeverMistakableForAHumanOverride()
    {
        // 1.0 means "a human confirmed this" here; the best an algorithm can score is 0.95.
        Assert.All(Films(), m =>
        {
            Assert.Equal(0.95, m.Mc);
            Assert.NotEqual(1.0, m.Mc);
        });
    }

    [Fact]
    public void EverySyntheticFilmHasNoPosterOrBackdropSoNoImageRequestCanBeMade()
    {
        var (movies, shows) = FixtureGenerator.Build(Library(), FixtureGeneratorRealismTests.SeriesRows());

        Assert.All(movies, m => { Assert.Null(m.P); Assert.Null(m.B); });
        Assert.All(shows, s =>
        {
            Assert.Null(s.P);
            Assert.All(s.Seasons.SelectMany(x => x.Eps), e => Assert.Null(e.S));
        });
        Assert.All(movies.SelectMany(m => m.Cast), c => Assert.Null(c.P));
    }

    private static List<SheetRow> EpisodeRows(params string[] series)
        => series.SelectMany(name => new[]
        {
            new SheetRow("Season 1, Episode 1: Pilot", "44:00:00", "", "", "Drama", name),
            new SheetRow("Season 1, Episode 2: Next", "44:00:00", "", "", "Drama", name),
        }).ToList();

    // "iqelaitl" hashes to exactly int.MinValue under the generator's unchecked h * 31 + c, the one value
    // Math.Abs cannot negate. Found by search. The claim is proved through the generator, never by copying its
    // formula (a copy cannot fail when the original changes): the generator masks that hash to seed 0, and a
    // film with seed 0 gets exactly these values, worked out by hand from Seed and Pick. If the hash is ever
    // changed this title stops being the overflowing one and THIS test fails, rather than the guard rotting.
    private const string IntMinValueTitle = "iqelaitl";

    [Fact]
    public void TheOverflowingTitleStillHasSeedZeroWhichIsWhatMakesItTheOverflowGuard()
    {
        var film = Films([Film(IntMinValueTitle)]).Single();

        Assert.Equal(986_808, film.I);
        Assert.Equal(2019, film.Y);
        Assert.Equal(6.5, film.Vote);
        Assert.Equal("Everything changes after a single, unremarkable decision.", film.Ov);
    }

    [Fact]
    public void AFilmSeriesOrEpisodeWhoseHashIsIntMinValueDoesNotThrow()
    {
        var (movies, shows) = FixtureGenerator.Build(
            [Film(IntMinValueTitle)],
            [new SheetRow($"Season 1, Episode 1: {IntMinValueTitle}", "44:00:00", "", "", "Drama", IntMinValueTitle)]);

        Assert.Equal(IntMinValueTitle, Assert.Single(movies).T);
        Assert.Equal(IntMinValueTitle, Assert.Single(shows).Seasons.Single(s => s.Owned > 0).Eps.Single(e => e.O).T);
    }

    // ---- identity ------------------------------------------------------------------------

    // "em" and "je" are different titles that draw the same id offset (found by search), so the second must step
    // past the first. That is asserted through the ids themselves, so the pair cannot quietly stop colliding.
    [Fact]
    public void AFilmWhoseIdDrawCollidesStepsToTheNextFreeId()
    {
        var pair = Films([Film("em"), Film("je")]);

        Assert.Equal(960_983, pair[0].I);
        Assert.Equal(960_984, pair[1].I);
    }

    [Fact]
    public void FilmIdsAreUniqueEvenForRepeatedTitlesAndCollidingDraws()
    {
        var rows = Library();
        rows.Add(Film("em"));
        rows.Add(Film("je"));
        rows.Add(Film("Aladdin", runtime: "1:30:00"));
        rows.Add(Film("Aladdin", runtime: "2:08:00"));      // the real sheet has same-titled films

        var ids = Films(rows).Select(m => m.I).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void ShowIdsAreUniqueEvenWhenTwoSeriesNamesCollide()
    {
        var (_, shows) = FixtureGenerator.Build([], EpisodeRows("em", "je", "Alias"));

        Assert.Equal(3, shows.Select(s => s.I).Distinct().Count());
        Assert.All(shows, s => Assert.True(s.I >= 800_000));
        Assert.Equal(shows.Single(s => s.T == "em").I + 1, shows.Single(s => s.T == "je").I);   // they did collide
    }

    [Fact]
    public void AFilmWithNoCollidingNeighbourKeepsTheSameRecordWhateverElseIsInTheSheet()
    {
        var alone = Films([Film("Alien", "Horror", "1:56:45")]).Single();
        var rows = Library();
        rows.Insert(700, Film("Alien", "Horror", "1:56:45"));
        var amongOthers = Films(rows).Single(m => m.T == "Alien");

        Assert.Equal(alone.I, amongOthers.I);
        Assert.Equal(alone.Y, amongOthers.Y);
        Assert.Equal(alone.Vote, amongOthers.Vote);
        Assert.Equal(alone.G, amongOthers.G);
        Assert.Equal(alone.Cast, amongOthers.Cast);
        Assert.Equal(alone.Tag, amongOthers.Tag);
        // Not Col: a collection is a run of neighbours in title order, so a film's collection depends on the
        // sheet around it by design (see FixtureGeneratorRealismTests).
    }

    [Fact]
    public void ARepeatedTitleIsSyntheticallyTheSameFilmExceptForItsId()
    {
        var twins = Films([Film("Aladdin"), Film("Aladdin")]);

        Assert.NotEqual(twins[0].I, twins[1].I);
        // Records compare arrays by reference, so compare what would be written instead.
        Assert.Equal(JsonSerializer.Serialize(twins[0] with { I = 0 }),
                     JsonSerializer.Serialize(twins[1] with { I = 0 }));
    }

    // ---- genres --------------------------------------------------------------------------

    [Fact]
    public void TheGenreTableIsTheRealTmdbMovieListAndCannotBeChangedByACaller()
    {
        var expected = new Dictionary<int, string>
        {
            [28] = "Action", [12] = "Adventure", [16] = "Animation", [35] = "Comedy", [80] = "Crime",
            [99] = "Documentary", [18] = "Drama", [10751] = "Family", [14] = "Fantasy", [36] = "History",
            [27] = "Horror", [10402] = "Music", [9648] = "Mystery", [10749] = "Romance",
            [878] = "Science Fiction", [10770] = "TV Movie", [53] = "Thriller", [10752] = "War",
            [37] = "Western",
        };

        Assert.Equal(expected.OrderBy(e => e.Key), FixtureGenerator.Genres.OrderBy(e => e.Key));

        // A shared public static: if a caller could add to it, every later run would see the change.
        var asMutable = Assert.IsAssignableFrom<IDictionary<int, string>>(FixtureGenerator.Genres);
        Assert.Throws<NotSupportedException>(() => asMutable.Add(1, "Invented"));
    }

    [Fact]
    public void EveryFilmHasOneOrTwoDistinctGenresAndEachOneHasAName()
    {
        Assert.All(Films(), m =>
        {
            Assert.InRange(m.G.Length, 1, 2);
            Assert.Equal(m.G.Length, m.G.Distinct().Count());
            Assert.All(m.G, id => Assert.True(FixtureGenerator.Genres.ContainsKey(id), $"genre {id} has no name"));
        });
    }

    // Year, vote, overview and id used to be seed % 55, % 60, % 5 and % 90,000: 5 divides all four, so within any one
    // overview every film shared a year, a vote and an id residue. Each now has its own salt.
    [Fact]
    public void YearVoteOverviewAndIdAreIndependentOfOneAnother()
    {
        var films = Films();

        Assert.All(films.GroupBy(m => m.Ov), sameOverview =>
        {
            Assert.Equal(5, sameOverview.Select(m => m.Y!.Value % 5).Distinct().Count());
            Assert.Equal(5, sameOverview.Select(m => (int)Math.Round(m.Vote * 10) % 5).Distinct().Count());
            Assert.Equal(5, sameOverview.Select(m => m.I % 5).Distinct().Count());
        });
        Assert.Equal(25, films.Select(m => (m.Y!.Value % 5, (int)Math.Round(m.Vote * 10) % 5)).Distinct().Count());
        Assert.Equal(25, films.Select(m => (m.Y!.Value % 5, m.I % 5)).Distinct().Count());
        Assert.Equal(25, films.Select(m => ((int)Math.Round(m.Vote * 10) % 5, m.I % 5)).Distinct().Count());
    }

    [Fact]
    public void ASeriesFirstYearAndIdAreIndependentOfOneAnother()
    {
        var rows = Library(600).Select(r => new SheetRow("Season 1, Episode 1: Pilot", "44:00:00", "", "", "Drama", r.Title)).ToList();
        var shows = FixtureGenerator.Build([], rows).Item2;

        Assert.Equal(25, shows.Select(s => (s.FirstYear!.Value % 5, s.I % 5)).Distinct().Count());
    }

    [Fact]
    public void BothSingleAndDoubleGenreFilmsOccurSoTheFacetSeesFilmsInTwoBuckets()
    {
        var films = Films();

        Assert.Contains(films, m => m.G.Length == 1);
        Assert.Contains(films, m => m.G.Length == 2);
    }

    [Fact]
    public void ASingleNamedGenreGainsASecondOneAboutHalfTheTime()
    {
        var rows = Library(400).Select(r => r with { Genre = "Comedy" }).ToList();
        var films = Films(rows);

        // Half draw a second genre; a few of those redraw the first and stay single.
        Assert.InRange(films.Count(m => m.G.Length == 2) / (double)films.Count, 0.40, 0.55);
        Assert.Contains(films, m => m.G.Length == 1);
    }

    [Theory]
    [InlineData("Comedy", new[] { 35 })]
    [InlineData("Drama", new[] { 18 })]
    [InlineData("Horror", new[] { 27 })]
    [InlineData("Thriller", new[] { 53 })]
    [InlineData("Documentary", new[] { 99 })]
    [InlineData("Kids & Family", new[] { 10751 })]
    [InlineData("Action & Adventure", new[] { 28, 12 })]
    [InlineData("Sci-Fi & Fantasy", new[] { 878, 14 })]
    [InlineData("comedy", new[] { 35 })]
    [InlineData("  Sci-Fi  &  fantasy ", new[] { 878, 14 })]
    [InlineData("Comedy & Comedy", new[] { 35 })]
    [InlineData("Action & Adventure & Comedy", new[] { 28, 12 })]
    public void GenresFollowTheAppleGenreByNameSoTheFacetAgreesWithAg(string apple, int[] expected)
    {
        var rows = Library(80).Select(r => r with { Genre = apple }).ToList();

        Assert.All(Films(rows), m =>
        {
            Assert.Equal(expected, m.G.Take(expected.Length));
            Assert.Equal(m.G.Length, m.G.Distinct().Count());
            Assert.InRange(m.G.Length, expected.Length, 2);
        });
    }

    [Theory]
    [InlineData("Holiday")]
    [InlineData("")]
    [InlineData("Something Apple Invents Next Year")]
    public void AnAppleGenreWithNoTmdbNameStillGetsGenresDrawnFromTheSeed(string apple)
    {
        var rows = Library(200).Select(r => r with { Genre = apple }).ToList();
        var films = Films(rows);

        Assert.All(films, m =>
        {
            Assert.InRange(m.G.Length, 1, 2);
            Assert.All(m.G, id => Assert.True(FixtureGenerator.Genres.ContainsKey(id)));
        });
        Assert.True(films.Select(m => m.G[0]).Distinct().Count() >= 12,
                    "the seeded primary genre should spread across most of the table");
    }

    // ---- cast ----------------------------------------------------------------------------

    [Fact]
    public void EveryFilmHasFourDistinctNamedCastMembersWithNoProfileImage()
    {
        Assert.All(Films(), m =>
        {
            Assert.Equal(4, m.Cast.Length);
            Assert.Equal(4, m.Cast.Select(c => c.N).Distinct().Count());
            Assert.Equal(4, m.Cast.Select(c => c.C).Distinct().Count());
            Assert.All(m.Cast, c =>
            {
                Assert.False(string.IsNullOrWhiteSpace(c.N));
                Assert.False(string.IsNullOrWhiteSpace(c.C));
                Assert.Null(c.P);
            });
        });
    }

    [Fact]
    public void CastNamesAndCharactersRunFromVeryShortToVeryLongToStressTheLayout()
    {
        var cast = Films().SelectMany(m => m.Cast).ToList();
        var names = cast.Select(c => c.N.Length).ToList();
        var characters = cast.Select(c => c.C.Length).ToList();

        Assert.True(names.Min() <= 8, $"shortest name is {names.Min()}");
        Assert.True(names.Max() >= 60, $"longest name is {names.Max()}");
        Assert.True(characters.Min() <= 4, $"shortest character is {characters.Min()}");
        Assert.True(characters.Max() >= 60, $"longest character is {characters.Max()}");
    }

    [Fact]
    public void TheCastVariesFromFilmToFilmInWhoIsBilledAndInWhichRole()
    {
        var films = Films();

        Assert.True(films.Select(m => m.Cast[0].N).Distinct().Count() >= 15, "leads should vary");
        Assert.True(films.Select(m => m.Cast[0].C).Distinct().Count() >= 15, "roles should vary");
        Assert.True(films.Select(m => string.Join("|", m.Cast.Select(c => c.N))).Distinct().Count() >= 200,
                    "whole casts should vary, not just rotate through one list");
        Assert.True(films.Select(m => m.Cast[0].N + "|" + m.Cast[0].C).Distinct().Count() >
                    films.Select(m => m.Cast[0].N).Distinct().Count(),
                    "a name should not be tied to one character");
    }

    // ---- collections ---------------------------------------------------------------------

    // How many films a collection holds, how it is named and how its members are chosen are pinned in
    // FixtureGeneratorRealismTests; this keeps the overall share.
    [Fact]
    public void RoughlyAQuarterOfFilmsBelongToACollection()
    {
        var films = Films();

        Assert.InRange(films.Count(m => m.Col is not null) / (double)films.Count, 0.22, 0.28);
        Assert.Contains(films, m => m.Col is null);
    }

    // ---- determinism ---------------------------------------------------------------------

    [Fact]
    public void TwoRunsOverTheSameSheetProduceIdenticalRecords()
    {
        var rows = Library();
        var shows = FixtureGeneratorRealismTests.SeriesRows();

        Assert.Equal(Run(rows, shows), Run(rows, shows));
    }

    // Serialises the two lists, never the tuple: System.Text.Json writes a ValueTuple as {} and the
    // comparison would pass vacuously.
    private static string Run(IReadOnlyList<SheetRow> movieRows, IReadOnlyList<SheetRow> showRows)
    {
        var (movies, shows) = FixtureGenerator.Build(movieRows, showRows);
        return JsonSerializer.Serialize(movies) + "\n" + JsonSerializer.Serialize(shows);
    }

    [Fact]
    public void ARunIsNotATrivialEmptyComparison()
    {
        var json = Run(Library(), EpisodeRows("Alias"));

        Assert.True(json.Length > 500_000, $"only {json.Length} characters were produced");
        Assert.Contains("\"Cast\":[{", json);
    }

    private async Task<string> WriteCatalogAsync(string folder)
    {
        var (movies, shows) = FixtureGenerator.Build(Library(), FixtureGeneratorRealismTests.SeriesRows());
        var meta = new CatalogMeta(
            "2026-01-01T00:00:00Z", movies.Count, shows.Count, shows.Sum(s => s.TotalEpisodes),
            FixtureGenerator.Genres.ToDictionary(g => g.Key.ToString(CultureInfo.InvariantCulture), g => g.Value),
            CatalogMeta.TmdbAttribution, []);

        var root = Path.Combine(_dir, folder);
        await new CatalogWriter(root).WriteAsync(movies, shows, meta, CancellationToken.None);
        return root;
    }

    [Fact]
    public async Task TwoRunsWriteByteIdenticalFilesSoDiffsStayReadable()
    {
        var a = await WriteCatalogAsync("a");
        var b = await WriteCatalogAsync("b");

        var names = Directory.EnumerateFiles(a, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(a, f)).OrderBy(f => f, StringComparer.Ordinal).ToList();

        Assert.Contains("movies.json", names);
        Assert.Contains("meta.json", names);
        Assert.Equal(names, Directory.EnumerateFiles(b, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(b, f)).OrderBy(f => f, StringComparer.Ordinal).ToList());
        Assert.All(names, name => Assert.True(
            File.ReadAllBytes(Path.Combine(a, name)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(b, name))),
            $"{name} differs between runs"));
    }

    [Fact]
    public async Task TheWrittenCatalogCarriesCastGenresCollectionsAndNoImagePaths()
    {
        var root = await WriteCatalogAsync("out");
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "movies.json")));
        var films = doc.RootElement.EnumerateArray().ToList();
        var meta = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "meta.json")));
        var named = meta.RootElement.GetProperty("genres").EnumerateObject().Select(p => p.Name).ToHashSet();

        Assert.Equal(1457, films.Count);
        Assert.All(films, f =>
        {
            Assert.Equal(4, f.GetProperty("cast").GetArrayLength());
            Assert.True(f.GetProperty("g").GetArrayLength() >= 1);
            Assert.Equal(0.95, f.GetProperty("mc").GetDouble());
            Assert.False(f.TryGetProperty("p", out _));
            Assert.False(f.TryGetProperty("b", out _));
            Assert.All(f.GetProperty("g").EnumerateArray(), id => Assert.Contains(id.GetInt32().ToString(CultureInfo.InvariantCulture), named));
        });
        Assert.Contains(films, f => f.TryGetProperty("col", out _));
        Assert.Contains(films, f => !f.TryGetProperty("col", out _));
        Assert.Contains(films, f => f.TryGetProperty("tag", out _));

        var episodes = Directory.EnumerateFiles(Path.Combine(root, "tv"), "*.json")
            .SelectMany(f => JsonDocument.Parse(File.ReadAllText(f)).RootElement.GetProperty("seasons").EnumerateArray())
            .SelectMany(season => season.GetProperty("eps").EnumerateArray())
            .ToList();
        Assert.Contains(episodes, e => !e.GetProperty("o").GetBoolean() && e.TryGetProperty("air", out _));
        Assert.All(episodes, e => Assert.False(e.TryGetProperty("s", out _)));
    }

    // ---- shows ---------------------------------------------------------------------------

    // Deliberately jumbled: season 2 before season 1, the lower-case series before the capitalised one, a
    // padded and re-cased spelling of a series, and a row with no series at all.
    private static List<SheetRow> MixedRows() =>
    [
        new("Season 2, Episode 1: The Telling", "44:50:59", "", "Bundle", "Drama", "alias"),
        new("Season 1, Episode 1: Truth Be Told", "44:50:59", "", "Bundle", "Drama", "alias"),
        new("Season 1, Episode 2: So It Begins", "44:50:59", "", "Bundle", "Drama", "alias"),
        new("A Standalone Special", "23:04", "", "", "Comedy", "Zulu"),
        new("Another Special", "23:04", "", "", "Comedy", " zulu "),
        new("Orphan row with no series", "40:00:00", "", "", "Drama", "  "),
    ];

    [Fact]
    public void GroupsRowsIntoSeriesInOrdinalNameOrderIgnoringCaseAndPaddingAndBlankSeries()
    {
        var (_, shows) = FixtureGenerator.Build([], MixedRows());

        Assert.Equal(["Zulu", "alias"], shows.Select(s => s.T));      // ordinal: capitals sort first
        Assert.Equal(["zulu", "alias"], shows.Select(s => s.Slug));
        Assert.Equal(["Comedy", "Drama"], shows.Select(s => s.Ag));
    }

    [Fact]
    public void BuildsSeasonsInNumberOrderAndEpisodesFromTheNumberedTitles()
    {
        var alias = FixtureGenerator.Build([], MixedRows()).Item2.Single(s => s.T == "alias");

        // Only the owned episodes are the Sheet's; the rest are padding (pinned in FixtureGeneratorRealismTests).
        var owned = alias.Seasons.Where(s => s.Owned > 0).Select(s => s.Eps.Where(e => e.O).ToArray()).ToArray();

        Assert.Equal([1, 2], alias.Seasons.Where(s => s.Owned > 0).Select(s => s.N));
        Assert.Equal([2, 1], owned.Select(o => o.Length));
        Assert.Equal(["Truth Be Told", "So It Begins"], owned[0].Select(e => e.T));
        Assert.Equal([1, 2], owned[0].Select(e => e.E));
        Assert.Equal(["The Telling"], owned[1].Select(e => e.T));
        Assert.Equal([1], owned[1].Select(e => e.E));
        Assert.Equal([2, 1], alias.Seasons.Select(s => s.Owned).Where(n => n > 0));
        Assert.All(alias.Seasons, s => Assert.Equal(s.Eps.Length, s.Total));
        Assert.Equal(3, alias.OwnedEpisodes);
        Assert.Equal(alias.Seasons.Sum(s => s.Total), alias.TotalEpisodes);
    }

    [Fact]
    public void UnnumberedRowsAreOwnedEpisodesExactlyOnceWhereverTheyLand()
    {
        var zulu = FixtureGenerator.Build([], MixedRows()).Item2.Single(s => s.T == "Zulu");

        // Each bare name either claims a slot in a season or is left in season 0; that is a draw (see
        // FixtureGeneratorRealismTests), so only "owned, once" is asserted here.
        var owned = zulu.Seasons.SelectMany(s => s.Eps.Where(e => e.O)).Select(e => e.T).Order(StringComparer.Ordinal);
        Assert.Equal(["A Standalone Special", "Another Special"], owned);
        Assert.Equal(2, zulu.OwnedEpisodes);
    }

    private static SheetRow Disagree(string series, string genre, string runtime, int e)
        => new($"Season 1, Episode {e}: Ep {e}", runtime, "", "", genre, series);

    [Fact]
    public void RowsThatDisagreeAboutASeriesAreResolvedByTheMostCommonValueWhateverTheirOrder()
    {
        // The pipeline takes the most common spelling, genre and (here) runtime, so a re-export that reorders Apple's
        // rows cannot change the series. The minority view comes first, so taking the first row would be wrong.
        var rows = new List<SheetRow>
        {
            Disagree(" quiz show", "Comedy", "30:00:00", 1),
            Disagree("Quiz Show", "Drama", "44:00:00", 2), Disagree("Quiz Show", "Drama", "44:00:00", 3),
            Disagree("Quiz Show", "Drama", "44:00:00", 4), Disagree("QUIZ SHOW", "Comedy", "30:00:00", 5),
        };

        var forward = FixtureGenerator.Build([], rows).Item2.Single();
        var backward = FixtureGenerator.Build([], [.. rows.AsEnumerable().Reverse()]).Item2.Single();

        Assert.Equal("Quiz Show", forward.T);
        Assert.Equal("quiz-show", forward.Slug);
        Assert.Equal("Drama", forward.Ag);
        Assert.Equal([44], forward.Seasons.SelectMany(s => s.Eps).Where(e => e.Rt is not null).Select(e => e.Rt!.Value).Distinct());
        Assert.Equal(JsonSerializer.Serialize(forward), JsonSerializer.Serialize(backward));
    }

    [Fact]
    public void ARowWithAnUnreadableRuntimeIsSkippedUnlessNoRowIsReadable()
    {
        var good = new SheetRow("Season 1, Episode 1: A", "44:00:00", "", "", "Drama", "Run");
        var bad = new SheetRow("Season 1, Episode 2: B", "not a runtime", "", "", "Drama", "Run");

        foreach (var order in new[] { new[] { bad, good }, [good, bad] })
        {
            var show = FixtureGenerator.Build([], order).Item2.Single();

            Assert.Equal([44], show.Seasons.SelectMany(s => s.Eps).Where(e => e.Rt is not null).Select(e => e.Rt!.Value).Distinct());
        }

        Assert.Throws<FormatException>(() => FixtureGenerator.Build([], [bad]));
    }

    [Fact]
    public void WhenRowsDisagreeEvenlyTheTieGoesToTheOrdinalFirstSpellingAndGenreAndTheShorterRuntime()
    {
        var rows = new List<SheetRow>
        {
            Disagree("beta", "Drama", "44:00:00", 1), Disagree("Beta", "Comedy", "30:00:00", 2),
        };

        foreach (var order in new[] { rows, [.. rows.AsEnumerable().Reverse()] })
        {
            var show = FixtureGenerator.Build([], order).Item2.Single();

            Assert.Equal("Beta", show.T);                   // 'B' sorts before 'b'
            Assert.Equal("Comedy", show.Ag);                // 'C' before 'D'
            Assert.Equal([30], show.Seasons.SelectMany(s => s.Eps).Where(e => e.Rt is not null).Select(e => e.Rt!.Value).Distinct());
        }
    }

    [Fact]
    public void EpisodeRuntimesComeFromTheSheetAndEveryPlacedEpisodeHasAnOverviewAndAirDate()
    {
        var (_, shows) = FixtureGenerator.Build([], MixedRows());

        // "44:50:59" is Sheets coercion of 44m50s, and a plain "23:04" is 23 minutes.
        // An owned numbered row TMDB does not list (a few, drawn per episode) has nothing but its title, as in TvPipeline.
        static bool Unlisted(EpisodeRecord e) => e.O && e.Air is null && e.Rt is null && e.Ov is null && e.S is null;
        Assert.All(shows.Single(s => s.T == "alias").Seasons.SelectMany(s => s.Eps), e => Assert.True(e.Rt == 44 || Unlisted(e)));
        Assert.All(shows.Single(s => s.T == "Zulu").Seasons.Where(s => s.N > 0).SelectMany(s => s.Eps), e => Assert.True(e.Rt == 23 || Unlisted(e)));
        Assert.All(shows.SelectMany(s => s.Seasons).Where(s => s.N > 0).SelectMany(s => s.Eps), e =>
            Assert.True(Unlisted(e) || (e.Air is not null && !string.IsNullOrWhiteSpace(e.Ov))));
        // Rows no TMDB episode claimed (season 0) carry no runtime, overview or date, as in TvPipeline.
        Assert.All(shows.SelectMany(s => s.Seasons).Where(s => s.N == 0).SelectMany(s => s.Eps), e =>
        {
            Assert.Null(e.Rt);
            Assert.Null(e.Ov);
            Assert.Null(e.Air);
        });
    }

    [Fact]
    public void ASeriesLibraryTheSizeOfTheRealOneStaysInRangeWithUniqueIds()
    {
        var rows = Library(122).Select(r => new SheetRow("Season 1, Episode 1: Pilot", "44:00:00", "", "", "Drama", r.Title)).ToList();
        var (_, shows) = FixtureGenerator.Build([], rows);

        Assert.Equal(122, shows.Count);
        Assert.Equal(122, shows.Select(s => s.I).Distinct().Count());
        Assert.All(shows, s =>
        {
            Assert.InRange(s.FirstYear!.Value, 1990, 2024);
            Assert.InRange(s.I, 800_000, 899_999);
            // Unlike a film, a series whose name matches exactly is 1.0 in TvPipeline (this one carries the
            // Sheet's own name, so it always matches). Only films reserve 1.0 for a human override.
            Assert.Equal(1.0, s.Mc);
        });
    }
}

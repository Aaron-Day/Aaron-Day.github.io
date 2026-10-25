using System.Globalization;
using MovieLibrary.Core.Models;
using MovieLibrary.Enrich.Emit;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// A fixture has to show every state the UI must lay out, not only the tidy one. In a real run "owned" comes from
/// the Sheet and "total" from TMDB; a fixture has only the Sheet, so it invents what TMDB would know and the
/// owner does not have: episodes, films that share a collection, taglines and air dates. These tests pin that the
/// owned-only view and its reveal toggle (and the other films in a collection) have something to show.
/// </summary>
public class FixtureGeneratorRealismTests
{
    private sealed record OwnedRow(string Series, int Season, int Episode, string Name);

    /// <summary>
    /// 122 series (the real count), one to four seasons each, eight to twenty-four owned episodes per season. A
    /// third of the seasons are ones the owner bought episode by episode, so a few numbers in the middle are
    /// missing. The rows are shuffled, because Apple does not keep the export in order.
    /// </summary>
    private static (List<SheetRow> Rows, List<OwnedRow> Owned) SeriesLibrary(int count = 122)
    {
        var rng = new Random(122);
        var rows = new List<SheetRow>();
        var owned = new List<OwnedRow>();

        foreach (var series in FixtureGeneratorCatalogueTests.Library(count).Select(r => r.Title))
        {
            var seasons = rng.Next(1, 5);
            for (var season = 1; season <= seasons; season++)
            {
                var perSeason = rng.Next(8, 25);
                var skipped = new HashSet<int>();
                if (rng.Next(3) == 0)
                    for (var k = rng.Next(1, 4); k > 0; k--) skipped.Add(rng.Next(2, perSeason));   // never the first or last

                for (var e = 1; e <= perSeason; e++)
                {
                    if (skipped.Contains(e)) continue;
                    var name = $"Episode name {season}-{e}";
                    rows.Add(new SheetRow($"Season {season}, Episode {e}: {name}", "44:50:59", "", "", "Drama", series));
                    owned.Add(new OwnedRow(series, season, e, name));
                }
            }
        }

        return ([.. rows.OrderBy(_ => rng.Next())], owned);
    }

    /// <summary>The 122-series Sheet rows, for tests elsewhere that want a realistic library.</summary>
    internal static List<SheetRow> SeriesRows() => SeriesLibrary().Rows;

    private static IReadOnlyList<ShowRecord> Shows() => FixtureGenerator.Build([], SeriesRows()).Item2;

    private static IReadOnlyList<MovieRecord> Films()
        => FixtureGenerator.Build(FixtureGeneratorCatalogueTests.Library(), []).Item1;

    // ---- A. unowned episodes -----------------------------------------------------------------

    [Fact]
    public void TheOwnedEpisodesAreExactlyTheSheetsAndTheCountsAddUp()
    {
        var (rows, ownedRows) = SeriesLibrary();
        var shows = FixtureGenerator.Build([], rows).Item2;

        Assert.Equal(122, shows.Count);
        foreach (var show in shows)
        {
            var expected = ownedRows.Where(o => o.Series == show.T)
                .OrderBy(o => o.Season).ThenBy(o => o.Episode).Select(o => (o.Season, o.Episode, o.Name)).ToList();
            var actual = show.Seasons
                .SelectMany(s => s.Eps.Where(e => e.O).Select(e => (s.N, e.E, e.T)))
                .OrderBy(x => x.N).ThenBy(x => x.E).ToList();

            Assert.Equal(expected, actual);                  // nothing owned is lost, nothing unowned is claimed
            Assert.All(show.Seasons, s =>
            {
                Assert.Equal(s.Eps.Count(e => e.O), s.Owned);
                Assert.Equal(s.Eps.Length, s.Total);
            });
            Assert.Equal(expected.Count, show.OwnedEpisodes);
            Assert.Equal(show.Seasons.Sum(s => s.Total), show.TotalEpisodes);
        }
    }

    [Fact]
    public void MostSeriesAreMissingEpisodesSoTheRevealToggleHasSomethingToReveal()
    {
        var shows = Shows();
        var missing = shows.Count(s => s.OwnedEpisodes < s.TotalEpisodes);

        Assert.True(missing >= shows.Count * 0.75, $"only {missing} of {shows.Count} series have an unowned episode");
        Assert.All(shows, s => Assert.True(s.OwnedEpisodes <= s.TotalEpisodes));
    }

    [Fact]
    public void ASeriesCanBeFullyOwnedSoTheToggleCorrectlyRevealsNothing()
    {
        var full = Shows().Where(s => s.OwnedEpisodes == s.TotalEpisodes).ToList();

        Assert.NotEmpty(full);
        Assert.All(full, s =>
        {
            Assert.True(s.OwnedEpisodes > 0);
            Assert.All(s.Seasons.SelectMany(x => x.Eps), e => Assert.True(e.O));
            Assert.All(s.Seasons, x => Assert.Equal(x.Owned, x.Total));
        });
    }

    [Fact]
    public void ASeriesCanBeMissingAFewHalfOrTwoThirdsOfItsEpisodes()
    {
        var shows = Shows();

        Assert.Contains(shows, s => s.OwnedEpisodes * 3 <= s.TotalEpisodes);                            // a large gap
        Assert.Contains(shows, s => s.OwnedEpisodes * 3 > s.TotalEpisodes                               // about half
                                    && s.OwnedEpisodes * 3 <= s.TotalEpisodes * 2);
        Assert.Contains(shows, s => s.OwnedEpisodes < s.TotalEpisodes                                   // a few
                                    && s.OwnedEpisodes * 3 > s.TotalEpisodes * 2);
    }

    /// <summary>600 series of three seasons, each season ten contiguous owned episodes, so Total - Owned is the padding alone.</summary>
    private static List<ShowRecord> TenEpisodeSeasons()
    {
        var rows = FixtureGeneratorCatalogueTests.Library(600).SelectMany(r =>
            Enumerable.Range(1, 3).SelectMany(season => Enumerable.Range(1, 10).Select(e =>
                new SheetRow($"Season {season}, Episode {e}: Owned", "44:00:00", "", "", "Drama", r.Title)))).ToList();

        return [.. FixtureGenerator.Build([], rows).Item2];
    }

    [Fact]
    public void TheMissingCountOfASeasonFollowsTheShapeOfItsSeries()
    {
        var gaps = TenEpisodeSeasons().Select(s => s.Seasons.Select(x => x.Total - x.Owned).ToList()).ToList();

        // With ten owned a season: fully owned is 0, a few missing 1-4, about half 5-10, a large gap 20-31.
        static int Band(int gap) => gap switch { 0 => 0, >= 1 and <= 4 => 1, >= 5 and <= 10 => 2, >= 20 and <= 31 => 3, _ => -1 };

        Assert.All(gaps, g =>
        {
            Assert.NotEqual(-1, Band(g[0]));
            Assert.All(g, n => Assert.Equal(Band(g[0]), Band(n)));          // one shape for the whole series
        });
        double Share(int band) => gaps.Count(g => Band(g[0]) == band) / (double)gaps.Count;
        Assert.InRange(Share(0), 0.06, 0.14);
        Assert.InRange(Share(1), 0.52, 0.68);
        Assert.InRange(Share(2), 0.15, 0.25);
        Assert.InRange(Share(3), 0.06, 0.14);

        var all = gaps.SelectMany(g => g).ToList();
        Assert.Equal([1, 2, 3, 4], all.Where(n => Band(n) == 1).Distinct().Order());
        Assert.True(all.Where(n => Band(n) == 2).Distinct().Count() >= 4);
        Assert.True(all.Where(n => Band(n) == 3).Distinct().Count() >= 6);
        Assert.Contains(gaps, g => g.Distinct().Count() > 1);               // drawn per season, not once per series
    }

    [Fact]
    public void TheNumberMissingFromASeasonVariesAcrossTheLibrary()
    {
        var gaps = Shows().SelectMany(s => s.Seasons).Select(s => s.Total - s.Owned).Distinct().Count();

        Assert.True(gaps >= 10, $"only {gaps} distinct gap sizes");
    }

    [Fact]
    public void EpisodesRunInNumberOrderWithTheMissingOnesAmongTheOwnedOnes()
    {
        var shows = Shows();
        var seasons = shows.SelectMany(s => s.Seasons).ToList();

        Assert.All(seasons, s =>
        {
            var numbers = s.Eps.Select(e => e.E).ToList();
            Assert.Equal(numbers.OrderBy(n => n), numbers);
            Assert.Equal(numbers.Count, numbers.Distinct().Count());
        });

        // Where a series is padded, a season is the contiguous 1..N list TMDB has: a number the owner skipped
        // is an unowned episode, not a hole.
        foreach (var show in shows.Where(s => s.OwnedEpisodes < s.TotalEpisodes))
            Assert.All(show.Seasons, s => Assert.Equal(Enumerable.Range(1, s.Total), s.Eps.Select(e => e.E)));

        Assert.Contains(seasons, s => s.Eps.Any(e => !e.O && e.E < s.Eps.Where(o => o.O).Max(o => o.E)));
    }

    [Fact]
    public void ANumberTheOwnerSkippedIsAnUnownedEpisodeBetweenTheOwnedOnes()
    {
        var rows = Enumerable.Range(0, 40)
            .SelectMany(i => new[] { 1, 2, 5, 6 }.Select(e =>
                new SheetRow($"Season 1, Episode {e}: Owned {e}", "44:00:00", "", "", "Drama", $"Series {i}")))
            .ToList();

        var padded = FixtureGenerator.Build([], rows).Item2.Where(s => s.TotalEpisodes > s.OwnedEpisodes).ToList();

        Assert.NotEmpty(padded);
        Assert.All(padded, show =>
        {
            var eps = show.Seasons.Single().Eps;
            Assert.True(eps.Length > 6);
            Assert.Equal([1, 2, 3, 4, 5, 6], eps.Take(6).Select(e => e.E));
            Assert.Equal([true, true, false, false, true, true], eps.Take(6).Select(e => e.O));
            Assert.All(eps.Skip(6), e => Assert.False(e.O));
        });
    }

    [Fact]
    public void AnUnownedEpisodeIsObviouslySyntheticHasNoImageAndIsLaidOutLikeAnOwnedOne()
    {
        var lengths = new List<int>();

        foreach (var season in Shows().SelectMany(s => s.Seasons))
        {
            var unowned = season.Eps.Where(e => !e.O).ToList();
            Assert.Equal(unowned.Count, unowned.Select(e => e.T).Distinct().Count());
            Assert.All(unowned, e =>
            {
                Assert.StartsWith("Fixture episode ", e.T);
                Assert.Null(e.S);                           // no still, so no image request
                Assert.Equal(44, e.Rt);                     // the Sheet's runtime, as for an owned one
                Assert.False(string.IsNullOrWhiteSpace(e.Ov));
                Assert.NotNull(e.Air);
            });
            lengths.AddRange(unowned.Select(e => e.T.Length));
        }

        Assert.True(lengths.Min() <= 30, $"shortest unowned title is {lengths.Min()}");
        Assert.True(lengths.Max() >= 100, $"longest unowned title is {lengths.Max()}");
    }

    [Fact]
    public void AnAbsurdEpisodeNumberOnTheSheetDoesNotBalloonTheSeason()
    {
        var rows = Enumerable.Range(0, 40).SelectMany(i => new[]
        {
            new SheetRow("Season 1, Episode 1: Pilot", "44:00:00", "", "", "Drama", $"Series {i}"),
            new SheetRow("Season 1, Episode 2000000000: Typo", "44:00:00", "", "", "Drama", $"Series {i}"),
        }).ToList();

        var shows = FixtureGenerator.Build([], rows).Item2;

        Assert.All(shows.SelectMany(s => s.Seasons), s =>
        {
            Assert.InRange(s.Eps.Length, 2, 400);
            Assert.All(s.Eps, e => Assert.NotNull(e.Air));
        });
        Assert.Contains(shows, s => s.TotalEpisodes > 2);       // the padded shape was exercised
    }

    // ---- C. air dates ------------------------------------------------------------------------

    [Fact]
    public void EveryEpisodeHasAnIsoAirDateThatAscendsThroughTheSeasonAndStartsInTheFirstYear()
    {
        var all = new HashSet<string>();
        var startDays = new HashSet<(int, int)>();

        foreach (var show in Shows())
        {
            foreach (var season in show.Seasons)
            {
                var dates = season.Eps.Select(e =>
                {
                    Assert.NotNull(e.Air);
                    Assert.True(DateTime.TryParseExact(e.Air, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                                       DateTimeStyles.None, out var d), $"'{e.Air}' is not yyyy-MM-dd");
                    all.Add(e.Air);
                    return d;
                }).ToList();

                for (var i = 1; i < dates.Count; i++) Assert.True(dates[i] > dates[i - 1], $"{show.T} S{season.N}");
            }

            var starts = show.Seasons.OrderBy(s => s.N)
                .Select(s => DateTime.ParseExact(s.Eps[0].Air!, "yyyy-MM-dd", CultureInfo.InvariantCulture)).ToList();
            Assert.Equal(show.FirstYear, starts[0].Year);
            startDays.Add((starts[0].Month, starts[0].Day));
            for (var i = 1; i < starts.Count; i++) Assert.True(starts[i] > starts[i - 1], $"{show.T}: a later season starts later");
        }

        Assert.True(all.Count > 1000, $"only {all.Count} distinct dates");
        Assert.True(startDays.Count >= 50, $"series start on only {startDays.Count} different days of the year");
    }

    [Fact]
    public void AirDatesAreIsoWhateverTheMachinesCulture()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");          // a Buddhist-calendar year: 2543 for 2000
            Assert.Equal("2543", new DateTime(2000, 1, 1).ToString("yyyy"));  // the culture really does change it

            var dates = Shows().SelectMany(s => s.Seasons).SelectMany(s => s.Eps).Select(e => e.Air!).ToList();

            Assert.All(dates, d => Assert.Matches(@"^(19|20)\d\d-\d\d-\d\d$", d));
        }
        finally { CultureInfo.CurrentCulture = before; }
    }

    // ---- B. collections ----------------------------------------------------------------------

    private static List<IGrouping<string, MovieRecord>> Collections(IReadOnlyList<MovieRecord> films)
        => films.Where(m => m.Col is not null).GroupBy(m => m.Col!).ToList();

    [Fact]
    public void ACollectionHoldsTwoToFiveFilmsNotOne()
    {
        var groups = Collections(Films());

        Assert.Contains(groups, g => g.Count() > 1);
        Assert.All(groups, g => Assert.InRange(g.Count(), 2, 5));
        Assert.Equal([2, 3, 4, 5], groups.Select(g => g.Count()).Distinct().Order());
    }

    // Half the titles start in lower case, where ordinal order (capitals first) differs from every other order.
    private static IReadOnlyList<MovieRecord> MixedCaseFilms()
        => FixtureGenerator.Build(FixtureGeneratorCatalogueTests.Library()
               .Select((r, i) => i % 2 == 0 ? r with { Title = char.ToLowerInvariant(r.Title[0]) + r.Title[1..] } : r)
               .ToList(), []).Item1;

    [Fact]
    public void ACollectionIsNamedAfterItsFirstMemberInOrdinalTitleOrder()
    {
        foreach (var films in new[] { Films(), MixedCaseFilms() })
            Assert.All(Collections(films), g =>
                Assert.Equal($"{g.Select(m => m.T).Order(StringComparer.Ordinal).First()} Collection", g.Key));
    }

    [Fact]
    public void ACollectionIsARunOfNeighboursInOrdinalTitleOrder()
    {
        foreach (var films in new[] { Films(), MixedCaseFilms() })
        {
            var inOrder = films.Where(m => m.Col is not null)
                .OrderBy(m => m.T, StringComparer.Ordinal).Select(m => m.Col!).ToList();

            // Once a collection's run ends, its name never comes back.
            var finished = new HashSet<string>();
            for (var i = 0; i < inOrder.Count; i++)
            {
                if (i > 0 && inOrder[i] != inOrder[i - 1]) finished.Add(inOrder[i - 1]);
                Assert.DoesNotContain(inOrder[i], finished);
            }
        }
    }

    [Fact]
    public void NoCollectionIsLeftWithASingleFilmWhateverTheSizeOfTheSheet()
    {
        for (var count = 1; count <= 90; count++)
        {
            var groups = Collections(FixtureGenerator.Build(FixtureGeneratorCatalogueTests.Library(count), []).Item1);

            Assert.All(groups, g => Assert.InRange(g.Count(), 2, 5));
        }
    }

    // ---- C. taglines -------------------------------------------------------------------------

    [Fact]
    public void MostFilmsHaveAnObviouslySyntheticTaglineAndSomeHaveNone()
    {
        var films = Films();
        var tagged = films.Where(m => m.Tag is not null).ToList();

        Assert.InRange(tagged.Count / (double)films.Count, 0.75, 0.85);
        Assert.Contains(films, m => m.Tag is null);           // the layout with no tagline is still exercised
        Assert.All(tagged, m => Assert.StartsWith("Fixture", m.Tag));
    }

    [Fact]
    public void TaglinesRunFromShortToVeryLongToStressTheLayout()
    {
        var tags = Films().Select(m => m.Tag).Where(t => t is not null).Select(t => t!).ToList();

        Assert.True(tags.Min(t => t.Length) <= 20, $"shortest tagline is {tags.Min(t => t.Length)}");
        Assert.True(tags.Max(t => t.Length) >= 100, $"longest tagline is {tags.Max(t => t.Length)}");
        Assert.True(tags.Distinct().Count() >= 4);
    }
}

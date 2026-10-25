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
    private sealed record OwnedRow(string Series, int? Season, int? Episode, string Name);

    /// <summary>
    /// 122 series (the real count), one to four seasons each, eight to twenty-four owned episodes per season. A
    /// third of the seasons are ones the owner bought episode by episode, so a few numbers in the middle are
    /// missing, and about one series in six with several seasons has only its later ones (the owner skipped season 1).
    /// The rows are shuffled, because Apple does not keep the export in order.
    /// Like the real export, over half the series (65 of 122) have no numbered row at all: every row is a bare name.
    /// A few numbered series also carry bare extras (two with the same name), an episode numbered into season 0, or
    /// the same numbered episode twice, so the placement rules are exercised and not only the tidy case.
    /// </summary>
    private static (List<SheetRow> Rows, List<OwnedRow> Owned) SeriesLibrary(int count = 122)
    {
        var rng = new Random(122);
        var later = new Random(6);          // apart from rng, so deciding who skips season 1 moves nobody else
        var kind = new Random(65);          // likewise for who is bare-only and who has extras
        var rows = new List<SheetRow>();
        var owned = new List<OwnedRow>();

        foreach (var series in FixtureGeneratorCatalogueTests.Library(count).Select(r => r.Title))
        {
            var seasons = rng.Next(1, 5);
            var firstOwned = later.Next(6) == 0 && seasons >= 2 ? 2 : 1;
            var bareOnly = kind.Next(100) < 53;
            var extras = kind.Next(4) == 0 ? kind.Next(1, 4) : 0;
            var duplicated = !bareOnly && kind.Next(8) == 0;
            var zeroed = !bareOnly && kind.Next(10) == 0;

            void Add(int? season, int? episode, string name)
            {
                var title = season is null ? name : $"Season {season}, Episode {episode}: {name}";
                rows.Add(new SheetRow(title, "44:50:59", "", "", "Drama", series));
                owned.Add(new OwnedRow(series, season, episode, name));
            }

            for (var season = 1; season <= seasons; season++)
            {
                var perSeason = rng.Next(8, 25);
                var skipped = new HashSet<int>();
                if (rng.Next(3) == 0)
                    for (var k = rng.Next(1, 4); k > 0; k--) skipped.Add(rng.Next(2, perSeason));   // never the first or last

                for (var e = 1; e <= perSeason; e++)
                {
                    if (skipped.Contains(e) || season < firstOwned) continue;
                    if (bareOnly) Add(null, null, $"Episode name {season}-{e}");
                    else Add(season, e, $"Episode name {season}-{e}");
                }
            }

            if (bareOnly) continue;
            for (var k = 1; k <= extras; k++) Add(null, null, $"Special {(k == 2 ? 1 : k)}");   // k = 2 repeats a name
            if (duplicated) Add(1, 1, "Alternate Pilot");
            if (zeroed) Add(0, 3, "Deleted Scene");
        }

        return ([.. rows.OrderBy(_ => rng.Next())], owned);
    }

    /// <summary>
    /// Where TvPipeline puts the owned rows when TMDB claims none of them: a numbered row keeps its season and number
    /// (the same episode twice is one, the first name in ordinal order), and every bare or season-0 row goes to season
    /// 0, the "unplaced" group, in ordinal name order and numbered from 1. Written out here independently of the generator.
    /// </summary>
    private static List<(int Season, int Episode, string Name)> Placed(IEnumerable<OwnedRow> rows)
    {
        var all = rows.ToList();
        var numbered = all.Where(r => r.Season >= 1)
            .GroupBy(r => (r.Season!.Value, r.Episode!.Value))
            .Select(g => g.OrderBy(r => r.Name, StringComparer.Ordinal).First())
            .Select(r => (r.Season!.Value, r.Episode!.Value, r.Name));
        var unplaced = all.Where(r => r.Season is null or 0)
            .OrderBy(r => r.Name, StringComparer.Ordinal).ThenBy(r => r.Episode)
            .Select((r, i) => (0, i + 1, r.Name));

        return [.. numbered.Concat(unplaced).OrderBy(x => x.Item1).ThenBy(x => x.Item2)];
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
            var expected = Placed(ownedRows.Where(o => o.Series == show.T));
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

    // With ten owned a season: fully owned is 0, a few missing 1-4, about half 5-10, a large gap 20-31.
    private static int Band(int gap) => gap switch
    {
        0 => 0, >= 1 and <= 4 => 1, >= 5 and <= 10 => 2, >= 20 and <= 31 => 3, _ => -1,
    };

    [Fact]
    public void TheMissingCountOfASeasonFollowsTheShapeOfItsSeries()
    {
        var gaps = TenEpisodeSeasons()
            .Select(s => s.Seasons.Where(x => x.Owned > 0).Select(x => x.Total - x.Owned).ToList()).ToList();

        // A series is its LAST season's gap: a padded series never ends on a complete season (the owner has all of
        // the early seasons and part of the latest), so every earlier gap is 0 or in the same band.
        Assert.All(gaps, g =>
        {
            Assert.NotEqual(-1, Band(g[^1]));
            Assert.All(g.Where(n => n > 0), n => Assert.Equal(Band(g[^1]), Band(n)));      // one shape for the series
            Assert.All(g.SkipLast(1).Where(n => n == 0), _ => Assert.True(Band(g[^1]) is 0 or 1 or 2));   // fully owned, or one that mixes (never a big gap)
        });
        double Share(int band) => gaps.Count(g => Band(g[^1]) == band) / (double)gaps.Count;
        Assert.InRange(Share(0), 0.06, 0.14);
        Assert.InRange(Share(1), 0.52, 0.68);
        Assert.InRange(Share(2), 0.15, 0.25);
        Assert.InRange(Share(3), 0.06, 0.14);

        var all = gaps.SelectMany(g => g).ToList();
        Assert.Equal([1, 2, 3, 4], all.Where(n => Band(n) == 1).Distinct().Order());
        Assert.True(all.Where(n => Band(n) == 2).Distinct().Count() >= 4);
        Assert.True(all.Where(n => Band(n) == 3).Distinct().Count() >= 6);
        Assert.Contains(gaps, g => g.Distinct().Count() > 1);               // drawn per season, not once per series

        // Nearly half of the few-missing and half-missing series mix a complete season with an incomplete one (half are
        // drawn to mix, and eight in nine of those finish an early season); a big-gap series never does.
        var partial = gaps.Where(g => Band(g[^1]) is 1 or 2).ToList();
        Assert.InRange(partial.Count(g => g.Contains(0)) / (double)partial.Count, 0.36, 0.53);
        Assert.DoesNotContain(gaps.Where(g => Band(g[^1]) == 3), g => g.Contains(0));

        // Of two early seasons, a mixed series can finish one, both, or neither: the draw is per season.
        Assert.Contains(gaps, g => g.SkipLast(1).Count(n => n == 0) == 1);
        Assert.Contains(gaps, g => g.Count == 3 && g.SkipLast(1).All(n => n == 0) && g[^1] > 0 && Band(g[^1]) is 1 or 2);
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
            Assert.All(show.Seasons.Where(s => s.N > 0), s => Assert.Equal(Enumerable.Range(1, s.Total), s.Eps.Select(e => e.E)));

        Assert.Contains(seasons, s => s.Owned > 0 && s.Eps.Any(e => !e.O && e.E < s.Eps.Where(o => o.O).Max(o => o.E)));
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
            var eps = show.Seasons.Single(x => x.N == 1).Eps;
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
            Assert.All(show.Seasons.Where(s => s.N == 0).SelectMany(s => s.Eps), e => Assert.Null(e.Air));

            foreach (var season in show.Seasons.Where(s => s.N > 0))
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

            var starts = show.Seasons.Where(s => s.N > 0).OrderBy(s => s.N)
                .Select(s => DateTime.ParseExact(s.Eps[0].Air!, "yyyy-MM-dd", CultureInfo.InvariantCulture)).ToList();
            // A series that opens at season 2 (the owner skipped season 1, so it is fully owned and nothing is
            // invented before it) airs its first listed episode a year after FirstYear.
            if (show.Seasons.Any(s => s.N == 1))
            {
                var seasonOne = DateTime.ParseExact(show.Seasons.Single(s => s.N == 1).Eps[0].Air!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                Assert.Equal(show.FirstYear, seasonOne.Year);
                startDays.Add((seasonOne.Month, seasonOne.Day));
            }
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

            var dates = Shows().SelectMany(s => s.Seasons).Where(s => s.N > 0).SelectMany(s => s.Eps).Select(e => e.Air!).ToList();

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

    // ---- D. seasons the owner has no episode of -----------------------------------------------

    [Fact]
    public void ASubsetOfSeriesHaveSeasonsTheOwnerHasNoEpisodeOf()
    {
        var shows = Shows();
        var unowned = shows.SelectMany(s => s.Seasons).Where(x => x.Owned == 0).ToList();
        var withUnowned = shows.Count(s => s.Seasons.Any(x => x.Owned == 0));

        Assert.InRange(withUnowned / (double)shows.Count, 0.10, 0.85);          // not all: some series list none
        Assert.Contains(shows, s => s.Seasons.All(x => x.Owned > 0));            // and series with none at all
        Assert.Contains(shows, s => s.Seasons.All(x => x.Owned == x.Total));     // the fully owned ones
        Assert.All(unowned, x =>
        {
            Assert.InRange(x.Total, 6, 24);
            Assert.Equal(x.Total, x.Eps.Length);
            Assert.Equal(Enumerable.Range(1, x.Total), x.Eps.Select(e => e.E));
            Assert.All(x.Eps, e =>
            {
                Assert.False(e.O);
                Assert.StartsWith("Fixture episode ", e.T);
                Assert.Null(e.S);
                Assert.Equal(44, e.Rt);
                Assert.False(string.IsNullOrWhiteSpace(e.Ov));
                Assert.NotNull(e.Air);
            });
        });
        Assert.True(unowned.Select(x => x.Total).Distinct().Count() >= 8, "unowned seasons should differ in length");
    }

    [Fact]
    public void SeasonsRunInNumberOrderAndAPaddedSeriesListsEverySeasonFromOne()
    {
        var shows = Shows();

        Assert.All(shows, s => Assert.Equal(s.Seasons.Select(x => x.N).OrderBy(n => n), s.Seasons.Select(x => x.N)));
        Assert.All(shows.Where(s => s.OwnedEpisodes < s.TotalEpisodes),
                   s => Assert.Equal(Enumerable.Range(1, s.Seasons.Count(x => x.N > 0)), s.Seasons.Where(x => x.N > 0).Select(x => x.N)));
    }

    [Fact]
    public void AnUnownedSeasonCanSitBeforeAnOwnedOneAndAnotherAfterIt()
    {
        var shows = Shows();

        Assert.Contains(shows, s => s.Seasons.Any(x => x.Owned == 0 && s.Seasons.Any(y => y.Owned > 0 && y.N > x.N)));
        Assert.Contains(shows, s => s.Seasons.Any(x => x.Owned == 0 && s.Seasons.Any(y => y.Owned > 0 && y.N >= 1 && y.N < x.N)));
    }

    [Fact]
    public void ASeasonTheOwnerSkippedIsAnUnownedSeasonBeforeTheOwnedOnes()
    {
        var rows = Enumerable.Range(0, 60)
            .SelectMany(i => Enumerable.Range(1, 6).Select(e =>
                new SheetRow($"Season 3, Episode {e}: Owned {e}", "44:00:00", "", "", "Drama", $"Series {i}")))
            .ToList();

        var shows = FixtureGenerator.Build([], rows).Item2;
        var plain = shows.Where(s => s.Seasons.Length == 1).ToList();
        var padded = shows.Where(s => s.Seasons.Length > 1).ToList();

        Assert.NotEmpty(plain);                                                  // fully owned: left as the Sheet has it
        Assert.NotEmpty(padded);
        Assert.All(plain, s => { Assert.Equal(3, s.Seasons[0].N); Assert.Equal(s.OwnedEpisodes, s.TotalEpisodes); });
        Assert.All(padded, s =>
        {
            Assert.Equal([1, 2, 3], s.Seasons.Take(3).Select(x => x.N));
            Assert.Equal([0, 0, 6], s.Seasons.Take(3).Select(x => x.Owned));
            Assert.All(s.Seasons.Take(2), x => Assert.True(x.Total > 0 && x.Eps.All(e => !e.O)));
            Assert.All(s.Seasons.Skip(3), x => Assert.Equal(0, x.Owned));       // any after the last owned one
        });
    }

    [Fact]
    public void TrailingUnownedSeasonsFollowTheShapeOfTheSeries()
    {
        var shows = TenEpisodeSeasons();                              // three owned seasons each, 1 to 3
        var byBand = shows
            .GroupBy(s => Band(s.Seasons.Last(x => x.Owned > 0).Total - 10))
            .ToDictionary(g => g.Key, g => g.Select(s => s.Seasons.Length - 3).ToList());

        Assert.Equal([0], byBand[0].Distinct());                      // fully owned: nothing added
        Assert.Equal([0], byBand[1].Distinct());                      // a few missing episodes: no missing seasons
        Assert.Equal([0, 1], byBand[2].Distinct().Order());           // about half: perhaps one
        Assert.Equal([1, 2, 3], byBand[3].Distinct().Order());        // a large gap: one to three
        Assert.All(shows, s => Assert.Equal(Enumerable.Range(1, s.Seasons.Length), s.Seasons.Select(x => x.N)));
    }

    [Fact]
    public void AnAbsurdSeasonNumberOnTheSheetDoesNotBalloonTheSeries()
    {
        var rows = Enumerable.Range(0, 40).SelectMany(i => new[]
        {
            new SheetRow("Season 1, Episode 1: Pilot", "44:00:00", "", "", "Drama", $"Series {i}"),
            new SheetRow("Season 2000000000, Episode 1: Typo", "44:00:00", "", "", "Drama", $"Series {i}"),
        }).ToList();

        var shows = FixtureGenerator.Build([], rows).Item2;

        Assert.All(shows, s => Assert.InRange(s.Seasons.Length, 2, 40));
        Assert.All(shows.SelectMany(s => s.Seasons).SelectMany(x => x.Eps), e => Assert.NotNull(e.Air));
        Assert.Contains(shows, s => s.Seasons.Length > 2);               // the padded shape was exercised
    }

    // ---- E. rows with no season: TvPipeline's "unplaced" group, season 0 ---------------------

    [Fact]
    public void ABareNamedRowIsAnOwnedEpisodeOfSeasonZeroNumberedInOrdinalNameOrder()
    {
        var rows = new[] { "Zeta", "Alpha", "Alpha", "beta", "Season 0, Episode 5: Middle" }
            .Select(t => new SheetRow(t, "23:04", "", "", "Comedy", "Specials")).ToList();

        var show = Assert.Single(FixtureGenerator.Build([], rows).Item2);
        var zero = Assert.Single(show.Seasons, s => s.N == 0);

        Assert.Equal(["Alpha", "Alpha", "Middle", "Zeta", "beta"], zero.Eps.Select(e => e.T));   // ordinal: capitals first
        Assert.Equal([1, 2, 3, 4, 5], zero.Eps.Select(e => e.E));
        Assert.Equal((5, 5), (zero.Owned, zero.Total));
        Assert.All(zero.Eps, e =>
        {
            Assert.True(e.O);
            Assert.Null(e.Air);                 // nothing claimed it, so TMDB gave it no date, runtime, overview or still
            Assert.Null(e.Rt);
            Assert.Null(e.Ov);
            Assert.Null(e.S);
        });
    }

    [Fact]
    public void TheSameNumberedEpisodeListedTwiceIsOneEpisodeAndTheFirstNameInOrdinalOrderWins()
    {
        var rows = new[] { "Season 1, Episode 2: Two B", "Season 1, Episode 1: One", "Season 1, Episode 2: Two A" }
            .Select(t => new SheetRow(t, "44:00:00", "", "", "Drama", "Dup")).ToList();

        var season = Assert.Single(FixtureGenerator.Build([], rows).Item2.Single().Seasons, s => s.N == 1);
        var owned = season.Eps.Where(e => e.O).ToList();

        Assert.Equal([1, 2], owned.Select(e => e.E));
        Assert.Equal(["One", "Two A"], owned.Select(e => e.T));
        Assert.Equal(2, season.Owned);
    }

    [Fact]
    public void ADuplicateNumberedEpisodeIsResolvedByOrdinalNameSoCapitalsBeatLowerCase()
    {
        var rows = new[] { "Season 1, Episode 1: beta", "Season 1, Episode 1: Zeta" }
            .Select(t => new SheetRow(t, "44:00:00", "", "", "Drama", "Case")).ToList();

        var owned = FixtureGenerator.Build([], rows).Item2.Single().Seasons.Single(s => s.N == 1).Eps.Where(e => e.O);

        Assert.Equal("Zeta", Assert.Single(owned).T);       // ordinal: 'Z' (0x5A) sorts before 'b' (0x62)
    }

    [Fact]
    public void SeasonZeroIsOnlyTheOwnedRowsAndIsTheCommonShape()
    {
        var shows = Shows();
        var withZero = shows.Where(s => s.Seasons.Any(x => x.N == 0)).ToList();

        Assert.InRange(withZero.Count / (double)shows.Count, 0.45, 0.80);        // 65 of the real 122 have no numbered row
        Assert.All(withZero.SelectMany(s => s.Seasons.Where(x => x.N == 0)), z =>
        {
            Assert.Equal(z.Owned, z.Total);                                      // nothing is ever invented in it
            Assert.Equal(Enumerable.Range(1, z.Total), z.Eps.Select(e => e.E));
            Assert.All(z.Eps, e => { Assert.True(e.O); Assert.Null(e.Air); Assert.Null(e.Rt); Assert.Null(e.Ov); Assert.Null(e.S); });
        });
    }

    [Fact]
    public void ABareOnlySeriesIsStillPaddedWithSeasonsTheOwnerHasNoEpisodeOf()
    {
        var shows = Shows();
        var bareOnly = shows.Where(s => s.Seasons.Where(x => x.Owned > 0).All(x => x.N == 0)).ToList();
        var padded = bareOnly.Where(s => s.OwnedEpisodes < s.TotalEpisodes).ToList();
        var full = bareOnly.Where(s => s.OwnedEpisodes == s.TotalEpisodes).ToList();

        Assert.True(bareOnly.Count > shows.Count / 3, "bare-only series should be a large share");
        Assert.NotEmpty(padded);
        Assert.NotEmpty(full);
        Assert.All(full, s => Assert.Equal([0], s.Seasons.Select(x => x.N)));
        Assert.All(padded, s =>
        {
            Assert.Equal(0, s.Seasons[0].N);
            Assert.True(s.Seasons.Length >= 2);
            Assert.Equal(Enumerable.Range(1, s.Seasons.Length - 1), s.Seasons.Skip(1).Select(x => x.N));
            Assert.All(s.Seasons.Skip(1), x => Assert.Equal(0, x.Owned));
        });
    }

    // ---- F. a complete season beside an incomplete one ---------------------------------------

    private static bool OwnsAllOfOneSeasonAndPartOfAnother(ShowRecord s)
        => s.Seasons.Any(x => x.N > 0 && x.Owned > 0 && x.Owned == x.Total)
           && s.Seasons.Any(x => x.N > 0 && x.Owned > 0 && x.Owned < x.Total);

    [Fact]
    public void ASubsetOfSeriesOwnAllOfOneSeasonAndPartOfAnother()
    {
        var shows = Shows();
        var mixed = shows.Count(OwnsAllOfOneSeasonAndPartOfAnother);

        Assert.InRange(mixed, 3, shows.Count * 0.30);
        Assert.Contains(shows, s => s.Seasons.Count(x => x.N > 0 && x.Owned > 0) > 1 && !OwnsAllOfOneSeasonAndPartOfAnother(s));
    }

    [Fact]
    public void ASeriesThatIsPaddedAlwaysEndsOnAnIncompleteOwnedSeason()
    {
        // The owner has all of the early seasons and part of the latest; a series is never "mixed" by finishing last.
        foreach (var show in Shows().Where(s => s.OwnedEpisodes < s.TotalEpisodes && s.Seasons.Any(x => x.N > 0 && x.Owned > 0)))
        {
            var last = show.Seasons.Last(x => x.N > 0 && x.Owned > 0);
            Assert.True(last.Owned < last.Total, $"{show.T} S{last.N}");
        }
    }
}

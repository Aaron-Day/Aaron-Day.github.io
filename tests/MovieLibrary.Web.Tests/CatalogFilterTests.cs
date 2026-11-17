using MovieLibrary.Core.Matching;
using MovieLibrary.Core.Models;
using MovieLibrary.Web.Models;
using MovieLibrary.Web.Services;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// What each facet does to a list, with no component and no renderer in the way. The page tests prove the
/// wiring; these prove the filtering, and they prove it over a catalogue where every facet can tell films apart,
/// because a filter exercised on records that all share one genre, one band and one decade filters nothing and
/// passes anyway.
/// </summary>
public class CatalogFilterTests
{
    private static readonly double Confident = MatchScorer.AcceptThreshold + 0.20;
    private static readonly double Uncertain = MatchScorer.AcceptThreshold - 0.20;

    /// <param name="srt">
    /// The record's sort title, and null -- as it is for every film in <see cref="Library"/> -- for the great
    /// majority whose display title is already where they file. See <see cref="Articled"/>.
    /// </param>
    private static MovieRecord Film(
        string title, int? year = 1990, int runtime = 100, int id = 1, double mc = -1,
        int[]? genres = null, string[]? directors = null, string? key = null, string? srt = null) =>
        new(I: id, K: key ?? title, T: title, Y: year, Rt: runtime, P: null, B: null,
            G: genres ?? [], Ag: "Drama", Vote: 0, Dir: directors ?? [], Ov: null, Tag: null, Cast: [],
            Col: null, Own: null, Mc: mc < 0 ? (id == 0 ? 0 : Confident) : mc, Srt: srt);

    /// <summary>
    /// Nine films that differ on every axis a facet can see: three bands, three genre ids in several
    /// combinations, four decades and a missing year, and two directors.
    /// </summary>
    private static readonly MovieRecord[] Library =
    [
        Film("Alpha", year: 1994, runtime: 140, id: 11, genres: [18, 28], directors: ["Jane Campion"]),
        Film("beta", year: 1999, runtime: 90, id: 12, genres: [35]),
        Film("Gamma", year: 2003, runtime: 120, id: 13, genres: [18], directors: ["Sofia Coppola", "Francis Coppola"]),
        Film("Delta", year: 1971, runtime: 100, id: 14, mc: Uncertain, genres: [28]),
        Film("Epsilon", year: null, runtime: 80, id: 0, key: "z-epsilon"),
        Film("Zeta", year: 2008, runtime: 150, id: 15, mc: Uncertain, genres: [35, 18]),
        Film("Eta", year: null, runtime: 110, id: 0, key: "a-eta"),
        Film("Theta", year: 1985, runtime: 100, id: 16, genres: [28, 35]),
        Film("Iota", year: 2001, runtime: 95, id: 17, genres: []),
    ];

    private static string[] Titles(FilterState state) =>
        CatalogFilter.Apply(Library, state).Select(m => m.T).ToArray();

    private static string[] Sorted(params string[] titles) =>
        titles.OrderBy(t => t, StringComparer.InvariantCultureIgnoreCase).ToArray();

    // ================================================================ the default

    [Fact]
    public void NoFilterShowsEveryFilmOnceInTitleOrder()
    {
        var shown = Titles(FilterState.Default);

        Assert.Equal(Library.Length, shown.Length);
        Assert.Equal(Sorted(Library.Select(m => m.T).ToArray()), shown);
    }

    /// <remarks>
    /// "beta" is lower case on purpose. An ordinal sort puts every capital before it and the list reads
    /// Alpha, Delta, ..., Zeta, beta: a title sort that looks broken to anyone who can read.
    /// </remarks>
    [Fact]
    public void TitleOrderIgnoresCase()
    {
        var shown = Titles(FilterState.Default);

        Assert.True(Array.IndexOf(shown, "beta") < Array.IndexOf(shown, "Delta"));
        Assert.True(Array.IndexOf(shown, "beta") > Array.IndexOf(shown, "Alpha"));
    }

    // ================================================================ search

    [Theory]
    [InlineData("alp", new[] { "Alpha" })]
    [InlineData("ALP", new[] { "Alpha" })]
    [InlineData("eta", new[] { "Eta", "Zeta", "Theta", "beta" })]
    [InlineData("nothing like it", new string[0])]
    public void SearchMatchesAnywhereInTheTitleWithoutRegardToCase(string query, string[] expected)
    {
        Assert.Equal(Sorted(expected), Titles(FilterState.Default with { Query = query }));
    }

    [Fact]
    public void SearchMatchesADirectorAndNotOnlyATitle()
    {
        Assert.Equal(new[] { "Gamma" }, Titles(FilterState.Default with { Query = "coppola" }));
    }

    [Fact]
    public void SearchSeesEveryDirectorOfAFilmAndNotOnlyTheFirst()
    {
        Assert.Equal(new[] { "Gamma" }, Titles(FilterState.Default with { Query = "Francis" }));
        Assert.Equal(new[] { "Gamma" }, Titles(FilterState.Default with { Query = "Sofia" }));
    }

    [Fact]
    public void SearchDoesNotGuessAtSpellings()
    {
        Assert.Empty(Titles(FilterState.Default with { Query = "alfa" }));
        Assert.Empty(Titles(FilterState.Default with { Query = "janne campion" }));
    }

    // ================================================================ confidence

    [Theory]
    [InlineData(Confidence.Confident, new[] { "Alpha", "beta", "Gamma", "Theta", "Iota" })]
    [InlineData(Confidence.Uncertain, new[] { "Delta", "Zeta" })]
    [InlineData(Confidence.Unmatched, new[] { "Epsilon", "Eta" })]
    public void EachBandShowsExactlyItsOwnFilms(Confidence band, string[] expected)
    {
        Assert.Equal(Sorted(expected), Titles(FilterState.Default with { Band = band }));
    }

    /// <remarks>
    /// The pipeline writes mc 0 for an unmatched film even when its best candidate scored well. The band is
    /// decided by ConfidenceBand.Of, which reads the id first: a film with I 0 and a high score is Unmatched,
    /// and a filter that banded on the score alone would call it Confident and hide it from the one view that
    /// exists to find it.
    /// </remarks>
    [Fact]
    public void AnUnmatchedFilmIsUnmatchedWhateverScoreItCarries()
    {
        var films = new[] { Film("Ghost", id: 0, mc: 0.99), Film("Plain", id: 5, mc: 0.99) };

        var unmatched = CatalogFilter.Apply(films, FilterState.Default with { Band = Confidence.Unmatched });

        Assert.Equal(new[] { "Ghost" }, unmatched.Select(m => m.T));
    }

    // ================================================================ genre

    [Fact]
    public void AGenreShowsEveryFilmCarryingItAmongAnyOfItsIds()
    {
        Assert.Equal(Sorted("Alpha", "Gamma", "Zeta"), Titles(FilterState.Default.WithGenre(18, true)));
    }

    [Fact]
    public void TwoGenresAreEitherOrNotBoth()
    {
        var shown = Titles(FilterState.Default.WithGenre(28, true).WithGenre(35, true));

        // Alpha (18, 28), beta (35), Delta (28), Zeta (35, 18), Theta (28, 35): all five, not just Theta.
        Assert.Equal(Sorted("Alpha", "beta", "Delta", "Zeta", "Theta"), shown);
    }

    [Fact]
    public void AFilmWithNoGenreAtAllIsNeverInAGenre()
    {
        var shown = Titles(FilterState.Default.WithGenre(18, true).WithGenre(28, true).WithGenre(35, true));

        Assert.DoesNotContain("Iota", shown);
        Assert.DoesNotContain("Epsilon", shown);
    }

    // ================================================================ decade

    [Fact]
    public void ADecadeShowsTheFilmsFromItsTenYears()
    {
        // 1999 is the 1990s, 2001 to 2008 are the 2000s: the boundary is the decade and not the century.
        Assert.Equal(Sorted("Alpha", "beta"), Titles(FilterState.Default.WithDecade(1990, true)));
        Assert.Equal(Sorted("Gamma", "Iota", "Zeta"), Titles(FilterState.Default.WithDecade(2000, true)));
    }

    [Fact]
    public void TwoDecadesAreEitherOr()
    {
        Assert.Equal(Sorted("Alpha", "beta", "Delta"),
                     Titles(FilterState.Default.WithDecade(1990, true).WithDecade(1970, true)));
    }

    /// <remarks>
    /// THE NULL-YEAR POLICY (R-M), both halves. A decade filter on its own excludes a film with no year, because
    /// it belongs to no decade -- and "Year unknown" is a checkbox of its own, so those films stay reachable and
    /// the totals the visitor can add up agree with the unfiltered count.
    /// </remarks>
    [Fact]
    public void AFilmWithNoYearIsInNoDecadeUnlessYearUnknownIsTicked()
    {
        var everyDecade = FilterState.Default
            .WithDecade(1970, true).WithDecade(1980, true).WithDecade(1990, true)
            .WithDecade(2000, true).WithDecade(2010, true);

        Assert.DoesNotContain("Epsilon", Titles(everyDecade));
        Assert.DoesNotContain("Eta", Titles(everyDecade));

        Assert.Equal(Sorted("Epsilon", "Eta"), Titles(FilterState.Default with { YearUnknown = true }));
        Assert.Contains("Epsilon", Titles(everyDecade with { YearUnknown = true }));
    }

    [Fact]
    public void EveryFilmIsInExactlyOneYearBucket()
    {
        var inABucket = CatalogFilter.DecadesOf(Library)
            .SelectMany(d => Titles(FilterState.Default.WithDecade(d, true)))
            .Concat(Titles(FilterState.Default with { YearUnknown = true }))
            .ToList();

        Assert.Equal(Library.Length, inABucket.Count);
        Assert.Equal(inABucket.Count, inABucket.Distinct().Count());
    }

    // ================================================================ the facets together

    [Fact]
    public void FacetsNarrowEachOtherAndSearchNarrowsThemAll()
    {
        var state = FilterState.Default.WithGenre(18, true).WithDecade(1990, true);
        Assert.Equal(new[] { "Alpha" }, Titles(state));

        Assert.Equal(new[] { "Alpha" }, Titles(state with { Query = "campion", Band = Confidence.Confident }));
        Assert.Empty(Titles(state with { Band = Confidence.Uncertain }));
    }

    // ================================================================ sort

    [Fact]
    public void YearSortsNewestFirstWithTheUndatedLastAndTitleBreakingTies()
    {
        var shown = Titles(FilterState.Default with { Sort = SortOrder.Year });

        Assert.Equal(new[] { "Zeta", "Gamma", "Iota", "beta", "Alpha", "Theta", "Delta", "Epsilon", "Eta" }, shown);
    }

    [Fact]
    public void RuntimeSortsLongestFirstWithTitleBreakingTies()
    {
        var shown = Titles(FilterState.Default with { Sort = SortOrder.Runtime });

        // Delta and Theta are both 100 minutes: Delta first.
        Assert.Equal(new[] { "Zeta", "Alpha", "Gamma", "Eta", "Delta", "Theta", "Iota", "beta", "Epsilon" }, shown);
    }

    // ---------------------------------------------------------------- the sort title

    /// <summary>
    /// Three films whose sort order is not their display order, and whose year and runtime are deliberately
    /// identical so that in EVERY order the title tiebreak decides the whole list.
    /// </summary>
    /// <remarks>
    /// Only the first carries an <c>Srt</c>; the other two file by their display titles, which is the
    /// <c>?? T</c> half of the rule, and they sit either side of where <c>Srt</c> puts the first. Sorted on
    /// the display title this reads Dune, Fargo, The Empire Strikes Back -- a different order, which is what
    /// makes these tests able to fail.
    /// </remarks>
    private static readonly MovieRecord[] Articled =
    [
        Film("The Empire Strikes Back", srt: "Empire Strikes Back", year: 1980, runtime: 124, id: 41),
        Film("Dune", year: 1980, runtime: 124, id: 42),
        Film("Fargo", year: 1980, runtime: 124, id: 43),
    ];

    /// <summary>The display titles of <see cref="Articled"/> in sort-title order: D, E(mpire), F.</summary>
    private static readonly string[] BySortTitle = ["Dune", "The Empire Strikes Back", "Fargo"];

    /// <remarks>
    /// The pipeline publishes <c>Srt</c> on every record that needs one and the record store overrides it,
    /// and until this nothing in the web app read it: one of the corrections the store advertises was a
    /// no-op, and the contract written on <c>MovieRecord.Srt</c> -- "a consumer sorts on <c>Srt ?? T</c>" --
    /// was a sentence its only consumer did not honour.
    /// </remarks>
    [Fact]
    public void TitleOrderIsTheSortTitleAndNotTheDisplayTitle()
    {
        var shown = CatalogFilter.Apply(Articled, FilterState.Default).Select(m => m.T);

        Assert.Equal(BySortTitle, shown);
    }

    /// <remarks>
    /// Both of these orders break their ties on the title, and a title tiebreak is title ordering: a film
    /// filed under E in the default view and under T here would be worse than either rule applied alone.
    /// Every film in <see cref="Articled"/> shares a year AND a runtime, so the tiebreak is the only thing
    /// deciding this list -- an assertion that passed on the leading key would prove nothing.
    /// </remarks>
    [Theory]
    [InlineData(SortOrder.Year)]
    [InlineData(SortOrder.Runtime)]
    public void TheYearAndRuntimeOrdersBreakTheirTiesOnTheSortTitleToo(SortOrder sort)
    {
        var shown = CatalogFilter.Apply(Articled, FilterState.Default with { Sort = sort }).Select(m => m.T);

        Assert.Equal(BySortTitle, shown);
    }

    // ----------------------------------------------------------------

    [Fact]
    public void SortOnlyReordersAndNeverFilters()
    {
        foreach (var sort in Enum.GetValues<SortOrder>())
            Assert.Equal(Library.Length, Titles(FilterState.Default with { Sort = sort }).Length);
    }

    /// <remarks>
    /// A list built here, not the shared Library: a mutant that sorted the source in place would reorder the shared
    /// array on the first test to call it, and every later test would then start from the already-reordered list
    /// and find nothing to complain about. Three films whose title, runtime and year orders all disagree.
    /// </remarks>
    [Fact]
    public void TheSourceListIsNeverReordered()
    {
        var source = new[]
        {
            Film("Mid", year: 1980, runtime: 100, id: 31),
            Film("Zed", year: 1990, runtime: 10, id: 32),
            Film("Alpha", year: 1970, runtime: 200, id: 33),
        };

        foreach (var sort in Enum.GetValues<SortOrder>())
        {
            _ = CatalogFilter.Apply(source, FilterState.Default with { Sort = sort });

            Assert.Equal(new[] { "Mid", "Zed", "Alpha" }, source.Select(m => m.T));
        }
    }

    [Fact]
    public void TwoFilmsWithOneTitleKeepAStableOrderByKeyWhateverOrderTheyArriveIn()
    {
        var one = Film("Twins", key: "b", id: 21);
        var two = Film("Twins", key: "a", id: 22);

        foreach (var sort in Enum.GetValues<SortOrder>())
        {
            Assert.Equal(new[] { "a", "b" }, CatalogFilter.Apply([one, two], FilterState.Default with { Sort = sort }).Select(m => m.K));
            Assert.Equal(new[] { "a", "b" }, CatalogFilter.Apply([two, one], FilterState.Default with { Sort = sort }).Select(m => m.K));
        }
    }

    // ================================================================ the options the panel offers

    [Fact]
    public void GenreOptionsAreTheMetasNamesKeyedByIntegerAndSortedByName()
    {
        var meta = CatalogMeta.Empty with
        {
            Genres = new Dictionary<string, string> { ["35"] = "Comedy", ["18"] = "Drama", ["28"] = "action" },
        };

        Assert.Equal(new[] { new GenreOption(28, "action"), new GenreOption(35, "Comedy"), new GenreOption(18, "Drama") },
                     CatalogFilter.GenreOptions(meta));
    }

    [Fact]
    public void AGenreKeyThatIsNotAnIntegerOrHasNoNameIsNotOffered()
    {
        var meta = CatalogMeta.Empty with
        {
            Genres = new Dictionary<string, string>
            {
                ["18"] = "Drama", ["x"] = "Not an id", ["99"] = "", ["98"] = "   ", ["-3"] = "Negative",
                ["0"] = "Zero", ["+7"] = "Signed", [" 5"] = "Padded",
            },
        };

        Assert.Equal(new[] { new GenreOption(18, "Drama") }, CatalogFilter.GenreOptions(meta));
    }

    /// <remarks>R-C's empty-map case, at the level where it can be said without a renderer.</remarks>
    [Fact]
    public void AMetaWithNoGenresOffersNone()
    {
        Assert.Empty(CatalogFilter.GenreOptions(CatalogMeta.Empty));
    }

    [Fact]
    public void DecadeOptionsAreTheDecadesFilmsAreActuallyInNewestFirst()
    {
        Assert.Equal(new[] { 2000, 1990, 1980, 1970 }, CatalogFilter.DecadesOf(Library));
    }

    [Fact]
    public void YearUnknownIsOnlyOfferedWhenAFilmHasNoYear()
    {
        Assert.True(CatalogFilter.AnyYearUnknown(Library));
        Assert.False(CatalogFilter.AnyYearUnknown(Library.Where(m => m.Y is not null).ToList()));
    }

    [Theory]
    [InlineData(1990, "1990s")]
    [InlineData(2000, "2000s")]
    [InlineData(1920, "1920s")]
    public void ADecadeIsLabelledByItsFirstYear(int decade, string label)
    {
        Assert.Equal(label, CatalogFilter.DecadeLabel(decade));
    }

    // ================================================================ choices the panel cannot show

    [Fact]
    public void WithinDropsWhatThePanelHasNoControlFor()
    {
        var asked = FilterState.Default.WithGenre(18, true).WithGenre(999, true)
            .WithDecade(1990, true).WithDecade(1800, true) with { YearUnknown = true };

        var shown = asked.Within(genres: [18, 28], decades: [1990, 2000], yearUnknownExists: false);

        Assert.Equal(new[] { 18 }, shown.Genres);
        Assert.Equal(new[] { 1990 }, shown.Decades);
        Assert.False(shown.YearUnknown);
    }

    [Fact]
    public void WithinKeepsYearUnknownWhenThePanelOffersIt()
    {
        var asked = FilterState.Default with { YearUnknown = true };

        Assert.True(asked.Within([], [], yearUnknownExists: true).YearUnknown);
    }

    // ================================================================ the state itself

    [Fact]
    public void SortAloneIsNotAFilter()
    {
        Assert.False((FilterState.Default with { Sort = SortOrder.Runtime }).IsFiltering);
        Assert.True((FilterState.Default with { Query = "a" }).IsFiltering);
        Assert.True((FilterState.Default with { Band = Confidence.Uncertain }).IsFiltering);
        Assert.True(FilterState.Default.WithGenre(1, true).IsFiltering);
        Assert.True(FilterState.Default.WithDecade(1990, true).IsFiltering);
        Assert.True((FilterState.Default with { YearUnknown = true }).IsFiltering);
    }

    [Fact]
    public void TickingAndUntickingKeepsGenresSortedAndDistinct()
    {
        var state = FilterState.Default.WithGenre(35, true).WithGenre(18, true).WithGenre(35, true).WithGenre(28, true);
        Assert.Equal(new[] { 18, 28, 35 }, state.Genres);

        Assert.Equal(new[] { 18, 35 }, state.WithGenre(28, false).Genres);
        Assert.Equal(new[] { 18, 28, 35 }, state.WithGenre(99, false).Genres);
    }
}

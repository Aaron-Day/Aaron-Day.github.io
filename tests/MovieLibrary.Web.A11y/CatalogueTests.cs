using System.Text.Json;
using MovieLibrary.Core.Matching;
using MovieLibrary.Core.Models;
using MovieLibrary.Web.A11y.Harness;

namespace MovieLibrary.Web.A11y;

/// <summary>
/// The catalogue the browser gate scans, checked as a catalogue. None of these drives a browser, so all of them run
/// everywhere, which is the point: they are what stops a facet test in the browser from being written against a
/// catalogue that cannot tell its own records apart.
/// </summary>
/// <remarks>
/// A filter exercised on records that all carry one genre filters nothing and passes anyway. This project has
/// shipped that once already: every record used to be <c>g: [28, 18]</c>, and ticking Drama showed 1,456 of
/// 1,456.
/// </remarks>
public class CatalogueTests
{
    private static readonly JsonSerializerOptions Read = new(JsonSerializerDefaults.Web);

    private static readonly MovieRecord[] Records =
        JsonSerializer.Deserialize<MovieRecord[]>(Catalogue.OnePosterAmong(Catalogue.Records), Read)!;

    private static readonly CatalogMeta Meta = JsonSerializer.Deserialize<CatalogMeta>(Catalogue.MetaJson(), Read)!;

    [Fact]
    public void TheCatalogueHasTheMeasuredNumberOfRecords()
    {
        Assert.Equal(Catalogue.Records, Records.Length);
    }

    [Fact]
    public void EveryGenreTheMetaNamesIsCarriedBySomeRecordsAndNotByAll()
    {
        var named = Meta.Genres.Keys.Select(int.Parse).ToList();

        // T1: a catalogue that named nothing would loop over nothing and pass.
        Assert.True(named.Count >= 8, $"Only {named.Count} genre(s) are named. A spread needs at least eight.");

        foreach (var id in named)
        {
            var carrying = Records.Count(r => r.G.Contains(id));

            Assert.InRange(carrying, 1, Records.Length - 1);
        }
    }

    [Fact]
    public void EveryGenreARecordCarriesHasANameSoTheFacetNeverShowsABareNumber()
    {
        var carried = Records.SelectMany(r => r.G).Distinct().Order().ToList();

        Assert.Equal(carried, Meta.Genres.Keys.Select(int.Parse).Order());
        Assert.All(Meta.Genres.Values, name => Assert.False(string.IsNullOrWhiteSpace(name)));
    }

    /// <remarks>
    /// "Agrees" is that the Apple text is the sheet's label for the genre TMDB names for the record's FIRST id: the
    /// same words, or beginning with them ("Action &amp; Adventure" for Action), or the one abbreviation the sheet
    /// uses (Sci-Fi for Science Fiction). It is the order that is being pinned, so a set listed the other way round
    /// (Adventure first, labelled "Action &amp; Adventure") is a disagreement.
    /// </remarks>
    [Fact]
    public void GenresAreSingleAndDoubleAndAnAppleGenreAgreesWithTheFirst()
    {
        Assert.Contains(Records, r => r.G.Length == 1);
        Assert.Contains(Records, r => r.G.Length == 2);
        Assert.True(Records.Select(r => r.Ag).Distinct().Count() >= 5, "the Apple genre text is as flat as it was");

        var abbreviations = new Dictionary<string, string> { ["Science Fiction"] = "Sci-Fi" };

        foreach (var (first, apple) in Records.Select(r => (First: r.G[0], r.Ag)).Distinct())
        {
            var named = Meta.Genres[first.ToString(System.Globalization.CultureInfo.InvariantCulture)];
            var label = abbreviations.GetValueOrDefault(named, named);

            Assert.True(apple is not null && apple.StartsWith(label, StringComparison.Ordinal),
                $"A record whose first genre is {first} ({named}) carries the Apple genre \"{apple}\", which does "
                + $"not begin with \"{label}\". The Apple text is the label for the FIRST id.");
        }
    }

    /// <remarks>
    /// The tab-order budget the catalogue's own remarks describe. ObscuredFocusTests and FocusVisibleTests walk the
    /// tab order in at most <see cref="TabWalk.Presses"/> presses and fail if the document has not been left by
    /// then. <see cref="TabWalk.ShellStops"/> stops are there whatever the catalogue holds, and then one more for
    /// each decade and each genre. The press that did not fit would turn a green gate red for a reason that has
    /// nothing to do with focus. The bound is sized for the REAL library and not for this catalogue (see
    /// <see cref="TabWalk"/>), so this one fits with room to spare and fails only when it names far more genres than
    /// TMDB has.
    /// </remarks>
    [Fact]
    public void TheGenreCountLeavesTheTabWalkRoomToFinish()
    {
        var decades = Records.Where(r => r.Y is not null).Select(r => r.Y!.Value / 10).Distinct().Count();
        var stops = TabWalk.ShellStops + decades + Meta.Genres.Count + TabWalk.StopsFromAFullPageOfTiles;

        Assert.True(stops < TabWalk.Presses,
            $"The panel adds {Meta.Genres.Count} genre stop(s) and {decades} decade stop(s), a full page adds "
            + $"{TabWalk.StopsFromAFullPageOfTiles} stops of its own ({TabWalk.ControlsPerTile} per tile), "
            + $"{stops} stops in all with the error bar up, and the tab walks allow {TabWalk.Presses} presses "
            + "including the one that leaves the document. Name fewer genres, or raise TabWalk.Presses in the "
            + "same change.");
    }

    /// <remarks>
    /// The other half of the budget, and the half that was missing. The test above compares this catalogue with the
    /// bound, so it is only worth anything while the bound is not itself fitted to this catalogue: the walks were
    /// once capped at forty, a number the gate fitted under by naming only nine genres, and the real library's panel
    /// could not have finished them. The bound must hold the most stops any library could produce, one press more
    /// to leave the document, and be derived from that and not typed as a figure.
    /// </remarks>
    [Fact]
    public void TheWalksAreAllowedEnoughPressesForTheLargestLibraryThePageCouldBeGiven()
    {
        var mostStops = TabWalk.GenresInTmdb + TabWalk.DecadesOfCinema + TabWalk.ShellStops
                        + TabWalk.StopsFromAFullPageOfTiles;
        var needed = mostStops + 1;

        Assert.True(TabWalk.Presses >= needed,
            $"The walks allow {TabWalk.Presses} presses, and a library with all {TabWalk.GenresInTmdb} of TMDB's "
            + $"genres across all {TabWalk.DecadesOfCinema} decades of film, showing a full page of "
            + $"{Catalogue.PageSize} tiles at {TabWalk.ControlsPerTile} stops each, has {mostStops} stops and "
            + $"needs {needed}. A cap below that fits the gate's catalogue and fails (or silently truncates) on "
            + "the real library.");

        // THE GRID'S OWN TERM, AND IT IS A PRODUCT RATHER THAN A COUNT OF TILES. It was the term missing from
        // the sum when a tile first became a link, and the shape of the defect repeated the day the tile gained
        // a second control: sixty tiles is no longer sixty stops. Said separately from the sum above so that
        // dropping either factor cannot be mistaken for a tidy-up, and read against the page size and the
        // per-tile count so that raising one without the other fails here rather than in the middle of a walk.
        Assert.True(TabWalk.StopsFromAFullPageOfTiles >= Catalogue.PageSize * TabWalk.ControlsPerTile,
            $"A full page shows {Catalogue.PageSize} tiles carrying {TabWalk.ControlsPerTile} controls each -- "
            + "the link around the body and the favourite button beside it -- and the bound counts "
            + $"{TabWalk.StopsFromAFullPageOfTiles} stops for the grid. The walk meets every one of them.");
    }

    [Fact]
    public void YearsSpanSeveralDecadesAndSomeRecordsHaveNone()
    {
        Assert.True(Records.Select(r => r.Y).Where(y => y is not null).Select(y => y!.Value / 10).Distinct().Count() >= 5);
        Assert.Contains(Records, r => r.Y is null);
    }

    /// <remarks>
    /// <b>The subject floor for every film page scan in this project.</b> Site.Addresses lands on
    /// <c>Catalogue.FirstKey</c>, and if that record carried the bare shape the loop builds -- no backdrop, no
    /// tagline, no overview, no cast, no collection, no extra fields -- the scan would render a heading, a
    /// byline and one definition list, and would read as covering a page most of which was never there. Rule
    /// T1: the thing that quietly empties is what has to be asserted.
    /// </remarks>
    [Fact]
    public void TheRecordTheFilmPageScanLandsOnHasSomethingInEveryPartOfThePage()
    {
        var subject = Assert.Single(Records, r => r.K == Catalogue.FirstKey);

        Assert.False(string.IsNullOrWhiteSpace(subject.B), "no backdrop, so the page's one wide img is absent");
        Assert.False(string.IsNullOrWhiteSpace(subject.Tag), "no tagline");
        Assert.False(string.IsNullOrWhiteSpace(subject.Ov), "no overview, so the Overview section is absent");
        Assert.False(string.IsNullOrWhiteSpace(subject.Col), "no collection row in the Details list");
        Assert.False(string.IsNullOrWhiteSpace(subject.Own), "no purchase row in the Details list");
        Assert.NotEmpty(subject.Dir);
        Assert.NotEmpty(subject.G);

        // The cast list, and the three branches a browser can tell apart: a portrait, the placeholder that
        // stands in for a missing one, and a name with no character to put after it.
        Assert.True(subject.Cast.Length >= 3, $"{subject.Cast.Length} cast member(s); three branches need three");
        Assert.Contains(subject.Cast, m => !string.IsNullOrWhiteSpace(m.P));
        Assert.Contains(subject.Cast, m => m.P is null);
        Assert.Contains(subject.Cast, m => string.IsNullOrEmpty(m.C));
        Assert.All(subject.Cast, m => Assert.False(string.IsNullOrWhiteSpace(m.N), "a cast member with no name"));

        // The second definition list exists only for a record with extra fields, and it is the owner's own
        // columns: the part of the page no code here interprets and no other record can stand in for.
        Assert.NotNull(subject.Ex);
        Assert.NotEmpty(subject.Ex);
        Assert.All(subject.Ex, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Key));
            Assert.False(string.IsNullOrWhiteSpace(p.Value));
        });
    }

    // ================================================================================ the series fixture

    private static readonly ShowSummary[] Shows =
        JsonSerializer.Deserialize<ShowSummary[]>(Catalogue.ShowsJson(), Read)!;

    [Fact]
    public void TheSeriesIndexHasTheMeasuredNumberOfSeries()
    {
        Assert.Equal(Catalogue.Series, Shows.Length);
        Assert.True(Shows.Length > Catalogue.PageSize,
            $"{Shows.Length} series fit on one page of {Catalogue.PageSize}, so the pager on /tv would "
            + "never be scanned with a Next to press.");
    }

    /// <remarks>
    /// The same argument as the genre spread above, one page over: a badge branch exercised over records
    /// that all carry one band renders one variant and passes anyway. Both degraded bands have to be
    /// present AND the confident majority with them, because the ordinary tile -- the one with no badge at
    /// all -- is the one a wall is mostly made of.
    /// </remarks>
    [Fact]
    public void EveryConfidenceBandIsCarriedBySomeSeriesAndNotByAll()
    {
        foreach (var band in Enum.GetValues<Confidence>())
        {
            var carrying = Shows.Count(s => ConfidenceBand.Of(s) == band);

            Assert.InRange(carrying, 1, Shows.Length - 1);
        }
    }

    /// <remarks>
    /// A series with nothing in it shows a title, a year and no counts at all. None of the owner's 122 is
    /// in that state today -- the 7 with no NUMBERED season still carry every episode they own in the
    /// unplaced bucket -- so this fixture is the ONLY subject the guard in <c>TileView.ForShow</c> has
    /// anywhere, and without it a change that printed "0 seasons" would go green in every scan.
    /// </remarks>
    [Fact]
    public void SomeSeriesHasNoEpisodeListAtAllAndMostDo()
    {
        Assert.Contains(Shows, s => s.SeasonCount == 0 && s.TotalEpisodes == 0);
        Assert.True(Shows.Count(s => s.TotalEpisodes > 0) >= Shows.Length - 1);
    }

    [Fact]
    public void SeriesAreCompleteAndPartialAndSomeHaveNoFirstYear()
    {
        Assert.Contains(Shows, s => s.TotalEpisodes > 0 && s.OwnedEpisodes == s.TotalEpisodes);
        Assert.Contains(Shows, s => s.OwnedEpisodes < s.TotalEpisodes);
        Assert.Contains(Shows, s => s.FirstYear is null);
        Assert.Contains(Shows, s => s.FirstYear is not null);

        // Never more owned than exist: the text says "45 of 984" and the reverse would be nonsense on screen.
        Assert.All(Shows, s => Assert.InRange(s.OwnedEpisodes, 0, s.TotalEpisodes));
    }

    [Fact]
    public void EverySeriesHasATitleAndADistinctSlug()
    {
        Assert.All(Shows, s => Assert.False(string.IsNullOrWhiteSpace(s.T)));
        Assert.Equal(Shows.Length, Shows.Select(s => s.Slug).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void NoSeriesCarriesAPosterPathSoNoTileOnTheSeriesListRequestsAnImage()
    {
        Assert.All(Shows, s => Assert.Null(s.P));
    }

    [Fact]
    public void WriteIntoPublishesTheSeriesIndexBesideTheMovies()
    {
        var root = Path.Combine(Path.GetTempPath(), "ml-gate-" + Guid.NewGuid());
        try
        {
            Catalogue.WriteInto(root);

            Assert.Equal(Catalogue.Series,
                         JsonSerializer.Deserialize<ShowSummary[]>(
                             File.ReadAllText(Path.Combine(root, "data", "shows.json")), Read)!.Length);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static ShowRecord Show(int n) =>
        JsonSerializer.Deserialize<ShowRecord>(Catalogue.ShowJson(n), Read)!;

    /// <remarks>
    /// <b>The subject floor for every series page scan in this project.</b> Site.Addresses lands on
    /// <c>Catalogue.FirstSlug</c>, and if that record carried the bare shape the loop builds -- no
    /// seasons, no episodes, nothing owned and nothing missing, no extra fields -- the scan would render a
    /// heading, a byline and one definition list and would read as covering a page most of which was never
    /// there. It also has to be PART-OWNED: a complete series renders no "Missing" row at all, and the
    /// mark is the one thing on this page that exists to be read rather than looked at.
    /// </remarks>
    [Fact]
    public void TheSeriesTheShowPageScanLandsOnHasSomethingInEveryPartOfThePage()
    {
        var subject = Show(0);

        Assert.Equal(Catalogue.FirstSlug, subject.Slug);
        Assert.Equal(Confidence.Confident, ConfidenceBand.Of(subject));
        Assert.False(string.IsNullOrWhiteSpace(subject.T));
        Assert.False(string.IsNullOrWhiteSpace(subject.Ag), "no genre row in the Details list");
        Assert.NotNull(subject.FirstYear);

        // Several seasons, so the page renders more than one disclosure and the gate walks past a closed
        // one to reach the next.
        Assert.True(subject.Seasons.Length >= 2, $"{subject.Seasons.Length} season(s); the page needs two");

        // AND AN UNPLACED BUCKET BESIDE THEM: season 0 is not a season, so it is in the records and out of
        // the count, and the page labels it "Season unknown" and sorts it last. With no bucket here that
        // label ships unscanned and NumberedSeasons has nothing to exclude.
        Assert.Contains(subject.Seasons, s => s.N == 0);
        Assert.Equal(subject.Seasons.Length - 1, subject.NumberedSeasons());
        Assert.All(subject.Seasons.Where(s => s.N == 0),
                   s => Assert.All(s.Eps, e => Assert.True(e.O, "an unplaced episode the owner does not own")));

        // Part-owned, and the gap is INSIDE a season rather than a whole season absent: a row marked
        // Missing beside rows that are not is what the mark has to be legible against.
        Assert.Contains(subject.Seasons, s => s.Eps.Any(e => e.O) && s.Eps.Any(e => !e.O));
        Assert.InRange(subject.OwnedEpisodes, 1, subject.TotalEpisodes - 1);

        // The owner's own columns, which is the part of the page no code here interprets.
        Assert.NotNull(subject.Ex);
        Assert.NotEmpty(subject.Ex);

        Assert.All(subject.Seasons, season =>
        {
            Assert.Equal(season.Total, season.Eps.Length);
            Assert.Equal(season.Owned, season.Eps.Count(e => e.O));
            Assert.All(season.Eps, e => Assert.False(string.IsNullOrWhiteSpace(e.T), "an episode with no title"));

            // Episode numbers are unique within a season, which is what @key on the row relies on.
            Assert.Equal(season.Eps.Length, season.Eps.Select(e => e.E).Distinct().Count());
        });

        // The numbered seasons run from 1 upwards with no gap, so a missing season cannot be mistaken for
        // one the owner simply has none of.
        Assert.Equal(Enumerable.Range(1, subject.NumberedSeasons()),
                     subject.Seasons.Where(s => s.N > 0).Select(s => s.N));
    }

    /// <remarks>
    /// The other two published records exist to be the shapes the furnished one is not, and a scan that
    /// landed on either expecting the first would be scanning a page that is not there.
    /// </remarks>
    [Fact]
    public void ThePublishedSeriesCoverTheThreeShapesTheSeriesPageRenders()
    {
        Assert.Equal(3, Catalogue.PublishedShows.Length);

        var shapes = Catalogue.PublishedShows.Select(Show).ToList();

        Assert.Contains(shapes, s => ConfidenceBand.Of(s) == Confidence.Confident && s.Seasons.Length > 0);
        Assert.Contains(shapes, s => s.Seasons.Length == 0);                      // no episode list at all
        Assert.Contains(shapes, s => ConfidenceBand.Of(s) != Confidence.Confident);

        // Each one's record agrees with its row in the index, which is what the page's byline and its
        // Details list are built from on one side and its tile on the other.
        foreach (var n in Catalogue.PublishedShows)
        {
            var record = Show(n);
            var row = JsonSerializer.Deserialize<ShowSummary[]>(Catalogue.ShowsJson(), Read)![n];

            Assert.Equal(row.Slug, record.Slug);
            Assert.Equal(row.T, record.T);
            Assert.Equal(row.I, record.I);
            Assert.Equal(row.Mc, record.Mc);
            // NumberedSeasons and not Seasons.Length, which is the whole point of the bucket: the index
            // row counts seasons and the record carries one entry that is not one.
            Assert.Equal(row.SeasonCount, record.NumberedSeasons());
            Assert.Equal(row.OwnedEpisodes, record.OwnedEpisodes);
            Assert.Equal(row.TotalEpisodes, record.TotalEpisodes);
            Assert.Equal(row.OwnedEpisodes, record.Seasons.Sum(s => s.Owned));
            Assert.Equal(row.TotalEpisodes, record.Seasons.Sum(s => s.Total));
        }
    }

    [Fact]
    public void WriteIntoPublishesEverySeriesRecordTheScanCanReach()
    {
        var root = Path.Combine(Path.GetTempPath(), "ml-gate-" + Guid.NewGuid());
        try
        {
            Catalogue.WriteInto(root);

            foreach (var n in Catalogue.PublishedShows)
            {
                var path = Path.Combine(root, "data", "tv", $"series-{n + 1}.json");

                Assert.True(File.Exists(path), path + " was not written, so that scan gets the host's 404 "
                            + "body and the page renders its not-found state instead of a series.");
            }

            Assert.Equal(Catalogue.FirstSlug,
                         JsonSerializer.Deserialize<ShowRecord>(
                             File.ReadAllText(Path.Combine(root, "data", "tv", Catalogue.FirstSlug + ".json")),
                             Read)!.Slug);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void NoRecordCarriesAPosterPathExceptTheOneThatIsMeantTo()
    {
        Assert.Single(Records, r => r.P is not null);
    }

    /// <remarks>
    /// Without data/meta.json beside data/movies.json the page's meta fetch gets the host's 404 body, which is HTML,
    /// the genre map comes back empty and the facet is not rendered: a gate scanning a panel with a fieldset
    /// missing and reading as though it had scanned them all.
    /// </remarks>
    [Fact]
    public void WriteIntoPublishesTheMetaBesideTheMovies()
    {
        var root = Path.Combine(Path.GetTempPath(), "ml-gate-" + Guid.NewGuid());
        try
        {
            Catalogue.WriteInto(root);

            var written = JsonSerializer.Deserialize<CatalogMeta>(
                File.ReadAllText(Path.Combine(root, "data", "meta.json")), Read)!;

            Assert.Equal(Meta.Genres, written.Genres);
            Assert.NotEmpty(written.Genres);
            Assert.Equal(Catalogue.Records,
                         JsonSerializer.Deserialize<MovieRecord[]>(
                             File.ReadAllText(Path.Combine(root, "data", "movies.json")), Read)!.Length);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}

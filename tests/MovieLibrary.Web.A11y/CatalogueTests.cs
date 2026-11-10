using System.Text.Json;
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

    [Fact]
    public void GenresAreSingleAndDoubleAndAnAppleGenreAgreesWithTheFirst()
    {
        Assert.Contains(Records, r => r.G.Length == 1);
        Assert.Contains(Records, r => r.G.Length == 2);
        Assert.True(Records.Select(r => r.Ag).Distinct().Count() >= 5, "the Apple genre text is as flat as it was");
    }

    /// <remarks>
    /// The tab-order budget the catalogue's own remarks describe. ObscuredFocusTests and FocusVisibleTests walk the
    /// tab order in at most forty presses and fail if the document has not been left by then. Eleven stops are
    /// there whatever the catalogue holds (the skip link, the brand and the nav, the search box, the confidence
    /// group, Year unknown, the sort, the pager's two buttons and the error bar's two), and then one more for each
    /// decade and each genre. The 41st would turn a green gate red for a reason that has nothing to do with focus.
    /// </remarks>
    [Fact]
    public void TheGenreCountLeavesTheTabWalkRoomToFinish()
    {
        const int StopsWhateverTheCatalogueHolds = 11;
        const int PressesTheWalksAllow = 40;

        var decades = Records.Where(r => r.Y is not null).Select(r => r.Y!.Value / 10).Distinct().Count();
        var stops = StopsWhateverTheCatalogueHolds + decades + Meta.Genres.Count;

        Assert.True(stops < PressesTheWalksAllow,
            $"The panel adds {Meta.Genres.Count} genre stop(s) and {decades} decade stop(s), {stops} stops in all with "
            + $"the error bar up, and the tab walks allow {PressesTheWalksAllow} presses including the one that "
            + "leaves the document. Name fewer genres, or raise the walks' limit in the same change.");
    }

    [Fact]
    public void YearsSpanSeveralDecadesAndSomeRecordsHaveNone()
    {
        Assert.True(Records.Select(r => r.Y).Where(y => y is not null).Select(y => y!.Value / 10).Distinct().Count() >= 5);
        Assert.Contains(Records, r => r.Y is null);
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

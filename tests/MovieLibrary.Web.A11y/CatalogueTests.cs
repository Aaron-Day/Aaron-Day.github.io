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
        var stops = TabWalk.ShellStops + decades + Meta.Genres.Count;

        Assert.True(stops < TabWalk.Presses,
            $"The panel adds {Meta.Genres.Count} genre stop(s) and {decades} decade stop(s), {stops} stops in all with "
            + $"the error bar up, and the tab walks allow {TabWalk.Presses} presses including the one that "
            + "leaves the document. Name fewer genres, or raise TabWalk.Presses in the same change.");
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
        var mostStops = TabWalk.GenresInTmdb + TabWalk.DecadesOfCinema + TabWalk.ShellStops;
        var needed = mostStops + 1;

        Assert.True(TabWalk.Presses >= needed,
            $"The walks allow {TabWalk.Presses} presses, and a library with all {TabWalk.GenresInTmdb} of TMDB's "
            + $"genres across all {TabWalk.DecadesOfCinema} decades of film has {mostStops} stops and needs {needed}. "
            + "A cap below that fits the gate's catalogue and fails (or silently truncates) on the real library.");
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

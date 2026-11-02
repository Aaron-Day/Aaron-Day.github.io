using System.Web;
using MovieLibrary.Core.Caching;
using MovieLibrary.Core.Identity;
using MovieLibrary.Core.Models;
using MovieLibrary.Core.Text;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// <see cref="CacheStoreAliasDirectorTests"/> pins the rule inside the store. These pin that the pipelines feed it:
/// <c>MoviePipeline</c> must store the row's directors on every entry it writes and ask with them, or the store
/// would have nothing to compare and every re-encode would cost a search. They run the owner's real pairs through
/// the real pipeline and count the searches that go out, which is the cost the alias exists to save.
/// </summary>
public class MoviePipelineAliasDirectorTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly IReadOnlyDictionary<string, int> NoOverrides = new Dictionary<string, int>();

    private static SheetRow Row(string title, string runtime, string directors)
        => new(title, runtime, directors, "", "Drama", "");

    private static TestPipeline.Harness EmptySearch()
        => TestPipeline.Create(0, req => TestPipeline.IsSearch(req)
            ? TestPipeline.Json(TestPipeline.EmptyResults) : TestPipeline.NotFound());

    private static int Searches(TestPipeline.Harness h) => h.Handler.Requests.Count(TestPipeline.IsSearch);

    // ---- what the pipeline writes ---------------------------------------------------------------------

    [Fact]
    public async Task EveryEntryCarriesTheRowsDirectorsInTheNormalizedFormTheKeyIsBuiltFrom()
    {
        var h = EmptySearch();   // both rows come back unmatched, so both are negative entries

        await h.Pipeline.RunAsync(
            [Row("Fargo", "1:38:00", "Joel Coen & Ethan Coen"), Row("Heat", "2:50:00", "Michael Mann")], NoOverrides, Ct);

        var fargo = Assert.Single(h.Cache.Entries, e => e.NormalizedTitle == "fargo");
        Assert.Equal("coen ethan joel", fargo.NormalizedDirectors);   // sorted name tokens, no ampersand
        Assert.Equal(TitleNormalizer.NormalizeDirectors("Ethan Coen, Joel Coen"), fargo.NormalizedDirectors);
        Assert.Equal("mann michael", Assert.Single(h.Cache.Entries, e => e.NormalizedTitle == "heat").NormalizedDirectors);
    }

    [Fact]
    public async Task AMatchedFilmsEntryCarriesItsDirectorsToo()
    {
        var h = TestPipeline.Create(0, req => req.RequestUri!.AbsolutePath switch
        {
            "/3/search/movie" => TestPipeline.Json("""{"results":[{"id":275,"title":"Fargo","release_date":"1996-03-08","popularity":30}]}"""),
            "/3/movie/275" => TestPipeline.Json("""{"id":275,"title":"Fargo","runtime":98,"release_date":"1996-03-08"}"""),
            "/3/movie/275/credits" => TestPipeline.Json("""{"crew":[{"job":"Director","name":"Joel Coen"}]}"""),
            _ => TestPipeline.NotFound(),
        });

        await h.Pipeline.RunAsync([Row("Fargo", "1:38:00", "Joel Coen")], NoOverrides, Ct);

        var entry = Assert.Single(h.Cache.Entries, e => !e.Negative);
        Assert.Equal("coen joel", entry.NormalizedDirectors);
    }

    [Fact]
    public async Task ARowWithNoDirectorIsStoredWithEmptyDirectorsNotNull()
    {
        // Null means "written before directors were stored"; empty means "the sheet named none". Neither aliases.
        var h = EmptySearch();

        await h.Pipeline.RunAsync([Row("Some Obscure Film", "1:35:00", "")], NoOverrides, Ct);

        Assert.Equal("", Assert.Single(h.Cache.Entries).NormalizedDirectors);
    }

    // ---- the five real pairs through the real pipeline ------------------------------------------------

    [Theory]
    [InlineData("BladeRunner", 1)]       // one key already: an exact hit, one search for the pair
    [InlineData("CaptainAmerica", 1)]    // 125 and 124 minutes, one director: the second aliases
    [InlineData("Hitman", 1)]            // 92 and 94 minutes, one director: the second aliases
    [InlineData("Carrie", 2)]            // 98 and 99 minutes, two directors: two films, two searches
    [InlineData("Overboard", 2)]         // 112 and 112 minutes, two directors: two films, two searches
    public async Task ARealPairCostsOneSearchWhenItIsOneFilmAndTwoWhenItIsTwo(string name, int searches)
    {
        var pair = RealFilmPair.Named(name);
        var h = EmptySearch();

        await h.Pipeline.RunAsync([pair.First, pair.Second], NoOverrides, Ct);

        Assert.Equal(searches, Searches(h));
        Assert.Equal(pair.SameFilm ? 1 : 2, searches);   // the table above agrees with the sheet's own verdict
    }

    [Fact]
    public async Task CarrieAndItsRemakeEachKeepTheirOwnTmdbId()
    {
        // The corruption, end to end. TMDB knows two Carries; the sheet has both, 98 and 99 minutes apart by one.
        var carrie = RealFilmPair.Named("Carrie");
        var h = TestPipeline.Create(0, req =>
        {
            var year = HttpUtility.ParseQueryString(req.RequestUri!.Query)["year"];
            return req.RequestUri.AbsolutePath switch
            {
                "/3/search/movie" => TestPipeline.Json(year == "1976"
                    ? """{"results":[{"id":7340,"title":"Carrie","release_date":"1976-11-03","popularity":30}]}"""
                    : """{"results":[{"id":127585,"title":"Carrie","release_date":"2013-10-18","popularity":30}]}"""),
                "/3/movie/7340" => TestPipeline.Json("""{"id":7340,"title":"Carrie","runtime":98,"release_date":"1976-11-03"}"""),
                "/3/movie/127585" => TestPipeline.Json("""{"id":127585,"title":"Carrie","runtime":99,"release_date":"2013-10-18"}"""),
                "/3/movie/7340/credits" => TestPipeline.Json("""{"crew":[{"job":"Director","name":"Stephen King"}]}"""),
                "/3/movie/127585/credits" => TestPipeline.Json("""{"crew":[{"job":"Director","name":"Kimberly Peirce"}]}"""),
                _ => TestPipeline.NotFound(),
            };
        });

        var result = await h.Pipeline.RunAsync([carrie.First, carrie.Second], NoOverrides, Ct);

        Assert.Equal([7340, 127585], result.Records.Select(r => r.I));
        Assert.Equal(2, Searches(h));
        Assert.Empty(result.ReviewQueue);   // two accepted matches; neither was silently inherited
    }

    // ---- what the guard costs, and where it must not cost -----------------------------------------------

    [Fact]
    public async Task AnEntryFromBeforeDirectorsWereStoredCostsOneSearchAndThenIsReplacedByOneThatCarriesThem()
    {
        var hitman = RealFilmPair.Named("Hitman");
        var h = EmptySearch();
        var old = TitleNormalizer.Normalize(hitman.First.Title);
        h.Cache.Put(new CacheEntry(
            IdentityKey.ForFilm(old, 92, TitleNormalizer.NormalizeDirectors(hitman.First.Directors)),
            4234, 0.95, TestPipeline.Now, Negative: false, old.Value, 92));   // the old shape: no directors

        await h.Pipeline.RunAsync([hitman.Second], NoOverrides, Ct);

        Assert.Equal(1, Searches(h));   // not aliased to the entry that cannot vouch for itself
        Assert.Equal("gens xavier", Assert.Single(h.Cache.Entries, e => e.RuntimeMinutes == 94).NormalizedDirectors);
    }

    [Fact]
    public async Task TwoFilmsWithNoDirectorAreSearchedSeparatelyThenEachHitsItsOwnKeyOnTheNextRun()
    {
        var h = EmptySearch();
        SheetRow[] rows = [Row("Alien", "1:57:00", ""), Row("Alien", "1:58:00", "")];

        await h.Pipeline.RunAsync(rows, NoOverrides, Ct);
        Assert.Equal(2, Searches(h));

        await h.Pipeline.RunAsync(rows, NoOverrides, Ct);
        Assert.Equal(2, Searches(h));   // warm: each film's own exact key, so the guard costs nothing twice
    }

    [Fact]
    public async Task ASeriesIsStoredWithEmptyDirectorsAndStillHitsItsKeyOnTheNextRun()
    {
        var tv = new FakeTv().Add(new FakeShow(2316, "The Office", [new FakeSeason(1, ["Pilot"])]));
        var h = TestPipeline.CreateTv(0, tv.Respond);
        SheetRow[] rows = [new("Pilot", "22:30:00", "", "The Office: The Complete Series", "Comedy", "The Office")];

        await h.Pipeline.RunAsync(rows, NoOverrides, Ct);
        var before = h.Handler.Requests.Count;
        await h.Pipeline.RunAsync(rows, NoOverrides, Ct);

        Assert.Equal("", Assert.Single(h.Cache.Entries).NormalizedDirectors);
        Assert.Equal(before, h.Handler.Requests.Count);
    }
}

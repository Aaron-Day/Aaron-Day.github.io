using System.Net;
using MovieLibrary.Core.Caching;
using MovieLibrary.Core.Identity;
using MovieLibrary.Core.Models;
using MovieLibrary.Core.Text;
using MovieLibrary.Enrich.Pipeline;
using MovieLibrary.Enrich.Tmdb;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// Task 14 amendment B. <c>MoviePipeline</c> used to hard-code <c>G: []</c> and <c>Cast: []</c>, and the stored
/// payload could not recover either (no genres field, credits thrown away). The matched film's details and
/// credits are fetched anyway for scoring, so keeping them costs no request; fetching them later costs ~2,900.
/// </summary>
public class MoviePipelineCastAndGenreTests
{
    private static readonly IReadOnlyDictionary<string, int> NoOverrides = new Dictionary<string, int>();

    // The sheet's genre is deliberately not TMDB's: Ag must stay Apple's own string.
    private static readonly SheetRow Fargo =
        new("Fargo", "1:38:00", "Joel Coen & Ethan Coen", "Blu-ray 2019", "Sci-Fi & Fantasy", "");

    private static string FargoKey => IdentityKey.ForFilm(TitleNormalizer.Normalize("Fargo"), 98,
        TitleNormalizer.NormalizeDirectors("Joel Coen & Ethan Coen"));

    private const string FargoDetails = """
        {"id":275,"title":"Fargo","overview":"A pregnant cop investigates.","runtime":98,"release_date":"1996-03-08",
         "vote_average":7.5,"poster_path":"/p.jpg","backdrop_path":"/b.jpg",
         "genres":[{"id":80,"name":"Crime"},{"id":53,"name":"Thriller"}]}
        """;

    // Six billed, out of order in the array. Billing 3 has a null character and no photo, as TMDB often gives.
    private const string FargoCredits = """
        {"cast":[
           {"name":"Fifth Billed","character":"E","profile_path":"/e.jpg","order":4},
           {"name":"Frances McDormand","character":"Marge Gunderson","profile_path":"/fm.jpg","order":0},
           {"name":"Sixth Billed","character":"F","profile_path":"/f.jpg","order":5},
           {"name":"William H. Macy","character":"Jerry Lundegaard","profile_path":"/wm.jpg","order":1},
           {"name":"Fourth Billed","character":"D","profile_path":"/d.jpg","order":3},
           {"name":"Steve Buscemi","character":null,"profile_path":null,"order":2}],
         "crew":[{"job":"Director","name":"Joel Coen"},{"job":"Director","name":"Ethan Coen"}]}
        """;

    private static Func<HttpRequestMessage, HttpResponseMessage> Routes(Dictionary<string, string> byPath)
        => req => byPath.TryGetValue(req.RequestUri!.AbsolutePath, out var body)
            ? TestPipeline.Json(body) : TestPipeline.NotFound();

    private static Dictionary<string, string> FargoRoutes() => new()
    {
        ["/3/search/movie"] = """{"results":[{"id":275,"title":"Fargo","release_date":"1996-03-08","popularity":30}]}""",
        ["/3/movie/275"] = FargoDetails,
        ["/3/movie/275/credits"] = FargoCredits,
    };

    private static IEnumerable<string> Paths(TestPipeline.Harness h)
        => h.Handler.Requests.Select(r => r.RequestUri!.AbsolutePath);

    private static Func<HttpRequestMessage, HttpResponseMessage> OnlyAnswers(
        string path, string body, HttpStatusCode? status = null)
        => req => req.RequestUri!.AbsolutePath == path
            ? (status is { } s ? new HttpResponseMessage(s) : TestPipeline.Json(body))
            : throw new Xunit.Sdk.XunitException($"Unexpected request: {req.RequestUri}");

    private static async Task SeedAsync(TestPipeline.Harness h, StoredMovie payload)
    {
        h.Cache.Put(new CacheEntry(FargoKey, 275, 0.85, TestPipeline.Now, false, "fargo", 98));
        await h.Cache.WritePayloadAsync("movies/275.json", payload, CancellationToken.None);
    }

    private static TmdbMovie BareFargo() =>
        new(275, "Fargo", "Old", null, 98, "1996-03-08", 7.5, "/old.jpg", null);

    [Fact]
    public async Task TheMatchedFilmsGenreIdsLandInGInTmdbOrder()
    {
        var h = TestPipeline.Create(0, Routes(FargoRoutes()));

        var result = await h.Pipeline.RunAsync([Fargo], NoOverrides, CancellationToken.None);

        Assert.Equal([80, 53], Assert.Single(result.Records).G);
    }

    [Fact]
    public async Task TheTopBilledCastLandsInCastWithNameCharacterAndPhoto()
    {
        var h = TestPipeline.Create(0, Routes(FargoRoutes()));

        var result = await h.Pipeline.RunAsync([Fargo], NoOverrides, CancellationToken.None);

        Assert.Equal(
            [("Frances McDormand", "Marge Gunderson", "/fm.jpg"), ("William H. Macy", "Jerry Lundegaard", "/wm.jpg"),
             ("Steve Buscemi", "", null), ("Fourth Billed", "D", "/d.jpg")],
            Assert.Single(result.Records).Cast.Select(c => (c.N, c.C, c.P)));
    }

    [Fact]
    public async Task CastIsCappedAtTheFourTopBilledNotTheWholeCredits()
    {
        var h = TestPipeline.Create(0, Routes(FargoRoutes()));

        var result = await h.Pipeline.RunAsync([Fargo], NoOverrides, CancellationToken.None);

        var cast = Assert.Single(result.Records).Cast;
        Assert.Equal(4, cast.Length);
        Assert.DoesNotContain(cast, c => c.N is "Fifth Billed" or "Sixth Billed");
    }

    [Fact]
    public async Task AppleGenreIsKeptExactlyAsTheSheetHasIt()
    {
        var h = TestPipeline.Create(0, Routes(FargoRoutes()));

        var result = await h.Pipeline.RunAsync([Fargo], NoOverrides, CancellationToken.None);

        Assert.Equal("Sci-Fi & Fantasy", Assert.Single(result.Records).Ag);
    }

    [Fact]
    public async Task GenresAndCastAreKeptInTheStoredPayloadSoNoLaterRunMustRefetchThem()
    {
        var h = TestPipeline.Create(0, Routes(FargoRoutes()));

        await h.Pipeline.RunAsync([Fargo], NoOverrides, CancellationToken.None);

        var stored = await h.Cache.ReadPayloadAsync<StoredMovie>("movies/275.json", CancellationToken.None);
        Assert.Equal(["Crime", "Thriller"], stored!.Movie!.Genres!.Select(g => g.Name));
        Assert.Equal(["Frances McDormand", "William H. Macy", "Steve Buscemi", "Fourth Billed"],
                     stored.Cast!.Select(c => c.Name));
    }

    [Fact]
    public async Task ASecondRunServesGenresAndCastFromTheCacheWithNoRequests()
    {
        var h = TestPipeline.Create(0, Routes(FargoRoutes()));
        var cold = await h.Pipeline.RunAsync([Fargo], NoOverrides, CancellationToken.None);
        var before = h.Handler.Requests.Count;

        var warm = await h.Pipeline.RunAsync([Fargo], NoOverrides, CancellationToken.None);

        Assert.Equal(before, h.Handler.Requests.Count);
        Assert.Equal(cold.Records[0].G, warm.Records[0].G);
        Assert.Equal(cold.Records[0].Cast.Select(c => c.N), warm.Records[0].Cast.Select(c => c.N));
        Assert.Equal(cold.Genres, warm.Genres);
    }

    [Fact]
    public async Task AnUnmatchedFilmHasNoGenresAndNoCast()
    {
        var h = TestPipeline.Create(0, req => TestPipeline.IsSearch(req)
            ? TestPipeline.Json(TestPipeline.EmptyResults) : TestPipeline.NotFound());

        var result = await h.Pipeline.RunAsync([Fargo], NoOverrides, CancellationToken.None);

        var r = Assert.Single(result.Records);
        Assert.Equal((0, 0), (r.G.Length, r.Cast.Length));
        Assert.Empty(result.Genres);
    }

    // ---- the paths that do NOT see the credits during a search: refresh, override, a payload with no cast ----

    private const string FargoWithCredits = """
        {"id":275,"title":"Fargo","overview":"Fresh","runtime":98,"release_date":"1996-03-08","vote_average":7.5,
         "poster_path":"/p.jpg","backdrop_path":"/b.jpg","genres":[{"id":80,"name":"Crime"}],
         "credits":{"cast":[{"name":"Frances McDormand","character":"Marge","profile_path":"/fm.jpg","order":0}],"crew":[]}}
        """;

    [Fact]
    public async Task ARefreshedPayloadTakesDetailsGenresAndCastInOneRequest()
    {
        var h = TestPipeline.Create(0, OnlyAnswers("/3/movie/275", FargoWithCredits));
        await SeedAsync(h, new StoredMovie(TestPipeline.Now.AddDays(-200), BareFargo()));

        var result = await h.Pipeline.RunAsync([Fargo], NoOverrides, CancellationToken.None);

        var req = Assert.Single(h.Handler.Requests);
        Assert.Equal("?append_to_response=credits", req.RequestUri!.Query);
        var r = Assert.Single(result.Records);
        Assert.Equal([80], r.G);
        Assert.Equal(["Frances McDormand"], r.Cast.Select(c => c.N));
        var stored = await h.Cache.ReadPayloadAsync<StoredMovie>("movies/275.json", CancellationToken.None);
        Assert.Equal("Fresh", stored!.Movie!.Overview);
        Assert.Equal(["Frances McDormand"], stored.Cast!.Select(c => c.Name));
    }

    [Fact]
    public async Task AnOverriddenFilmGetsGenresAndCastWithoutASearch()
    {
        var h = TestPipeline.Create(0, OnlyAnswers("/3/movie/275", FargoWithCredits));

        var result = await h.Pipeline.RunAsync([Fargo], new Dictionary<string, int> { [MoviePipeline.OverrideKey("Fargo", 98)] = 275 },
                                               CancellationToken.None);

        Assert.Single(h.Handler.Requests);
        var r = Assert.Single(result.Records);
        Assert.Equal([80], r.G);
        Assert.Equal(["Frances McDormand"], r.Cast.Select(c => c.N));
    }

    [Fact]
    public async Task AFreshPayloadThatNeverRecordedItsCastFetchesItOnceAndKeepsIt()
    {
        var h = TestPipeline.Create(0, OnlyAnswers("/3/movie/275/credits", FargoCredits));
        await SeedAsync(h, new StoredMovie(TestPipeline.Now, BareFargo()));   // Cast == null: never recorded

        var first = await h.Pipeline.RunAsync([Fargo], NoOverrides, CancellationToken.None);
        var second = await h.Pipeline.RunAsync([Fargo], NoOverrides, CancellationToken.None);

        Assert.Equal(["/3/movie/275/credits"], Paths(h));        // once, not once per run
        Assert.Equal(4, first.Records[0].Cast.Length);
        Assert.Equal(4, second.Records[0].Cast.Length);
    }

    [Fact]
    public async Task ACastTmdbDoesNotHaveIsRecordedAsEmptyAndNotAskedForAgain()
    {
        var h = TestPipeline.Create(0, OnlyAnswers("/3/movie/275/credits", "", HttpStatusCode.NotFound));
        await SeedAsync(h, new StoredMovie(TestPipeline.Now, BareFargo()));

        await h.Pipeline.RunAsync([Fargo], NoOverrides, CancellationToken.None);
        await h.Pipeline.RunAsync([Fargo], NoOverrides, CancellationToken.None);

        Assert.Single(h.Handler.Requests);
        var stored = await h.Cache.ReadPayloadAsync<StoredMovie>("movies/275.json", CancellationToken.None);
        Assert.NotNull(stored!.Cast);
        Assert.Empty(stored.Cast!);
    }

    [Fact]
    public async Task ACastTheBudgetCouldNotFetchStaysUnrecordedSoALaterRunTriesAgain()
    {
        // Budget 1 is spent on the override "Aaa", so Fargo's missing cast cannot be fetched this run.
        var h = TestPipeline.Create(budget: 1, req => req.RequestUri!.AbsolutePath == "/3/movie/1"
            ? TestPipeline.Json("""{"id":1,"title":"Aaa"}""") : TestPipeline.NotFound());
        await SeedAsync(h, new StoredMovie(TestPipeline.Now, BareFargo()));

        var result = await h.Pipeline.RunAsync(
            [new SheetRow("Aaa", "1:30:00", "X", "", "Drama", ""), Fargo],
            new Dictionary<string, int> { [MoviePipeline.OverrideKey("Aaa", 90)] = 1 }, CancellationToken.None);

        Assert.True(result.StoppedOnBudget);
        Assert.Empty(result.Records.Single(r => r.T == "Fargo").Cast);
        var stored = await h.Cache.ReadPayloadAsync<StoredMovie>("movies/275.json", CancellationToken.None);
        Assert.Null(stored!.Cast);   // null means "not recorded", [] would mean "TMDB has none"
    }

    // ---- which film's data, and which genres are named ----

    [Fact]
    public async Task TheCastBelongsToTheMatchedFilmNotToAnotherCandidateThatWasAlsoCredited()
    {
        // Two films called Fargo, neither at the sheet's 98 minutes; only the director separates them and the
        // less popular one (id 2, Joel Coen) is right. Both are sent for credits; only id 2's cast is ours.
        var h = TestPipeline.Create(0, Routes(new()
        {
            ["/3/search/movie"] = """
                {"results":[{"id":1,"title":"Fargo","release_date":"1996-03-08","popularity":80},
                            {"id":2,"title":"Fargo","release_date":"1996-03-08","popularity":30}]}
                """,
            ["/3/movie/1"] = """{"id":1,"title":"Fargo","runtime":120,"release_date":"1996-03-08","genres":[{"id":99,"name":"Documentary"}]}""",
            ["/3/movie/2"] = """{"id":2,"title":"Fargo","runtime":70,"release_date":"1996-03-08","genres":[{"id":80,"name":"Crime"}]}""",
            ["/3/movie/1/credits"] = """{"crew":[{"job":"Director","name":"Noah Hawley"}],"cast":[{"name":"Wrong Actor","order":0}]}""",
            ["/3/movie/2/credits"] = """{"crew":[{"job":"Director","name":"Joel Coen"}],"cast":[{"name":"Right Actor","order":0}]}""",
        }));

        var result = await h.Pipeline.RunAsync([Fargo], NoOverrides, CancellationToken.None);

        var r = Assert.Single(result.Records);
        Assert.Equal(2, r.I);
        Assert.Equal(["Right Actor"], r.Cast.Select(c => c.N));
        Assert.Equal([80], r.G);
    }

    [Fact]
    public async Task TheGenreMapNamesExactlyTheIdsEmittedAndNoCandidateThatLostThe()
    {
        // The decoy (999) is enriched for scoring and carries genre 99, but it is not the film: not in the map.
        var h = TestPipeline.Create(0, Routes(new()
        {
            ["/3/search/movie"] = """
                {"results":[{"id":999,"title":"Fargo Rock City","release_date":"2000-01-01","popularity":80},
                            {"id":275,"title":"Fargo","release_date":"1996-03-08","popularity":30}]}
                """,
            ["/3/movie/275"] = FargoDetails,
            ["/3/movie/275/credits"] = FargoCredits,
            ["/3/movie/999"] = """{"id":999,"title":"Fargo Rock City","runtime":120,"genres":[{"id":99,"name":"Documentary"}]}""",
        }));

        var result = await h.Pipeline.RunAsync([Fargo], NoOverrides, CancellationToken.None);

        Assert.Equal([(53, "Thriller"), (80, "Crime")], result.Genres.OrderBy(g => g.Key).Select(g => (g.Key, g.Value)));
        Assert.Equal(result.Records.SelectMany(r => r.G).Distinct().Order(), result.Genres.Keys.Order());
    }
}

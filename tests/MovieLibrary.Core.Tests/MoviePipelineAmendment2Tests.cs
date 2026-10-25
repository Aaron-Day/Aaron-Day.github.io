using System.Web;
using MovieLibrary.Core.Caching;
using MovieLibrary.Core.Identity;
using MovieLibrary.Core.Models;
using MovieLibrary.Core.Text;
using MovieLibrary.Enrich.Pipeline;
using MovieLibrary.Enrich.Tmdb;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// Task 14 amendment H (belongs_to_collection is in the details response being parsed; discarding it would mean
/// refetching 1,457 films later, the same mistake amendment B fixed for genres and cast) and the pipeline half of
/// amendment G (a credits body the parser cannot read must not stop a seed).
/// </summary>
public class MoviePipelineAmendment2Tests
{
    private static readonly IReadOnlyDictionary<string, int> NoOverrides = new Dictionary<string, int>();

    private static readonly SheetRow Fargo =
        new("Fargo", "1:38:00", "Joel Coen & Ethan Coen", "Blu-ray 2019", "Crime", "");

    private static string FargoKey => IdentityKey.ForFilm(TitleNormalizer.Normalize("Fargo"), 98,
        TitleNormalizer.NormalizeDirectors("Joel Coen & Ethan Coen"));

    /// <summary>Fargo's details with <paramref name="extra"/> spliced in as further JSON members (or nothing).</summary>
    private static string Details(string extra = "") => $$"""
        {"id":275,"title":"Fargo","overview":"A pregnant cop investigates.","runtime":98,"release_date":"1996-03-08",
         "vote_average":7.5,"poster_path":"/p.jpg","backdrop_path":"/b.jpg"{{extra}}}
        """;

    private const string Halloween = """,  "belongs_to_collection":{"id":2344,"name":"Halloween Collection","poster_path":"/c.jpg","backdrop_path":null}""";

    private const string EmptyCredits = """,  "credits":{"cast":[],"crew":[]}""";

    private const string Credits = """{"cast":[{"name":"Frances McDormand","order":0}],"crew":[{"job":"Director","name":"Joel Coen"},{"job":"Director","name":"Ethan Coen"}]}""";

    private static Func<HttpRequestMessage, HttpResponseMessage> Routes(Dictionary<string, string> byPath)
        => req => byPath.TryGetValue(req.RequestUri!.AbsolutePath, out var body)
            ? TestPipeline.Json(body) : TestPipeline.NotFound();

    private static Dictionary<string, string> FargoRoutes(string extra = "") => new()
    {
        ["/3/search/movie"] = """{"results":[{"id":275,"title":"Fargo","release_date":"1996-03-08","popularity":30}]}""",
        ["/3/movie/275"] = Details(extra),
        ["/3/movie/275/credits"] = Credits,
    };

    private static Func<HttpRequestMessage, HttpResponseMessage> OnlyAnswers(string path, string body)
        => req => req.RequestUri!.AbsolutePath == path
            ? TestPipeline.Json(body)
            : throw new Xunit.Sdk.XunitException($"Unexpected request: {req.RequestUri}");

    private static async Task SeedStaleAsync(TestPipeline.Harness h)
    {
        h.Cache.Put(new CacheEntry(FargoKey, 275, 0.85, TestPipeline.Now, false, "fargo", 98));
        var stale = new TmdbMovie(275, "Fargo", "Old", null, 98, "1996-03-08", 7.5, "/old.jpg", null);
        await h.Cache.WritePayloadAsync("movies/275.json",
            new StoredMovie(TestPipeline.Now.AddDays(-200), stale, []), CancellationToken.None);
    }

    // ---- H ----

    [Fact]
    public async Task TheCollectionNameLandsInCol()
    {
        var h = TestPipeline.Create(0, Routes(FargoRoutes(Halloween)));

        var result = await h.Pipeline.RunAsync([Fargo], NoOverrides, CancellationToken.None);

        Assert.Equal("Halloween Collection", Assert.Single(result.Records).Col);
    }

    [Theory]
    [InlineData("")]                                                              // no such member
    [InlineData(""", "belongs_to_collection":null""")]                            // what TMDB sends for a standalone film
    [InlineData(""", "belongs_to_collection":{"id":1,"name":null}""")]
    [InlineData(""", "belongs_to_collection":{"id":1,"name":""}""")]
    [InlineData(""", "belongs_to_collection":{"id":1,"name":"   "}""")]
    public async Task AFilmInNoUsableCollectionHasNullCol(string extra)
    {
        var h = TestPipeline.Create(0, Routes(FargoRoutes(extra)));

        var result = await h.Pipeline.RunAsync([Fargo], NoOverrides, CancellationToken.None);

        Assert.Null(Assert.Single(result.Records).Col);
    }

    [Fact]
    public async Task ACollectionNameIsTrimmed()
    {
        var h = TestPipeline.Create(0, Routes(FargoRoutes(""", "belongs_to_collection":{"id":1,"name":"  Alien Collection "}""")));

        var result = await h.Pipeline.RunAsync([Fargo], NoOverrides, CancellationToken.None);

        Assert.Equal("Alien Collection", Assert.Single(result.Records).Col);
    }

    [Fact]
    public async Task AnUnmatchedFilmHasNoCollection()
    {
        var h = TestPipeline.Create(0, req => TestPipeline.IsSearch(req)
            ? TestPipeline.Json(TestPipeline.EmptyResults) : TestPipeline.NotFound());

        var result = await h.Pipeline.RunAsync([Fargo], NoOverrides, CancellationToken.None);

        Assert.Null(Assert.Single(result.Records).Col);
    }

    [Fact]
    public async Task TheCollectionIsKeptInTheStoredPayloadAndServedFromTheCache()
    {
        var h = TestPipeline.Create(0, Routes(FargoRoutes(Halloween)));
        await h.Pipeline.RunAsync([Fargo], NoOverrides, CancellationToken.None);
        var before = h.Handler.Requests.Count;

        var warm = await h.Pipeline.RunAsync([Fargo], NoOverrides, CancellationToken.None);

        var stored = await h.Cache.ReadPayloadAsync<StoredMovie>("movies/275.json", CancellationToken.None);
        Assert.Equal(("Halloween Collection", 2344),
                     (stored!.Movie!.BelongsToCollection!.Name, stored.Movie.BelongsToCollection.Id));
        Assert.Equal(before, h.Handler.Requests.Count);
        Assert.Equal("Halloween Collection", Assert.Single(warm.Records).Col);
    }

    [Fact]
    public async Task ARefreshedPayloadTakesTheCollectionFromTheSameSingleRequest()
    {
        var h = TestPipeline.Create(0, OnlyAnswers("/3/movie/275", Details(Halloween + EmptyCredits)));
        await SeedStaleAsync(h);

        var result = await h.Pipeline.RunAsync([Fargo], NoOverrides, CancellationToken.None);

        Assert.Single(h.Handler.Requests);
        Assert.Equal("Halloween Collection", Assert.Single(result.Records).Col);
    }

    [Fact]
    public async Task AnOverriddenFilmTakesTheCollectionFromTheSameSingleRequest()
    {
        var h = TestPipeline.Create(0, OnlyAnswers("/3/movie/275", Details(Halloween + EmptyCredits)));

        var result = await h.Pipeline.RunAsync([Fargo], new Dictionary<string, int> { ["fargo"] = 275 }, CancellationToken.None);

        Assert.Single(h.Handler.Requests);
        Assert.Equal("Halloween Collection", Assert.Single(result.Records).Col);
    }

    // ---- G, at the pipeline ----

    [Fact]
    public async Task AMalformedCreditsBodyForOneFilmDoesNotStopTheRunForTheRest()
    {
        var byPath = new Dictionary<string, string>
        {
            ["/3/movie/1"] = """{"id":1,"title":"Alpha","runtime":90,"release_date":"2000-01-01"}""",
            ["/3/movie/1/credits"] = """{"crew":null,"cast":[null,5,{"name":7,"order":"x"}]}""",   // nothing in it is usable
            ["/3/movie/2"] = """{"id":2,"title":"Bravo","runtime":100,"release_date":"2001-01-01"}""",
            ["/3/movie/2/credits"] = """{"crew":[{"job":"Director","name":"Dir B"}],"cast":[{"name":"Good Actor","order":0}]}""",
        };
        var h = TestPipeline.Create(0, req =>
        {
            if (!TestPipeline.IsSearch(req))
                return byPath.TryGetValue(req.RequestUri!.AbsolutePath, out var body) ? TestPipeline.Json(body) : TestPipeline.NotFound();
            var alpha = HttpUtility.ParseQueryString(req.RequestUri!.Query)["query"] == "Alpha";
            return TestPipeline.Json(alpha
                ? """{"results":[{"id":1,"title":"Alpha","release_date":"2000-01-01","popularity":10}]}"""
                : """{"results":[{"id":2,"title":"Bravo","release_date":"2001-01-01","popularity":10}]}""");
        });

        var result = await h.Pipeline.RunAsync(
            [new SheetRow("Alpha", "1:30:00", "Dir A", "", "Drama", ""), new SheetRow("Bravo", "1:40:00", "Dir B", "", "Drama", "")],
            NoOverrides, CancellationToken.None);

        Assert.Equal(2, result.Records.Count);
        var bravo = result.Records.Single(r => r.T == "Bravo");
        Assert.Equal(2, bravo.I);
        Assert.Equal(["Good Actor"], bravo.Cast.Select(c => c.N));
    }
}

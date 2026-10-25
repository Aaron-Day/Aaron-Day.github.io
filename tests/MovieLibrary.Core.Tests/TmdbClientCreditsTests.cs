using System.Net;
using MovieLibrary.Core.Throttling;
using MovieLibrary.Enrich.Tmdb;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// Task 14 amendment B: genres and cast must be read from the responses the pipeline already fetches.
/// Before this, <c>TmdbMovie</c> had no genres and <c>GetDirectorsAsync</c> threw the cast away.
/// </summary>
public class TmdbClientCreditsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private static (TmdbClient Client, StubHandler Handler) Build(
        Func<HttpRequestMessage, HttpResponseMessage> respond, string token = "eyJ.stub.token")
    {
        var handler = new StubHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.themoviedb.org/") };
        return (new TmdbClient(http, token, new SlidingWindowRateLimiter(100_000, TimeSpan.FromSeconds(1)),
                               new RequestBudget(0), () => T0), handler);
    }

    // Six people, deliberately out of billing order in the array: `order` decides, not position.
    private const string Credits = """
        {"id":348,
         "cast":[
           {"name":"Sixth Billed","character":"Extra","profile_path":"/f.jpg","order":5},
           {"name":"Tom Skerritt","character":"Dallas","profile_path":"/b.jpg","order":1},
           {"name":"Sigourney Weaver","character":"Ripley","profile_path":"/a.jpg","order":0},
           {"name":"Fifth Billed","character":"Extra","profile_path":null,"order":4},
           {"name":"Veronica Cartwright","character":"Lambert","profile_path":null,"order":2},
           {"name":"Harry Dean Stanton","character":"Brett","profile_path":"/d.jpg","order":3}],
         "crew":[{"job":"Producer","name":"Gordon Carroll"},{"job":"Director","name":"Ridley Scott"}]}
        """;

    private const string Details = """
        {"id":348,"title":"Alien","runtime":117,"release_date":"1979-05-25","vote_average":8.1,
         "genres":[{"id":878,"name":"Science Fiction"},{"id":27,"name":"Horror"}],
         "credits":{"cast":[
           {"name":"Sigourney Weaver","character":"Ripley","profile_path":"/a.jpg","order":0},
           {"name":"Tom Skerritt","character":"Dallas","profile_path":"/b.jpg","order":1},
           {"name":"John Hurt","character":"Kane","profile_path":"/c.jpg","order":2}],"crew":[]}}
        """;

    [Fact]
    public async Task GetMovieReadsTheGenresTheDetailsResponseCarries()
    {
        var (client, _) = Build(_ => Json(Details));

        var movie = await client.GetMovieAsync(348, CancellationToken.None);

        Assert.Equal([(878, "Science Fiction"), (27, "Horror")], movie!.Genres!.Select(g => (g.Id, g.Name)));
    }

    [Fact]
    public async Task CreditsReturnTheDirectorsAndTheTopBilledCastInBillingOrder()
    {
        var (client, handler) = Build(_ => Json(Credits));

        var credits = await client.GetCreditsAsync(348, 4, CancellationToken.None);

        Assert.Single(handler.Requests);
        Assert.Equal(["Ridley Scott"], credits!.Directors);
        Assert.Equal(
            [("Sigourney Weaver", "Ripley", "/a.jpg"), ("Tom Skerritt", "Dallas", "/b.jpg"),
             ("Veronica Cartwright", "Lambert", null), ("Harry Dean Stanton", "Brett", "/d.jpg")],
            credits.Cast.Select(c => (c.Name, c.Character, c.ProfilePath)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(6, 6)]
    [InlineData(50, 6)]
    public async Task TheCastLimitIsHonouredExactly(int limit, int expected)
    {
        var (client, _) = Build(_ => Json(Credits));

        var credits = await client.GetCreditsAsync(348, limit, CancellationToken.None);

        Assert.Equal(expected, credits!.Cast.Count);
    }

    [Fact]
    public async Task CreditsWithNoCastArrayStillYieldTheDirectors()
    {
        var (client, _) = Build(_ => Json("""{"crew":[{"job":"Director","name":"Joel Coen"}]}"""));

        var credits = await client.GetCreditsAsync(275, 4, CancellationToken.None);

        Assert.Equal(["Joel Coen"], credits!.Directors);
        Assert.Empty(credits.Cast);
    }

    [Fact]
    public async Task GetDirectorsStillReturnsJustTheDirectorsFromTheSameSingleRequest()
    {
        var (client, handler) = Build(_ => Json(Credits));

        var directors = await client.GetDirectorsAsync(348, CancellationToken.None);

        Assert.Equal(["Ridley Scott"], directors);
        Assert.Equal("/3/movie/348/credits", handler.Requests.Single().RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task ACastMemberWithNoNameIsSkippedAndDoesNotUseUpALimitSlot()
    {
        var (client, _) = Build(_ => Json("""
            {"crew":[],"cast":[{"name":"","character":"X","order":0},{"name":"Real Actor","character":"Y","order":1}]}
            """));

        var credits = await client.GetCreditsAsync(1, 1, CancellationToken.None);

        Assert.Equal(["Real Actor"], credits!.Cast.Select(c => c.Name));
    }

    [Fact]
    public async Task DetailsAndCastComeBackInOneRequestWithAppendToResponse()
    {
        var (client, handler) = Build(_ => Json(Details));

        var both = await client.GetMovieWithCreditsAsync(348, 2, CancellationToken.None);

        var req = Assert.Single(handler.Requests);
        Assert.Equal("/3/movie/348", req.RequestUri!.AbsolutePath);
        Assert.Equal("?append_to_response=credits", req.RequestUri!.Query);
        Assert.Equal(["Science Fiction", "Horror"], both!.Movie.Genres!.Select(g => g.Name));
        Assert.Equal(["Sigourney Weaver", "Tom Skerritt"], both.Cast.Select(c => c.Name)); // limit 2
        Assert.Equal(117, both.Movie.Runtime);
    }

    [Fact]
    public async Task AV3KeyIsAppendedAfterTheAppendToResponseParameter()
    {
        var (client, handler) = Build(_ => Json(Details), token: "0123456789abcdef0123456789abcdef");

        await client.GetMovieWithCreditsAsync(348, 4, CancellationToken.None);

        Assert.Equal("?append_to_response=credits&api_key=0123456789abcdef0123456789abcdef",
                     handler.Requests.Single().RequestUri!.Query);
    }

    [Fact]
    public async Task ADetailsBodyWithoutACreditsObjectGivesAnEmptyCastNotAFailure()
    {
        var (client, _) = Build(_ => Json("""{"id":275,"title":"Fargo","runtime":98}"""));

        var both = await client.GetMovieWithCreditsAsync(275, 4, CancellationToken.None);

        Assert.Equal(275, both!.Movie.Id);
        Assert.Empty(both.Cast);
    }

    [Fact]
    public async Task AMissingFilmIsNullForBothCalls()
    {
        var (client, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.Null(await client.GetMovieWithCreditsAsync(1, 4, CancellationToken.None));
        Assert.Null(await client.GetCreditsAsync(1, 4, CancellationToken.None));
    }
}

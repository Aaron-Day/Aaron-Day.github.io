using System.Net;
using MovieLibrary.Core.Throttling;
using MovieLibrary.Enrich.Tmdb;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// Task 14 amendment G. The credits parse now sits in the scoring path, so a member it cannot read must be
/// skipped, not allowed to abort a seed that has already spent thousands of requests. None of these shapes is
/// modelled by the stubs elsewhere, and the parse has never seen a real TMDB response.
/// </summary>
public class TmdbClientCreditsRobustnessTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private static TmdbClient Client(string body)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.themoviedb.org/") };
        return new TmdbClient(http, "eyJ.stub.token", new SlidingWindowRateLimiter(100_000, TimeSpan.FromSeconds(1)),
                              new RequestBudget(0), () => T0);
    }

    [Fact]
    public async Task ACreditsObjectThatIsNullLeavesTheMovieAndAnEmptyCast()
    {
        var both = await Client("""{"id":275,"title":"Fargo","runtime":98,"credits":null}""")
            .GetMovieWithCreditsAsync(275, 4, CancellationToken.None);

        Assert.Equal(275, both!.Movie.Id);
        Assert.Empty(both.Cast);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"credits\"")]
    [InlineData("5")]
    [InlineData("true")]
    public async Task ACreditsValueThatIsNotAnObjectGivesAnEmptyCast(string credits)
    {
        var both = await Client($$"""{"id":275,"title":"Fargo","credits":{{credits}}}""")
            .GetMovieWithCreditsAsync(275, 4, CancellationToken.None);

        Assert.Equal(275, both!.Movie.Id);
        Assert.Empty(both.Cast);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("\"cast\"")]
    [InlineData("7")]
    public async Task ACastThatIsNotAnArrayGivesAnEmptyCastAndKeepsTheDirectors(string cast)
    {
        var credits = await Client($$"""{"cast":{{cast}},"crew":[{"job":"Director","name":"Joel Coen"}]}""")
            .GetCreditsAsync(275, 4, CancellationToken.None);

        Assert.Empty(credits!.Cast);
        Assert.Equal(["Joel Coen"], credits.Directors);
    }

    [Fact]
    public async Task AnUnparseableCastMemberIsSkippedNotFatalAndTheRestAreKept()
    {
        var credits = await Client("""
            {"crew":[],"cast":[null, 5, "x", [], {"name":5,"order":0}, {"name":null,"order":1}, {"name":"   ","order":2},
                               {"character":"nameless","order":3}, {"name":"Good One","order":4}]}
            """).GetCreditsAsync(275, 4, CancellationToken.None);

        Assert.Equal(["Good One"], credits!.Cast.Select(c => c.Name));
    }

    [Theory]
    [InlineData("\"2\"")]
    [InlineData("1.5")]
    [InlineData("null")]
    [InlineData("1e30")]
    [InlineData("true")]
    [InlineData("{}")]
    public async Task AnOrderThatIsNotAnIntegerKeepsTheMemberAndBillsItAfterTheNumberedOnes(string order)
    {
        // A name is the data; a bad billing number only loses the member its place, and array position is the tiebreak.
        var credits = await Client($$"""
            {"crew":[],"cast":[{"name":"Bad Order","order":{{order}}},{"name":"Second","order":1},{"name":"First","order":0}]}
            """).GetCreditsAsync(275, 4, CancellationToken.None);

        Assert.Equal(["First", "Second", "Bad Order"], credits!.Cast.Select(c => c.Name));
    }

    [Fact]
    public async Task ACharacterOrPhotoThatIsNotAStringBecomesNullInsteadOfThrowing()
    {
        var credits = await Client("""{"crew":[],"cast":[{"name":"A","character":7,"profile_path":{},"order":0}]}""")
            .GetCreditsAsync(275, 4, CancellationToken.None);

        var member = Assert.Single(credits!.Cast);
        Assert.Equal(("A", null, null), (member.Name, member.Character, member.ProfilePath));
    }

    [Theory]
    [InlineData("""{"cast":[]}""")]
    [InlineData("""{"crew":null}""")]
    [InlineData("""{"crew":"x"}""")]
    [InlineData("""{"crew":{}}""")]
    [InlineData("""[]""")]
    public async Task CreditsWithNoUsableCrewGiveNoDirectorsInsteadOfThrowing(string body)
    {
        var credits = await Client(body).GetCreditsAsync(275, 4, CancellationToken.None);

        Assert.Empty(credits!.Directors);
    }

    [Fact]
    public async Task ACrewMemberWithAnUnreadableJobOrNameIsSkipped()
    {
        var directors = await Client("""
            {"crew":[null, 5, {"job":5,"name":"A"}, {"job":"Director","name":7}, {"job":"Director","name":null},
                     {"job":"Producer","name":"B"}, {"job":"Director","name":"Real Director"}]}
            """).GetDirectorsAsync(275, CancellationToken.None);

        Assert.Equal(["Real Director"], directors);
    }
}

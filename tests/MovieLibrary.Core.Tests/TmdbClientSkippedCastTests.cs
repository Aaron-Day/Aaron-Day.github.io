using System.Net;
using MovieLibrary.Core.Throttling;
using MovieLibrary.Enrich.Tmdb;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// Task 17 Step H. A cast member TMDB sends that cannot be read is skipped on purpose, so that one odd entry cannot abort
/// a seed that has already spent thousands of requests. The price of that tolerance is silence, so the client counts what
/// it skips and the job summary prints the number: a TMDB shape change (a renamed <c>name</c>) then shows up as thousands
/// instead of nothing.
/// </summary>
public class TmdbClientSkippedCastTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);

    private static TmdbClient Client(string body)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.themoviedb.org/") };
        return new TmdbClient(http, "eyJ.stub.token", new SlidingWindowRateLimiter(100_000, TimeSpan.FromSeconds(1)),
                              new RequestBudget(0), () => T0);
    }

    private static Task<TmdbCredits?> Credits(TmdbClient c, int limit = 4) => c.GetCreditsAsync(275, limit, CancellationToken.None);

    [Fact]
    public async Task ACleanCastSkipsNothing()
    {
        var c = Client("""{"cast":[{"name":"A","order":0},{"name":"B","order":1}],"crew":[]}""");

        await Credits(c);

        Assert.Equal(0, c.CastMembersSkipped);
    }

    [Theory]
    [InlineData("""{"character":"X","order":0}""")]            // no name
    [InlineData("""{"name":"","order":0}""")]                  // empty
    [InlineData("""{"name":"   ","order":0}""")]               // blank
    [InlineData("""{"name":null,"order":0}""")]
    [InlineData("""{"name":7,"order":0}""")]                   // not a string
    [InlineData("null")]                                       // not an object
    [InlineData("7")]
    public async Task AMemberWithNoUsableNameIsCountedAsSkipped(string member)
    {
        var c = Client($$"""{"cast":[{"name":"Kept","order":1},{{member}}],"crew":[]}""");

        var credits = await Credits(c);

        Assert.Equal(["Kept"], credits!.Cast.Select(m => m.Name));
        Assert.Equal(1, c.CastMembersSkipped);
    }

    [Fact]
    public async Task OnlyTheSkippedMembersAreCounted_NotTheOnesKept()
    {
        var c = Client("""{"cast":[{"name":"A"},{"name":""},{"name":"B"},{"character":"C"},{"name":"D"}],"crew":[]}""");

        await Credits(c);

        Assert.Equal(2, c.CastMembersSkipped);
    }

    [Fact]
    public async Task AMemberBeyondTheCastLimitThatIsUnreadableIsStillCounted()
    {
        var c = Client("""{"cast":[{"name":"A","order":0},{"name":"B","order":1},{"name":"","order":50}],"crew":[]}""");

        await Credits(c, limit: 1);

        Assert.Equal(1, c.CastMembersSkipped);
    }

    [Fact]
    public async Task TheCountAddsUpAcrossRequests()
    {
        // One body that both endpoints can read: credits at the root, and appended to the details.
        var c = Client("""{"id":275,"title":"Fargo","cast":[{"name":"A"},{"name":""}],"crew":[],"credits":{"cast":[{"name":"A"},{"name":""}]}}""");

        await Credits(c);
        await Credits(c);
        await c.GetMovieWithCreditsAsync(275, 4, CancellationToken.None);

        Assert.Equal(3, c.CastMembersSkipped);
    }

    [Fact]
    public async Task TheCombinedDetailsAndCreditsRequestCountsToo()
    {
        var c = Client("""{"id":275,"title":"Fargo","credits":{"cast":[{"name":"A"},{"name":null},{}]}}""");

        var result = await c.GetMovieWithCreditsAsync(275, 4, CancellationToken.None);

        Assert.Equal(["A"], result!.Cast.Select(m => m.Name));
        Assert.Equal(2, c.CastMembersSkipped);
    }

    [Fact]
    public async Task ACreditsReadForDirectorsOnlyDoesNotReadTheCastSoCountsNothing()
    {
        var c = Client("""{"cast":[{"name":""},{"name":""}],"crew":[{"job":"Director","name":"Joel Coen"}]}""");

        await c.GetDirectorsAsync(275, CancellationToken.None);

        Assert.Equal(0, c.CastMembersSkipped);
    }

    [Fact]
    public async Task AnAbsentCastIsNotASkippedMember()
    {
        var c = Client("""{"crew":[{"job":"Director","name":"Joel Coen"}]}""");

        await Credits(c);

        Assert.Equal(0, c.CastMembersSkipped);
    }
}

using System.Net;
using MovieLibrary.Core.Throttling;
using MovieLibrary.Enrich.Tmdb;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// Task 14 amendment G, and the ruling that completed it. The credits parse sits in the scoring path, so one
/// <i>member</i> it cannot read is skipped, not allowed to abort a seed that has already spent thousands of requests.
/// A <i>container</i> of the wrong shape (credits not an object, cast or crew present but not an array) is the opposite
/// case: it is wrong for every film, and an empty result would be cached for five months as "TMDB has none", so it
/// throws. A container that is simply absent stays empty. None of these shapes is modelled by the stubs elsewhere, and
/// the parse has never seen a real TMDB response.
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

    // ---- containers: a wrong shape is a changed response, not a film with no credits ----

    private static void AssertNamesTheFilmAndTheField(InvalidDataException ex, string film, string field)
    {
        Assert.Contains($"film {film}", ex.Message);
        Assert.Contains($"'{field}'", ex.Message);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"credits\"")]
    [InlineData("5")]
    [InlineData("true")]
    public async Task ACreditsValueThatIsNotAnObjectThrowsInsteadOfGivingAnEmptyCast(string credits)
    {
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            Client($$"""{"id":275,"title":"Fargo","credits":{{credits}}}""").GetMovieWithCreditsAsync(275, 4, CancellationToken.None));

        AssertNamesTheFilmAndTheField(ex, "275", "credits");
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("\"cast\"")]
    [InlineData("7")]
    [InlineData("true")]
    public async Task ACastThatIsNotAnArrayThrowsInsteadOfGivingAnEmptyCast(string cast)
    {
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            Client($$"""{"cast":{{cast}},"crew":[{"job":"Director","name":"Joel Coen"}]}""").GetCreditsAsync(275, 4, CancellationToken.None));

        AssertNamesTheFilmAndTheField(ex, "275", "credits.cast");
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("\"cast\"")]
    public async Task ACastThatIsNotAnArrayThrowsOnTheAppendToResponsePathToo(string cast)
    {
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            Client($$$"""{"id":275,"title":"Fargo","credits":{"cast":{{{cast}}},"crew":[]}}""").GetMovieWithCreditsAsync(275, 4, CancellationToken.None));

        AssertNamesTheFilmAndTheField(ex, "275", "credits.cast");
    }

    [Theory]
    [InlineData("""{"id":275,"title":"Fargo"}""")]                          // no credits at all: TMDB omits it without append_to_response
    [InlineData("""{"id":275,"title":"Fargo","credits":{}}""")]             // a credits object with neither array
    [InlineData("""{"id":275,"title":"Fargo","credits":{"crew":[]}}""")]    // no cast array
    public async Task AbsenceIsNotAMistypeAndStillGivesAnEmptyCast(string body)
    {
        var both = await Client(body).GetMovieWithCreditsAsync(275, 4, CancellationToken.None);

        Assert.Equal(275, both!.Movie.Id);
        Assert.Empty(both.Cast);
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
    [InlineData("""{}""")]
    public async Task CreditsWithNoCrewPropertyGiveNoDirectorsInsteadOfThrowing(string body)
    {
        var credits = await Client(body).GetCreditsAsync(275, 4, CancellationToken.None);

        Assert.Empty(credits!.Directors);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"x\"")]
    [InlineData("{}")]
    [InlineData("5")]
    public async Task ACrewThatIsNotAnArrayThrowsInsteadOfGivingNoDirectors(string crew)
    {
        // Directors carry 0.30 of the match score: emptying them silently would weaken matching for every film.
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            Client($$"""{"cast":[],"crew":{{crew}}}""").GetCreditsAsync(275, 4, CancellationToken.None));

        AssertNamesTheFilmAndTheField(ex, "275", "credits.crew");
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("\"x\"")]
    [InlineData("7")]
    public async Task ACreditsBodyThatIsNotAnObjectThrows(string body)
    {
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            Client(body).GetCreditsAsync(275, 4, CancellationToken.None));

        AssertNamesTheFilmAndTheField(ex, "275", "credits");
    }

    [Fact]
    public async Task GetDirectorsThrowsOnAWronglyShapedCrewToo()
    {
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            Client("""{"crew":"x"}""").GetDirectorsAsync(275, CancellationToken.None));

        AssertNamesTheFilmAndTheField(ex, "275", "credits.crew");
    }

    [Fact]
    public async Task TheCastIsNotCheckedWhenNoCastWasAskedFor()
    {
        // GetDirectorsAsync reads no cast, so a changed cast loses it nothing, and it must not stop a directors-only call.
        var directors = await Client("""{"cast":null,"crew":[{"job":"Director","name":"Joel Coen"}]}""")
            .GetDirectorsAsync(275, CancellationToken.None);

        Assert.Equal(["Joel Coen"], directors);
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

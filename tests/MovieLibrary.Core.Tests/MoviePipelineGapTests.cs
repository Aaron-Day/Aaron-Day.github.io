using System.Globalization;
using System.Web;
using MovieLibrary.Core.Caching;
using MovieLibrary.Core.Identity;
using MovieLibrary.Core.Models;
using MovieLibrary.Core.Text;
using MovieLibrary.Enrich.Pipeline;
using MovieLibrary.Enrich.Tmdb;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// Behaviour the brief's three tests do not reach: a successful match, the cache and alias paths, the
/// search query, the budget rules, ordering, request cost, payload freshness and the review queue. Each test
/// was written to kill a specific mutant of <see cref="MoviePipeline"/> (see the Task 12 report).
/// </summary>
public class MoviePipelineGapTests
{
    private static SheetRow Row(string title, string runtime, string directors)
        => new(title, runtime, directors, "", "Sci-Fi & Fantasy", "");

    private static readonly IReadOnlyDictionary<string, int> NoOverrides = new Dictionary<string, int>();

    private static Func<HttpRequestMessage, HttpResponseMessage> Routes(Dictionary<string, string> byPath)
        => req => byPath.TryGetValue(req.RequestUri!.AbsolutePath, out var body)
            ? TestPipeline.Json(body) : TestPipeline.NotFound();

    private static IEnumerable<string> Searches(TestPipeline.Harness h)
        => h.Handler.Requests.Where(TestPipeline.IsSearch)
            .Select(r => HttpUtility.ParseQueryString(r.RequestUri!.Query)["query"]!);

    // Three candidates, most popular first. Only the third (the real Fargo) has a runtime within
    // tolerance of the sheet's 98 minutes, and it is the least popular, so it is found only if all
    // three are enriched and it alone is sent for credits.
    private static readonly Dictionary<string, string> FargoRoutes = new()
    {
        ["/3/search/movie"] = """
            {"results":[{"id":999,"title":"Fargo Rock City","release_date":"2000-01-01","popularity":80},
                        {"id":555,"title":"Fargo Documentary","release_date":"2005-01-01","popularity":50},
                        {"id":275,"title":"Fargo","release_date":"1996-03-08","popularity":30}]}
            """,
        ["/3/movie/275"] = """
            {"id":275,"title":"Fargo","overview":"A pregnant cop investigates.","tagline":"A homespun murder story.",
             "runtime":98,"release_date":"1996-03-08","vote_average":7.5,
             "poster_path":"/p.jpg","backdrop_path":"/b.jpg"}
            """,
        ["/3/movie/275/credits"] = """{"crew":[{"job":"Director","name":"Joel Coen"}]}""",
        ["/3/movie/999"] = """{"id":999,"title":"Fargo Rock City","runtime":120,"release_date":"2000-01-01"}""",
        ["/3/movie/999/credits"] = """{"crew":[{"job":"Director","name":"Someone Else"}]}""",
        ["/3/movie/555"] = """{"id":555,"title":"Fargo Documentary","runtime":150,"release_date":"2005-01-01"}""",
        ["/3/movie/555/credits"] = """{"crew":[{"job":"Director","name":"Someone Else"}]}""",
    };

    // Title matches (0.15) but runtime, director and year do not: Unmatched, with a non-zero
    // best score that must neither leak into the emitted record nor be formatted culture-sensitively.
    private static readonly Dictionary<string, string> FooRoutes = new()
    {
        ["/3/search/movie"] = """{"results":[{"id":100,"title":"Foo","release_date":"2001-01-01","popularity":1}]}""",
        ["/3/movie/100"] = """{"id":100,"title":"Foo","runtime":200,"release_date":"2001-01-01"}""",
        ["/3/movie/100/credits"] = """{"crew":[{"job":"Director","name":"Someone Else"}]}""",
    };

    private static readonly SheetRow Fargo =
        new("Fargo", "1:38:00", "Joel Coen & Ethan Coen", "Blu-ray 2019", "Crime", "");

    [Fact]
    public async Task ASuccessfulSearchResolvesTheFilmEndToEnd()
    {
        var h = TestPipeline.Create(0, Routes(FargoRoutes));

        var result = await h.Pipeline.RunAsync([Fargo], NoOverrides, CancellationToken.None);

        var r = Assert.Single(result.Records);
        Assert.Equal(275, r.I);
        Assert.Equal(1996, r.Y);
        Assert.Equal("/p.jpg", r.P);
        Assert.Equal("/b.jpg", r.B);
        Assert.Equal(7.5, r.Vote);
        Assert.Equal("A pregnant cop investigates.", r.Ov);
        Assert.Equal("Crime", r.Ag);
        Assert.Equal("Blu-ray 2019", r.Own);
        Assert.Equal(["Joel Coen", "Ethan Coen"], r.Dir);
        Assert.Equal(0.85, r.Mc); // runtime 0.40 + director 0.30 + title 0.15
        Assert.Equal(h.Handler.Requests.Count, result.RequestsUsed);
        Assert.Empty(result.ReviewQueue);
    }

    [Fact]
    public async Task ASecondRunIsServedFromTheCacheWithoutSearching()
    {
        var h = TestPipeline.Create(0, Routes(FargoRoutes));
        await h.Pipeline.RunAsync([Fargo], NoOverrides, CancellationToken.None);
        var before = h.Handler.Requests.Count;

        var second = await h.Pipeline.RunAsync([Fargo], NoOverrides, CancellationToken.None);

        Assert.Equal(before, h.Handler.Requests.Count); // nothing re-fetched, payload read from disk
        Assert.Equal(275, Assert.Single(second.Records).I);
        Assert.Equal("/p.jpg", second.Records[0].P);
        Assert.Empty(second.ReviewQueue); // an accepted match is not re-queued
    }

    [Fact]
    public async Task AReEncodedFilmAliasesToTheCachedResultEvenWhenItsTitleHasPunctuation()
    {
        // Cleaned ("Ocean's Eleven") differs from the folded Value ("ocean s eleven"); only a
        // cache entry written with Value can alias. Contract 4.
        var h = TestPipeline.Create(0, req => TestPipeline.IsSearch(req)
            ? TestPipeline.Json(TestPipeline.EmptyResults) : TestPipeline.NotFound());
        await h.Pipeline.RunAsync([Row("Ocean's Eleven (Unrated)", "1:56:00", "Steven Soderbergh")],
            NoOverrides, CancellationToken.None);
        var before = h.Handler.Requests.Count;

        await h.Pipeline.RunAsync([Row("Ocean's Eleven", "1:57:00", "Steven Soderbergh")],
            NoOverrides, CancellationToken.None);

        Assert.Equal(before, h.Handler.Requests.Count);
    }

    [Fact]
    public async Task TheSearchQueryKeepsCaseAndPunctuationAndCarriesTheYear()
    {
        var h = TestPipeline.Create(0, req => TestPipeline.IsSearch(req)
            ? TestPipeline.Json(TestPipeline.EmptyResults) : TestPipeline.NotFound());

        await h.Pipeline.RunAsync([Row("Ocean's Eleven (2001)", "1:56:00", "Steven Soderbergh")],
            NoOverrides, CancellationToken.None);

        var search = Assert.Single(h.Handler.Requests);
        Assert.Equal("Ocean's Eleven", HttpUtility.ParseQueryString(search.RequestUri!.Query)["query"]);
        Assert.Equal("2001", HttpUtility.ParseQueryString(search.RequestUri!.Query)["year"]);
    }

    [Fact]
    public async Task ATitleThatIsOnlyAParentheticalIsNeverSearched()
    {
        var h = TestPipeline.Create(0, Routes([]));

        var result = await h.Pipeline.RunAsync([Row("(2011)", "1:30:00", "X")],
            NoOverrides, CancellationToken.None);

        Assert.Empty(h.Handler.Requests);
        var r = Assert.Single(result.Records);
        Assert.Equal((0, 0d, "(2011)"), (r.I, r.Mc, r.T)); // still emitted, under its sheet title
        Assert.Contains("(2011)", result.ReviewQueue.Single());
    }

    [Fact]
    public async Task RowsAreSearchedInTitleOrderNotSheetOrder()
    {
        var h = TestPipeline.Create(0, req => TestPipeline.IsSearch(req)
            ? TestPipeline.Json(TestPipeline.EmptyResults) : TestPipeline.NotFound());

        var result = await h.Pipeline.RunAsync(
            [Row("Zulu", "1:30:00", "X"), Row("Ørn", "1:31:00", "X"), Row("charlie", "1:32:00", "X"),
             Row("Alpha", "1:33:00", "X")],
            NoOverrides, CancellationToken.None);

        // By folded title, ordinally: "charlie" sorts between Alpha and Zulu despite its case, and
        // "ø" sorts after "z", so the order is the same on every machine.
        string[] expected = ["Alpha", "charlie", "Zulu", "Ørn"];
        Assert.Equal(expected, Searches(h));
        Assert.Equal(expected, result.Records.Select(r => r.T));
    }

    [Fact]
    public async Task TheSearchThatExhaustsTheBudgetIsNotCachedAsANegative()
    {
        var h = TestPipeline.Create(budget: 1, req => TestPipeline.IsSearch(req)
            ? TestPipeline.Json(TestPipeline.EmptyResults) : TestPipeline.NotFound());

        var result = await h.Pipeline.RunAsync(
            [Row("A", "1:30:00", "X"), Row("B", "1:40:00", "Y")], NoOverrides, CancellationToken.None);

        Assert.True(result.StoppedOnBudget);
        Assert.Equal(2, result.Records.Count);
        // A's empty result is a real answer. B's is "the budget ran out", not "TMDB has no such film".
        Assert.Single(h.Cache.Entries);
    }

    [Fact]
    public async Task ABudgetThatRunsOutBeforeAnyDetailsCachesNothingAndLeavesTheFilmUnresolved()
    {
        // Budget 1: the search succeeds, then the first details lookup is refused. Nothing is
        // known about any candidate, so nothing matches and nothing may be cached.
        var h = TestPipeline.Create(budget: 1, Routes(FargoRoutes));

        var result = await h.Pipeline.RunAsync([Fargo], NoOverrides, CancellationToken.None);

        Assert.True(result.StoppedOnBudget);
        Assert.Empty(h.Cache.Entries);
        var r = Assert.Single(result.Records);
        Assert.Equal((0, 0d), (r.I, r.Mc));
    }

    [Fact]
    public async Task AnUnmatchedFilmWhoseBestCandidateScoredBelowTheFloorStillReportsZeroConfidence()
    {
        var h = TestPipeline.Create(0, Routes(FooRoutes));

        var result = await h.Pipeline.RunAsync([Row("Foo", "1:30:00", "Nobody")],
            NoOverrides, CancellationToken.None);

        var r = Assert.Single(result.Records);
        Assert.Equal((0, 0d), (r.I, r.Mc));
        Assert.Contains("Foo", result.ReviewQueue.Single());
        // ...and it is emitted from the sheet alone: nothing of TMDB's leaks in.
        Assert.Equal(("Foo", 90, "Sci-Fi & Fantasy", 0d), (r.T, r.Rt, r.Ag, r.Vote));
        Assert.Equal(["Nobody"], r.Dir);
        Assert.Null(r.Own); // blank PurchaseInfo is null, not ""
        Assert.Equal((null, null, null, null), (r.P, r.B, r.Ov, r.Tag));
    }

    [Fact]
    public async Task ReviewLinesFormatTheScoreInvariantlyWhateverTheMachineCulture()
    {
        var saved = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE"); // decimal comma
        try
        {
            var h = TestPipeline.Create(0, Routes(FooRoutes));

            var result = await h.Pipeline.RunAsync([Row("Foo", "1:30:00", "Nobody")],
                NoOverrides, CancellationToken.None);

            Assert.Contains("Unmatched at 0.15", result.ReviewQueue.Single());
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }

    [Fact]
    public async Task ATitleOfPurePunctuationIsStillSearched()
    {
        // Its folded Value is empty but its Cleaned form is not, and Cleaned is what is sent.
        var h = TestPipeline.Create(0, req => TestPipeline.IsSearch(req)
            ? TestPipeline.Json(TestPipeline.EmptyResults) : TestPipeline.NotFound());

        await h.Pipeline.RunAsync([Row("!!!", "1:30:00", "X")], NoOverrides, CancellationToken.None);

        Assert.Equal(["!!!"], Searches(h));
    }

    [Fact]
    public async Task AnOverrideIsRecordedAtFullConfidence()
    {
        var pipeline = TestPipeline.ThatFailsIfSearched(tmdbId: 948);

        var result = await pipeline.RunAsync([Row("Halloween", "1:31:00", "John Carpenter")],
            new Dictionary<string, int> { ["halloween"] = 948 }, CancellationToken.None);

        Assert.Equal(1d, Assert.Single(result.Records).Mc);
    }

    [Fact]
    public async Task TwoUnmatchedRowsWithTheSameTitleBothReachTheReviewQueueOnAWarmRun()
    {
        var h = TestPipeline.Create(0, req => TestPipeline.IsSearch(req)
            ? TestPipeline.Json(TestPipeline.EmptyResults) : TestPipeline.NotFound());
        SheetRow[] rows = [Row("Halloween", "1:31:00", "X"), Row("Halloween", "1:45:00", "X")];
        await h.Pipeline.RunAsync(rows, NoOverrides, CancellationToken.None);

        var warm = await h.Pipeline.RunAsync(rows, NoOverrides, CancellationToken.None); // negatives from cache

        Assert.Equal(2, warm.ReviewQueue.Count);
    }

    [Fact]
    public async Task TheCacheRecordsWhatWasResolvedAndWhenUsingTheFoldedTitle()
    {
        // Any search returns the Fargo candidates; the second film matches none of them.
        var h = TestPipeline.Create(0, Routes(FargoRoutes));

        await h.Pipeline.RunAsync(
            [Fargo, Row("Ocean's Eleven (Unrated)", "1:56:00", "Steven Soderbergh")],
            NoOverrides, CancellationToken.None);

        var hit = h.Cache.Entries.Single(e => !e.Negative);
        Assert.Equal((275, 0.85, "fargo", 98), (hit.TmdbId, hit.Confidence, hit.NormalizedTitle, hit.RuntimeMinutes));
        Assert.Equal(TestPipeline.Now, hit.FetchedUtc);
        Assert.Equal(  // the identity key, not the title: same-titled films of other lengths must not collide
            IdentityKey.ForFilm(TitleNormalizer.Normalize("Fargo"), 98, TitleNormalizer.NormalizeDirectors("Joel Coen & Ethan Coen")),
            hit.Key);

        var miss = h.Cache.Entries.Single(e => e.Negative);
        Assert.Null(miss.TmdbId);
        Assert.Equal("ocean s eleven", miss.NormalizedTitle); // the folded Value, never Cleaned
        Assert.Equal(116, miss.RuntimeMinutes);
        Assert.Equal(TestPipeline.Now, miss.FetchedUtc);
    }

    [Fact]
    public async Task ACancelledRunThrowsBeforeTouchingTmdb()
    {
        var h = TestPipeline.Create(0, Routes(FargoRoutes));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => h.Pipeline.RunAsync([Fargo], NoOverrides, cts.Token));

        Assert.Empty(h.Handler.Requests);
    }

    // ---- Amendment F-K: request cost, payload freshness, budget trips, the review queue ----

    private static IEnumerable<string> Paths(TestPipeline.Harness h)
        => h.Handler.Requests.Select(r => r.RequestUri!.AbsolutePath);

    private static string FargoDetails(string overview) => $$"""
        {"id":275,"title":"Fargo","overview":"{{overview}}","runtime":98,"release_date":"1996-03-08",
         "vote_average":7.5,"poster_path":"/p.jpg","backdrop_path":"/b.jpg"}
        """;

    [Fact]
    public async Task TheMatchedFilmsDetailsAreFetchedOnceNotOncePerUse()
    {
        var h = TestPipeline.Create(0, Routes(FargoRoutes));

        await h.Pipeline.RunAsync([Fargo], NoOverrides, CancellationToken.None);

        Assert.Equal(1, Paths(h).Count(p => p == "/3/movie/275"));
    }

    [Fact]
    public async Task CreditsAreFetchedOnlyForCandidatesWhoseRuntimeAgrees()
    {
        var h = TestPipeline.Create(0, Routes(FargoRoutes));

        await h.Pipeline.RunAsync([Fargo], NoOverrides, CancellationToken.None);

        // The decoys run 120 and 150 minutes against the sheet's 98: they cannot win on runtime.
        Assert.Equal(["/3/movie/275/credits"], Paths(h).Where(p => p.EndsWith("/credits")));
        Assert.Equal(5, h.Handler.Requests.Count); // 1 search + 3 details + 1 credits
    }

    [Fact]
    public async Task WhenNoRuntimeAgreesTheMostPopularCandidateStillGetsItsCredits()
    {
        // The most popular candidate has no runtime on TMDB, so nothing qualifies on runtime, but
        // its director and title are still enough to flag it. The other candidate gets no credits.
        var h = TestPipeline.Create(0, Routes(new()
        {
            ["/3/search/movie"] = """
                {"results":[{"id":7,"title":"Fargo","release_date":"1996-03-08","popularity":90},
                            {"id":8,"title":"Fargo Two","release_date":"1997-01-01","popularity":10}]}
                """,
            ["/3/movie/7"] = """{"id":7,"title":"Fargo","runtime":null,"release_date":"1996-03-08"}""",
            ["/3/movie/8"] = """{"id":8,"title":"Fargo Two","runtime":140,"release_date":"1997-01-01"}""",
            ["/3/movie/7/credits"] = """{"crew":[{"job":"Director","name":"Joel Coen"}]}""",
            ["/3/movie/8/credits"] = """{"crew":[{"job":"Director","name":"Joel Coen"}]}""",
        }));

        var result = await h.Pipeline.RunAsync([Fargo], NoOverrides, CancellationToken.None);

        Assert.Equal(["/3/movie/7/credits"], Paths(h).Where(p => p.EndsWith("/credits")));
        var r = Assert.Single(result.Records);
        Assert.Equal((7, 0.45), (r.I, r.Mc)); // director 0.30 + title 0.15
    }

    [Fact]
    public async Task AStoredPayloadInsideTheRefreshWindowIsServedAndOneOutsideItIsRefetched()
    {
        // An override has no index entry to age out, so the payload must age on its own clock.
        var now = TestPipeline.Now;
        var overview = "Old";
        var h = TestPipeline.Create(0, req => req.RequestUri!.AbsolutePath == "/3/movie/948"
            ? TestPipeline.Json($$"""{"id":948,"title":"Halloween","overview":"{{overview}}"}""")
            : throw new Xunit.Sdk.XunitException($"Unexpected request: {req.RequestUri}"), () => now);
        var overrides = new Dictionary<string, int> { ["halloween"] = 948 };
        SheetRow[] rows = [Row("Halloween", "1:31:00", "John Carpenter")];

        var first = await h.Pipeline.RunAsync(rows, overrides, CancellationToken.None);
        overview = "Newer";
        now = now.AddDays(100); // inside the 150-day window
        var inside = await h.Pipeline.RunAsync(rows, overrides, CancellationToken.None);
        var requestsInside = h.Handler.Requests.Count;
        now = now.AddDays(51); // 151 days after the fetch
        var outside = await h.Pipeline.RunAsync(rows, overrides, CancellationToken.None);

        Assert.Equal("Old", first.Records[0].Ov);
        Assert.Equal("Old", inside.Records[0].Ov);
        Assert.Equal(1, requestsInside); // served from disk, nothing refetched
        Assert.Equal("Newer", outside.Records[0].Ov);
        Assert.Equal(2, h.Handler.Requests.Count);
    }

    [Fact]
    public async Task AFreshIndexEntryDoesNotKeepAStalePayloadAlive()
    {
        var h = TestPipeline.Create(0, req => req.RequestUri!.AbsolutePath == "/3/movie/275"
            ? TestPipeline.Json(FargoDetails("Fresh"))
            : throw new Xunit.Sdk.XunitException($"Unexpected request: {req.RequestUri}"));
        await SeedFargoWithAStalePayload(h);

        var result = await h.Pipeline.RunAsync([Fargo], NoOverrides, CancellationToken.None);

        var r = Assert.Single(result.Records);
        Assert.Equal(("Fresh", "/p.jpg"), (r.Ov, r.P));
        Assert.Equal(["/3/movie/275"], Paths(h)); // no search: the index entry was fine
    }

    [Fact]
    public async Task AReResolvedFilmTakesItsDetailsFromThatSearchNotItsOldPayload()
    {
        var now = TestPipeline.Now;
        var overview = "Old";
        var fargo = Routes(FargoRoutes);
        var h = TestPipeline.Create(0, req => req.RequestUri!.AbsolutePath == "/3/movie/275"
            ? TestPipeline.Json(FargoDetails(overview)) : fargo(req), () => now);
        await h.Pipeline.RunAsync([Fargo], NoOverrides, CancellationToken.None);
        overview = "New";
        now = now.AddDays(151); // the index entry has expired, so the film is searched again
        var before = h.Handler.Requests.Count;

        var second = await h.Pipeline.RunAsync([Fargo], NoOverrides, CancellationToken.None);

        Assert.Equal("New", Assert.Single(second.Records).Ov);
        Assert.Equal(5, h.Handler.Requests.Count - before); // and the details from that search are reused
    }

    [Fact]
    public async Task ABudgetThatRunsOutMidEnrichmentUsesTheScoredResultButDoesNotCacheIt()
    {
        // Budget 4: the search and all three details land, then the credits call is refused. With no
        // director to confirm it the real Fargo is only flagged (runtime 0.40 + title 0.15), but it is
        // used, with the details already paid for, and nothing is cached.
        var h = TestPipeline.Create(budget: 4, Routes(FargoRoutes));

        var result = await h.Pipeline.RunAsync([Fargo], NoOverrides, CancellationToken.None);

        Assert.True(result.StoppedOnBudget);
        var r = Assert.Single(result.Records);
        Assert.Equal((275, 0.55, "/p.jpg"), (r.I, r.Mc, r.P));
        Assert.Empty(h.Cache.Entries);
        Assert.Contains("Flagged", result.ReviewQueue.Single());
    }

    [Fact]
    public async Task AFlaggedMatchIsQueuedAgainOnEveryWarmRun()
    {
        var h = TestPipeline.Create(0, Routes(FargoRoutes));
        // A different director: Fargo is matched on runtime and title alone, so it is flagged at 0.55.
        SheetRow[] rows = [Row("Fargo", "1:38:00", "Somebody Else")];

        var cold = await h.Pipeline.RunAsync(rows, NoOverrides, CancellationToken.None);
        var before = h.Handler.Requests.Count;
        var warm = await h.Pipeline.RunAsync(rows, NoOverrides, CancellationToken.None);

        Assert.Contains("Flagged at 0.55, id 275", cold.ReviewQueue.Single());
        Assert.Equal(before, h.Handler.Requests.Count); // the warm run is served from the cache...
        Assert.Equal(cold.ReviewQueue, warm.ReviewQueue); // ...and still asks a human to look
    }

    // Candidate 1 is the more popular and 3 minutes off the sheet's 98; candidate 2 is 2 minutes off.
    // Whichever of them carries the film's name is the one the title rule sends credits to anyway, so the
    // two tests below swap the name to pin each side of the +/-2 gate on its own.
    private static Dictionary<string, string> PairRoutes(string title1, string title2) => new()
    {
        ["/3/search/movie"] = $$"""
            {"results":[{"id":1,"title":"{{title1}}","release_date":"1996-03-08","popularity":80},
                        {"id":2,"title":"{{title2}}","release_date":"1996-03-08","popularity":30}]}
            """,
        ["/3/movie/1"] = $$"""{"id":1,"title":"{{title1}}","runtime":101}""",
        ["/3/movie/2"] = $$"""{"id":2,"title":"{{title2}}","runtime":100,"release_date":"1996-03-08"}""",
        ["/3/movie/1/credits"] = """{"crew":[{"job":"Director","name":"Someone Else"}]}""",
        ["/3/movie/2/credits"] = """{"crew":[{"job":"Director","name":"Joel Coen"}]}""",
    };

    private static IEnumerable<string> CreditsPaths(TestPipeline.Harness h)
        => Paths(h).Where(p => p.EndsWith("/credits"));

    [Fact]
    public async Task ACandidateTwoMinutesOffStillGetsItsCreditsWhenADecoyCarriesTheFilmsName()
    {
        // The decoy (101 min) is the one named "Fargo", so a gate narrowed below 2 leaves nobody passing,
        // sends the single credits call to the decoy, and the real film (100 min) never gets a director.
        var h = TestPipeline.Create(0, Routes(PairRoutes("Fargo", "Fargo Redux")));

        var result = await h.Pipeline.RunAsync([Fargo], NoOverrides, CancellationToken.None);

        Assert.Equal(["/3/movie/1/credits", "/3/movie/2/credits"], CreditsPaths(h));
        var r = Assert.Single(result.Records);
        Assert.Equal((2, 0.70), (r.I, r.Mc)); // runtime 0.40 + director 0.30, no title match
    }

    [Fact]
    public async Task ACandidateThreeMinutesOffGetsNoCreditsUnlessItIsNamedLikeTheFilm()
    {
        var h = TestPipeline.Create(0, Routes(PairRoutes("Fargo X", "Fargo Redux")));

        var result = await h.Pipeline.RunAsync([Fargo], NoOverrides, CancellationToken.None);

        Assert.Equal(["/3/movie/2/credits"], CreditsPaths(h)); // the 101-minute decoy is not worth a request
        Assert.Equal((2, 0.70), (result.Records[0].I, result.Records[0].Mc));
    }

    [Fact]
    public async Task TheFilmNamedLikeTheSheetGetsItsCreditsEvenWhenADecoyAgreesOnRuntime()
    {
        // An edition cut: TMDB lists the real film at 120 minutes against the sheet's 98, while a decoy
        // happens to land within tolerance. Credits for the runtime passer alone would leave the real
        // film at 0.15 against the decoy's 0.40: Unmatched. With its director it reaches 0.45.
        var h = TestPipeline.Create(0, Routes(new()
        {
            ["/3/search/movie"] = """
                {"results":[{"id":1,"title":"Fargo Redux","release_date":"2010-01-01","popularity":90},
                            {"id":2,"title":"Fargo","release_date":"1996-03-08","popularity":30}]}
                """,
            ["/3/movie/1"] = """{"id":1,"title":"Fargo Redux","runtime":99}""",
            ["/3/movie/2"] = """{"id":2,"title":"Fargo","runtime":120,"release_date":"1996-03-08"}""",
            ["/3/movie/1/credits"] = """{"crew":[{"job":"Director","name":"Someone Else"}]}""",
            ["/3/movie/2/credits"] = """{"crew":[{"job":"Director","name":"Joel Coen"}]}""",
        }));

        var result = await h.Pipeline.RunAsync([Fargo], NoOverrides, CancellationToken.None);

        Assert.Equal(["/3/movie/1/credits", "/3/movie/2/credits"], CreditsPaths(h));
        var r = Assert.Single(result.Records);
        Assert.Equal((2, 0.45), (r.I, r.Mc)); // director 0.30 + title 0.15
        Assert.Contains("Flagged", result.ReviewQueue.Single());
    }

    [Fact]
    public async Task AStalePayloadIsUsedRatherThanABlankCardWhenTheBudgetCannotRefetchIt()
    {
        // Budget 1 is spent on row "Aaa", so Fargo (fresh index entry, stale payload) cannot refetch.
        var h = TestPipeline.Create(budget: 1, req => req.RequestUri!.AbsolutePath == "/3/movie/1"
            ? TestPipeline.Json("""{"id":1,"title":"Aaa"}""") : TestPipeline.NotFound());
        await SeedFargoWithAStalePayload(h);

        var result = await h.Pipeline.RunAsync([Row("Aaa", "1:30:00", "X"), Fargo],
            new Dictionary<string, int> { ["aaa"] = 1 }, CancellationToken.None);

        Assert.True(result.StoppedOnBudget);
        var fargo = result.Records.Single(r => r.T == "Fargo");
        Assert.Equal(("Stale", "/old.jpg"), (fargo.Ov, fargo.P));
    }

    [Theory]
    [InlineData(0.75, false)]
    [InlineData(0.7499, true)]
    public async Task OnlyACachedMatchBelowTheAcceptThresholdIsQueuedAgain(double confidence, bool queued)
    {
        var h = TestPipeline.Create(0, Routes(FargoRoutes));
        h.Cache.Put(new CacheEntry(FargoKey, 275, confidence, TestPipeline.Now, false, "fargo", 98));

        var result = await h.Pipeline.RunAsync([Fargo], NoOverrides, CancellationToken.None);

        Assert.Equal(queued, result.ReviewQueue.Count == 1);
    }

    private static string FargoKey => IdentityKey.ForFilm(TitleNormalizer.Normalize("Fargo"), 98,
        TitleNormalizer.NormalizeDirectors("Joel Coen & Ethan Coen"));

    private static async Task SeedFargoWithAStalePayload(TestPipeline.Harness h)
    {
        h.Cache.Put(new CacheEntry(FargoKey, 275, 0.85, TestPipeline.Now, false, "fargo", 98));
        var stale = new TmdbMovie(275, "Fargo", "Stale", null, 98, "1996-03-08", 7.5, "/old.jpg", null);
        await h.Cache.WritePayloadAsync("movies/275.json",
            new StoredMovie(TestPipeline.Now.AddDays(-200), stale), CancellationToken.None);
    }

    [Fact]
    public async Task WhenNoRuntimeAgreesTheCandidateNamedLikeTheFilmGetsTheCreditsEvenIfLessPopular()
    {
        // TMDB has the real film at the wrong runtime and ranks it below a making-of and a spin-off, so
        // nothing is within tolerance of the sheet's 98 minutes. The one credits call must go to the
        // candidate whose folded title equals the sheet's ("WALL·E" and "Wall-E" both fold to "wall e"),
        // not the most popular, or the director never gets to flag the real film.
        var h = TestPipeline.Create(0, Routes(new()
        {
            ["/3/search/movie"] = """
                {"results":[{"id":10,"title":"Wall-E: Making Of","release_date":"2008-06-27","popularity":90},
                            {"id":11,"title":"Escape to Wall-E Land","release_date":"2009-01-01","popularity":60},
                            {"id":12,"title":"WALL·E","release_date":"2008-06-27","popularity":30}]}
                """,
            ["/3/movie/10"] = """{"id":10,"title":"Wall-E: Making Of","runtime":20}""",
            ["/3/movie/11"] = """{"id":11,"title":"Escape to Wall-E Land","runtime":45}""",
            ["/3/movie/12"] = """{"id":12,"title":"WALL·E","runtime":70,"release_date":"2008-06-27"}""",
            ["/3/movie/10/credits"] = """{"crew":[{"job":"Director","name":"Someone Else"}]}""",
            ["/3/movie/11/credits"] = """{"crew":[{"job":"Director","name":"Someone Else"}]}""",
            ["/3/movie/12/credits"] = """{"crew":[{"job":"Director","name":"Andrew Stanton"}]}""",
        }));

        var result = await h.Pipeline.RunAsync(
            [Row("Wall-E", "1:38:00", "Andrew Stanton")], NoOverrides, CancellationToken.None);

        Assert.Equal(["/3/movie/12/credits"], Paths(h).Where(p => p.EndsWith("/credits")));
        var r = Assert.Single(result.Records);
        Assert.Equal((12, 0.45), (r.I, r.Mc)); // director 0.30 + title 0.15
        Assert.Contains("Flagged", result.ReviewQueue.Single());
    }

    [Fact]
    public async Task WhenNoRuntimeAgreesAndNoCandidateIsNamedLikeTheFilmTheMostPopularGetsTheCredits()
    {
        var h = TestPipeline.Create(0, Routes(new()
        {
            ["/3/search/movie"] = """
                {"results":[{"id":1,"title":"Fargo X","release_date":"1996-03-08","popularity":90},
                            {"id":2,"title":"Fargo Y","release_date":"1997-01-01","popularity":10}]}
                """,
            ["/3/movie/1"] = """{"id":1,"title":"Fargo X","runtime":140}""",
            ["/3/movie/2"] = """{"id":2,"title":"Fargo Y","runtime":70}""",
            ["/3/movie/1/credits"] = """{"crew":[{"job":"Director","name":"Joel Coen"}]}""",
            ["/3/movie/2/credits"] = """{"crew":[{"job":"Director","name":"Joel Coen"}]}""",
        }));

        await h.Pipeline.RunAsync([Fargo], NoOverrides, CancellationToken.None);

        Assert.Equal(["/3/movie/1/credits"], Paths(h).Where(p => p.EndsWith("/credits")));
    }

    [Theory]
    [InlineData("")]                           // File.Create ran, the write never did
    [InlineData("{\"fetchedUtc\":\"2026-10-2")] // killed mid-write
    public async Task ATruncatedPayloadIsRefetchedInsteadOfFailingEveryLaterRun(string damaged)
    {
        var h = TestPipeline.Create(0, req => req.RequestUri!.AbsolutePath == "/3/movie/275"
            ? TestPipeline.Json(FargoDetails("Fresh"))
            : throw new Xunit.Sdk.XunitException($"Unexpected request: {req.RequestUri}"));
        h.Cache.Put(new CacheEntry(FargoKey, 275, 0.85, TestPipeline.Now, false, "fargo", 98));
        Directory.CreateDirectory(Path.Combine(h.CacheDir, "movies"));
        await File.WriteAllTextAsync(Path.Combine(h.CacheDir, "movies", "275.json"), damaged);

        var result = await h.Pipeline.RunAsync([Fargo], NoOverrides, CancellationToken.None);

        Assert.Equal("Fresh", Assert.Single(result.Records).Ov);
        Assert.Equal(["/3/movie/275"], Paths(h));
        var repaired = await h.Cache.ReadPayloadAsync<StoredMovie>("movies/275.json", CancellationToken.None);
        Assert.Equal("Fresh", repaired!.Movie!.Overview); // overwritten, so the next run is served from disk
    }

    [Fact]
    public async Task TwoFilmsWhoseTitlesFoldToNothingAreNeverAliasedToEachOther()
    {
        var h = TestPipeline.Create(0, req => TestPipeline.IsSearch(req)
            ? TestPipeline.Json(TestPipeline.EmptyResults) : TestPipeline.NotFound());
        SheetRow[] rows = [Row("!!!", "1:35:00", "X"), Row("???", "1:36:00", "X")];

        await h.Pipeline.RunAsync(rows, NoOverrides, CancellationToken.None);
        var cold = h.Handler.Requests.Count;
        await h.Pipeline.RunAsync(rows, NoOverrides, CancellationToken.None);

        Assert.Equal(["!!!", "???"], Searches(h).Take(2)); // the 96-minute one is not "the same film" as the 95
        Assert.Equal(2, cold);
        Assert.Equal(cold, h.Handler.Requests.Count); // yet each still hits its own exact cache entry
    }

    [Fact]
    public async Task CutsOfOneFilmAreProcessedInRuntimeOrderWhateverTheSheetOrder()
    {
        SheetRow[] sheetOrder = [Row("Halloween", "1:45:00", "X"), Row("Halloween", "1:31:00", "X")];
        var a = TestPipeline.Create(0, req => TestPipeline.IsSearch(req)
            ? TestPipeline.Json(TestPipeline.EmptyResults) : TestPipeline.NotFound());
        var b = TestPipeline.Create(0, req => TestPipeline.IsSearch(req)
            ? TestPipeline.Json(TestPipeline.EmptyResults) : TestPipeline.NotFound());

        var first = await a.Pipeline.RunAsync(sheetOrder, NoOverrides, CancellationToken.None);
        var second = await b.Pipeline.RunAsync([.. sheetOrder.Reverse()], NoOverrides, CancellationToken.None);

        Assert.Equal([91, 105], first.Records.Select(r => r.Rt));
        Assert.Equal([91, 105], second.Records.Select(r => r.Rt));
    }

    [Fact]
    public async Task AMalformedRuntimeFailsBeforeAnyRequestIsSpent()
    {
        var h = TestPipeline.Create(0, req => TestPipeline.IsSearch(req)
            ? TestPipeline.Json(TestPipeline.EmptyResults) : TestPipeline.NotFound());

        await Assert.ThrowsAsync<FormatException>(() => h.Pipeline.RunAsync(
            [Row("A", "1:30:00", "X"), Row("B", "not a runtime", "X")], NoOverrides, CancellationToken.None));

        Assert.Empty(h.Handler.Requests); // "A" sorts first and would otherwise have been searched
    }
}

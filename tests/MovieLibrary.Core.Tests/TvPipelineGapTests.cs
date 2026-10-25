using System.Text.Json;
using System.Text.RegularExpressions;
using MovieLibrary.Core.Caching;
using MovieLibrary.Core.Models;
using MovieLibrary.Enrich.Pipeline;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// Pins what the brief's four tests cannot see: the contracts from the Task 11 and 12 reviews, amendment A2
/// (a payload ages out on its own stamp), slug safety, and the resume and warm-run promises. Kept apart from
/// <see cref="TvPipelineTests"/>, which holds the brief's tests verbatim.
/// </summary>
public class TvPipelineGapTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static SheetRow Ep(string title, string series, string runtime = "44:00:00")
        => new(title, runtime, "", series + ": The Complete Series", "Drama", series);

    private static FakeShow Show(int id, string name, params (int Season, string[] Episodes)[] seasons)
        => new(id, name, [.. seasons.Select(s => new FakeSeason(s.Season, s.Episodes))]);

    private static FakeTv Tmdb(params FakeShow[] shows)
    {
        var fake = new FakeTv();
        foreach (var show in shows) fake.Add(show);
        return fake;
    }

    private static TestPipeline.TvHarness Harness(
        FakeTv fake, int budget = 0, Func<DateTimeOffset>? clock = null,
        TestPipeline.TvHarness? shareCacheWith = null, bool freshIndex = false)
        => TestPipeline.CreateTv(budget, fake.Respond, clock, shareCacheWith, freshIndex);

    private static List<string> Paths(TestPipeline.TvHarness h)
        => [.. h.Handler.Requests.Select(r => r.RequestUri!.AbsolutePath)];

    private static List<string> Searches(TestPipeline.TvHarness h)
        => [.. h.Handler.Requests
            .Where(r => r.RequestUri!.AbsolutePath == "/3/search/tv")
            .Select(r => Uri.UnescapeDataString(r.RequestUri!.Query["?query=".Length..].Split('&')[0]))];

    private static List<string> SearchQueries(TestPipeline.TvHarness h)
        => [.. h.Handler.Requests
            .Where(r => r.RequestUri!.AbsolutePath == "/3/search/tv")
            .Select(r => r.RequestUri!.Query)];

    private static string Payload(TestPipeline.TvHarness h, string fileName)
        => Directory.GetFiles(h.CacheDir, fileName, SearchOption.AllDirectories).Single();

    /// <summary>The stamp a payload file carries for itself: the clock at the moment it was fetched.</summary>
    private static DateTimeOffset StampOf(string file)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        Assert.True(doc.RootElement.TryGetProperty("fetchedUtc", out var stamp),
            $"{Path.GetFileName(file)} carries no fetchedUtc of its own, so it can never age out");
        return stamp.GetDateTimeOffset();
    }

    /// <summary>Rewrites a payload's own stamp to <paramref name="age"/> before <paramref name="now"/>, leaving the index alone.</summary>
    private static void Age(string file, DateTimeOffset now, TimeSpan age)
    {
        var text = File.ReadAllText(file);
        var aged = Regex.Replace(text, "\"fetchedUtc\": *\"[^\"]+\"", $"\"fetchedUtc\": \"{now - age:O}\"");
        Assert.NotEqual(text, aged); // fails when the payload has no stamp to age
        File.WriteAllText(file, aged);
    }

    private static readonly TimeSpan PastTheWindow = RefreshPolicy.Window + TimeSpan.FromDays(1);

    // ---- what a matched series carries ------------------------------------------------------------

    [Fact]
    public async Task AMatchedSeriesCarriesEverythingTmdbKnowsAboutIt()
    {
        var alias = Show(456, "Alias", (1, ["Truth Be Told", "So It Begins"])) with
        {
            PosterPath = "/alias.jpg", FirstAirDate = "2001-09-30"
        };
        var h = Harness(Tmdb(alias));

        // The sheet's runtime is deliberately nonsense (9 hours): episode runtime must come from TMDB.
        var result = await h.Pipeline.RunAsync(
            [Ep("Season 1, Episode 2: So It Begins", "Alias", runtime: "9:00:00")], Ct);

        var show = Assert.Single(result.Shows);
        Assert.Equal(456, show.I);
        Assert.Equal("Alias", show.T);
        Assert.Equal("alias", show.Slug);
        Assert.Equal("/alias.jpg", show.P);
        Assert.Equal(2001, show.FirstYear);
        Assert.Equal("Drama", show.Ag);
        Assert.Equal(1d, show.Mc);
        Assert.Equal(1, show.OwnedEpisodes);
        Assert.Equal(2, show.TotalEpisodes);

        var ep = Assert.Single(show.Seasons).Eps.Single(e => e.E == 2);
        Assert.Equal("So It Begins", ep.T);
        Assert.Equal("2001-10-02", ep.Air);
        Assert.Equal(FakeTv.EpisodeRuntime, ep.Rt);
        Assert.Equal("/still-1-2.jpg", ep.S);
        Assert.Equal("So It Begins overview.", ep.Ov);

        Assert.Empty(result.ReviewQueue);
        Assert.False(result.StoppedOnBudget);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")] // TMDB sends an empty date for a show with none yet
    public async Task ASeriesWithNoFirstAirDateHasNoFirstYearRatherThanFailingTheRun(string? firstAirDate)
    {
        var h = Harness(Tmdb(Show(456, "Alias", (1, ["A"])) with { FirstAirDate = firstAirDate, PosterPath = null }));

        var result = await h.Pipeline.RunAsync([Ep("Season 1, Episode 1: A", "Alias")], Ct);

        var show = Assert.Single(result.Shows);
        Assert.Null(show.FirstYear);
        Assert.Null(show.P);
        Assert.Equal(456, show.I);
    }

    // ---- the requested mutations ------------------------------------------------------------------

    [Fact]
    public async Task ANumberedRowIsMatchedByItsNumberNeverByItsName()
    {
        // The row says episode 2 but carries the NAME of episode 1. By number, episode 2 is owned.
        var h = Harness(Tmdb(Show(456, "Alias", (1, ["A", "B"]))));

        var result = await h.Pipeline.RunAsync([Ep("Season 1, Episode 2: A", "Alias")], Ct);

        var eps = Assert.Single(Assert.Single(result.Shows).Seasons).Eps;
        Assert.False(eps.Single(e => e.E == 1).O);
        Assert.True(eps.Single(e => e.E == 2).O);
    }

    [Fact]
    public async Task ABareNameIsMatchedWithinItsOwnSeriesOnly()
    {
        // Both series have a "Pilot". Only Alpha's sheet rows name one.
        var h = Harness(Tmdb(
            Show(1, "Alpha", (1, ["Pilot", "Alpha Two"])),
            Show(2, "Beta", (1, ["Pilot", "Beta Two"]))));

        var result = await h.Pipeline.RunAsync(
            [Ep("Pilot", "Alpha"), Ep("Season 1, Episode 2: Beta Two", "Beta")], Ct);

        var alpha = result.Shows.Single(s => s.T == "Alpha");
        var beta = result.Shows.Single(s => s.T == "Beta");
        Assert.True(alpha.Seasons.Single().Eps.Single(e => e.E == 1).O);
        Assert.False(beta.Seasons.Single().Eps.Single(e => e.E == 1).O); // Alpha's "Pilot" is not Beta's
        Assert.Equal(1, beta.OwnedEpisodes);
    }

    [Fact]
    public async Task ASeriesCostsOneSearchOneDetailsAndOneCallPerSeasonWhateverItOwns()
    {
        var h = Harness(Tmdb(Show(456, "Alias", (1, ["A", "B", "C"]), (2, ["D", "E", "F"]))));

        // Six owned rows, numbered and bare, across both seasons.
        var result = await h.Pipeline.RunAsync([
            Ep("Season 1, Episode 1: A", "Alias"), Ep("Season 1, Episode 3: C", "Alias"),
            Ep("Season 2, Episode 2: E", "Alias"), Ep("B", "Alias"), Ep("D", "Alias"), Ep("F", "Alias")], Ct);

        Assert.Equal(6, Assert.Single(result.Shows).OwnedEpisodes);
        Assert.Equal(
            ["/3/search/tv", "/3/tv/456", "/3/tv/456/season/1", "/3/tv/456/season/2"],
            Paths(h));
    }

    // ---- bare names live anywhere in the series, and TMDB's whole tree is emitted -----------------

    [Fact]
    public async Task ABareNameMatchesAnEpisodeInAnySeasonNotJustTheFirst()
    {
        var h = Harness(Tmdb(Show(456, "Alias", (1, ["A", "B"]), (2, ["C", "D"]), (3, ["E", "F"]))));

        var result = await h.Pipeline.RunAsync([Ep("F", "Alias")], Ct); // season 3, and no numbered row to say so

        var show = Assert.Single(result.Shows);
        Assert.Equal([1, 2, 3], show.Seasons.Select(s => s.N));
        Assert.Equal(6, show.TotalEpisodes);
        Assert.Equal(1, show.OwnedEpisodes);
        Assert.True(show.Seasons.Single(s => s.N == 3).Eps.Single(e => e.E == 2).O);
        Assert.Empty(result.ReviewQueue); // a name that occurs once is not ambiguous
    }

    [Fact]
    public async Task SeasonsTheSheetNeverMentionsAreStillEmittedForTheRevealToggle()
    {
        var h = Harness(Tmdb(Show(456, "Alias", (1, ["A"]), (2, ["B", "C"]))));

        var result = await h.Pipeline.RunAsync([Ep("Season 1, Episode 1: A", "Alias")], Ct);

        var show = Assert.Single(result.Shows);
        Assert.Equal(3, show.TotalEpisodes);
        var two = show.Seasons.Single(s => s.N == 2);
        Assert.Equal(0, two.Owned);
        Assert.Equal(2, two.Total);
    }

    [Fact]
    public async Task ADuplicateEpisodeNameClaimsTheFirstUnclaimedSlotAndIsFlagged()
    {
        var h = Harness(Tmdb(Show(456, "Alias", (1, ["Pilot", "B"]), (2, ["Pilot", "C"]))));

        var result = await h.Pipeline.RunAsync([Ep("Pilot", "Alias")], Ct);

        var show = Assert.Single(result.Shows);
        Assert.True(show.Seasons.Single(s => s.N == 1).Eps.Single(e => e.E == 1).O);
        Assert.False(show.Seasons.Single(s => s.N == 2).Eps.Single(e => e.E == 1).O);
        Assert.Equal(1, show.OwnedEpisodes);
        Assert.Contains(result.ReviewQueue, l => l.Contains("Alias") && l.Contains("Pilot"));
    }

    [Fact]
    public async Task TwoRowsWithTheSameNameClaimBothSlotsOfThatName()
    {
        var h = Harness(Tmdb(Show(456, "Alias", (1, ["Pilot", "B"]), (2, ["Pilot", "C"]))));

        var result = await h.Pipeline.RunAsync([Ep("Pilot", "Alias"), Ep("Pilot", "Alias")], Ct);

        Assert.Equal(2, Assert.Single(result.Shows).OwnedEpisodes);
    }

    [Fact]
    public async Task ANumberedRowHasClaimedItsSlotBeforeABareNameCanTakeIt()
    {
        var h = Harness(Tmdb(Show(456, "Alias", (1, ["Pilot", "B"]), (2, ["Pilot", "C"]))));

        // The numbered row owns season 1's "Pilot"; the bare "Pilot" is therefore season 2's.
        var result = await h.Pipeline.RunAsync(
            [Ep("Season 1, Episode 1: Pilot", "Alias"), Ep("Pilot", "Alias")], Ct);

        var show = Assert.Single(result.Shows);
        Assert.Equal(2, show.OwnedEpisodes);
        Assert.True(show.Seasons.Single(s => s.N == 2).Eps.Single(e => e.E == 1).O);
    }

    [Fact]
    public async Task OwnedRowsThatTmdbDoesNotListAreKeptNotDropped()
    {
        var h = Harness(Tmdb(Show(456, "Alias", (1, ["A", "B"]))));

        var result = await h.Pipeline.RunAsync([
            Ep("Season 1, Episode 1: A", "Alias"),
            Ep("Season 1, Episode 9: Beyond The List", "Alias"), // TMDB has no episode 9
            Ep("Season 1, Episode 9: Beyond The List", "Alias"), // the same row twice is one episode
            Ep("Season 3, Episode 1: Lost Season", "Alias"),     // nor a season 3
            Ep("Not On Tmdb", "Alias"), Ep("Also Missing", "Alias")], Ct); // nor these names

        var show = Assert.Single(result.Shows);
        Assert.Equal(5, show.OwnedEpisodes); // every distinct owned episode is accounted for
        Assert.Equal([0, 1, 3], show.Seasons.Select(s => s.N));

        var one = show.Seasons.Single(s => s.N == 1);
        Assert.Equal(3, one.Total); // TMDB's two plus the one it lacks
        Assert.Equal(2, one.Owned);
        Assert.Equal([1, 2, 9], one.Eps.Select(e => e.E));
        Assert.Contains(one.Eps, e => e is { E: 9, O: true, T: "Beyond The List" });

        var three = Assert.Single(show.Seasons.Single(s => s.N == 3).Eps);
        Assert.Equal((1, "Lost Season", true), (three.E, three.T, three.O));

        // No place to hang on: season 0, the "unplaced episodes" group, numbered in name order.
        var unplaced = show.Seasons.Single(s => s.N == 0);
        Assert.Equal([(1, "Also Missing"), (2, "Not On Tmdb")], unplaced.Eps.Select(e => (e.E, e.T)));
        Assert.All(unplaced.Eps, e => Assert.True(e.O));
        Assert.Contains(result.ReviewQueue, l => l.Contains("Alias") && l.Contains("not found on TMDB"));
    }

    [Fact]
    public async Task ASeasonZeroRowIsUnplacedAndSeasonZeroIsNeverFetched()
    {
        var h = Harness(Tmdb(Show(456, "Alias", (1, ["A"]))));

        var result = await h.Pipeline.RunAsync(
            [Ep("Season 0, Episode 5: Zed Special", "Alias"), Ep("Alpha Extra", "Alias")], Ct);

        var show = Assert.Single(result.Shows);
        Assert.Equal(2, show.OwnedEpisodes);
        var unplaced = Assert.Single(show.Seasons, s => s.N == 0);
        Assert.Equal([(1, "Alpha Extra"), (2, "Zed Special")], unplaced.Eps.Select(e => (e.E, e.T)));
        Assert.DoesNotContain("/3/tv/456/season/0", Paths(h));
    }

    [Fact]
    public async Task AnOwnedEpisodeInAGapOfTmdbsNumberingSortsIntoPlace()
    {
        var gappy = new FakeShow(456, "Alias", [new FakeSeason(1, ["A", "B", "D"]) { Numbers = [1, 2, 4] }]);
        var h = Harness(Tmdb(gappy));

        var result = await h.Pipeline.RunAsync([Ep("Season 1, Episode 3: C", "Alias")], Ct);

        var season = Assert.Single(Assert.Single(result.Shows).Seasons);
        Assert.Equal([1, 2, 3, 4], season.Eps.Select(e => e.E));
        Assert.Equal(4, season.Total);
        Assert.Equal(1, season.Owned);
    }

    [Fact]
    public async Task ABareNameMatchesDespiteCaseAccentsAndPunctuation()
    {
        var h = Harness(Tmdb(Show(456, "Alias", (1, ["Truth Be Told", "Café Society: Part 1"]))));

        var result = await h.Pipeline.RunAsync([Ep("cafe society part 1!", "Alias")], Ct);

        var season = Assert.Single(Assert.Single(result.Shows).Seasons);
        Assert.Equal(1, season.Owned);
        Assert.True(season.Eps.Single(e => e.E == 2).O);
    }

    // ---- series are grouped and ordered deterministically -----------------------------------------

    [Fact]
    public async Task RowsThatDifferOnlyInSeriesCaseBelongToOneSeries()
    {
        var h = Harness(Tmdb(Show(456, "Alias", (1, ["A", "B"]))));

        var result = await h.Pipeline.RunAsync(
            [Ep("Season 1, Episode 1: A", "Alias"), Ep("Season 1, Episode 2: B", "ALIAS ")], Ct);

        Assert.Equal(2, Assert.Single(result.Shows).OwnedEpisodes);
    }

    [Fact]
    public async Task SeriesAreSearchedInNameOrderNotSheetOrder()
    {
        var h = Harness(Tmdb(
            Show(1, "alpha", (1, ["A"])), Show(2, "Mid", (1, ["M"])), Show(3, "Zed", (1, ["Z"]))));

        var result = await h.Pipeline.RunAsync(
            [Ep("Z", "Zed"), Ep("A", "alpha"), Ep("M", "Mid")], Ct);

        // Ordinal, so a capital sorts before any lower-case letter: not the culture's idea of alphabetical.
        Assert.Equal(["Mid", "Zed", "alpha"], Searches(h));
        Assert.Equal(["Mid", "Zed", "alpha"], result.Shows.Select(s => s.T));
    }

    [Fact]
    public async Task ACancelledRunThrowsBeforeTouchingTmdb()
    {
        var h = Harness(Tmdb(Show(456, "Alias", (1, ["A"]))));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => h.Pipeline.RunAsync([Ep("A", "Alias")], cts.Token));
        Assert.Empty(h.Handler.Requests);
    }

    [Fact]
    public async Task TheResultDoesNotDependOnTheOrderOfTheSheetRows()
    {
        // Apple's export order is not stable. Two ambiguous names make two review notes whose order shows it.
        FakeTv Backend() => Tmdb(
            Show(1, "Alpha", (1, ["Pilot", "Zed Dup"]), (2, ["Alpha Dup", "Zed Dup"]), (3, ["Alpha Dup"])),
            Show(2, "Beta", (1, ["B"])));
        SheetRow[] rows = [
            Ep("Zed Dup", "Alpha"), Ep("Alpha Dup", "Alpha"), Ep("Pilot", "Alpha"), Ep("Lost", "Alpha"),
            Ep("B", "Beta"), Ep("Season 4, Episode 1: Nowhere", "Alpha")];

        var forward = await Harness(Backend()).Pipeline.RunAsync(rows, Ct);
        var backward = await Harness(Backend()).Pipeline.RunAsync([.. rows.Reverse()], Ct);

        Assert.Equal(JsonSerializer.Serialize(forward.Shows), JsonSerializer.Serialize(backward.Shows));
        Assert.Equal(forward.ReviewQueue, backward.ReviewQueue);
        Assert.True(forward.ReviewQueue.Count >= 3); // the two ambiguities and the unplaced rows are in there
    }

    // ---- slugs (Task 6 review, items 7 and 8) -----------------------------------------------------

    [Fact]
    public async Task TwoSeriesWithTheSameSlugFailLoudlyBeforeAnyRequestIsSpent()
    {
        var h = Harness(Tmdb(Show(1, "Amélie", (1, ["A"])), Show(2, "Amelie", (1, ["B"]))));

        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => h.Pipeline.RunAsync([Ep("A", "Amélie"), Ep("B", "Amelie")], Ct));

        Assert.Contains("Amélie", ex.Message);
        Assert.Contains("Amelie", ex.Message);
        Assert.Contains("amelie", ex.Message);
        Assert.Empty(h.Handler.Requests);
    }

    [Theory]
    [InlineData("(2020)")] // folds to "", slug "-2020"
    [InlineData("(1999)")]
    [InlineData("!!!")]    // folds to "", slug ""
    public async Task ASeriesWhoseSlugIsEmptyOrStartsWithAHyphenFailsLoudly(string series)
    {
        var h = Harness(Tmdb());

        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => h.Pipeline.RunAsync([Ep("A", series)], Ct));

        Assert.Contains(series, ex.Message);
        Assert.Empty(h.Handler.Requests);
    }

    [Fact]
    public async Task ASeriesAndItsYearSuffixedTwinAreDistinctSeriesNotACollision()
    {
        var h = Harness(Tmdb(
            Show(10, "The Biggest Loser", (1, ["A"])),
            Show(20, "The Biggest Loser", (1, ["B"])) with { SearchYear = 2020 },
            Show(30, "dune", (1, ["C"])),
            Show(40, "dune", (1, ["D"])) with { SearchYear = 2021 }));

        var result = await h.Pipeline.RunAsync([
            Ep("A", "The Biggest Loser"), Ep("B", "The Biggest Loser (2020)"),
            Ep("C", "dune"), Ep("D", "dune (2021)")], Ct);

        Assert.Equal(
            ["the-biggest-loser", "the-biggest-loser-2020", "dune", "dune-2021"], result.Shows.Select(s => s.Slug));
        // Each is its own cache identity: the folded title alone is the same for both of a pair, and for
        // the lower-case pair so is its cleaned title, which the cache would otherwise take for an alias.
        Assert.Equal([10, 20, 30, 40], result.Shows.Select(s => s.I));
        Assert.Equal(4, Searches(h).Count);
        Assert.Equal(4, h.Cache.Entries.Count);
        // TMDB calls all four by the plain name, so the year in the Sheet name must not count against them.
        Assert.Equal([1d, 1d, 1d, 1d], result.Shows.Select(s => s.Mc));
        Assert.Empty(result.ReviewQueue);
    }

    // ---- the cache: warm runs, negatives, and the budget (Task 11 and 12 contracts) ----------------

    [Fact]
    public async Task ASecondRunOverAnUnchangedSheetMakesNoRequestsAndChangesNothing()
    {
        var h = Harness(Tmdb(
            Show(456, "Alias", (1, ["A", "B"]), (2, ["C"])) with { PosterPath = "/alias.jpg" },
            Show(789, "Bones", (1, ["D"])) with { PosterPath = "/bones.jpg", FirstAirDate = "2005-09-13" }));
        SheetRow[] rows = [Ep("Season 1, Episode 1: A", "Alias"), Ep("C", "Alias"), Ep("D", "Bones")];

        var first = await h.Pipeline.RunAsync(rows, Ct);
        h.Handler.Requests.Clear();
        var second = await h.Pipeline.RunAsync(rows, Ct);

        Assert.Empty(h.Handler.Requests);
        Assert.Equal(JsonSerializer.Serialize(first.Shows), JsonSerializer.Serialize(second.Shows));
        // Each series gets its own poster and year back from the cache.
        Assert.Equal([(456, "/alias.jpg", 2001), (789, "/bones.jpg", 2005)],
                     second.Shows.Select(s => (s.I, s.P, s.FirstYear)));
    }

    [Fact]
    public async Task AnUnmatchedSeriesIsNotSearchedAgainWhileItsNegativeIsFresh()
    {
        var now = TestPipeline.Now;
        var h = Harness(Tmdb(), clock: () => now);
        SheetRow[] rows = [Ep("A", "Unknown Show")];

        await h.Pipeline.RunAsync(rows, Ct);
        Assert.Single(h.Handler.Requests);
        Assert.True(Assert.Single(h.Cache.Entries).Negative);

        now += TimeSpan.FromDays(29);
        h.Handler.Requests.Clear();
        var warm = await h.Pipeline.RunAsync(rows, Ct);

        Assert.Empty(h.Handler.Requests);
        // A cached negative is still waiting for a human, so it stays in the review queue, once.
        Assert.Equal(["Unknown Show → series not found"], warm.ReviewQueue);
    }

    [Fact]
    public async Task AnUnmatchedSeriesIsSearchedAgainOnceItsNegativeExpires()
    {
        var now = TestPipeline.Now;
        var h = Harness(Tmdb(), clock: () => now);
        SheetRow[] rows = [Ep("A", "Unknown Show")];

        await h.Pipeline.RunAsync(rows, Ct);
        now += TimeSpan.FromDays(31);
        h.Handler.Requests.Clear();
        await h.Pipeline.RunAsync(rows, Ct);

        Assert.Single(h.Handler.Requests);
    }

    [Fact]
    public async Task TheSeriesLookupThatExhaustsTheBudgetIsNotCachedAsANegative()
    {
        // Budget 1: the search goes out, the details call behind it is refused. The client returns null
        // for that exactly as it does for "TMDB has no such series".
        var h = Harness(Tmdb(Show(456, "Alias", (1, ["A"]))), budget: 1);

        var result = await h.Pipeline.RunAsync([Ep("A", "Alias")], Ct);

        Assert.True(result.StoppedOnBudget);
        Assert.Empty(h.Cache.Entries);
        Assert.Equal(0, Assert.Single(result.Shows).I);
        Assert.Contains(result.ReviewQueue, l => l.Contains("Alias") && l.Contains("budget"));
    }

    [Fact]
    public async Task SeriesTheBudgetNeverReachedAreNeitherSearchedNorCachedAsNegatives()
    {
        // Alpha takes all three requests. Beta's search is the first one refused (the flag flips inside
        // that call); Gamma then finds the flag already set and is not even tried.
        var h = Harness(Tmdb(
            Show(1, "Alpha", (1, ["A"])), Show(2, "Beta", (1, ["B"])), Show(3, "Gamma", (1, ["G"]))),
            budget: 3);

        var result = await h.Pipeline.RunAsync(
            [Ep("A", "Alpha"), Ep("B", "Beta"), Ep("G", "Gamma")], Ct);

        Assert.True(result.StoppedOnBudget);
        var entry = Assert.Single(h.Cache.Entries);
        Assert.False(entry.Negative);
        Assert.Equal(1, entry.TmdbId);
        Assert.Equal([1, 0, 0], result.Shows.Select(s => s.I));
        Assert.Equal([1d, 0d, 0d], result.Shows.Select(s => s.Mc));
        Assert.Equal(3, result.Shows.Count); // unresolved series are still emitted from sheet data
        Assert.Contains(result.ReviewQueue, l => l.Contains("Beta") && l.Contains("budget"));
        Assert.Contains(result.ReviewQueue, l => l.Contains("Gamma") && l.Contains("budget"));
        Assert.Equal(3, h.Handler.Requests.Count); // the budget was never exceeded
        Assert.Equal(["Alpha"], Searches(h));      // Beta's was refused before it was sent, Gamma's never tried
    }

    [Fact]
    public async Task ABudgetThatRunsOutBetweenSeasonsKeepsWhatWasFetchedAndTheNextRunResumes()
    {
        var fake = Tmdb(Show(456, "Alias", (1, ["A", "B"]), (2, ["C", "D"]), (3, ["E", "F"])));
        SheetRow[] rows = [
            Ep("Season 1, Episode 1: A", "Alias"), Ep("Season 2, Episode 1: C", "Alias"),
            Ep("Season 3, Episode 1: E", "Alias")];

        // 4 requests: search, details, season 1, season 2. Season 3 is refused.
        var first = Harness(fake, budget: 4);
        var partial = await first.Pipeline.RunAsync(rows, Ct);

        Assert.True(partial.StoppedOnBudget);
        var show = Assert.Single(partial.Shows);
        Assert.Equal(456, show.I); // the series itself resolved, and that is kept
        Assert.Equal(1d, show.Mc);
        Assert.False(Assert.Single(first.Cache.Entries).Negative);
        Assert.Equal(2, show.Seasons.Single(s => s.N == 1).Total);
        Assert.Equal(2, show.Seasons.Single(s => s.N == 2).Total);
        Assert.Equal(3, show.OwnedEpisodes); // the owned season-3 row is not lost while its season is missing
        Assert.Contains(partial.ReviewQueue, l => l.Contains("Alias") && l.Contains("budget"));
        Assert.DoesNotContain(partial.ReviewQueue, l => l.Contains("not found on TMDB")); // pending, not missing

        // The next job has a fresh budget over the same cache and picks up only what is missing.
        var second = Harness(fake, shareCacheWith: first);
        var resumed = await second.Pipeline.RunAsync(rows, Ct);

        Assert.Equal(["/3/tv/456/season/3"], Paths(second));
        Assert.False(resumed.StoppedOnBudget);
        Assert.Empty(resumed.ReviewQueue);
        var done = Assert.Single(resumed.Shows);
        Assert.Equal(6, done.TotalEpisodes);
        Assert.Equal(3, done.OwnedEpisodes);
    }

    [Fact]
    public async Task TheCacheRecordsWhatWasResolvedAndWhenUsingTheFoldedTitle()
    {
        var h = Harness(Tmdb(Show(456, "NCIS: Los Angeles", (1, ["A"]))));

        await h.Pipeline.RunAsync([Ep("A", "NCIS: Los Angeles"), Ep("B", "Unknown Show")], Ct);

        var found = Assert.Single(h.Cache.Entries, e => !e.Negative);
        // The golden key from IdentityKeyTests: "series|ncis los angeles".
        Assert.Equal("1c1cd2a6dc513639168a1f895a50792236ae37e5", found.Key);
        Assert.Equal("ncis los angeles", found.NormalizedTitle); // folded Value, never Cleaned
        Assert.Equal(456, found.TmdbId);
        Assert.Equal(1d, found.Confidence);
        Assert.Equal(TestPipeline.Now, found.FetchedUtc);
        Assert.Equal(0, found.RuntimeMinutes);

        var missing = Assert.Single(h.Cache.Entries, e => e.Negative);
        Assert.Equal("unknown show", missing.NormalizedTitle);
        Assert.Null(missing.TmdbId);
        Assert.Equal(0d, missing.Confidence);
        Assert.Equal(TestPipeline.Now, missing.FetchedUtc);
    }

    // ---- A2: a payload ages out on its own stamp --------------------------------------------------

    [Fact]
    public async Task ASeasonPayloadIsStampedWithTheTimeItWasFetched()
    {
        var h = Harness(Tmdb(Show(456, "Alias", (1, ["A"]))));

        await h.Pipeline.RunAsync([Ep("A", "Alias")], Ct);

        Assert.Equal(TestPipeline.Now, StampOf(Payload(h, "season-1.json")));
    }

    [Fact]
    public async Task AStaleSeasonPayloadIsRefetchedEvenThoughTheIndexEntryIsStillFresh()
    {
        var h = Harness(Tmdb(Show(456, "Alias", (1, ["A", "B"]), (2, ["C"]))));
        SheetRow[] rows = [Ep("Season 1, Episode 1: A", "Alias")];
        await h.Pipeline.RunAsync(rows, Ct);

        // Only the season's own stamp ages. The index entry and series payload are as fresh as ever.
        Age(Payload(h, "season-1.json"), TestPipeline.Now, PastTheWindow);
        h.Handler.Requests.Clear();
        var result = await h.Pipeline.RunAsync(rows, Ct);

        Assert.Equal(["/3/tv/456/season/1"], Paths(h));
        Assert.Equal(TestPipeline.Now, StampOf(Payload(h, "season-1.json")));
        Assert.Equal(3, Assert.Single(result.Shows).TotalEpisodes);

        // Re-stamped, so it is not refetched a third time.
        h.Handler.Requests.Clear();
        await h.Pipeline.RunAsync(rows, Ct);
        Assert.Empty(h.Handler.Requests);
    }

    [Theory]
    [InlineData(0, 0)]  // exactly the window old: still fresh
    [InlineData(1, 1)]  // a minute past it: stale
    public async Task ASeasonPayloadIsStaleOnlyOnceItIsOlderThanTheWindow(int minutesPast, int expectedRequests)
    {
        var h = Harness(Tmdb(Show(456, "Alias", (1, ["A"]))));
        await h.Pipeline.RunAsync([Ep("A", "Alias")], Ct);

        Age(Payload(h, "season-1.json"), TestPipeline.Now, RefreshPolicy.Window + TimeSpan.FromMinutes(minutesPast));
        h.Handler.Requests.Clear();
        await h.Pipeline.RunAsync([Ep("A", "Alias")], Ct);

        Assert.Equal(expectedRequests, h.Handler.Requests.Count);
    }

    [Fact]
    public async Task AStaleSeasonPayloadIsUsedWhenTheBudgetCannotRefetchIt()
    {
        var fake = Tmdb(Show(456, "Alias", (1, ["A", "B"]), (2, ["C", "D"])));
        SheetRow[] rows = [Ep("Season 1, Episode 1: A", "Alias"), Ep("Season 2, Episode 1: C", "Alias")];
        var first = Harness(fake);
        await first.Pipeline.RunAsync(rows, Ct);
        foreach (var file in Directory.GetFiles(first.CacheDir, "season-*.json", SearchOption.AllDirectories))
            Age(file, TestPipeline.Now, PastTheWindow);

        // One request: season 1 is refetched, season 2's refetch is refused.
        var second = Harness(fake, budget: 1, shareCacheWith: first);
        var result = await second.Pipeline.RunAsync(rows, Ct);

        Assert.True(result.StoppedOnBudget);
        var two = Assert.Single(result.Shows).Seasons.Single(s => s.N == 2);
        Assert.Equal(2, two.Total); // TMDB's list from the stale copy, not a one-row stub
    }

    [Fact]
    public async Task AStaleSeriesPayloadIsRefetchedEvenThoughTheIndexEntryIsStillFresh()
    {
        var h = Harness(Tmdb(Show(456, "Alias", (1, ["A"]))));
        SheetRow[] rows = [Ep("A", "Alias")];
        await h.Pipeline.RunAsync(rows, Ct);
        Assert.Equal(TestPipeline.Now, StampOf(Payload(h, "series.json")));

        Age(Payload(h, "series.json"), TestPipeline.Now, PastTheWindow);
        h.Handler.Requests.Clear();
        await h.Pipeline.RunAsync(rows, Ct);

        Assert.Equal(["/3/search/tv", "/3/tv/456"], Paths(h)); // seasons are fresh and stay on disk
        Assert.Equal(TestPipeline.Now, StampOf(Payload(h, "series.json")));
    }

    [Fact]
    public async Task AStaleSeriesPayloadIsUsedWhenTheBudgetCannotRefetchIt()
    {
        var fake = Tmdb(Show(456, "Alias", (1, ["A"])) with { PosterPath = "/stale.jpg" });
        SheetRow[] rows = [Ep("A", "Alias")];
        var first = Harness(fake);
        await first.Pipeline.RunAsync(rows, Ct);
        Age(Payload(first, "series.json"), TestPipeline.Now, PastTheWindow);

        // Budget 1: the re-search goes out, the details call behind it is refused.
        var second = Harness(fake, budget: 1, shareCacheWith: first);
        var result = await second.Pipeline.RunAsync(rows, Ct);

        var show = Assert.Single(result.Shows);
        Assert.Equal(456, show.I);
        Assert.Equal("/stale.jpg", show.P);
        Assert.Equal(1d, show.Mc);
        Assert.False(Assert.Single(second.Cache.Entries).Negative);
    }

    [Fact]
    public async Task APayloadCutShortByACrashIsRefetchedNotFatal()
    {
        var h = Harness(Tmdb(Show(456, "Alias", (1, ["A", "B"]))));
        SheetRow[] rows = [Ep("A", "Alias")];
        await h.Pipeline.RunAsync(rows, Ct);
        File.WriteAllText(Payload(h, "season-1.json"), "{ \"fetchedUtc\": \"2026-10-24T12:00");
        File.WriteAllText(Payload(h, "series.json"), "{ \"fetchedUtc\": ");

        h.Handler.Requests.Clear();
        var result = await h.Pipeline.RunAsync(rows, Ct);

        Assert.Equal(2, Assert.Single(result.Shows).TotalEpisodes);
        Assert.Equal(["/3/search/tv", "/3/tv/456", "/3/tv/456/season/1"], Paths(h));
        Assert.Equal(TestPipeline.Now, StampOf(Payload(h, "season-1.json")));
    }

    [Fact]
    public async Task ASeriesThatNowResolvesToADifferentIdNeverReadsTheOldIdsSeasons()
    {
        var first = Harness(Tmdb(Show(100, "Alias", (1, ["Old One"]))));
        await first.Pipeline.RunAsync([Ep("Old One", "Alias")], Ct);

        // The index is lost, TMDB's top result for the name has changed, and the old season files remain.
        var second = Harness(
            Tmdb(Show(200, "Alias", (1, ["New One", "New Two"]))), shareCacheWith: first, freshIndex: true);
        var result = await second.Pipeline.RunAsync([Ep("New Two", "Alias")], Ct);

        var show = Assert.Single(result.Shows);
        Assert.Equal(200, show.I);
        Assert.Equal(["New One", "New Two"], Assert.Single(show.Seasons).Eps.Select(e => e.T));
    }

    // ---- fix pass: A (is it the right show), B (order), C (lost series), D (blank series) ----------

    private static SheetRow Row(string title, string series, string genre)
        => new(title, "44:00:00", "", series + ": The Complete Series", genre, series);

    [Fact]
    public async Task AMatchThatIsTheSameNameOnceFoldedIsFullConfidenceAndSilent()
    {
        // The Sheet has no colon and no capitals; folding makes it the same name as TMDB has.
        var h = Harness(Tmdb(Show(456, "NCIS: Los Angeles", (1, ["A"])) with { SearchAs = "ncis los angeles" }));

        var result = await h.Pipeline.RunAsync([Ep("A", "ncis los angeles")], Ct);

        var show = Assert.Single(result.Shows);
        Assert.Equal(456, show.I);
        Assert.Equal(1d, show.Mc);
        Assert.Equal(1d, Assert.Single(h.Cache.Entries).Confidence);
        Assert.Empty(result.ReviewQueue);
    }

    [Fact]
    public async Task AMatchUnderADifferentNameIsAGuessAndTheReviewQueueNamesBoth()
    {
        var h = Harness(Tmdb(
            Show(1, "Alias", (1, ["A"])),
            Show(2996, "The Office (UK)", (1, ["B"])) with { SearchAs = "The Office" }));

        var result = await h.Pipeline.RunAsync(
            [Ep("A", "Alias"), Ep("B", "The Office"), Ep("C", "Nothing Known")], Ct);

        // Mc is not a constant: 1.0 for a name match, 0.5 for a guess, 0 for no match.
        Assert.Equal(["Alias", "Nothing Known", "The Office"], result.Shows.Select(s => s.T));
        Assert.Equal([1d, 0d, 0.5d], result.Shows.Select(s => s.Mc));
        Assert.Equal([1, 0, 2996], result.Shows.Select(s => s.I)); // the guess is still used

        var guess = Assert.Single(h.Cache.Entries, e => e.TmdbId == 2996);
        Assert.Equal(0.5d, guess.Confidence);
        Assert.False(guess.Negative);

        Assert.Equal(2, result.ReviewQueue.Count);
        var line = Assert.Single(result.ReviewQueue, l => l.Contains("The Office"));
        Assert.Contains("The Office (UK)", line); // the name TMDB has
        Assert.Contains("2996", line);            // the id to paste
        Assert.Contains(result.ReviewQueue, l => l.Contains("Nothing Known") && l.Contains("not found"));
    }

    [Fact]
    public async Task AGuessStaysAGuessAndStaysInTheReviewQueueOnAWarmRun()
    {
        var h = Harness(Tmdb(Show(2996, "The Office (UK)", (1, ["B"])) with { SearchAs = "The Office" }));
        SheetRow[] rows = [Ep("B", "The Office")];

        var first = await h.Pipeline.RunAsync(rows, Ct);
        h.Handler.Requests.Clear();
        var second = await h.Pipeline.RunAsync(rows, Ct);

        Assert.Empty(h.Handler.Requests);
        Assert.Equal(0.5d, Assert.Single(second.Shows).Mc);
        Assert.Equal(first.ReviewQueue, second.ReviewQueue);
        Assert.Single(second.ReviewQueue);
    }

    [Fact]
    public async Task TheSearchCarriesTheTrailingYearOfTheSheetNameInsteadOfTheParenthetical()
    {
        var h = Harness(Tmdb(
            Show(10, "The Biggest Loser", (1, ["A"])),
            Show(20, "The Biggest Loser", (1, ["B"])) with { SearchYear = 2020 }));

        var result = await h.Pipeline.RunAsync(
            [Ep("A", "The Biggest Loser"), Ep("B", "The Biggest Loser (2020)")], Ct);

        Assert.Equal(
            ["?query=The%20Biggest%20Loser", "?query=The%20Biggest%20Loser&first_air_date_year=2020"],
            SearchQueries(h));
        Assert.Equal([10, 20], result.Shows.Select(s => s.I));
    }

    [Fact]
    public async Task TheSeriesNameTakesTheMostFrequentCasingWhateverTheRowOrder()
    {
        FakeTv Backend() => new() { AnyQuery = Show(1, "Alias", (1, ["A", "B", "C"])) };
        SheetRow[] majority = [
            Ep("Season 1, Episode 1: A", "ALIAS"), Ep("Season 1, Episode 2: B", "Alias"),
            Ep("Season 1, Episode 3: C", "Alias")];
        SheetRow[] tie = [Ep("Season 1, Episode 1: A", "alias"), Ep("Season 1, Episode 2: B", "ALIAS")];

        foreach (var rows in new[] { majority, [.. majority.Reverse()] })
            Assert.Equal("Alias", Assert.Single((await Harness(Backend()).Pipeline.RunAsync(rows, Ct)).Shows).T);

        // Equal counts: ordinal order, so the capitals win.
        foreach (var rows in new[] { tie, [.. tie.Reverse()] })
            Assert.Equal("ALIAS", Assert.Single((await Harness(Backend()).Pipeline.RunAsync(rows, Ct)).Shows).T);
    }

    [Fact]
    public async Task TheGenreTakesTheMostFrequentValueWhateverTheRowOrder()
    {
        FakeTv Backend() => new() { AnyQuery = Show(1, "Alias", (1, ["A", "B", "C"])) };
        SheetRow[] majority = [Row("A", "Alias", "Drama"), Row("B", "Alias", "Comedy"), Row("C", "Alias", "Comedy")];
        SheetRow[] tie = [Row("A", "Alias", "Drama"), Row("B", "Alias", "Comedy")];

        foreach (var rows in new[] { majority, [.. majority.Reverse()] })
            Assert.Equal("Comedy", Assert.Single((await Harness(Backend()).Pipeline.RunAsync(rows, Ct)).Shows).Ag);

        // Equal counts: ordinal order.
        foreach (var rows in new[] { tie, [.. tie.Reverse()] })
            Assert.Equal("Comedy", Assert.Single((await Harness(Backend()).Pipeline.RunAsync(rows, Ct)).Shows).Ag);
    }

    [Fact]
    public async Task AStaleSeriesThatTmdbNoLongerFindsKeepsItsCachedIdAndIsQueuedForReview()
    {
        SheetRow[] rows = [Ep("Season 1, Episode 1: A", "Alias")];
        var first = Harness(Tmdb(Show(456, "Alias", (1, ["A", "B"]))));
        await first.Pipeline.RunAsync(rows, Ct);
        Age(Payload(first, "series.json"), TestPipeline.Now, PastTheWindow);

        // The re-search finds nothing, with the budget intact: that is not "the budget ran out".
        var second = Harness(Tmdb(), shareCacheWith: first);
        var result = await second.Pipeline.RunAsync(rows, Ct);

        Assert.Equal(["/3/search/tv"], Paths(second));
        var show = Assert.Single(result.Shows);
        Assert.Equal(456, show.I);
        Assert.Equal(1d, show.Mc);
        Assert.Equal(2, show.TotalEpisodes); // the stale payload still says one season, and it is on disk
        var entry = Assert.Single(second.Cache.Entries);
        Assert.False(entry.Negative);
        Assert.Equal(456, entry.TmdbId);
        Assert.Contains(result.ReviewQueue, l => l.Contains("Alias") && l.Contains("456"));
    }

    [Fact]
    public async Task AStaleGuessThatTmdbNoLongerFindsIsStillAGuess()
    {
        SheetRow[] rows = [Ep("Season 1, Episode 1: A", "The Office")];
        var first = Harness(Tmdb(Show(2996, "The Office (UK)", (1, ["A"])) with { SearchAs = "The Office" }));
        await first.Pipeline.RunAsync(rows, Ct);
        Age(Payload(first, "series.json"), TestPipeline.Now, PastTheWindow);

        var second = Harness(Tmdb(), shareCacheWith: first);
        var result = await second.Pipeline.RunAsync(rows, Ct);

        Assert.Equal(0.5d, Assert.Single(result.Shows).Mc); // judged from the stale payload, still a guess
        Assert.Equal(0.5d, Assert.Single(second.Cache.Entries).Confidence);
        Assert.Equal(2, result.ReviewQueue.Count); // the name mismatch and the lost series, both naming it
        Assert.All(result.ReviewQueue, l => Assert.Contains("The Office", l));
        Assert.Contains(result.ReviewQueue, l => l.Contains("The Office (UK)"));
        Assert.Contains(result.ReviewQueue, l => l.Contains("no longer finds") && l.Contains("2996"));
    }

    [Fact]
    public async Task ASeriesWhosePayloadIsGoneAndWhomTmdbNoLongerFindsKeepsItsCachedIdAndConfidence()
    {
        SheetRow[] rows = [Ep("Season 1, Episode 1: A", "The Office")];
        var first = Harness(Tmdb(Show(2996, "The Office (UK)", (1, ["A", "B"])) with { SearchAs = "The Office" }));
        await first.Pipeline.RunAsync(rows, Ct);
        File.Delete(Payload(first, "series.json"));

        var second = Harness(Tmdb(), shareCacheWith: first);
        var result = await second.Pipeline.RunAsync(rows, Ct);

        var show = Assert.Single(result.Shows);
        Assert.Equal(2996, show.I);
        Assert.Equal(0.5d, show.Mc); // the index entry remembers it was a guess
        Assert.Equal(1, show.OwnedEpisodes); // no details to hang on, so the owned row stands alone
        var entry = Assert.Single(second.Cache.Entries);
        Assert.False(entry.Negative);
        Assert.Equal(2996, entry.TmdbId);
        Assert.Contains(result.ReviewQueue, l => l.Contains("The Office") && l.Contains("2996"));
    }

    [Fact]
    public async Task RowsWithNoSeriesNameAreCountedInOneReviewLine()
    {
        var h = Harness(Tmdb(Show(456, "Alias", (1, ["A"]))));

        var result = await h.Pipeline.RunAsync([
            Ep("A", "Alias"),
            new SheetRow("x", "44:00:00", "", "p", "Drama", ""),
            new SheetRow("y", "44:00:00", "", "p", "Drama", "   "),
            new SheetRow("z", "44:00:00", "", "p", "Drama", "")], Ct);

        Assert.Single(result.Shows);
        Assert.Equal(["3 row(s) have no series name and were skipped"], result.ReviewQueue);
    }
}

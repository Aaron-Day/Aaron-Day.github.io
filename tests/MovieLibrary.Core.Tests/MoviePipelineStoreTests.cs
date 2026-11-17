using MovieLibrary.Core.Caching;
using MovieLibrary.Core.Identity;
using MovieLibrary.Core.Matching;
using MovieLibrary.Core.Models;
using MovieLibrary.Core.Store;
using MovieLibrary.Core.Text;
using MovieLibrary.Enrich.Store;

namespace MovieLibrary.Core.Tests;

public class MoviePipelineStoreTests
{
    private const string Coens = "Joel Coen & Ethan Coen";

    private static SheetRow Row(string title = "Fargo", string runtime = "1:38:00")
        => new(title, runtime, Coens, "iTunes", "Thriller", "");

    private static BoundStore Bound(params (SheetRow Row, RecordOverride[] Records)[] entries)
        => TestStore.Films(entries);

    private static HttpResponseMessage SearchFindsFargo(HttpRequestMessage req)
        => req.RequestUri!.AbsolutePath switch
        {
            "/3/search/movie" => TestPipeline.Json(
                """{"results":[{"id":275,"title":"Fargo","release_date":"1996-03-08","popularity":30}]}"""),
            "/3/movie/275" => TestPipeline.Json(RunLibrary.FargoDetails),
            "/3/movie/275/credits" => TestPipeline.Json(RunLibrary.FargoCredits),
            _ => TestPipeline.NotFound(),
        };

    [Fact]
    public async Task AnAssertedIdIsTakenWithoutASearchAndAtFullConfidence()
    {
        var row = Row();
        var pipeline = TestPipeline.ThatFailsIfSearched(275);

        var result = await pipeline.RunAsync([row], Bound((row, [new RecordOverride(TmdbId: 275)])), default);

        var film = Assert.Single(result.Records);
        Assert.Equal(275, film.I);
        Assert.Equal(1d, film.Mc);
        Assert.Equal(Confidence.Confident, ConfidenceBand.Of(film));
    }

    [Fact]
    public async Task EveryOverridableFieldReplacesWhatTheSheetAndTheSearchWouldHaveGiven()
    {
        var row = Row();
        var pipeline = TestPipeline.ThatFailsIfSearched(275);

        var result = await pipeline.RunAsync([row], Bound((row,
        [
            new RecordOverride(
                TmdbId: 275,
                Runtime: 97,
                Titles: new TitleSet(Display: "Fargo (1996)", Sort: "Fargo 1996"),
                Year: 1997,
                Directors: "Joel Coen",
                AppleGenre: "Crime",
                PurchaseInfo: "4K Blu-ray",
                Extra: new Dictionary<string, string> { ["Purchase Date"] = "2019-03-14" }),
        ])), default);

        var film = Assert.Single(result.Records);
        Assert.Equal("Fargo (1996)", film.T);
        Assert.Equal("Fargo 1996", film.Srt);
        Assert.Equal(97, film.Rt);
        Assert.Equal("275-97", film.K);              // the key follows the OVERRIDDEN runtime
        Assert.Equal(1997, film.Y);                  // not the stub's 1978
        Assert.Equal(["Joel Coen"], film.Dir);
        Assert.Equal("Crime", film.Ag);
        Assert.Equal("4K Blu-ray", film.Own);
        Assert.Equal("2019-03-14", Assert.Contains("Purchase Date", film.Ex!));
    }

    // Genres, collection and cast need a payload that carries them; the shared stub has none, so an assertion on
    // G, Col and Cast against it would be true of an empty record and prove nothing.
    private static HttpResponseMessage RichFargo(HttpRequestMessage req)
    {
        if (TestPipeline.IsSearch(req)) throw new Xunit.Sdk.XunitException($"Unexpected TMDB search: {req.RequestUri}");

        return req.RequestUri!.AbsolutePath == "/3/movie/275"
            ? TestPipeline.Json(System.Text.Json.JsonSerializer.Serialize(new
            {
                id = 275, title = "Fargo", overview = "Stub overview.", tagline = "Stub tagline.", runtime = 98,
                release_date = "1996-03-08", vote_average = 7.5, poster_path = "/poster.jpg", backdrop_path = "/backdrop.jpg",
                genres = new[] { new { id = 80, name = "Crime" }, new { id = 53, name = "Thriller" } },
                belongs_to_collection = new { id = 9, name = "The Fargo Collection" },
                credits = new
                {
                    cast = new[] { new { name = "Frances McDormand", character = "Marge", profile_path = "/fm.jpg", order = 0 } },
                    crew = Array.Empty<object>(),
                },
            }))
            : TestPipeline.NotFound();
    }

    [Fact]
    public async Task TmdbDerivedFieldsAreNotOverridableAndStillComeFromThePayload()
    {
        var row = Row();
        var pipeline = TestPipeline.Create(budget: 0, RichFargo).Pipeline;

        var result = await pipeline.RunAsync([row], Bound((row, [new RecordOverride(TmdbId: 275)])), default);

        var film = Assert.Single(result.Records);
        Assert.Equal("/poster.jpg", film.P);
        Assert.Equal("/backdrop.jpg", film.B);
        Assert.Equal("Stub overview.", film.Ov);
        Assert.Equal("Stub tagline.", film.Tag);
        Assert.Equal(7.5, film.Vote);
        Assert.Equal([80, 53], film.G);
        Assert.Equal("The Fargo Collection", film.Col);
        Assert.Equal("Frances McDormand", Assert.Single(film.Cast).N);
    }

    [Fact]
    public async Task ADisplayTitleWithNoSortOverrideFallsBackToTheDerivedRule()
    {
        var row = Row();
        var pipeline = TestPipeline.ThatFailsIfSearched(275);

        var result = await pipeline.RunAsync(
            [row],
            Bound((row, [new RecordOverride(TmdbId: 275, Titles: new TitleSet(Display: "The Fargo Story"))])),
            default);

        var film = Assert.Single(result.Records);
        Assert.Equal("The Fargo Story", film.T);
        Assert.Equal("Fargo Story", film.Srt);       // derived from the OVERRIDDEN display title, not the sheet's
    }

    [Fact]
    public async Task CandidatesWithoutAChoiceLeaveTheFilmUnmatchedBadgedAndUnsearched()
    {
        var row = Row();
        var pipeline = TestPipeline.ThatFailsIfSearched(275);

        var result = await pipeline.RunAsync(
            [row], Bound((row, [new RecordOverride(Candidates: [275, 276])])), default);

        var film = Assert.Single(result.Records);
        Assert.Equal(0, film.I);
        Assert.Equal(0d, film.Mc);
        Assert.Equal(Confidence.Unmatched, ConfidenceBand.Of(film));
        Assert.StartsWith(RecordKey.UnmatchedPrefix, film.K, StringComparison.Ordinal);

        var line = Assert.Single(result.ReviewQueue);
        Assert.Contains("275", line, StringComparison.Ordinal);
        Assert.Contains("276", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFieldOnlyOverrideStillGoesThroughTheOrdinarySearch()
    {
        var row = Row();
        var harness = TestPipeline.Create(budget: 0, SearchFindsFargo);

        var result = await harness.Pipeline.RunAsync(
            [row], Bound((row, [new RecordOverride(Titles: new TitleSet(Display: "FARGO"))])), default);

        var film = Assert.Single(result.Records);
        Assert.Equal(275, film.I);           // matched the ordinary way, so the search really did run
        Assert.Equal("FARGO", film.T);       // and the override still applied on top
        Assert.True(film.Mc < 1d);           // a scored match, not an assertion
    }

    [Fact]
    public async Task AnAssertedIdSurvivesTheConflictingIdRuleBecauseItCameFromTheOwner()
    {
        var fargo = Row();
        var other = Row("Not Fargo At All", "1:40:00");
        var harness = TestPipeline.Create(budget: 0, SearchFindsFargo);

        var result = await harness.Pipeline.RunAsync(
            [fargo, other], Bound((other, [new RecordOverride(TmdbId: 275)])), default);

        Assert.NotEmpty(result.Records);
        Assert.Contains(result.Records, m => m.T == "Not Fargo At All" && m.I == 275);
        Assert.Contains(result.Records, m => m.T == "Fargo" && m.I == 275);
    }

    // ---- rule 1: a row the store says nothing about is untouched -------------------------------------------

    [Fact]
    public async Task ARowWithNoEntryIsPublishedExactlyAsTheSheetHasItEvenWhenOtherRowsAreOverridden()
    {
        // A trailing space on the cell. An unoverridden row must keep it, because Apply returns the record
        // untouched; a chain of "?? record.X" that trims on the way through would lose it.
        var plain = Row("The Fargo ");
        var overridden = Row("Heat", "2:50:00");
        var harness = TestPipeline.Create(budget: 0, req => TestPipeline.IsSearch(req)
            ? TestPipeline.Json(TestPipeline.EmptyResults) : TestPipeline.NotFound());

        var result = await harness.Pipeline.RunAsync(
            [plain, overridden], Bound((overridden, [new RecordOverride(Year: 1995)])), default);

        var film = Assert.Single(result.Records, m => m.T.StartsWith("The Fargo", StringComparison.Ordinal));
        Assert.Equal("The Fargo ", film.T);
        Assert.Equal("Fargo", film.Srt);
        Assert.Equal(["Joel Coen", "Ethan Coen"], film.Dir);
        Assert.Equal("iTunes", film.Own);
        Assert.Null(film.Ex);
    }

    // ---- rule 2: the cache and the search see the SHEET's runtime, never the override's ---------------------

    [Fact]
    public async Task TheSearchScoresAgainstTheSheetRuntimeWhileTheRecordPublishesTheOverridden()
    {
        var row = Row();   // 98 minutes, which is what TMDB says too
        var plainRun = await TestPipeline.Create(budget: 0, SearchFindsFargo).Pipeline
            .RunAsync([row], BoundStore.Empty, default);
        var harness = TestPipeline.Create(budget: 0, SearchFindsFargo);

        var result = await harness.Pipeline.RunAsync(
            [row], Bound((row, [new RecordOverride(Runtime: 140)])), default);

        var film = Assert.Single(result.Records);
        Assert.Equal(140, film.Rt);                                    // published: the override's
        Assert.Equal(275, film.I);
        Assert.Equal(Assert.Single(plainRun.Records).Mc, film.Mc);     // scored: as if there were no override
        Assert.Equal(98, Assert.Single(harness.Cache.Entries).RuntimeMinutes);   // cached under the sheet's
    }

    [Fact]
    public async Task TheCacheLookupUsesTheSheetRuntimeSoAnAliasStillHitsWhenTheOverrideIsFarOff()
    {
        // The cache holds Fargo at 98 minutes. The sheet row says 99, within the alias window, so it is the
        // same film and costs no search. The override says 140, far outside it: a lookup that used THAT
        // would miss, search, and spend the owner's budget on a film already answered.
        var row = Row(runtime: "1:39:00");
        var harness = TestPipeline.Create(budget: 0, SearchFindsFargo);
        harness.Cache.Put(new CacheEntry("seed", 275, 0.95, TestPipeline.Now, false, "fargo", 98,
                                         TitleNormalizer.NormalizeDirectors(Coens)));

        var result = await harness.Pipeline.RunAsync(
            [row], Bound((row, [new RecordOverride(Runtime: 140)])), default);

        Assert.DoesNotContain(harness.Handler.Requests, TestPipeline.IsSearch);
        var film = Assert.Single(result.Records);
        Assert.Equal(275, film.I);
        Assert.Equal(140, film.Rt);
    }

    // ---- the fields the store overrides must come out as the sheet's would ---------------------------------

    [Fact]
    public async Task AnOverriddenDirectorListSplitsExactlyAsTheSheetsDoes()
    {
        const string names = "  Joel Coen  &  Ethan Coen & Sam Raimi ";   // padding around every separator, and at both ends
        var row = Row();
        var sheetSide = new SheetRow("Fargo", "1:38:00", names, "iTunes", "Thriller", "");

        var viaStore = await TestPipeline.ThatFailsIfSearched(275).RunAsync(
            [row], Bound((row, [new RecordOverride(TmdbId: 275, Directors: names)])), default);
        var viaSheet = await TestPipeline.ThatFailsIfSearched(275).RunAsync(
            [sheetSide], Bound((sheetSide, [new RecordOverride(TmdbId: 275)])), default);

        Assert.Equal(["Joel Coen", "Ethan Coen", "Sam Raimi"], Assert.Single(viaStore.Records).Dir);
        Assert.Equal(Assert.Single(viaSheet.Records).Dir, Assert.Single(viaStore.Records).Dir);
    }

    [Fact]
    public async Task TheStoresExtraIsCopiedNotHandedToTheRecord()
    {
        var row = Row();
        var extra = new Dictionary<string, string> { ["Purchase Date"] = "2019-03-14" };

        var result = await TestPipeline.ThatFailsIfSearched(275).RunAsync(
            [row], Bound((row, [new RecordOverride(TmdbId: 275, Extra: extra)])), default);

        var film = Assert.Single(result.Records);
        Assert.NotSame(extra, film.Ex);

        extra["Purchase Date"] = "changed afterwards";
        extra["Added"] = "afterwards too";
        Assert.Equal("2019-03-14", Assert.Contains("Purchase Date", film.Ex!));
        Assert.DoesNotContain("Added", film.Ex!);
    }

    [Fact]
    public async Task AnEmptyExtraPublishesNoExAtAll()
    {
        var row = Row();

        var result = await TestPipeline.ThatFailsIfSearched(275).RunAsync(
            [row], Bound((row, [new RecordOverride(TmdbId: 275, Extra: new Dictionary<string, string>())])), default);

        Assert.Null(Assert.Single(result.Records).Ex);
    }

    // ---- an expanded row is ONE row: one progress tick, one search, one cache entry ---------------------------

    private sealed class Ticks : IProgress<MovieLibrary.Enrich.Reporting.PassProgress>
    {
        public List<MovieLibrary.Enrich.Reporting.PassProgress> Seen { get; } = [];
        public void Report(MovieLibrary.Enrich.Reporting.PassProgress value) => Seen.Add(value);
    }

    // Field-only records: none asserts an id or lists candidates, so each goes down the ordinary cache-then-search path.
    // Runtimes differ so the four records keep four keys and the reconciler collapses none of them.
    private static RecordOverride[] FourFieldOnly() =>
        [.. new[] { 140, 150, 160, 170 }.Select(r => new RecordOverride(Runtime: r))];

    [Fact]
    public async Task AnExpandedRowReportsOneTickBecauseItIsOneRow()
    {
        var expanded = Row();
        var plain = Row("Heat", "2:50:00");
        var ticks = new Ticks();
        var harness = TestPipeline.Create(budget: 0, SearchFindsFargo, progress: ticks);

        var result = await harness.Pipeline.RunAsync(
            [expanded, plain], Bound((expanded, FourFieldOnly())), default);

        Assert.Equal(5, result.Records.Count);                    // four records and one: five records, two rows
        Assert.Equal([1, 2], ticks.Seen.Select(t => t.Done));
        Assert.All(ticks.Seen, t => Assert.Equal(2, t.Total));
    }

    [Fact]
    public async Task AnExpandedRowRunsOneSearchAndWritesOneCacheEntryNotOnePerRecord()
    {
        var row = Row();
        var harness = TestPipeline.Create(budget: 0, SearchFindsFargo);

        var result = await harness.Pipeline.RunAsync([row], Bound((row, FourFieldOnly())), default);

        Assert.Equal(4, result.Records.Count);
        Assert.All(result.Records, m => Assert.Equal(275, m.I));
        Assert.Equal(1, harness.Handler.Requests.Count(TestPipeline.IsSearch));
        Assert.Single(harness.Cache.Entries);
    }
}

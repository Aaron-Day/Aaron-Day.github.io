using MovieLibrary.Core.Models;
using MovieLibrary.Core.Store;
using MovieLibrary.Enrich.Store;

namespace MovieLibrary.Core.Tests;

public class TvPipelineStoreTests
{
    private static SheetRow Episode(string series)
        => new("Season 1, Episode 1: Pilot", "0:22:00", "", "iTunes", "Comedy", series);

    private static BoundStore For(string series, RecordOverride record) => TestStore.Series((series, record));

    [Fact]
    public async Task AnAssertedIdIsTakenWithoutASearch()
    {
        var result = await TestPipeline.TvHarnessThatFailsIfSearched(2316)
            .RunAsync([Episode("The Office")], For("The Office", new RecordOverride(TmdbId: 2316)), default);

        var show = Assert.Single(result.Shows);
        Assert.Equal(2316, show.I);
        Assert.Equal(1d, show.Mc);
    }

    [Fact]
    public async Task ADisplayTitleAndExtrasAreAppliedButTheSlugStaysTheSheetsOwn()
    {
        var result = await TestPipeline.TvHarnessThatFailsIfSearched(2316).RunAsync(
            [Episode("The Office")], For("The Office", new RecordOverride(
                TmdbId: 2316,
                Titles: new TitleSet(Display: "The Office (US)", Sort: "Office (US)"),
                Extra: new Dictionary<string, string> { ["Purchase Date"] = "2020-01-02" })),
            default);

        var show = Assert.Single(result.Shows);
        Assert.Equal("The Office (US)", show.T);
        Assert.Equal("Office (US)", show.Srt);
        Assert.Equal("the-office", show.Slug);      // the URL does not move when the label does
        Assert.Equal("2020-01-02", Assert.Contains("Purchase Date", show.Ex!));
    }

    [Fact]
    public async Task CandidatesWithoutAChoiceLeaveTheSeriesUnmatchedAndNoted()
    {
        var result = await TestPipeline.TvHarnessThatFailsIfSearched(2316).RunAsync(
            [Episode("The Office")], For("The Office", new RecordOverride(Candidates: [2316, 4604])), default);

        var show = Assert.Single(result.Shows);
        Assert.Equal(0, show.I);
        Assert.Equal(0d, show.Mc);
        Assert.Contains(result.ReviewQueue, l => l.Contains("2316", StringComparison.Ordinal)
                                              && l.Contains("4604", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ASeriesWhoseNameCarriesAYearBindsToo()
    {
        // The whole reason IdentityKey.ForSeriesName exists: Normalize moves the year out, and the key folds
        // it back in. A lookup that skipped the fold-back would bind nothing here and say nothing either.
        var result = await TestPipeline.TvHarnessThatFailsIfSearched(2316).RunAsync(
            [Episode("Doctor Who (2005)")], For("Doctor Who (2005)", new RecordOverride(TmdbId: 2316)), default);

        Assert.Equal(2316, Assert.Single(result.Shows).I);
    }

    [Fact]
    public async Task ASeriesWithNoEntryPublishesExactlyWhatItAlwaysDid()
    {
        // "The Office" would sort as "Office". If the store arm derived a sort title for a series it says
        // nothing about, this record would gain an Srt and a trimmed title it never had.
        var pipeline = TestPipeline.Tv(2316, [(1, ["Pilot"])]);

        var result = await pipeline.RunAsync([Episode("  The Office ")], BoundStore.Empty, default);

        var show = Assert.Single(result.Shows);
        Assert.Equal("The Office", show.T);
        Assert.Null(show.Srt);
        Assert.Null(show.Ex);
    }

    [Fact]
    public async Task AnEntryThatSaysNothingAboutTitlesLeavesTheTitleAndSlugAlone()
    {
        var result = await TestPipeline.TvHarnessThatFailsIfSearched(2316).RunAsync(
            [Episode("The Office")], For("The Office", new RecordOverride(TmdbId: 2316)), default);

        var show = Assert.Single(result.Shows);
        Assert.Equal("The Office", show.T);
        Assert.Equal("the-office", show.Slug);
        Assert.Equal("Office", show.Srt);   // derived from the display title, as on the film side
    }

    [Fact]
    public async Task ADisplayTitleReachesASeriesWhoseNameCarriesAYear()
    {
        // The record-building lookup folds the year back in too, separately from the resolving one.
        var result = await TestPipeline.TvHarnessThatFailsIfSearched(2316).RunAsync(
            [Episode("Doctor Who (2005)")],
            For("Doctor Who (2005)",
                new RecordOverride(TmdbId: 2316, Titles: new TitleSet(Display: "Doctor Who: Revival"))),
            default);

        var show = Assert.Single(result.Shows);
        Assert.Equal("Doctor Who: Revival", show.T);
        Assert.Equal("doctor-who-2005", show.Slug);
    }

    // ---- what Apply does with the fields, pinned the way the film side's twins are ---------------------

    /// <summary>
    /// The twin of <c>MoviePipelineStoreTests.TheStoresExtraIsCopiedNotHandedToTheRecord</c>. Asserting only
    /// that "Purchase Date" comes through leaves the COPY unpinned: hand the record the store's own
    /// dictionary and the assertion still holds, while the show and the parsed store file then share one
    /// dictionary and a later mutation of either alters the other.
    /// </summary>
    [Fact]
    public async Task TheStoresExtraIsCopiedNotHandedToTheShow()
    {
        var extra = new Dictionary<string, string> { ["Purchase Date"] = "2020-01-02" };

        var result = await TestPipeline.TvHarnessThatFailsIfSearched(2316).RunAsync(
            [Episode("The Office")],
            For("The Office", new RecordOverride(TmdbId: 2316, Extra: extra)), default);

        var show = Assert.Single(result.Shows);
        Assert.NotSame(extra, show.Ex);

        extra["Purchase Date"] = "changed afterwards";
        extra["Added"] = "afterwards too";
        Assert.Equal("2020-01-02", Assert.Contains("Purchase Date", show.Ex!));
        Assert.DoesNotContain("Added", show.Ex!);
    }

    /// <summary>
    /// <c>Count: > 0</c>, not <c>is not null</c>: an entry that carries an empty <c>extra</c> object must
    /// publish no <c>Ex</c> at all rather than an empty one, as on the film side.
    /// </summary>
    [Fact]
    public async Task AnEmptyExtraPublishesNoExAtAllOnTheSeriesSideEither()
    {
        var result = await TestPipeline.TvHarnessThatFailsIfSearched(2316).RunAsync(
            [Episode("The Office")],
            For("The Office", new RecordOverride(TmdbId: 2316, Extra: new Dictionary<string, string>())),
            default);

        Assert.Null(Assert.Single(result.Shows).Ex);
    }

    /// <summary>
    /// <c>Srt</c> is omitted when it would only repeat the display title. Every other test here uses "The
    /// Office" or "The Office (US)", whose sort titles differ from their displays, so <c>Srt = sort</c> would
    /// pass them all - and publish an <c>Srt</c> equal to <c>T</c> for every series with no leading article.
    /// </summary>
    [Fact]
    public async Task ADisplayTitleThatSortsWhereItAlreadySitsPublishesNoSortTitle()
    {
        var result = await TestPipeline.TvHarnessThatFailsIfSearched(2316).RunAsync(
            [Episode("The Office")],
            For("The Office", new RecordOverride(TmdbId: 2316, Titles: new TitleSet(Display: "Severance"))),
            default);

        var show = Assert.Single(result.Shows);
        Assert.Equal("Severance", show.T);
        Assert.Null(show.Srt);
    }

    // ---- an entry that corrects FIELDS and says nothing about the id ----------------------------------

    private static List<string> Paths(TestPipeline.TvHarness h)
        => [.. h.Handler.Requests.Select(r => r.RequestUri!.AbsolutePath)];

    /// <summary>
    /// An entry may carry titles or extras and no <c>tmdbId</c> and no <c>candidates</c> at all. Such an
    /// entry must not settle the match: the series falls through to the cache and then to the search, exactly
    /// as a series with no entry does, and the entry's fields are laid over whatever that finds. A pipeline
    /// that returned unmatched for any entry it could not resolve itself would pass every other test here.
    /// </summary>
    [Fact]
    public async Task AnEntryWithNeitherAnIdNorCandidatesFallsThroughToTheSearch()
    {
        var harness = TestPipeline.CreateTv(budget: 0,
            new FakeTv { AnyQuery = new FakeShow(2316, "Stub series", [new FakeSeason(1, ["Pilot"])]) }.Respond);

        var result = await harness.Pipeline.RunAsync(
            [Episode("The Office")],
            For("The Office", new RecordOverride(Titles: new TitleSet(Display: "The Office (US)"))), default);

        Assert.Contains("/3/search/tv", Paths(harness));
        var show = Assert.Single(result.Shows);
        Assert.Equal(2316, show.I);
        Assert.Equal("The Office (US)", show.T);                 // the entry's field still applies
        Assert.Equal(0.5d, show.Mc);                             // and it is a SEARCH result, not an assertion
        Assert.Contains(result.ReviewQueue, l => l.Contains("Stub series", StringComparison.Ordinal));
    }

    /// <inheritdoc cref="AnEntryWithNeitherAnIdNorCandidatesFallsThroughToTheSearch"/>
    /// <summary>The cache comes first, so the second run spends nothing.</summary>
    [Fact]
    public async Task AnEntryWithNeitherAnIdNorCandidatesAnswersFromTheCacheWithoutSearchingAgain()
    {
        var fake = new FakeTv { AnyQuery = new FakeShow(2316, "Stub series", [new FakeSeason(1, ["Pilot"])]) };
        var first = TestPipeline.CreateTv(budget: 0, fake.Respond);
        await first.Pipeline.RunAsync([Episode("The Office")], default);
        Assert.Contains("/3/search/tv", Paths(first));            // the run that paid for it really did search

        var second = TestPipeline.CreateTv(budget: 0, fake.Respond, shareCacheWith: first);
        var result = await second.Pipeline.RunAsync(
            [Episode("The Office")],
            For("The Office", new RecordOverride(Titles: new TitleSet(Display: "The Office (US)"))), default);

        Assert.DoesNotContain("/3/search/tv", Paths(second));
        var show = Assert.Single(result.Shows);
        Assert.Equal(2316, show.I);
        Assert.Equal("The Office (US)", show.T);
    }
}

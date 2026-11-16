using MovieLibrary.Core.Identity;
using MovieLibrary.Core.Models;
using MovieLibrary.Core.Store;
using MovieLibrary.Enrich.Store;

namespace MovieLibrary.Core.Tests;

public class TvPipelineStoreTests
{
    private static readonly IReadOnlyDictionary<string, int> NoTabOverrides =
        new Dictionary<string, int>(StringComparer.Ordinal);

    private static SheetRow Episode(string series)
        => new("Season 1, Episode 1: Pilot", "0:22:00", "", "iTunes", "Comedy", series);

    private static BoundStore For(string series, RecordOverride record) => new()
    {
        Films = new Dictionary<string, FilmEntry>(StringComparer.Ordinal),
        Notes = [],
        Series = new Dictionary<string, SeriesEntry>(StringComparer.Ordinal)
        {
            [IdentityKey.ForSeriesName(series)] = new SeriesEntry
            {
                IdentityKey = IdentityKey.ForSeriesName(series),
                Sheet = new SheetEcho(series),
                Record = record,
            },
        },
    };

    [Fact]
    public async Task AnAssertedIdIsTakenWithoutASearch()
    {
        var result = await TestPipeline.TvHarnessThatFailsIfSearched(2316)
            .RunAsync([Episode("The Office")], NoTabOverrides, For("The Office", new RecordOverride(TmdbId: 2316)), default);

        var show = Assert.Single(result.Shows);
        Assert.Equal(2316, show.I);
        Assert.Equal(1d, show.Mc);
    }

    [Fact]
    public async Task ADisplayTitleAndExtrasAreAppliedButTheSlugStaysTheSheetsOwn()
    {
        var result = await TestPipeline.TvHarnessThatFailsIfSearched(2316).RunAsync(
            [Episode("The Office")], NoTabOverrides,
            For("The Office", new RecordOverride(
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
            [Episode("The Office")], NoTabOverrides,
            For("The Office", new RecordOverride(Candidates: [2316, 4604])), default);

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
            [Episode("Doctor Who (2005)")], NoTabOverrides,
            For("Doctor Who (2005)", new RecordOverride(TmdbId: 2316)), default);

        Assert.Equal(2316, Assert.Single(result.Shows).I);
    }

    [Fact]
    public async Task ASeriesWithNoEntryPublishesExactlyWhatItAlwaysDid()
    {
        // "The Office" would sort as "Office". If the store arm derived a sort title for a series it says
        // nothing about, this record would gain an Srt and a trimmed title it never had.
        var pipeline = TestPipeline.Tv(2316, [(1, ["Pilot"])]);

        var result = await pipeline.RunAsync([Episode("  The Office ")], NoTabOverrides, BoundStore.Empty, default);

        var show = Assert.Single(result.Shows);
        Assert.Equal("The Office", show.T);
        Assert.Null(show.Srt);
        Assert.Null(show.Ex);
    }

    [Fact]
    public async Task AnEntryThatSaysNothingAboutTitlesLeavesTheTitleAndSlugAlone()
    {
        var result = await TestPipeline.TvHarnessThatFailsIfSearched(2316).RunAsync(
            [Episode("The Office")], NoTabOverrides, For("The Office", new RecordOverride(TmdbId: 2316)), default);

        var show = Assert.Single(result.Shows);
        Assert.Equal("The Office", show.T);
        Assert.Equal("the-office", show.Slug);
        Assert.Equal("Office", show.Srt);   // derived from the display title, as on the film side
    }
}

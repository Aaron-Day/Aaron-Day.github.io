using MovieLibrary.Core.Models;
using MovieLibrary.Enrich.Pipeline;

namespace MovieLibrary.Core.Tests;

public class TvPipelineTests
{
    private static SheetRow Ep(string title, string series, string runtime = "44:00:00")
        => new(title, runtime, "", series + ": The Complete Series", "Drama", series);

    [Fact]
    public async Task NumberedRowsMapBySeasonAndEpisodeNumber()
    {
        var pipeline = TestPipeline.Tv(seriesId: 456, seasons: [(1, ["Truth Be Told", "So It Begins"])]);

        var result = await pipeline.RunAsync(
            [Ep("Season 1, Episode 2: So It Begins", "Alias")], CancellationToken.None);

        var season = Assert.Single(Assert.Single(result.Shows).Seasons);
        Assert.Equal(2, season.Total);
        Assert.Equal(1, season.Owned);
        Assert.False(season.Eps.Single(e => e.E == 1).O);
        Assert.True(season.Eps.Single(e => e.E == 2).O);
    }

    [Fact]
    public async Task BareNamedRowsMatchByEpisodeNameWithinTheSeries()
    {
        var pipeline = TestPipeline.Tv(seriesId: 456, seasons: [(1, ["King Me", "Poll Position"])]);

        var result = await pipeline.RunAsync([Ep("Poll Position", "Avatar")], CancellationToken.None);

        var season = Assert.Single(Assert.Single(result.Shows).Seasons);
        Assert.True(season.Eps.Single(e => e.E == 2).O);
    }

    [Fact]
    public async Task UnownedEpisodesAreStillEmittedForTheRevealToggle()
    {
        var pipeline = TestPipeline.Tv(seriesId: 456, seasons: [(1, ["A", "B", "C"])]);

        var result = await pipeline.RunAsync(
            [Ep("Season 1, Episode 1: A", "Show")], CancellationToken.None);

        var show = Assert.Single(result.Shows);
        Assert.Equal(3, show.TotalEpisodes);
        Assert.Equal(1, show.OwnedEpisodes);
        Assert.Equal(3, show.Seasons.Single().Eps.Length);
    }

    [Fact]
    public async Task UnmatchedSeriesStillAppearsWithSheetDataOnly()
    {
        var pipeline = TestPipeline.TvWithNoResults();

        var result = await pipeline.RunAsync([Ep("Some Episode", "Unknown Show")], CancellationToken.None);

        var show = Assert.Single(result.Shows);
        Assert.Equal(0, show.I);
        Assert.Equal("Unknown Show", show.T);
        Assert.Equal(1, show.OwnedEpisodes);
    }
}

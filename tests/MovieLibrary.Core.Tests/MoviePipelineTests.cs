using MovieLibrary.Core.Models;
using MovieLibrary.Enrich.Pipeline;

namespace MovieLibrary.Core.Tests;

public class MoviePipelineTests
{
    private static SheetRow Row(string title, string runtime, string directors)
        => new(title, runtime, directors, "", "Sci-Fi & Fantasy", "");

    [Fact]
    public async Task UnmatchedFilmIsStillEmittedFromSheetData()
    {
        var pipeline = TestPipeline.WithNoTmdbResults();

        var result = await pipeline.RunAsync(
            [Row("Some Obscure Film", "1:35:00", "Nobody")], new Dictionary<string, int>(),
            CancellationToken.None);

        var record = Assert.Single(result.Records);
        Assert.Equal("Some Obscure Film", record.T);
        Assert.Equal(95, record.Rt);
        Assert.Equal(0d, record.Mc);
        Assert.Contains("Some Obscure Film", result.ReviewQueue.Single());
    }

    [Fact]
    public async Task OverrideSkipsSearchEntirely()
    {
        var pipeline = TestPipeline.ThatFailsIfSearched(tmdbId: 948);
        var overrides = new Dictionary<string, int> { [MoviePipeline.OverrideKey("Halloween", 91)] = 948 };

        var result = await pipeline.RunAsync(
            [Row("Halloween", "1:31:00", "John Carpenter")], overrides, CancellationToken.None);

        Assert.Equal(948, Assert.Single(result.Records).I);
    }

    [Fact]
    public async Task RunStopsCleanlyWhenTheBudgetIsSpent()
    {
        var pipeline = TestPipeline.WithBudget(1);

        var result = await pipeline.RunAsync(
            [Row("A", "1:30:00", "X"), Row("B", "1:40:00", "Y"), Row("C", "1:50:00", "Z")],
            new Dictionary<string, int>(), CancellationToken.None);

        Assert.True(result.StoppedOnBudget);
        Assert.Equal(3, result.Records.Count); // all rows still emitted, unresolved ones at mc 0
    }
}

using MovieLibrary.Core.Models;
using MovieLibrary.Enrich.Store;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// The pipeline's own wiring of the sort title. <c>SortTitleTests</c> covers <c>SortTitle.From</c> alone; this
/// pins that <c>BuildRecordAsync</c> actually publishes it, and publishes nothing when it would repeat the title.
/// </summary>
public class MoviePipelineSortTitleTests
{
    private static SheetRow Row(string title)
        => new(title, "1:30:00", "A Director", "", "Sci-Fi & Fantasy", "");

    private static async Task<MovieRecord> Publish(string title)
    {
        var result = await TestPipeline.WithNoTmdbResults().RunAsync(
            [Row(title)], BoundStore.Empty, CancellationToken.None);

        return Assert.Single(result.Records);
    }

    [Theory]
    [InlineData("The Thing", "Thing")]
    [InlineData("A Quiet Place", "Quiet Place")]
    [InlineData("An American Werewolf in London", "American Werewolf in London")]
    public async Task ATitleThatStartsWithAnArticlePublishesASortTitleWithoutIt(string title, string expected)
    {
        var record = await Publish(title);

        Assert.Equal(title, record.T);
        Assert.Equal(expected, record.Srt);
    }

    [Theory]
    [InlineData("Alien")]
    [InlineData("Theodore Rex")]
    public async Task ATitleWithNoArticlePublishesNoSortTitleSoThePayloadDoesNotGrow(string title)
    {
        var record = await Publish(title);

        Assert.Null(record.Srt);
    }

    [Fact]
    public async Task APaddedTitleWithNoArticlePublishesNoSortTitle()
    {
        // Production cannot reach this (the sheet reader trims every cell); it guards a future change.
        var result = await TestPipeline.WithNoTmdbResults().RunAsync(
            [Row(" Zorro ")], BoundStore.Empty, CancellationToken.None);

        var record = Assert.Single(result.Records);

        Assert.Null(record.Srt);
    }
}

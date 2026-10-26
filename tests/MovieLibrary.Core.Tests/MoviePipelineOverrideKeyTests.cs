using MovieLibrary.Core.Models;
using MovieLibrary.Enrich.Pipeline;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// A film override is keyed on folded title, year and runtime minutes (<see cref="MoviePipeline.OverrideKey"/>),
/// not on the folded title alone. With a title-only key, correcting one Halloween silently retargeted the others.
/// </summary>
public class MoviePipelineOverrideKeyTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static SheetRow Film(string title, string runtime)
        => new(title, runtime, "Someone", "", "Horror", "");

    private static Dictionary<int, int> IdByRuntime(MoviePipelineResult r)
        => r.Records.ToDictionary(x => x.Rt, x => x.I);

    [Fact]
    public async Task AnOverrideTakesTheFilmItNamesAndLeavesItsSameTitledNeighboursAlone()
    {
        // TMDB knows only id 948, and every search comes back empty, so a neighbour that wrongly took the
        // override would carry 948 and Mc 1, and one that is left alone is unmatched.
        var h = TestPipeline.Create(0, req =>
            TestPipeline.IsSearch(req) ? TestPipeline.Json(TestPipeline.EmptyResults)
            : req.RequestUri!.AbsolutePath == "/3/movie/948"
                ? TestPipeline.Json("""{"id":948,"title":"Halloween","overview":"The 1978 one."}""")
                : TestPipeline.NotFound());
        var overrides = new Dictionary<string, int> { [MoviePipeline.OverrideKey("Halloween", 90)] = 948 };

        var result = await h.Pipeline.RunAsync(
            [Film("Halloween", "1:30:47"), Film("Halloween", "1:50:18"), Film("Halloween (2018)", "1:46:07")],
            overrides, Ct);

        var byRuntime = result.Records.ToDictionary(r => r.Rt);
        Assert.Equal((948, 1d), (byRuntime[90].I, byRuntime[90].Mc));
        Assert.Equal((0, 0d), (byRuntime[110].I, byRuntime[110].Mc));
        Assert.Equal((0, 0d), (byRuntime[106].I, byRuntime[106].Mc));
    }

    [Fact]
    public async Task TwoFilmsOfOneTitleAndNoYearEachTakeTheirOwnId()
    {
        var pipeline = TestPipeline.ThatFailsIfSearched(11, 12);
        var overrides = new Dictionary<string, int>
        {
            [MoviePipeline.OverrideKey("Aladdin", 129)] = 11,
            [MoviePipeline.OverrideKey("Aladdin", 91)] = 12,
        };

        var result = await pipeline.RunAsync([Film("Aladdin", "2:09:10"), Film("Aladdin", "1:31:37")], overrides, Ct);

        Assert.Equal(new Dictionary<int, int> { [129] = 11, [91] = 12 }, IdByRuntime(result));
    }

    [Fact]
    public async Task TwoFilmsOfOneTitleAndOneRuntimeAreSeparatedByTheirYear()
    {
        // 1:52:17 and 1:52:18 are both 112 minutes.
        var pipeline = TestPipeline.ThatFailsIfSearched(21, 22);
        var overrides = new Dictionary<string, int>
        {
            [MoviePipeline.OverrideKey("Overboard (1987)", 112)] = 21,
            [MoviePipeline.OverrideKey("Overboard (2018)", 112)] = 22,
        };

        var result = await pipeline.RunAsync(
            [Film("Overboard (1987)", "1:52:17"), Film("Overboard (2018)", "1:52:18")], overrides, Ct);

        Assert.Equal(new Dictionary<string, int> { ["Overboard (1987)"] = 21, ["Overboard (2018)"] = 22 },
                     result.Records.ToDictionary(r => r.T, r => r.I));   // T is the sheet's own title
    }

    [Fact]
    public async Task ARuntimeOneMinuteAwayDoesNotMatchBecauseThereIsNoToleranceWindow()
    {
        // The sheet says 125 and the override says 124: the film is searched, not overridden.
        var h = TestPipeline.Create(0, req =>
            TestPipeline.IsSearch(req) ? TestPipeline.Json(TestPipeline.EmptyResults) : TestPipeline.NotFound());
        var overrides = new Dictionary<string, int>
            { [MoviePipeline.OverrideKey("Captain America: The First Avenger", 124)] = 1771 };

        var result = await h.Pipeline.RunAsync(
            [Film("Captain America: The First Avenger", "2:05:15")], overrides, Ct);

        Assert.Equal((0, 0d), (Assert.Single(result.Records).I, result.Records[0].Mc));
        Assert.Single(h.Handler.Requests, TestPipeline.IsSearch);
    }

    [Fact]
    public async Task ATitleOnlyKeyNoLongerMatchesAnything()
    {
        var h = TestPipeline.Create(0, req =>
            TestPipeline.IsSearch(req) ? TestPipeline.Json(TestPipeline.EmptyResults) : TestPipeline.NotFound());

        var result = await h.Pipeline.RunAsync(
            [Film("Halloween", "1:31:00")], new Dictionary<string, int> { ["halloween"] = 948 }, Ct);

        Assert.Equal(0, Assert.Single(result.Records).I);
    }
}

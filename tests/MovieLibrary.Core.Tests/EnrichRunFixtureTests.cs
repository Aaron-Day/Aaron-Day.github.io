using MovieLibrary.Enrich.Emit;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// Task 17 Step F, fixture side. A fixture run builds a full synthetic catalogue from the Sheet alone, so the UI can be
/// built before a real run is affordable. It needs no token, makes no TMDB request, and its genre table is the one the
/// generator draws its ids from, passed through rather than re-declared: two copies would drift and leave the facet with
/// ids that have no names.
/// </summary>
public class EnrichRunFixtureTests
{
    private static RunHarness Fixtures()
    {
        var h = new RunHarness();
        h.MoviesCsv = RunLibrary.Movies(
            ("Alien", "1:56:00", "Ridley Scott", "Sci-Fi"),
            ("Airplane!", "1:27:00", "Jim Abrahams", "Comedy"),
            ("Heat", "2:50:00", "Michael Mann", "Crime"),
            ("Psycho", "1:49:00", "Alfred Hitchcock", "Horror"));
        h.ShowsCsv = RunLibrary.Shows(
            ("Season 1, Episode 1: Pilot", "The Office", "Comedy"),
            ("Season 1, Episode 2: Diversity Day", "The Office", "Comedy"));
        h.Options = h.Options with { Fixtures = true, Token = "" };
        return h;
    }

    private static Dictionary<string, string> MetaGenres(RunHarness h)
    {
        using var doc = h.Json("meta.json");
        return doc.RootElement.GetProperty("genres").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
    }

    [Fact]
    public async Task AFixtureRunNeedsNoTokenAndNeverTouchesTmdb()
    {
        using var h = Fixtures();

        var result = await h.RunAsync();

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(h.TmdbHandler.Requests);
        Assert.Contains("Fixture mode", h.Out.ToString());
        Assert.True(h.Wrote("movies.json"));
        Assert.True(h.Wrote("shows.json"));
        Assert.True(h.Wrote("meta.json"));
        Assert.True(h.Wrote(Path.Combine("tv", "the-office.json")));
    }

    [Fact]
    public async Task AFixtureRunReadsTheTwoTabsAndNotTheOverridesTab()
    {
        using var h = Fixtures();

        await h.RunAsync();

        var tabs = h.SheetHandler.Requests.Select(r => r.RequestUri!.Query).ToList();
        Assert.Equal(2, tabs.Count);
        Assert.Contains(tabs, q => q.EndsWith("sheet=Movies", StringComparison.Ordinal));
        Assert.Contains(tabs, q => q.EndsWith("sheet=Shows", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFixtureRunNeverTouchesTheCache()
    {
        using var h = Fixtures();

        await h.RunAsync();

        Assert.False(Directory.Exists(h.CacheDir));
    }

    [Fact]
    public async Task AFixtureRunIsSyntheticSoItIsNeverDeployable()
    {
        using var h = Fixtures();

        var result = await h.RunAsync();

        Assert.False(result.Deployable);
        Assert.False(result.StoppedOnBudget);
        Assert.Contains("deployable=false", h.GitHubOutput);
        Assert.Contains("Fixture run", h.Summary);
        Assert.DoesNotContain("INCOMPLETE", h.Summary);
    }

    [Fact]
    public async Task AFixtureRunPublishesANonEmptyGenreMapThatIsTheGeneratorsOwnTable()
    {
        using var h = Fixtures();

        await h.RunAsync();

        var genres = MetaGenres(h);
        Assert.NotEmpty(genres);
        Assert.Equal(
            FixtureGenerator.Genres.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"),
            genres.OrderBy(kv => int.Parse(kv.Key)).Select(kv => $"{kv.Key}={kv.Value}"));
    }

    [Fact]
    public async Task EveryGenreIdAFixtureFilmCarriesHasANameInMeta()
    {
        using var h = Fixtures();

        await h.RunAsync();

        var genres = MetaGenres(h);
        using var doc = h.Json("movies.json");
        var used = doc.RootElement.EnumerateArray().SelectMany(f => f.GetProperty("g").EnumerateArray().Select(g => g.GetInt32())).Distinct().ToList();
        Assert.NotEmpty(used);
        foreach (var id in used)
            Assert.False(string.IsNullOrWhiteSpace(genres.GetValueOrDefault(id.ToString())), $"genre {id} has no name in meta.json");
    }

    [Fact]
    public async Task AFixtureRunHasAnEmptyReviewQueueAndTheRunsClockInMeta()
    {
        using var h = Fixtures();

        await h.RunAsync();

        Assert.Empty(h.ReviewQueue());
        using var doc = h.Json("meta.json");
        Assert.Equal(RunHarness.T0.UtcDateTime.ToString("O"), doc.RootElement.GetProperty("builtUtc").GetString());
    }

    [Fact]
    public async Task AFixtureRunReportsTheEpisodeCountsItsSeriesActuallyHave()
    {
        using var h = Fixtures();

        await h.RunAsync();

        using var meta = h.Json("meta.json");
        using var shows = h.Json("shows.json");
        Assert.Equal(shows.RootElement.EnumerateArray().Sum(s => s.GetProperty("totalEpisodes").GetInt32()),
                     meta.RootElement.GetProperty("totalEpisodeCount").GetInt32());
        Assert.Equal(shows.RootElement.EnumerateArray().Sum(s => s.GetProperty("ownedEpisodes").GetInt32()),
                     meta.RootElement.GetProperty("ownedEpisodeCount").GetInt32());
        Assert.Equal(4, meta.RootElement.GetProperty("movieCount").GetInt32());
        Assert.Equal(1, meta.RootElement.GetProperty("showCount").GetInt32());
    }
}

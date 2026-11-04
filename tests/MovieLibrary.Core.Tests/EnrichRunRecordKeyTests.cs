namespace MovieLibrary.Core.Tests;

/// <summary>
/// The published file, end to end and offline: a real (non-fixture) run through stubbed Sheet and TMDB endpoints,
/// read back from the movies.json it wrote. This is what a catalogue re-emit looks like to the browser.
/// </summary>
public class EnrichRunRecordKeyTests
{
    [Fact]
    public async Task APublishedCatalogueKeysEveryFilmAndGivesTheUnmatchedAHashKey()
    {
        using var h = new RunHarness();
        h.MoviesCsv = RunLibrary.Movies(RunLibrary.Fargo,
            ("Zzyzx Road", "1:30:00", "Nobody", "Drama"), ("Qux Lane", "1:40:00", "Nobody", "Drama"));
        h.ShowsCsv = GoogleSheetClientTests.ShowsHeader;
        h.Tmdb = RunLibrary.FargoOnly();

        var result = await h.RunAsync();

        Assert.Equal(0, result.ExitCode);
        using var doc = h.Json("movies.json");
        var films = doc.RootElement.EnumerateArray().ToList();
        Assert.Equal(3, films.Count);
        Assert.All(films, f => Assert.Equal(["i", "k"], f.EnumerateObject().Take(2).Select(p => p.Name)));

        var keys = films.ToDictionary(f => f.GetProperty("t").GetString()!, f => f.GetProperty("k").GetString()!);
        Assert.Equal("275-98", keys["Fargo"]);                                   // matched: its TMDB id, then its 1:38 runtime
        Assert.Equal(3, keys.Values.Distinct().Count());
        Assert.StartsWith("u-", keys["Zzyzx Road"], StringComparison.Ordinal);   // unmatched: id 0 for both...
        Assert.StartsWith("u-", keys["Qux Lane"], StringComparison.Ordinal);     // ...and a key of its own each
        Assert.Equal(2, films.Count(f => f.GetProperty("i").GetInt32() == 0));
    }
}

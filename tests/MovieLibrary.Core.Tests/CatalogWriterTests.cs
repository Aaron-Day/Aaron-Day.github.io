using System.Text.Json;
using MovieLibrary.Core.Models;
using MovieLibrary.Enrich.Emit;

namespace MovieLibrary.Core.Tests;

public class CatalogWriterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ml-out-" + Guid.NewGuid());
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private static MovieRecord Movie(int id, string title) =>
        new(id, title, 1979, 117, "/p.jpg", null, [], "Sci-Fi & Fantasy", 8.1, ["Ridley Scott"],
            "overview", null, [], null, null, 0.95);

    private static ShowRecord Show(string title, string slug) =>
        new(456, title, slug, null, 2001, "Drama",
            [new SeasonRecord(1, 1, 2, [new EpisodeRecord(1, "Pilot", true, null, 44, null, null)])],
            1, 2, 1.0);

    private static CatalogMeta Meta() =>
        new("2026-10-23T00:00:00Z", 1, 1, 2, new() { ["878"] = "Science Fiction" },
            "This website uses TMDB and the TMDB APIs but is not endorsed, certified, or otherwise approved by TMDB.",
            []);

    [Fact]
    public async Task WritesTheFourArtefactsAtTheirSpecifiedPaths()
    {
        await new CatalogWriter(_dir).WriteAsync([Movie(348, "Alien")], [Show("Alias", "alias")],
                                                 Meta(), CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(_dir, "movies.json")));
        Assert.True(File.Exists(Path.Combine(_dir, "shows.json")));
        Assert.True(File.Exists(Path.Combine(_dir, "meta.json")));
        Assert.True(File.Exists(Path.Combine(_dir, "tv", "alias.json")));
    }

    [Fact]
    public async Task ShowsIndexExcludesEpisodesToStaySmall()
    {
        await new CatalogWriter(_dir).WriteAsync([], [Show("Alias", "alias")], Meta(), CancellationToken.None);

        var json = await File.ReadAllTextAsync(Path.Combine(_dir, "shows.json"));
        Assert.DoesNotContain("Pilot", json);     // episodes live only in tv/{slug}.json
        Assert.Contains("\"ownedEpisodes\":1", json);
    }

    [Fact]
    public async Task PerShowFileCarriesEveryEpisodeIncludingUnowned()
    {
        await new CatalogWriter(_dir).WriteAsync([], [Show("Alias", "alias")], Meta(), CancellationToken.None);

        var json = await File.ReadAllTextAsync(Path.Combine(_dir, "tv", "alias.json"));
        Assert.Contains("Pilot", json);
        Assert.Contains("\"total\":2", json);
    }

    [Fact]
    public async Task MetaCarriesTheExactAttributionSentence()
    {
        await new CatalogWriter(_dir).WriteAsync([], [], Meta(), CancellationToken.None);

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_dir, "meta.json")));
        Assert.Equal(
            "This website uses TMDB and the TMDB APIs but is not endorsed, certified, or otherwise approved by TMDB.",
            doc.RootElement.GetProperty("attribution").GetString());
    }

    [Fact]
    public async Task OutputIsMinifiedNotIndented()
    {
        await new CatalogWriter(_dir).WriteAsync([Movie(348, "Alien")], [], Meta(), CancellationToken.None);

        var json = await File.ReadAllTextAsync(Path.Combine(_dir, "movies.json"));
        Assert.DoesNotContain("\n  ", json);
    }
}

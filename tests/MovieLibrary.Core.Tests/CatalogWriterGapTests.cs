using System.Text.Json;
using MovieLibrary.Core.Models;
using MovieLibrary.Enrich.Emit;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// Gap tests for <see cref="CatalogWriter"/>. The brief tests never read the content of movies.json,
/// never give the per-series file an unowned episode (the fixture has one owned episode and a hand-set
/// total of 2), never write two series, and never put anything in the review queue.
/// </summary>
public class CatalogWriterGapTests : IDisposable
{
    private const string Attribution =
        "This website uses TMDB and the TMDB APIs but is not endorsed, certified, or otherwise approved by TMDB.";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ml-out-gap-" + Guid.NewGuid());
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private static MovieRecord Film(int id, string title) =>
        new(id, title, 1979, 117, "/p.jpg", "/b.jpg", [878, 27], "Sci-Fi & Fantasy", 8.1, ["Ridley Scott"],
            "In space, no one can hear you scream.", "A tagline",
            [new CastMember("Sigourney Weaver", "Ripley", "/c.jpg")],
            "Alien Collection", "Alien 4-Film Collection", 0.95);

    /// <summary>A film no TMDB candidate matched: sheet data only, I = 0 and Mc = 0.</summary>
    private static MovieRecord Unmatched(string title) =>
        new(0, title, null, 90, null, null, [], "Drama", 0, [], null, null, [], null, null, 0);

    private static EpisodeRecord Ep(int number, string title, bool owned) =>
        new(number, title, owned, null, null, null, null);

    private static SeasonRecord Season(int number, params EpisodeRecord[] eps) =>
        new(number, eps.Count(e => e.O), eps.Length, eps);

    private static ShowRecord Series(int id, string title, string slug, params SeasonRecord[] seasons) =>
        new(id, title, slug, id == 0 ? null : "/s.jpg", id == 0 ? null : 1999, "Drama", seasons,
            seasons.Sum(s => s.Owned), seasons.Sum(s => s.Total), id == 0 ? 0d : 1d);

    private static CatalogMeta Meta(params string[] reviewQueue) =>
        new("2026-10-23T04:05:06Z", 1457, 122, 13850,
            new() { ["28"] = "Action", ["878"] = "Science Fiction" }, Attribution, reviewQueue);

    private Task WriteAsync(MovieRecord[] movies, ShowRecord[] shows, CatalogMeta? meta = null)
        => new CatalogWriter(_dir).WriteAsync(movies, shows, meta ?? Meta(), CancellationToken.None);

    private string PathOf(params string[] parts) => Path.Combine(_dir, Path.Combine(parts));

    private async Task<JsonDocument> ReadAsync(params string[] parts)
        => JsonDocument.Parse(await File.ReadAllBytesAsync(PathOf(parts)));

    private static string[] Names(JsonElement element)
        => element.EnumerateObject().Select(p => p.Name).ToArray();

    [Fact]
    public async Task MoviesFileKeepsEveryFilmInOrderIncludingTheUnmatchedOnes()
    {
        await WriteAsync([Film(348, "Alien"), Unmatched("Home Movie"), Film(949, "Heat")], []);

        using var doc = await ReadAsync("movies.json");
        var films = doc.RootElement.EnumerateArray().ToList();
        Assert.Equal(new[] { 348, 0, 949 }, films.Select(f => f.GetProperty("i").GetInt32()));
        Assert.Equal(new[] { "Alien", "Home Movie", "Heat" }, films.Select(f => f.GetProperty("t").GetString()));
        Assert.Equal(0d, films[1].GetProperty("mc").GetDouble());
    }

    [Fact]
    public async Task AFullyPopulatedFilmUsesTheAbbreviatedFieldNames()
    {
        await WriteAsync([Film(348, "Alien")], []);

        using var doc = await ReadAsync("movies.json");
        var film = doc.RootElement[0];
        Assert.Equal(
            new[] { "i", "t", "y", "rt", "p", "b", "g", "ag", "vote", "dir", "ov", "tag", "cast", "col", "own", "mc" },
            Names(film));
        Assert.Equal(new[] { "n", "c", "p" }, Names(film.GetProperty("cast")[0]));
    }

    [Fact]
    public async Task MoviesAndShowsRoundTripThroughTheTypesTheWebAppWillUse()
    {
        // Blazor GetFromJsonAsync uses JsonSerializerDefaults.Web. Producer and consumer share
        // MovieLibrary.Core types, so what the writer emits must read back into them unchanged.
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        MovieRecord[] films = [Film(348, "Alien"), Unmatched("Home Movie")];
        var series = Series(456, "Alias", "alias",
            new SeasonRecord(1, 1, 2, [new EpisodeRecord(1, "Pilot", true, "2001-09-30", 44, "/still.jpg", "ov"),
                                       Ep(2, "Truth Be Told", false)]));

        await WriteAsync(films, [series]);

        var filmsBack = JsonSerializer.Deserialize<MovieRecord[]>(
            await File.ReadAllBytesAsync(PathOf("movies.json")), web);
        var seriesBack = JsonSerializer.Deserialize<ShowRecord>(
            await File.ReadAllBytesAsync(PathOf("tv", "alias.json")), web);
        Assert.Equal(JsonSerializer.Serialize(films, web), JsonSerializer.Serialize(filmsBack, web));
        Assert.Equal(JsonSerializer.Serialize(series, web), JsonSerializer.Serialize(seriesBack, web));
    }

    [Fact]
    public async Task NullFieldsAreOmittedRatherThanWrittenAsNull()
    {
        // 1,457 films and ~13,850 episodes repeat these keys; "p":null on each would be pure ballast.
        await WriteAsync([Unmatched("Home Movie")],
            [Series(0, "Obscure Show", "obscure-show", Season(0, Ep(1, "Some Episode", true))),
             Series(456, "Alias", "alias", Season(1, Ep(1, "Pilot", true), Ep(2, "Truth Be Told", false)))]);

        foreach (var file in new[] { new[] { "movies.json" }, new[] { "shows.json" },
                                     new[] { "tv", "alias.json" }, new[] { "tv", "obscure-show.json" } })
            Assert.DoesNotContain("null", await File.ReadAllTextAsync(PathOf(file)));
    }

    [Fact]
    public async Task TheShowsIndexCarriesGridFieldsOnlyWithNoSeasonsOrEpisodes()
    {
        var show = Series(456, "Alias", "alias",
            Season(1, Ep(1, "Pilot", true), Ep(2, "Truth Be Told", false)),
            Season(2, Ep(1, "The Telling", true))) with { Mc = 0.5 };

        await WriteAsync([], [show]);

        var json = await File.ReadAllTextAsync(PathOf("shows.json"));
        Assert.DoesNotContain("Truth Be Told", json);   // an unowned episode must not leak either
        using var doc = JsonDocument.Parse(json);
        var entry = Assert.Single(doc.RootElement.EnumerateArray());
        Assert.Equal(
            new[] { "i", "t", "slug", "p", "firstYear", "ag", "seasonCount", "ownedEpisodes", "totalEpisodes", "mc" },
            Names(entry));
        Assert.Equal(456, entry.GetProperty("i").GetInt32());
        Assert.Equal("Alias", entry.GetProperty("t").GetString());
        Assert.Equal("alias", entry.GetProperty("slug").GetString());
        Assert.Equal("/s.jpg", entry.GetProperty("p").GetString());
        Assert.Equal(1999, entry.GetProperty("firstYear").GetInt32());
        Assert.Equal("Drama", entry.GetProperty("ag").GetString());
        Assert.Equal(2, entry.GetProperty("ownedEpisodes").GetInt32());
        Assert.Equal(3, entry.GetProperty("totalEpisodes").GetInt32());
        Assert.Equal(0.5d, entry.GetProperty("mc").GetDouble());
    }

    [Theory]
    [InlineData(new[] { 1, 2, 3 }, 3)]
    [InlineData(new[] { 0, 1, 2 }, 2)]   // season 0 is TvPipeline "unplaced episodes" bucket, not a season
    [InlineData(new[] { 0 }, 0)]         // an unmatched series holds everything in that bucket
    [InlineData(new[] { 1, 2, 4 }, 3)]   // a gap is a count of seasons, not the highest season number
    public async Task SeasonCountCountsNumberedSeasonsAndNeverTheUnplacedBucket(int[] seasonNumbers, int expected)
    {
        var seasons = seasonNumbers.Select(n => Season(n, Ep(1, "Pilot", true))).ToArray();

        await WriteAsync([], [Series(456, "Alias", "alias", seasons)]);

        using var doc = await ReadAsync("shows.json");
        Assert.Equal(expected, doc.RootElement[0].GetProperty("seasonCount").GetInt32());
    }

    [Fact]
    public async Task EachSeriesIsWrittenToItsOwnFileNamedByItsSlug()
    {
        ShowRecord[] shows =
        [
            Series(101, "The Wire", "the-wire", Season(1, Ep(1, "The Target", true))),
            Series(102, "Breaking Bad", "breaking-bad", Season(1, Ep(1, "Gray Matter", true))),
            Series(103, "Alias", "alias", Season(1, Ep(1, "Truth Be Told", true)))
        ];

        await WriteAsync([], shows);

        var files = Directory.GetFiles(PathOf("tv")).Select(f => Path.GetFileName(f)!).Order(StringComparer.Ordinal);
        Assert.Equal(new[] { "alias.json", "breaking-bad.json", "the-wire.json" }, files);
        foreach (var show in shows)
        {
            using var doc = await ReadAsync("tv", show.Slug + ".json");
            Assert.Equal(show.I, doc.RootElement.GetProperty("i").GetInt32());
            Assert.Equal(show.T, doc.RootElement.GetProperty("t").GetString());
            Assert.Equal(show.Slug, doc.RootElement.GetProperty("slug").GetString());
            Assert.Equal(show.Seasons[0].Eps[0].T,
                doc.RootElement.GetProperty("seasons")[0].GetProperty("eps")[0].GetProperty("t").GetString());
        }
    }

    [Fact]
    public async Task AnUnownedEpisodeIsWrittenToTheSeriesFileWithOwnedFalse()
    {
        await WriteAsync([], [Series(456, "Alias", "alias",
            Season(1, Ep(1, "Pilot", true), Ep(2, "Truth Be Told", false), Ep(3, "So It Begins", false)),
            Season(2, Ep(1, "The Telling", false)))]);   // a season owned in no part still ships

        using var doc = await ReadAsync("tv", "alias.json");
        var seasons = doc.RootElement.GetProperty("seasons");
        Assert.Equal(2, seasons.GetArrayLength());
        var eps = seasons[0].GetProperty("eps").EnumerateArray().ToList();
        Assert.Equal(new[] { "Pilot", "Truth Be Told", "So It Begins" }, eps.Select(e => e.GetProperty("t").GetString()));
        Assert.Equal(new[] { true, false, false }, eps.Select(e => e.GetProperty("o").GetBoolean()));
        var second = Assert.Single(seasons[1].GetProperty("eps").EnumerateArray());
        Assert.False(second.GetProperty("o").GetBoolean());
    }

    [Fact]
    public async Task AnUnmatchedSeriesIsStillInTheIndexAndGetsItsOwnFile()
    {
        await WriteAsync([], [Series(0, "Obscure Show", "obscure-show", Season(0, Ep(1, "Some Episode", true))),
                              Series(456, "Alias", "alias", Season(1, Ep(1, "Pilot", true)))]);

        using var doc = await ReadAsync("shows.json");
        Assert.Equal(new[] { 0, 456 }, doc.RootElement.EnumerateArray().Select(s => s.GetProperty("i").GetInt32()));
        Assert.Contains("Some Episode", await File.ReadAllTextAsync(PathOf("tv", "obscure-show.json")));
    }

    [Fact]
    public async Task MetaCarriesTheReviewQueueGenresCountsAndBuildTimestamp()
    {
        string[] queue =
        [
            "Alien → unmatched",
            "Alias → seasons incomplete, request budget spent",
            "Halloween → flagged 0.62 (1978)"
        ];

        await WriteAsync([], [], Meta(queue));

        using var doc = await ReadAsync("meta.json");
        var root = doc.RootElement;
        Assert.Equal(
            new[] { "builtUtc", "movieCount", "showCount", "totalEpisodeCount", "genres", "attribution", "reviewQueue" },
            Names(root));
        Assert.Equal("2026-10-23T04:05:06Z", root.GetProperty("builtUtc").GetString());
        Assert.Equal(1457, root.GetProperty("movieCount").GetInt32());
        Assert.Equal(122, root.GetProperty("showCount").GetInt32());
        Assert.Equal(13850, root.GetProperty("totalEpisodeCount").GetInt32());
        Assert.Equal("Action", root.GetProperty("genres").GetProperty("28").GetString());
        Assert.Equal("Science Fiction", root.GetProperty("genres").GetProperty("878").GetString());
        Assert.Equal(queue, root.GetProperty("reviewQueue").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task EveryFileIsOneLineOfUtf8WithNoByteOrderMark()
    {
        // TMDB overviews carry real CR/LF pairs; they must leave as escapes, so a Windows run and a
        // Linux run write byte-identical files and no NewLine setting is needed anywhere.
        var multiline = Film(348, "Alien") with { Ov = "First paragraph.\r\nSecond paragraph." };

        await WriteAsync([multiline], [Series(456, "Alias", "alias", Season(1, Ep(1, "Pilot", true)))]);

        foreach (var file in new[] { new[] { "movies.json" }, new[] { "shows.json" }, new[] { "meta.json" },
                                     new[] { "tv", "alias.json" } })
        {
            var bytes = await File.ReadAllBytesAsync(PathOf(file));
            Assert.DoesNotContain((byte)'\n', bytes);
            Assert.DoesNotContain((byte)'\r', bytes);
            Assert.True(bytes[0] is (byte)'[' or (byte)'{', $"{string.Join('/', file)} starts with a byte-order mark");
        }
    }

    [Fact]
    public async Task ARerunIntoTheSameFolderLeavesNoTailFromTheLongerPreviousRun()
    {
        var longFilm = Film(348, "Alien") with { Ov = new string('x', 5000) };
        await WriteAsync([longFilm, longFilm, longFilm], [Series(456, "Alias", "alias",
            Season(1, Ep(1, "Pilot", true), Ep(2, "Two", true), Ep(3, "Three", true)))]);

        await WriteAsync([Film(1, "A")], [Series(456, "Alias", "alias", Season(1, Ep(1, "Pilot", true)))]);

        using var movies = await ReadAsync("movies.json");     // Parse throws on trailing garbage
        using var series = await ReadAsync("tv", "alias.json");
        Assert.Equal(1, movies.RootElement.GetArrayLength());
        Assert.Equal(1, series.RootElement.GetProperty("seasons")[0].GetProperty("eps").GetArrayLength());
    }

    [Fact]
    public async Task ACancelledTokenStopsTheWrite()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new CatalogWriter(_dir).WriteAsync([Film(348, "Alien")], [], Meta(), cts.Token));
    }
}

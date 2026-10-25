using System.Text.Json;
using MovieLibrary.Core.Models;
using MovieLibrary.Enrich.Emit;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// Task 14 amendment C (the writer owns the attribution sentence), D (atomic writes) and E (EpisodeCount
/// says which count it is). A: SeasonCount is in <c>CatalogWriterGapTests</c>.
/// </summary>
public class CatalogWriterAmendmentTests : IDisposable
{
    private const string Sentence =
        "This website uses TMDB and the TMDB APIs but is not endorsed, certified, or otherwise approved by TMDB.";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ml-out-amend-" + Guid.NewGuid());
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private static MovieRecord Film(int id, string title, double vote = 8.1) =>
        new(id, title, 1979, 117, "/p.jpg", null, [878], "Sci-Fi & Fantasy", vote, ["Ridley Scott"],
            "overview", null, [], null, null, 0.95);

    private static ShowRecord Series(string slug, double mc = 1d) =>
        new(456, "Alias", slug, null, 2001, "Drama",
            [new SeasonRecord(1, 1, 2, [new EpisodeRecord(1, "Pilot", true, null, 44, null, null),
                                        new EpisodeRecord(2, "Two", false, null, null, null, null)])],
            1, 2, mc);

    private static CatalogMeta Meta(string attribution = Sentence) =>
        new("2026-10-24T00:00:00Z", 1, 1, 2, new() { ["878"] = "Science Fiction" }, attribution, []);

    private Task WriteAsync(MovieRecord[] movies, ShowRecord[] shows, CatalogMeta? meta = null, CancellationToken ct = default)
        => new CatalogWriter(_dir).WriteAsync(movies, shows, meta ?? Meta(), ct);

    private string[] AllFiles() => Directory.Exists(_dir)
        ? [.. Directory.GetFiles(_dir, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(_dir, f)).Order()]
        : [];

    private Dictionary<string, byte[]> Snapshot()
        => AllFiles().ToDictionary(f => f, f => File.ReadAllBytes(Path.Combine(_dir, f)));

    // ---- C: attribution ----

    [Fact]
    public void TheAttributionConstantIsTheExactSentenceTmdbRequires()
        => Assert.Equal(Sentence, CatalogMeta.TmdbAttribution);

    [Theory]
    [InlineData("This website uses TMDB and the TMDB APIs but is not endorsed, certified, or otherwise approved by TMDB")]   // no full stop
    [InlineData("This website uses TMDB and the TMDB APIs but is not endorsed, certified, or otherwise approved by TMDB. ")]  // trailing space
    [InlineData(" This website uses TMDB and the TMDB APIs but is not endorsed, certified, or otherwise approved by TMDB.")]  // leading space
    [InlineData("this website uses tmdb and the tmdb apis but is not endorsed, certified, or otherwise approved by tmdb.")]   // case
    [InlineData("This website uses TMDB and the TMDB APIs but is not endorsed or certified by TMDB.")]                        // reworded
    [InlineData("")]
    public async Task AnAttributionThatIsNotTheConstantIsRefusedAndNothingIsWritten(string wrong)
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => WriteAsync([Film(348, "Alien")], [Series("alias")], Meta(wrong)));

        Assert.Empty(AllFiles());
    }

    [Fact]
    public async Task TheConstantItselfIsAccepted()
    {
        await WriteAsync([Film(348, "Alien")], [], Meta(CatalogMeta.TmdbAttribution));

        Assert.Contains("meta.json", AllFiles());
    }

    // ---- D: atomic writes ----

    [Fact]
    public async Task AFailedRunLeavesThePreviousCatalogByteForByteIntact()
    {
        await WriteAsync([Film(348, "Alien")], [Series("alias")]);
        var before = Snapshot();

        // NaN is not JSON: the serializer throws part-way, after it has started the file.
        await Assert.ThrowsAnyAsync<Exception>(() => WriteAsync([Film(1, "New", double.NaN)], [Series("alias")]));

        var after = Snapshot();
        Assert.Equal(before.Keys, after.Keys);                 // and no *.writing leftovers
        foreach (var (file, bytes) in before) Assert.Equal(bytes, after[file]);
    }

    [Fact]
    public async Task AFailureWhileWritingAnyFileLeavesEveryOldFileUntouched()
    {
        await WriteAsync([Film(348, "Alien")], [Series("alias")]);
        var before = Snapshot();

        // The series file is staged first and is fine (and differs from the old one: mc 0.5 against 1); movies.json
        // then fails to serialize. Had files been moved into place as they were written, the new series file would
        // already have replaced the old. Staging is all-or-nothing; the move phase is covered in CatalogWriterMoveTests.
        await Assert.ThrowsAnyAsync<Exception>(() => WriteAsync([Film(999, "Other", double.NaN)], [Series("alias", mc: 0.5)]));

        var after = Snapshot();
        Assert.Equal(before.Keys, after.Keys);
        foreach (var (file, bytes) in before) Assert.Equal(bytes, after[file]);
    }

    [Fact]
    public async Task AFirstRunThatFailsLeavesNoFilesAtAll()
    {
        await Assert.ThrowsAnyAsync<Exception>(() => WriteAsync([Film(1, "New", double.NaN)], [Series("alias")]));

        Assert.Empty(AllFiles());
    }

    [Fact]
    public async Task ACancelledRunLeavesNoZeroByteMoviesJson()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WriteAsync([Film(348, "Alien")], [], ct: cts.Token));

        Assert.Empty(AllFiles());
    }

    [Fact]
    public async Task ASuccessfulRunLeavesNoTemporaryFilesBehind()
    {
        await WriteAsync([Film(348, "Alien")], [Series("alias")]);

        Assert.Equal(["meta.json", "movies.json", "shows.json", Path.Combine("tv", "alias.json")], AllFiles());
    }

    [Fact]
    public async Task ATempFileFromACrashedRunIsSweptButARealOldSeriesFileIsLeftAlone()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "tv"));
        await File.WriteAllTextAsync(Path.Combine(_dir, "movies.json.writing"), "{half a fi");
        await File.WriteAllTextAsync(Path.Combine(_dir, "tv", "alias.json.writing"), "{half a fi");
        // These two names this run never writes, so only the sweep can remove them.
        await File.WriteAllTextAsync(Path.Combine(_dir, "gone.json.writing"), "{half a fi");
        await File.WriteAllTextAsync(Path.Combine(_dir, "tv", "gone-series.json.writing"), "{half a fi");
        await File.WriteAllTextAsync(Path.Combine(_dir, "tv", "dropped-series.json"), "{}");   // stale cleanup is not wanted

        await WriteAsync([Film(348, "Alien")], [Series("alias")]);

        Assert.Equal(["meta.json", "movies.json", "shows.json", Path.Combine("tv", "alias.json"),
                      Path.Combine("tv", "dropped-series.json")], AllFiles());
    }

    // ---- slugs name files, so the writer is the last gate (D made a clash fail late instead of overwriting) ----

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("..")]
    [InlineData("alias ")]   // trailing space: Windows drops it, so the file would not be where the client looks
    [InlineData(" alias")]
    [InlineData("a\0b")]   // NUL is invalid in a file name on every OS, unlike the separators
    [InlineData("../escape")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    public async Task ASlugThatIsNotASafeFileNameIsRefusedAndNothingIsWritten(string slug)
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => WriteAsync([Film(348, "Alien")], [Series(slug)]));

        Assert.Empty(AllFiles());
    }

    [Theory]
    [InlineData("alias", "alias")]
    [InlineData("alias", "ALIAS")]   // one file on a case-insensitive disk
    public async Task TwoSeriesSharingASlugAreRefusedAndNothingIsWritten(string first, string second)
    {
        await Assert.ThrowsAsync<InvalidDataException>(
            () => WriteAsync([Film(348, "Alien")], [Series(first), Series(second)]));

        Assert.Empty(AllFiles());
    }

    // ---- E: episode counts ----

    [Fact]
    public async Task MetaSaysWhetherAnEpisodeCountIsTheTotalOrTheOwnedOne()
    {
        var meta = new CatalogMeta("2026-10-24T00:00:00Z", 1457, 122, 13850, new(), Sentence, [], OwnedEpisodeCount: 9421);

        await WriteAsync([], [], meta);

        using var doc = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(_dir, "meta.json")));
        Assert.Equal(13850, doc.RootElement.GetProperty("totalEpisodeCount").GetInt32());
        Assert.Equal(9421, doc.RootElement.GetProperty("ownedEpisodeCount").GetInt32());
        Assert.False(doc.RootElement.TryGetProperty("episodeCount", out _));   // the ambiguous name is gone
    }

    [Fact]
    public async Task AnOwnedCountNobodySuppliedIsAbsentNotAFalseZero()
    {
        await WriteAsync([], [], Meta());

        using var doc = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(_dir, "meta.json")));
        Assert.False(doc.RootElement.TryGetProperty("ownedEpisodeCount", out _));
    }
}

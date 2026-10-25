using System.Text.Json;
using MovieLibrary.Core.Models;
using MovieLibrary.Enrich.Emit;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// Task 14 amendment F. Staging makes each file whole and the staging phase all-or-nothing, but publishing is a
/// run of separate moves. These tests fail a MOVE (every earlier failure test faults during staging) and pin the
/// order that makes a partial failure safe: series files first, then the index, and meta.json last as the commit
/// marker, so the index never points at a series file that is not there yet.
/// </summary>
public class CatalogWriterMoveTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ml-out-move-" + Guid.NewGuid());
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private static readonly string Alias = Path.Combine("tv", "alias.json");
    private static readonly string NewSeries = Path.Combine("tv", "new-series.json");

    private static MovieRecord Film(int id) =>
        new(id, "Film " + id, 1979, 117, "/p.jpg", null, [878], "Drama", 8.1, ["Ridley Scott"],
            "overview", null, [], null, null, 0.95);

    private static ShowRecord Series(string slug, int id) =>
        new(id, slug, slug, null, 2001, "Drama",
            [new SeasonRecord(1, 1, 1, [new EpisodeRecord(1, "Pilot", true, null, 44, null, null)])], 1, 1, 1d);

    private static CatalogMeta Meta(int movies, int shows) =>
        new("2026-10-25T00:00:00Z", movies, shows, shows, new() { ["878"] = "Science Fiction" },
            CatalogMeta.TmdbAttribution, []);

    /// <summary>Records every move attempt and the order moves succeeded in; <paramref name="fault"/> may veto an attempt.</summary>
    private sealed class MoveSpy(string root, Func<string, int, Exception?>? fault = null)
    {
        public List<string> Done { get; } = [];
        public Dictionary<string, int> Attempts { get; } = [];

        public void Move(string temp, string final)
        {
            var name = Path.GetRelativePath(root, final);
            Attempts[name] = Attempts.GetValueOrDefault(name) + 1;
            if (fault?.Invoke(name, Attempts[name]) is { } ex) throw ex;
            File.Move(temp, final, overwrite: true);
            Done.Add(name);
        }
    }

    private Task V1Async() => new CatalogWriter(_dir).WriteAsync([Film(1)], [Series("alias", 10)], Meta(1, 1), default);

    private Task V2Async(MoveSpy spy) => new CatalogWriter(_dir, spy.Move, TimeSpan.Zero)
        .WriteAsync([Film(2)], [Series("alias", 10), Series("new-series", 11)], Meta(1, 2), default);

    private byte[] Bytes(string relative) => File.ReadAllBytes(Path.Combine(_dir, relative));

    private string[] Leftovers() => [.. Directory.GetFiles(_dir, "*.writing", SearchOption.AllDirectories)];

    /// <summary>The invariant the order exists to keep: whatever is on disk, the index names only series files that exist.</summary>
    private void AssertTheIndexOnlyNamesSeriesThatExist()
    {
        var shows = Path.Combine(_dir, "shows.json");
        if (!File.Exists(shows)) return;
        using var doc = JsonDocument.Parse(File.ReadAllBytes(shows));
        foreach (var entry in doc.RootElement.EnumerateArray())
            Assert.True(File.Exists(Path.Combine(_dir, "tv", entry.GetProperty("slug").GetString() + ".json")),
                $"shows.json lists {entry.GetProperty("slug").GetString()} but its tv file is missing");
    }

    [Fact]
    public async Task SeriesFilesAreMovedFirstThenTheIndexAndMetaLast()
    {
        var spy = new MoveSpy(_dir);
        await V1Async();

        await V2Async(spy);

        Assert.Equal([Alias, NewSeries], spy.Done.Take(2).Order());
        Assert.Equal(["movies.json", "shows.json"], spy.Done.Skip(2).Take(2).Order());
        Assert.Equal("meta.json", spy.Done[^1]);
        Assert.Equal(5, spy.Done.Count);
    }

    [Fact]
    public async Task ATransientMoveFailureIsRetriedAndTheCatalogStillPublishes()
    {
        var spy = new MoveSpy(_dir, (name, attempt) => name == Alias && attempt == 1 ? new IOException("held by a scanner") : null);
        await V1Async();

        await V2Async(spy);

        Assert.Equal(2, spy.Attempts[Alias]);
        Assert.Equal(5, spy.Done.Count);
        Assert.Contains("Film 2", System.Text.Encoding.UTF8.GetString(Bytes("movies.json")));
        Assert.Empty(Leftovers());
    }

    [Fact]
    public async Task TheRetryPausesBetweenAttempts()
    {
        // 60 ms before the second attempt and 120 ms before the third: 180 ms in all. Only the lower bound is asserted,
        // with slack, because a timer may fire a little early but never makes a loop wait longer than it was asked to.
        var spy = new MoveSpy(_dir, (name, attempt) => name == "shows.json" && attempt < 3 ? new IOException("held") : null);
        await V1Async();
        var clock = System.Diagnostics.Stopwatch.StartNew();

        await new CatalogWriter(_dir, spy.Move, TimeSpan.FromMilliseconds(60)).WriteAsync(
            [Film(2)], [Series("alias", 10)], Meta(1, 1), default);

        Assert.True(clock.ElapsedMilliseconds >= 100, $"retried after only {clock.ElapsedMilliseconds} ms");
        Assert.Equal(3, spy.Attempts["shows.json"]);
    }

    [Fact]
    public async Task AMoveThatKeepsFailingIsTriedThreeTimesAndThenSurfaces()
    {
        var spy = new MoveSpy(_dir, (name, _) => name == "shows.json" ? new IOException("still held") : null);
        await V1Async();

        await Assert.ThrowsAsync<IOException>(() => V2Async(spy));

        Assert.Equal(3, spy.Attempts["shows.json"]);
    }

    [Fact]
    public async Task OnlyAnIOExceptionIsRetried()
    {
        var spy = new MoveSpy(_dir, (name, _) => name == "movies.json" ? new InvalidOperationException("not transient") : null);
        await V1Async();

        await Assert.ThrowsAsync<InvalidOperationException>(() => V2Async(spy));

        Assert.Equal(1, spy.Attempts["movies.json"]);
    }

    [Fact]
    public async Task AFailedIndexMoveLeavesTheNewSeriesPublishedAndTheOldIndexAndMetaInPlace()
    {
        await V1Async();
        var (oldShows, oldMeta) = (Bytes("shows.json"), Bytes("meta.json"));
        var spy = new MoveSpy(_dir, (name, _) => name == "shows.json" ? new IOException("held") : null);

        await Assert.ThrowsAsync<IOException>(() => V2Async(spy));

        Assert.True(File.Exists(Path.Combine(_dir, NewSeries)));           // series files went first
        Assert.Equal(oldShows, Bytes("shows.json"));                       // the index did not
        Assert.Equal(oldMeta, Bytes("meta.json"));                         // and the commit marker is the old build's
        AssertTheIndexOnlyNamesSeriesThatExist();
        Assert.Empty(Leftovers());
    }

    [Fact]
    public async Task AFailedSeriesMoveLeavesTheIndexMoviesAndMetaUntouched()
    {
        await V1Async();
        var (oldMovies, oldShows, oldMeta) = (Bytes("movies.json"), Bytes("shows.json"), Bytes("meta.json"));
        var spy = new MoveSpy(_dir, (name, _) => name == NewSeries ? new IOException("held") : null);

        await Assert.ThrowsAsync<IOException>(() => V2Async(spy));

        Assert.Equal(oldMovies, Bytes("movies.json"));
        Assert.Equal(oldShows, Bytes("shows.json"));   // a new index here would list new-series, whose file is not there
        Assert.Equal(oldMeta, Bytes("meta.json"));
        AssertTheIndexOnlyNamesSeriesThatExist();
        Assert.Empty(Leftovers());
    }

    [Fact]
    public async Task OnAFirstRunAFailedMetaMoveLeavesEverythingButMetaJson()
    {
        var spy = new MoveSpy(_dir, (name, _) => name == "meta.json" ? new IOException("held") : null);

        await Assert.ThrowsAsync<IOException>(() => new CatalogWriter(_dir, spy.Move, TimeSpan.Zero)
            .WriteAsync([Film(1)], [Series("alias", 10)], Meta(1, 1), default));

        Assert.False(File.Exists(Path.Combine(_dir, "meta.json")));      // no meta.json = an unfinished build
        Assert.True(File.Exists(Path.Combine(_dir, Alias)));
        Assert.True(File.Exists(Path.Combine(_dir, "movies.json")));
        Assert.True(File.Exists(Path.Combine(_dir, "shows.json")));
        AssertTheIndexOnlyNamesSeriesThatExist();
        Assert.Empty(Leftovers());
    }
}

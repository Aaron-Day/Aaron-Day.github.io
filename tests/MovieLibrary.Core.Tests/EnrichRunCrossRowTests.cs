namespace MovieLibrary.Core.Tests;

/// <summary>
/// The cross-row rules, end to end and offline: a real (non-fixture) run through stubbed Sheet and TMDB endpoints, read
/// back from the movies.json it wrote and the review queue it printed. It holds one of each case the owner's library
/// has: a wrong match (Fast &amp; Furious took Tokyo Drift), a genuine duplicate (Blade Runner), and a probable one
/// (Captain America at 124 and 125 minutes).
/// </summary>
public class EnrichRunCrossRowTests
{
    private static string Quote(string cell) => "\"" + cell.Replace("\"", "\"\"") + "\"";

    private static string Line(string title, string runtime, string directors, string own)
        => string.Join(",", Quote(title), Quote(runtime), Quote(directors), Quote(own), Quote("Action"), Quote("")) + "\n";

    private static readonly string Library = GoogleSheetClientTests.MoviesHeader
        + Line("Fast & Furious (2009)", "1:46:00", "Justin Lin", "Fast & Furious Bundle")
        + Line("The Fast and the Furious: Tokyo Drift", "1:44:00", "Justin Lin", "Fast & Furious Bundle")
        + Line("Blade Runner", "1:57:10", "Ridley Scott", RealCollision.BladeRunnerOwn)
        + Line("Blade Runner (The Final Cut)", "1:57:29", "Ridley Scott", RealCollision.BladeRunnerFinalCutOwn)
        + Line("Captain America: The First Avenger", "2:05:15", "Joe Johnston", "Captain America 4-Movie Collection")
        + Line("Captain America: The First Avenger", "2:04:09", "Joe Johnston", "");

    [Fact]
    public async Task ARealRunPublishesTheReconciledCatalogue()
    {
        using var h = new RunHarness();
        h.MoviesCsv = Library;
        h.ShowsCsv = GoogleSheetClientTests.ShowsHeader;
        h.Tmdb = new FakeTmdb(
            new FakeTmdb.Film(9615, "The Fast and the Furious: Tokyo Drift", 104, "Justin Lin", Year: 2006),
            new FakeTmdb.Film(78, "Blade Runner", 117, "Ridley Scott", Year: 1982),
            new FakeTmdb.Film(1771, "Captain America: The First Avenger", 124, "Joe Johnston", Year: 2011)).Respond;

        var result = await h.RunAsync();

        Assert.Equal(0, result.ExitCode);
        using var doc = h.Json("movies.json");
        var films = doc.RootElement.EnumerateArray().ToList();

        Assert.Equal(5, films.Count);   // six sheet rows, one collapse
        Assert.Equal(films.Count, films.Select(f => f.GetProperty("k").GetString()).Distinct().Count());

        System.Text.Json.JsonElement Film(string title) => films.Single(f => f.GetProperty("t").GetString() == title);
        string Key(string title) => Film(title).GetProperty("k").GetString()!;

        Assert.Equal(0, Film("Fast & Furious (2009)").GetProperty("i").GetInt32());
        Assert.StartsWith("u-", Key("Fast & Furious (2009)"), StringComparison.Ordinal);
        Assert.Equal("9615-104", Key("The Fast and the Furious: Tokyo Drift"));
        Assert.Equal("78-117", Key("Blade Runner (The Final Cut)"));
        Assert.DoesNotContain(films, f => f.GetProperty("t").GetString() == "Blade Runner");   // the shorter title was dropped
        Assert.Equal(
            "Blade Runner / Blade Runner: The Final Cut. Colección 2 películas / Warner Bros Firsts: Action & Adventure Bundle",
            Film("Blade Runner (The Final Cut)").GetProperty("own").GetString());
        Assert.Equal(["1771-124", "1771-125"],
            films.Where(f => f.GetProperty("i").GetInt32() == 1771).Select(f => f.GetProperty("k").GetString()!).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task TheReviewQueueTheOwnerReadsNamesEachDecision()
    {
        using var h = new RunHarness();
        h.MoviesCsv = Library;
        h.ShowsCsv = GoogleSheetClientTests.ShowsHeader;
        h.Tmdb = new FakeTmdb(
            new FakeTmdb.Film(9615, "The Fast and the Furious: Tokyo Drift", 104, "Justin Lin", Year: 2006),
            new FakeTmdb.Film(78, "Blade Runner", 117, "Ridley Scott", Year: 1982),
            new FakeTmdb.Film(1771, "Captain America: The First Avenger", 124, "Joe Johnston", Year: 2011)).Respond;

        await h.RunAsync();

        var queue = h.ReviewQueue();
        Assert.Contains(queue, l => l.StartsWith("Fast & Furious (2009) (1:46:00, Justin Lin) → rejected TMDB id 9615", StringComparison.Ordinal));
        Assert.Contains(queue, l => l.Contains("duplicate key 78-117: kept \"Blade Runner (The Final Cut)\"", StringComparison.Ordinal));
        Assert.Contains(queue, l => l.Contains("(1771) → same title and id at 124, 125 min; probably one film listed twice", StringComparison.Ordinal));
        Assert.DoesNotContain(queue, l => l.Contains("Flagged", StringComparison.Ordinal));   // the rejected row's stale note is gone
    }
}

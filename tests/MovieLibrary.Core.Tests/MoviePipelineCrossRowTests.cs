using System.Text.Json;
using MovieLibrary.Core.Models;
using MovieLibrary.Enrich.Pipeline;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// The three cross-row rules on the REAL pipeline, over a stubbed TMDB that cannot reach the network. The rules' own
/// tests prove the logic over records; these prove the pipeline accumulates what the rules need (the TMDB title, the
/// sheet row, the identity key, whether an override forced the id) and hands the result back.
/// </summary>
public class MoviePipelineCrossRowTests
{
    private static readonly IReadOnlyDictionary<string, int> NoOverrides = new Dictionary<string, int>();

    private static readonly FakeTmdb.Film TokyoDrift = new(9615, "The Fast and the Furious: Tokyo Drift", 104, "Justin Lin", Year: 2006);

    private static SheetRow Row(string title, string runtime, string directors, string own = "")
        => new(title, runtime, directors, own, "Action", "");

    private static SheetRow FastAndFurious => Row("Fast & Furious (2009)", "1:46:00", "Justin Lin", "Fast & Furious Bundle");
    private static SheetRow TokyoDriftRow => Row("The Fast and the Furious: Tokyo Drift", "1:44:00", "Justin Lin", "Fast & Furious Bundle");

    private static Task<MoviePipelineResult> Run(TestPipeline.Harness h, params SheetRow[] rows)
        => h.Pipeline.RunAsync(rows, NoOverrides, CancellationToken.None);

    private static TestPipeline.Harness Tmdb(params FakeTmdb.Film[] films) => TestPipeline.Create(0, new FakeTmdb(films).Respond);

    [Fact]
    public async Task AWrongMatchIsRejectedAndTheRealFilmKeepsItsRecord()
    {
        var result = await Run(Tmdb(TokyoDrift), FastAndFurious, TokyoDriftRow);

        var real = Assert.Single(result.Records, r => r.T == TokyoDriftRow.Title);
        var rejected = Assert.Single(result.Records, r => r.T == FastAndFurious.Title);
        Assert.Equal(("9615-104", 9615, 2006, 0.85), (real.K, real.I, real.Y, real.Mc));
        Assert.Equal(0, rejected.I);
        Assert.StartsWith("u-", rejected.K, StringComparison.Ordinal);
        Assert.Equal(0d, rejected.Mc);
        Assert.Equal(2009, rejected.Y);          // the year its own title hints at, not Tokyo Drift's 2006
        Assert.Null(rejected.P);
        Assert.Empty(rejected.Cast);
    }

    // The proof the right set of fields was cleared, without listing them: the rejected record is, field for field, what
    // the same sheet row produces when TMDB has never heard of it. A TMDB-sourced field added to MovieRecord later and
    // left behind here would make these two differ.
    [Fact]
    public async Task ARejectedRowIsExactlyWhatTheSameRowProducesWhenItNeverMatched()
    {
        var collided = await Run(Tmdb(TokyoDrift), FastAndFurious, TokyoDriftRow);
        var neverMatched = await Run(Tmdb(), FastAndFurious);

        var rejected = Assert.Single(collided.Records, r => r.I == 0);
        var expected = Assert.Single(neverMatched.Records);
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(rejected));
    }

    [Fact]
    public async Task TheRejectedRowHasOneReviewLineNotTheStaleFlaggedOne()
    {
        var result = await Run(Tmdb(TokyoDrift), FastAndFurious, TokyoDriftRow);

        var line = Assert.Single(result.ReviewQueue);
        Assert.Equal(
            "Fast & Furious (2009) (1:46:00, Justin Lin) → rejected TMDB id 9615 (\"The Fast and the Furious: Tokyo Drift\"): " +
            "its title does not match that film and another row's does, so it is now unmatched",
            line);
    }

    // The TMDB budget is the owner's: reconciling is a pure post-pass. Each row costs what it costs alone.
    [Fact]
    public async Task ReconcilingSpendsNoRequests()
    {
        var together = Tmdb(TokyoDrift);
        var first = Tmdb(TokyoDrift);
        var second = Tmdb(TokyoDrift);

        var result = await Run(together, FastAndFurious, TokyoDriftRow);
        var a = await Run(first, FastAndFurious);
        var b = await Run(second, TokyoDriftRow);

        Assert.Equal(a.RequestsUsed + b.RequestsUsed, result.RequestsUsed);
        Assert.Equal(first.Handler.Requests.Count + second.Handler.Requests.Count, together.Handler.Requests.Count);
        Assert.NotEmpty(together.Handler.Requests);
    }

    [Fact]
    public async Task AnOverrideRowIsNotRejectedByTheRealPipeline()
    {
        var newHope = new FakeTmdb.Film(11, "Star Wars", 121, "George Lucas", Year: 1977);
        var overrides = new Dictionary<string, int> { [MoviePipeline.OverrideKey("Star Wars: A New Hope", 125)] = 11 };

        var result = await Tmdb(newHope).Pipeline.RunAsync(
            [Row("Star Wars: A New Hope", "2:05:00", "George Lucas"), Row("Star Wars", "2:01:00", "George Lucas")],
            overrides, CancellationToken.None);

        Assert.Equal([11, 11], result.Records.Select(r => r.I));
        Assert.Equal(["11-121", "11-125"], result.Records.Select(r => r.K).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task TheBladeRunnerPairCollapsesIntoOneRecordThatKeepsBothPurchases()
    {
        var bladeRunner = new FakeTmdb.Film(78, "Blade Runner", 117, "Ridley Scott", Year: 1982);

        var result = await Run(Tmdb(bladeRunner),
            Row("Blade Runner", "1:57:10", "Ridley Scott", RealCollision.BladeRunnerOwn),
            Row("Blade Runner (The Final Cut)", "1:57:29", "Ridley Scott", RealCollision.BladeRunnerFinalCutOwn));

        var record = Assert.Single(result.Records);
        Assert.Equal(("78-117", "Blade Runner (The Final Cut)"), (record.K, record.T));
        Assert.Equal(
            "Blade Runner / Blade Runner: The Final Cut. Colección 2 películas / Warner Bros Firsts: Action & Adventure Bundle",
            record.Own);
        Assert.Equal(   // rule 1 sees two agreeing rows on one id; rule 2 then collapses them
            ["Blade Runner (78) → 2 rows share this TMDB id and all agree on its title (117 min); none rejected",
             "Blade Runner (The Final Cut) (1:57:29, Ridley Scott) → duplicate key 78-117: kept \"Blade Runner (The Final Cut)\", " +
             "dropped \"Blade Runner\"; purchase info merged into Own"],
            result.ReviewQueue);
    }

    [Fact]
    public async Task TheCaptainAmericaPairPublishesBothRowsUnderTheirOwnKeysAndIsNoted()
    {
        var captain = new FakeTmdb.Film(1771, "Captain America: The First Avenger", 124, "Joe Johnston", Year: 2011);
        const string title = "Captain America: The First Avenger";

        var result = await Run(Tmdb(captain),
            Row(title, "2:05:15", "Joe Johnston", "Captain America 4-Movie Collection"), Row(title, "2:04:09", "Joe Johnston"));

        Assert.Equal(["1771-124", "1771-125"], result.Records.Select(r => r.K).Order(StringComparer.Ordinal));
        Assert.Equal(   // rule 1's note for the group, then rule 3's
            [$"{title} (1771) → 2 rows share this TMDB id and all agree on its title (124, 125 min); none rejected",
             $"{title} (1771) → same title and id at 124, 125 min; probably one film listed twice"],
            result.ReviewQueue);
    }

    // A library with no shared ids reports exactly what it reported before the rules existed.
    [Fact]
    public async Task ALibraryWithNoSharedIdsHasNoReconcilerNotes()
    {
        var fargo = new FakeTmdb.Film(275, "Fargo", 98, "Joel Coen", Year: 1996);

        var result = await Run(Tmdb(fargo), Row("Fargo", "1:38:00", "Joel Coen"), Row("Zzyzx Road", "1:30:00", "Nobody"));

        Assert.Equal(["Zzyzx Road (1:30:00, Nobody) → Unmatched at 0.00, id none"], result.ReviewQueue);
    }

    // The job summary prints the first 150 lines of the film section and the real queue is already longer, so a decision
    // that sat after the row notes would be exactly what it cuts. Here 160 unmatched rows precede it alphabetically.
    [Fact]
    public async Task TheRulesLinesComeBeforeTheRowNotesSoTheSummaryCapCannotCutThem()
    {
        var rows = Enumerable.Range(0, 160).Select(i => Row($"Aaa Obscure {i:000}", "1:30:00", "Nobody"))
            .Append(FastAndFurious).Append(TokyoDriftRow).ToArray();

        var result = await Run(Tmdb(TokyoDrift), rows);

        Assert.Equal(161, result.ReviewQueue.Count);                       // 160 unmatched, plus the rejection
        Assert.Contains("rejected TMDB id 9615", result.ReviewQueue[0], StringComparison.Ordinal);
        Assert.True(result.ReviewQueue.Count > MovieLibrary.Enrich.Reporting.JobSummary.MaxReviewLinesPerSection);
    }
}

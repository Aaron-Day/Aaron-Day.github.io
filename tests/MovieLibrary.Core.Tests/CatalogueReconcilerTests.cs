using MovieLibrary.Core.Matching;
using MovieLibrary.Enrich.Pipeline;
using static MovieLibrary.Core.Tests.ResolvedFilms;

namespace MovieLibrary.Core.Tests;

/// <summary>The three rules together, over the owner's real colliding rows.</summary>
public class CatalogueReconcilerTests
{
    private static List<ResolvedFilm> AllRealRows() => [.. RealCollision.All.SelectMany(c => c.Films())];

    private static (int Confident, int Uncertain, int Unmatched) Bands(IEnumerable<ResolvedFilm> films)
    {
        var records = films.Select(f => f.Record).ToList();
        return (records.Count(r => r.I != 0 && r.Mc >= MatchScorer.AcceptThreshold),
                records.Count(r => r.I != 0 && r.Mc < MatchScorer.AcceptThreshold),
                records.Count(r => r.I == 0));
    }

    // The shape of the measured end state, on the 32 real rows: three flagged rows become unmatched, one pair collapses
    // into a single record, and the collapse takes one confident row with it. The same arithmetic takes 1266/174/17 to 1265/171/20 over
    // the 1457 published rows, of which these 32 are the only ones the rules can touch.
    [Fact]
    public void ThreeUncertainRowsBecomeUnmatchedAndOnePairCollapses()
    {
        var before = AllRealRows();
        var after = CatalogueReconciler.Reconcile(before).Films;

        var (confidentBefore, uncertainBefore, unmatchedBefore) = Bands(before);
        var (confidentAfter, uncertainAfter, unmatchedAfter) = Bands(after);

        Assert.Equal(32, before.Count);
        Assert.Equal(31, after.Count);                              // one fewer record: Blade Runner
        Assert.Equal(confidentBefore - 1, confidentAfter);          // the collapse took one confident row (0.85, 0.85 became one)
        Assert.Equal(uncertainBefore - 3, uncertainAfter);          // the three 0.70 impostors left the band...
        Assert.Equal(unmatchedBefore + 3, unmatchedAfter);          // ...and arrived here
    }

    [Fact]
    public void EveryKeyIsDistinctAfterTheRulesHaveRun()
    {
        var after = CatalogueReconciler.Reconcile(AllRealRows()).Films;

        Assert.Equal(after.Count, after.Select(f => f.Record.K).Distinct().Count());
    }

    // The sheet's row order is unstable (Apple's export), so the outcome must not depend on it.
    [Fact]
    public void TheOutcomeDoesNotDependOnTheOrderOfTheRows()
    {
        var forward = CatalogueReconciler.Reconcile(AllRealRows());
        var reversed = CatalogueReconciler.Reconcile([.. AllRealRows().AsEnumerable().Reverse()]);

        Assert.Equal(forward.Notes, reversed.Notes);
        Assert.Equal(
            forward.Films.Select(f => (f.Record.K, f.Record.T, f.Record.Own)).Order(),
            reversed.Films.Select(f => (f.Record.K, f.Record.T, f.Record.Own)).Order());
    }

    [Fact]
    public void ReconcilingTwiceChangesNothingTheSecondTime()
    {
        var once = CatalogueReconciler.Reconcile(AllRealRows()).Films;

        var twice = CatalogueReconciler.Reconcile(once);

        Assert.Equal(once.Select(f => f.Record.K), twice.Films.Select(f => f.Record.K));
        Assert.Equal(once.Select(f => f.Record.Own), twice.Films.Select(f => f.Record.Own));
    }

    [Fact]
    public void ARunWithNothingSharedIsReturnedUnchangedWithNoNotes()
    {
        var films = new[] { ResolvedFilms.Make(1, "One", 90, "One"), ResolvedFilms.Make(2, "Two", 91, "Two"), ResolvedFilms.Make(0, "Three", 92, null) };

        var result = CatalogueReconciler.Reconcile(films);

        Assert.Equal(films, result.Films);
        Assert.Empty(result.Notes);
    }

    // A group that BOTH conflicts on id and collapses on key: two agreeing titles and one impostor, all id 78 at 117
    // minutes, so all three share the key 78-117 until rule 1 has run. Rule 1 must reject the impostor first, or rule 2
    // would swallow it, and the survivor must merge only the two rows that stayed.
    [Fact]
    public void AGroupThatConflictsOnIdAndCollapsesOnKeyIsRejectedFirstAndThenCollapsed()
    {
        var plain = Make(78, "Blade Runner", 117, "Blade Runner", own: "Bundle A");
        var cut = Make(78, "Blade Runner (The Final Cut)", 117, "Blade Runner", own: "Bundle B");
        var impostor = Make(78, "Blade Runner 2049", 117, "Blade Runner", own: "Bundle C");
        Assert.Single(new[] { plain, cut, impostor }.Select(f => f.Record.K).Distinct());   // one key, before the rules

        var result = CatalogueReconciler.Reconcile([plain, cut, impostor]);

        Assert.Equal(2, result.Films.Count);
        var survivor = Assert.Single(result.Films, f => f.Record.I == 78).Record;
        Assert.Equal(("78-117", "Blade Runner (The Final Cut)", "Bundle A / Bundle B"), (survivor.K, survivor.T, survivor.Own));   // no Bundle C
        var rejected = Assert.Single(result.Films, f => f.Record.I == 0).Record;
        Assert.Equal(("Blade Runner 2049", "Bundle C"), (rejected.T, rejected.Own));   // unmatched, and it keeps its own purchase info
        Assert.StartsWith("u-", rejected.K, StringComparison.Ordinal);
        Assert.Equal(2, result.Notes.Count);
        Assert.Contains("rejected TMDB id 78", result.Notes[0], StringComparison.Ordinal);             // rule 1's line, then rule 2's
        Assert.Contains("dropped \"Blade Runner\"; purchase info merged into Own", result.Notes[1], StringComparison.Ordinal);
        Assert.DoesNotContain("2049", result.Notes[1], StringComparison.Ordinal);
    }

    [Fact]
    public void ThatGroupResolvesTheSameWayInEveryOrder()
    {
        var group = new[]
        {
            Make(78, "Blade Runner", 117, "Blade Runner", own: "Bundle A"),
            Make(78, "Blade Runner (The Final Cut)", 117, "Blade Runner", own: "Bundle B"),
            Make(78, "Blade Runner 2049", 117, "Blade Runner", own: "Bundle C"),
        };

        var outcomes = new[] { new[] { 0, 1, 2 }, [0, 2, 1], [1, 0, 2], [1, 2, 0], [2, 0, 1], [2, 1, 0] }
            .Select(order => Outcome(order.Select(i => group[i]))).Distinct();

        Assert.Single(outcomes);
    }

    // The sheet's row order is not stable, so the whole outcome, not only the survivors, must be a function of the rows.
    [Fact]
    public void TwoHundredShufflesOfTheRealRowsAllGiveTheSameOutcome()
    {
        var rows = AllRealRows();
        var expected = Outcome(rows);
        var random = new Random(20261103);

        for (var n = 0; n < 200; n++)
            Assert.Equal(expected, Outcome(rows.OrderBy(_ => random.Next())));
    }
}

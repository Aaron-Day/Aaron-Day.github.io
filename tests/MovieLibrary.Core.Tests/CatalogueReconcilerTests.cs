using MovieLibrary.Core.Matching;
using MovieLibrary.Enrich.Pipeline;

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
    // into a single record, nothing that was confident moves. The same arithmetic takes 1266/174/17 to 1266/171/20 over
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
}

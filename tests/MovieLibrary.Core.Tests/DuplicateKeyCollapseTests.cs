using MovieLibrary.Enrich.Pipeline;
using static MovieLibrary.Core.Tests.ResolvedFilms;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// Rule 2: Blazor throws on a duplicate sibling <c>@key</c>, so a key must be unique in the published catalogue. Rows
/// that still share one are collapsed into the row with the longest sheet title, and no purchase info is lost.
/// </summary>
public class DuplicateKeyCollapseTests
{
    private static (IReadOnlyList<ResolvedFilm> Films, List<string> Notes) Run(params ResolvedFilm[] films)
    {
        var notes = new List<string>();
        return (CatalogueReconciler.CollapseDuplicateKeys(films, notes), notes);
    }

    // Same id and runtime, so the same key; only the title and the purchase info tell the rows apart.
    private static ResolvedFilm Cut(string title, string? own = null, int id = 78, int runtime = 117)
        => Make(id, title, runtime, "Blade Runner", own);

    [Fact]
    public void ARowWithAKeyOfItsOwnIsReturnedUnchanged()
    {
        var a = Cut("Blade Runner");
        var b = Cut("The Martian", id: 286217, runtime: 142);

        var (films, notes) = Run(a, b);

        Assert.Equal([a, b], films);
        Assert.Same(a, films[0]);
        Assert.Empty(notes);
    }

    [Fact]
    public void TheLongestSheetTitleSurvives()
    {
        var (films, _) = Run(Cut("Blade Runner"), Cut("Blade Runner (The Final Cut)"));

        Assert.Equal("Blade Runner (The Final Cut)", Assert.Single(films).Record.T);
    }

    [Fact]
    public void EqualLengthTitlesFallToTheOrdinalComparisonWhicheverOrderTheyArriveIn()
    {
        var first = Cut("Blade Runner A");
        var second = Cut("Blade Runner B");

        Assert.Equal("Blade Runner A", Assert.Single(Run(first, second).Films).Record.T);
        Assert.Equal("Blade Runner A", Assert.Single(Run(second, first).Films).Record.T);
    }

    // Ordinal, not culture-aware: "Z" sorts before "a" by code point, and a culture comparison would put "a" first.
    [Fact]
    public void TheTieBreakIsOrdinalNotCultural()
        => Assert.Equal("Z Film", Assert.Single(Run(Cut("a Film"), Cut("Z Film")).Films).Record.T);

    [Fact]
    public void TheSurvivorTakesEveryOtherFieldFromTheLongestTitleRow()
    {
        var shortRow = Make(78, "Blade Runner", 117, "Blade Runner", directors: "Ridley Scott", mc: 0.95);
        var longRow = Make(78, "Blade Runner (The Final Cut)", 117, "Blade Runner", directors: "R. Scott", mc: 0.85);

        var survivor = Assert.Single(Run(shortRow, longRow).Films);

        Assert.Equal(0.85, survivor.Record.Mc);
        Assert.Equal(["R. Scott"], survivor.Record.Dir);
        Assert.Same(longRow.Row, survivor.Row);
    }

    [Fact]
    public void OwnIsTheDistinctValuesOrderedOrdinallyAndJoinedWithSlashes()
    {
        var (films, _) = Run(Cut("A film", own: "Zeta Bundle"), Cut("A film, longer", own: "Alpha Bundle"), Cut("A film!", own: "Zeta Bundle"));

        Assert.Equal("Alpha Bundle / Zeta Bundle", Assert.Single(films).Record.Own);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankOwnOnOneRowAddsNeitherALeadingNorADoubledSeparator(string? blank)
    {
        // the blank one is the LONGEST title and so the survivor, which is where a naive join would leave a leading " / "
        var (films, _) = Run(Cut("Blade Runner", own: "Bundle"), Cut("Blade Runner (The Final Cut)", own: blank));

        Assert.Equal("Bundle", Assert.Single(films).Record.Own);
    }

    [Fact]
    public void WhenNoRowHasAnOwnTheMergedOneIsNullNotEmpty()
    {
        var (films, _) = Run(Cut("Blade Runner", own: null), Cut("Blade Runner (The Final Cut)", own: "  "));

        Assert.Null(Assert.Single(films).Record.Own);
    }

    [Fact]
    public void TheRealBladeRunnerPairMergesToTheExactString()
    {
        var pair = RealCollision.All.Single(c => c.Id == 78).Films();

        var (films, _) = Run([.. pair]);

        var survivor = Assert.Single(films).Record;
        Assert.Equal("Blade Runner (The Final Cut)", survivor.T);
        Assert.Equal(
            "Blade Runner / Blade Runner: The Final Cut. Colección 2 películas / Warner Bros Firsts: Action & Adventure Bundle",
            survivor.Own);
        Assert.Equal("78-117", survivor.K);
    }

    [Fact]
    public void TheNoteNamesEveryTitleAndWhichOneWasKept()
    {
        var (films, notes) = Run(Cut("Blade Runner", own: "One"), Cut("Blade Runner (The Final Cut)", own: "Two"));

        Assert.Equal(
            ["Blade Runner (The Final Cut) (1:57:00, Some Director) → duplicate key 78-117: kept \"Blade Runner (The Final Cut)\", " +
             "dropped \"Blade Runner\"; purchase info merged into Own"],
            notes);
        Assert.Equal("Blade Runner (The Final Cut)", Assert.Single(films).Record.T);
    }

    [Fact]
    public void TheNoteDoesNotClaimAMergeWhenThereWasNothingToMerge()
    {
        var (_, notes) = Run(Cut("Blade Runner", own: "Same"), Cut("Blade Runner (The Final Cut)", own: "Same"));

        Assert.Equal(
            ["Blade Runner (The Final Cut) (1:57:00, Some Director) → duplicate key 78-117: kept \"Blade Runner (The Final Cut)\", " +
             "dropped \"Blade Runner\""],
            notes);
    }

    [Fact]
    public void ThreeRowsCollapseToOneAndTheNoteNamesBothDroppedTitles()
    {
        var (films, notes) = Run(Cut("Blade Runner"), Cut("Blade Runner (The Final Cut)"), Cut("Blade Runner (Theatrical)"));

        Assert.Equal("Blade Runner (The Final Cut)", Assert.Single(films).Record.T);
        Assert.Contains("dropped \"Blade Runner\", \"Blade Runner (Theatrical)\"", Assert.Single(notes), StringComparison.Ordinal);
    }

    // The rule reads only the rows' own titles, never where they sit.
    [Fact]
    public void TheSurvivorDoesNotDependOnTheOrderOfTheRows()
    {
        var rows = new[] { Cut("Blade Runner", own: "B"), Cut("Blade Runner (The Final Cut)", own: "A"), Cut("Blade Runner (Alt)", own: "C") };

        var forward = Assert.Single(Run(rows).Films).Record;
        var reversed = Assert.Single(Run([.. rows.Reverse()]).Films).Record;

        Assert.Equal(forward.T, reversed.T);
        Assert.Equal(forward.Own, reversed.Own);
        Assert.Equal("A / B / C", forward.Own);
    }

    [Fact]
    public void UntouchedRowsKeepTheirPlaceAndTheSurvivorKeepsItsOwn()
    {
        var before = Cut("The Martian", id: 286217, runtime: 142);
        var after = Cut("Tropic Thunder", id: 7446, runtime: 106);

        var (films, _) = Run(before, Cut("Blade Runner"), Cut("Blade Runner (The Final Cut)"), after);

        Assert.Equal(["The Martian", "Blade Runner (The Final Cut)", "Tropic Thunder"], films.Select(f => f.Record.T));
        Assert.Same(before, films[0]);
        Assert.Same(after, films[2]);
    }

    [Fact]
    public void EveryKeyIsDistinctAfterwards()
    {
        var (films, _) = Run(Cut("Blade Runner"), Cut("Blade Runner (The Final Cut)"), Cut("Other", id: 5, runtime: 90));

        Assert.Equal(films.Count, films.Select(f => f.Record.K).Distinct().Count());
    }

    [Fact]
    public void TwoUnmatchedRowsWithTheSameContentCollapseToo()
    {
        var (films, _) = Run(Make(0, "Obscure Film", 95, null), Make(0, "Obscure Film", 95, null));

        Assert.Single(films);
    }

    // Rule 2 reads the ids rule 1 left. A rejected row is unmatched, so its key no longer matches its sibling's and it
    // must NOT be swallowed by the collapse; run the other way round, the wrong row would silently win or lose.
    [Fact]
    public void ARowRejectedByRuleOneIsNotCollapsedIntoItsSibling()
    {
        const string tokyo = "The Fast and the Furious: Tokyo Drift";
        var real = Make(9615, tokyo, 104, tokyo);
        var impostor = Make(9615, "Fast & Furious", 104, tokyo);   // the same runtime: the same key before rule 1

        Assert.Equal(real.Record.K, impostor.Record.K);
        var result = CatalogueReconciler.Reconcile([real, impostor]);

        Assert.Equal(2, result.Films.Count);
        Assert.Contains(result.Films, f => f.Record.K == "9615-104" && f.Record.T == tokyo);
        Assert.Contains(result.Films, f => f.Record.I == 0 && f.Record.T == "Fast & Furious"
                                           && f.Record.K.StartsWith("u-", StringComparison.Ordinal));
    }
}

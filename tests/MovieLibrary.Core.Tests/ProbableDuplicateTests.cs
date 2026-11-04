using MovieLibrary.Enrich.Pipeline;
using static MovieLibrary.Core.Tests.ResolvedFilms;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// Rule 3: one TMDB id held by rows with byte-identical sheet titles at different runtimes is probably one film listed
/// twice. It is a detection rule: the note is the whole output, and only the owner knows whether he owns one copy or two.
/// </summary>
public class ProbableDuplicateTests
{
    private const string Captain = "Captain America: The First Avenger";

    private static List<string> Run(params ResolvedFilm[] films)
    {
        var notes = new List<string>();
        CatalogueReconciler.NoteProbableDuplicates(films, notes);
        return notes;
    }

    private static ResolvedFilm Film(string title, int runtime, int id = 1771) => Make(id, title, runtime, Captain);

    [Fact]
    public void TheRealCaptainAmericaPairIsNotedOnce()
    {
        var notes = Run([.. RealCollision.All.Single(c => c.Id == 1771).Films()]);

        Assert.Equal([$"{Captain} (1771) → same title and id at 124, 125 min; probably one film listed twice"], notes);
    }

    [Fact]
    public void ItMutatesNothing()
    {
        var films = new[] { Film(Captain, 125), Film(Captain, 124) };
        var snapshot = films.ToArray();
        var recordsBefore = films.Select(f => f.Record).ToArray();

        var notes = new List<string>();
        CatalogueReconciler.NoteProbableDuplicates(films, notes);

        Assert.Single(notes);
        Assert.Equal(snapshot, films);   // the same rows, in the same places
        Assert.All(films.Zip(recordsBefore), p => Assert.Same(p.Second, p.First.Record));
        Assert.Equal(["1771-124", "1771-125"], films.Select(f => f.Record.K).Order());   // both keep their own key
    }

    // TitleNormalizer folds an edition suffix away, so a folded comparison would flag every edition pair. The raw title does not.
    [Fact]
    public void EditionPairsWhoseRawTitlesDifferAreNotNoted()
    {
        Assert.Empty(Run(Film("American Pie 2", 108, id: 4), Film("American Pie 2 (Unrated)", 110, id: 4)));
    }

    [Fact]
    public void NoneOfTheOtherFifteenRealPairsIsNoted()
    {
        foreach (var c in RealCollision.All.Where(c => c.Id != 1771))
            Assert.Empty(Run([.. c.Films()]));
    }

    [Fact]
    public void ARepeatedTitleOnDifferentIdsIsNotNoted()
        => Assert.Empty(Run(Film("Carrie", 98, id: 10), Film("Carrie", 99, id: 11)));

    [Fact]
    public void TheTitleComparisonIsOrdinalNotFolded()
        => Assert.Empty(Run(Film("Captain America", 124), Film("captain america", 125)));

    [Fact]
    public void TwoRowsWithTheSameKeyAreNotNotedBecauseTheirKeysDoNotDiffer()
        => Assert.Empty(Run(Film(Captain, 124), Film(Captain, 124)));

    [Fact]
    public void UnmatchedRowsAreNeverGroupedByTheirSharedZeroId()
        => Assert.Empty(Run(Make(0, "Obscure Film", 95, null), Make(0, "Obscure Film", 96, null)));

    [Fact]
    public void ThreeRowsAtThreeRuntimesAreNotedOnceWithAllThree()
    {
        var notes = Run(Film(Captain, 126), Film(Captain, 124), Film(Captain, 125));

        Assert.Equal([$"{Captain} (1771) → same title and id at 124, 125, 126 min; probably one film listed twice"], notes);
    }

    [Fact]
    public void EachRepeatedTitleGetsItsOwnNoteOrderedByIdThenTitle()
    {
        var notes = Run(Film("Zed", 90), Film("Zed", 91), Film("Alpha", 100), Film("Alpha", 101), Film("Zed", 92, id: 2), Film("Zed", 93, id: 2));

        Assert.Equal(
            ["Zed (2) → same title and id at 92, 93 min; probably one film listed twice",           // by id...
             "Alpha (1771) → same title and id at 100, 101 min; probably one film listed twice",    // ...then by title
             "Zed (1771) → same title and id at 90, 91 min; probably one film listed twice"],
            notes);
    }

    [Fact]
    public void AfterTheCollapseAnExactDuplicateIsOneRowAndSoNeverNoted()
    {
        var result = CatalogueReconciler.Reconcile([Film(Captain, 124), Film(Captain, 124)]);

        Assert.Single(result.Films);
        Assert.DoesNotContain(result.Notes, n => n.Contains("probably one film listed twice", StringComparison.Ordinal));
    }

    [Fact]
    public void OverTheWholeRealCatalogueItFiresExactlyOnce()
    {
        var all = RealCollision.All.SelectMany(c => c.Films()).ToList();

        var result = CatalogueReconciler.Reconcile(all);

        Assert.Single(result.Notes, n => n.Contains("probably one film listed twice", StringComparison.Ordinal));
    }
}

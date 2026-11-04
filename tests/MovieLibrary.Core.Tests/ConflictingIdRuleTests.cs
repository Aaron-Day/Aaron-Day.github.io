using MovieLibrary.Enrich.Pipeline;
using static MovieLibrary.Core.Tests.ResolvedFilms;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// Rule 1: when two rows resolve to one TMDB id, a row that does not agree on title with that film is the wrong
/// one and is rejected, but only when some other row DOES agree. All agreeing and none agreeing change nothing.
/// </summary>
public class ConflictingIdRuleTests
{
    private const string TokyoDrift = "The Fast and the Furious: Tokyo Drift";

    private static (IReadOnlyList<ResolvedFilm> Films, List<string> Notes) Run(params ResolvedFilm[] films)
    {
        var notes = new List<string>();
        return (CatalogueReconciler.RejectConflictingIds(films, notes), notes);
    }

    private static ResolvedFilm Impostor(string? note = null) => Make(9615, "Fast & Furious", 106, TokyoDrift, note: note);
    private static ResolvedFilm RealFilm() => Make(9615, TokyoDrift, 104, TokyoDrift);

    [Fact]
    public void ALoneRowIsLeftAlone()
    {
        var only = Make(9615, "Fast & Furious", 106, TokyoDrift);   // disagrees, but there is nobody to disagree WITH

        var (films, notes) = Run(only);

        Assert.Same(only, Assert.Single(films));
        Assert.Empty(notes);
    }

    [Fact]
    public void UnmatchedRowsAreNeverAGroup()
    {
        var a = Make(0, "Obscure One", 95, null);
        var b = Make(0, "Obscure Two", 96, null);

        var (films, notes) = Run(a, b);

        Assert.Equal([a, b], films);
        Assert.Empty(notes);
    }

    [Fact]
    public void TheRowThatDisagreesIsRejectedWhenAnotherAgrees()
    {
        var impostor = Impostor();
        var real = RealFilm();

        var (films, notes) = Run(impostor, real);

        Assert.Equal(2, films.Count);
        Assert.Same(real, films[1]);                       // the film that belongs keeps everything
        Assert.Equal(0, films[0].Record.I);                // the other is unmatched
        Assert.Equal(0d, films[0].Record.Mc);
        Assert.StartsWith("u-", films[0].Record.K, StringComparison.Ordinal);
        Assert.Single(notes);                              // the rejection's line, and no group note beside it
    }

    // The point of the rule: it is not enough to null the id. Fast & Furious published y 2006 (Tokyo Drift's year).
    [Fact]
    public void ARejectedRowKeepsNothingTheWrongFilmGaveIt()
    {
        var (films, _) = Run(Impostor(), RealFilm());
        var r = films[0].Record;

        Assert.Null(r.P);
        Assert.Null(r.B);
        Assert.Empty(r.G);
        Assert.Equal(0d, r.Vote);
        Assert.Null(r.Ov);
        Assert.Null(r.Tag);
        Assert.Empty(r.Cast);
        Assert.Null(r.Col);
        Assert.Null(films[0].TmdbTitle);
        Assert.Null(r.Y);   // the sheet title has no year hint, and Tokyo Drift's 2006 must not survive
    }

    [Fact]
    public void ARejectedRowKeepsEverythingTheSheetGaveIt()
    {
        var impostor = Make(9615, "Fast & Furious", 106, TokyoDrift, own: "Fast & Furious Bundle", directors: "Justin Lin");

        var (films, _) = Run(impostor, RealFilm());
        var r = films[0].Record;

        Assert.Equal("Fast & Furious", r.T);
        Assert.Equal(106, r.Rt);
        Assert.Equal(["Justin Lin"], r.Dir);
        Assert.Equal("Fast & Furious Bundle", r.Own);
        Assert.Equal("Drama", r.Ag);
    }

    // BuildRecordAsync falls back to the year the sheet title hints at when TMDB has none, so that is what an unmatched film shows.
    [Fact]
    public void ARejectedRowFallsBackToTheYearItsSheetTitleHints()
    {
        var impostor = Make(9615, "Fast & Furious (2009)", 106, TokyoDrift);

        var (films, _) = Run(impostor, RealFilm());

        Assert.Equal(2009, films[0].Record.Y);
    }

    [Fact]
    public void TheRejectionNoteNamesTheIdAndTheFilmAndReplacesTheStaleFlaggedOne()
    {
        var (films, notes) = Run(Impostor(note: "Flagged at 0.70, id 9615"), RealFilm());

        Assert.Equal(
            ["Fast & Furious (1:46:00, Some Director) → rejected TMDB id 9615 (\"The Fast and the Furious: Tokyo Drift\"): " +
             "its title does not match that film and another row's does, so it is now unmatched"],
            notes);
        Assert.Null(films[0].ReviewLine);   // the row's old "Flagged at 0.70" line must not be printed beside it
    }

    [Fact]
    public void WhenEveryRowAgreesNothingChangesAndTheGroupGetsOneNote()
    {
        var a = Make(120, "The Lord of the Rings: The Fellowship of the Ring", 178, "The Lord of the Rings: The Fellowship of the Ring");
        var b = Make(120, "The Lord of the Rings: The Fellowship of the Ring (Extended Edition)", 228, "The Lord of the Rings: The Fellowship of the Ring");

        var (films, notes) = Run(a, b);

        Assert.Equal([a, b], films);
        Assert.Equal(
            ["The Lord of the Rings: The Fellowship of the Ring (120) → 2 rows share this TMDB id and all agree on its title " +
             "(178, 228 min); none rejected"],
            notes);
    }

    [Fact]
    public void WhenNoRowAgreesNothingChangesAndTheGroupGetsOneNote()
    {
        var a = Make(9615, "Fast & Furious", 106, TokyoDrift);
        var b = Make(9615, "Furious Fast", 104, TokyoDrift);

        var (films, notes) = Run(a, b);

        Assert.Equal([a, b], films);
        Assert.Equal(
            [$"{TokyoDrift} (9615) → 2 rows share this TMDB id and none agrees on its title (104, 106 min); " +
             "nothing says which is right, so none rejected"],
            notes);
    }

    [Fact]
    public void EveryRowThatDisagreesIsRejectedWhenOneAgrees()
    {
        var (films, _) = Run(Impostor(), Make(9615, "Another Furious", 105, TokyoDrift), RealFilm());

        Assert.Equal([0, 0, 9615], films.Select(f => f.Record.I));
    }

    [Fact]
    public void GroupsAreJudgedIndependently()
    {
        var logan = Make(263115, "Logan", 137, "Logan");
        var wolverine = Make(263115, "The Wolverine", 138, "Logan");
        var fellowship = Make(120, "Fellowship", 178, "Fellowship");
        var extended = Make(120, "Fellowship (Extended Edition)", 228, "Fellowship");

        var (films, notes) = Run(logan, wolverine, fellowship, extended);

        Assert.Equal([263115, 0, 120, 120], films.Select(f => f.Record.I));
        Assert.Equal(2, notes.Count);   // groups in ascending id order: Fellowship (120) all agree, then Logan (263115) rejected
        Assert.Contains("all agree", notes[0], StringComparison.Ordinal);
        Assert.Contains("rejected TMDB id 263115", notes[1], StringComparison.Ordinal);
    }

    // An Overrides row is the owner's decision at confidence 1.0, and exists precisely where the TMDB title cannot fold
    // to the sheet's (Seven is Se7en). No heuristic may undo it, or the owner could never correct a row.
    [Fact]
    public void AnOverrideRowIsNeverRejected()
    {
        var forced = Make(11, "Star Wars: A New Hope", 125, "Star Wars", fromOverride: true, mc: 1d);
        var matched = Make(11, "Star Wars", 121, "Star Wars");

        var (films, notes) = Run(forced, matched);

        Assert.Equal([forced, matched], films);
        Assert.Equal(
            ["Star Wars (11) → 2 rows share this TMDB id; Overrides rows are never rejected, so kept despite a title " +
             "that does not agree: \"Star Wars: A New Hope\""],
            notes);
    }

    [Fact]
    public void ARowThatDisagreesIsStillRejectedBesideAnOverrideRow()
    {
        var forced = Make(11, "Star Wars: A New Hope", 125, "Star Wars", fromOverride: true, mc: 1d);
        var matched = Make(11, "Star Wars", 121, "Star Wars");
        var wrong = Make(11, "Star Trek", 122, "Star Wars");

        var (films, _) = Run(forced, matched, wrong);

        Assert.Equal([11, 11, 0], films.Select(f => f.Record.I));
    }

    // A budget-stopped run can leave a matched film whose payload never loaded. One row's title stands for the group,
    // because every row in it holds the same film.
    [Fact]
    public void TheGroupsTmdbTitleIsTakenFromWhicheverRowHasIt()
    {
        var real = Make(9615, TokyoDrift, 104, null);          // payload missing
        var impostor = Make(9615, "Fast & Furious", 106, TokyoDrift);

        var (films, _) = Run(real, impostor);

        Assert.Equal([9615, 0], films.Select(f => f.Record.I));
    }

    [Fact]
    public void WhenNoRowKnowsTheTitleNothingCanBeCheckedAndNothingIsRejected()
    {
        var a = Make(9615, "Fast & Furious", 106, null);
        var b = Make(9615, TokyoDrift, 104, null);

        var (films, notes) = Run(a, b);

        Assert.Equal([a, b], films);
        Assert.Equal(
            ["TMDB id 9615 → 2 rows share this id, but its title is unavailable so they could not be checked (104, 106 min)"],
            notes);
    }

    // ---- the measured population ----------------------------------------------------------------------

    [Fact]
    public void ThereAreSixteenCollidingGroupsAndThreeOfThemSplit()
    {
        Assert.Equal(16, RealCollision.All.Count);
        Assert.Equal(3, RealCollision.All.Count(c => c.IsSplit));
    }

    [Theory, MemberData(nameof(Collisions))]
    public void TheRuleSeparatesTheRealCollisionsExactly(int id)
    {
        var c = RealCollision.All.Single(x => x.Id == id);
        var notes = new List<string>();
        var output = CatalogueReconciler.RejectConflictingIds(c.Films(), notes);

        var rejected = output.Where(f => f.Record.I == 0).Select(f => f.Record.T).ToList();
        var expected = c.Rows.Where(r => r.Impostor).Select(r => r.SheetTitle).ToList();
        Assert.Equal(expected, rejected);                 // only the impostors, and all of them
        Assert.Single(notes);                             // a clean pair is noted once; a split by its rejection, once
        Assert.Contains(c.IsSplit ? "rejected" : "all agree", notes[0], StringComparison.Ordinal);
    }

    [Fact]
    public void AcrossTheWholeCatalogueExactlyThreeRowsFromThreeGroupsAreRejected()
    {
        var all = RealCollision.All.SelectMany(c => c.Films()).ToList();

        var output = CatalogueReconciler.RejectConflictingIds(all, []);

        Assert.Equal(["Fast & Furious", "Ip Man", "The Wolverine"], output.Where(f => f.Record.I == 0).Select(f => f.Record.T).Order());
    }

    public static TheoryData<int> Collisions() => [.. RealCollision.All.Select(c => c.Id)];
}

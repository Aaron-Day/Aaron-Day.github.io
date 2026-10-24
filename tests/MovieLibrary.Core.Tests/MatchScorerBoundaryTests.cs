using MovieLibrary.Core.Matching;
using MovieLibrary.Core.Text;

namespace MovieLibrary.Core.Tests;

// Pins the weights (R 0.40, D 0.30, T 0.15, Y 0.10), thresholds, runtime tolerance and
// candidate ordering. The query is always "Same Title (1999)", 100 min, "Jane Doe", and
// each candidate matches exactly the signals asked for.
// Reachable scores: 0, .10, .15, .25, .30, .40, .45, .50, .55, .65, .70, .80, .85, .95.
// There is no combination that sums to exactly 0.75, so the Accept boundary is pinned by
// 0.70 (RD) and 0.80 (RDY) plus the constants test.
public class MatchScorerBoundaryTests
{
    private static MovieCandidate Cand(int id, bool runtime = false, bool director = false,
                                       bool title = false, bool year = false, double pop = 1.0)
        => new(id, title ? "Same Title" : "Other Words", year ? 1999 : 2001,
               runtime ? 100 : 150, [director ? "Jane Doe" : "John Smith"], pop);

    private static MatchResult Score(params MovieCandidate[] candidates)
        => MatchScorer.Score(TitleNormalizer.Normalize("Same Title (1999)"), 100, "Jane Doe", candidates);

    [Theory]
    [InlineData(true,  false, false, false, 0.40)]
    [InlineData(false, true,  false, false, 0.30)]
    [InlineData(false, false, true,  false, 0.15)]
    [InlineData(false, false, false, true,  0.10)]
    [InlineData(false, true,  true,  false, 0.45)]   // 0.30 + 0.15 is 0.44999999999999996 unrounded
    [InlineData(true,  true,  false, true,  0.80)]   // sums to 0.7999999999999999 unrounded
    [InlineData(true,  true,  true,  true,  0.95)]   // the algorithmic maximum; 1.0 is reserved for overrides
    public void EachSignalContributesExactlyItsWeight(
        bool runtime, bool director, bool title, bool year, double expected)
    {
        var r = Score(Cand(1, runtime, director, title, year));
        Assert.Equal(expected, r.Confidence);
    }

    [Theory]
    [InlineData(true,  false, false, false, MatchOutcome.Unmatched)]  // 0.40, a tick under Flag
    [InlineData(false, true,  true,  false, MatchOutcome.Flagged)]    // 0.45, exactly Flag
    [InlineData(true,  false, false, true,  MatchOutcome.Flagged)]    // 0.50, a tick over Flag
    [InlineData(true,  true,  false, false, MatchOutcome.Flagged)]    // 0.70, a tick under Accept
    [InlineData(true,  true,  false, true,  MatchOutcome.Accepted)]   // 0.80, a tick over Accept
    public void OutcomesSwitchAtTheThresholds(
        bool runtime, bool director, bool title, bool year, MatchOutcome expected)
    {
        var r = Score(Cand(1, runtime, director, title, year));
        Assert.Equal(expected, r.Outcome);
        Assert.Equal(expected == MatchOutcome.Unmatched ? null : 1, r.TmdbId);
    }

    [Fact]
    public void ThresholdsAreThePublishedValues()
    {
        Assert.Equal(0.75, MatchScorer.AcceptThreshold);
        Assert.Equal(0.45, MatchScorer.FlagThreshold);
    }

    [Theory]
    [InlineData(100, 0.40)]
    [InlineData(98,  0.40)]
    [InlineData(102, 0.40)]
    [InlineData(97,  0.0)]
    [InlineData(103, 0.0)]
    public void RuntimeToleranceIsExactlyTwoMinutesEitherWay(int candidateRuntime, double expected)
    {
        var r = Score(new MovieCandidate(1, "Other Words", 2001, candidateRuntime, ["John Smith"], 1.0));
        Assert.Equal(expected, r.Confidence);
    }

    [Fact]
    public void YearHintPicksTheRightFilmEvenWhenItHasTheHigherTmdbId()
    {
        var r = MatchScorer.Score(TitleNormalizer.Normalize("The Accountant (2016)"), 128, "Gavin O'Connor",
        [
            new MovieCandidate(100, "The Accountant", 2024, 128, ["Gavin O'Connor"], 1.0),
            new MovieCandidate(200, "The Accountant", 2016, 128, ["Gavin O'Connor"], 1.0),
        ]);
        Assert.Equal(200, r.TmdbId);
        Assert.Equal(0.95, r.Confidence);
    }

    [Fact]
    public void WithoutAYearHintTheYearEarnsNothing()
    {
        var r = MatchScorer.Score(TitleNormalizer.Normalize("Same Title"), 100, "Jane Doe",
            [new MovieCandidate(1, "Same Title", 1999, 100, ["Jane Doe"], 1.0)]);
        Assert.Equal(0.85, r.Confidence);
    }

    [Theory]
    [InlineData("David Zucker, Jerry Zucker & Jim Abrahams", "Jim Abrahams", 0.30)]
    [InlineData("David Zucker, Jerry Zucker & Jim Abrahams", "Jerry Zucker", 0.30)]
    [InlineData("David Zucker, Jerry Zucker & Jim Abrahams", "David Zucker", 0.30)]
    [InlineData("Jerry Zucker", "David Zucker", 0.0)]      // shares a surname only
    [InlineData("Jerry Zucker", "Jerry Zucker Jr", 0.0)]   // an extra token is not a subset
    [InlineData("Jerry Zucker", "", 0.0)]                  // a blank TMDB director never matches
    public void DirectorMatchNeedsAllOfTheTmdbNamesTokensInTheRow(string row, string tmdbDirector, double expected)
    {
        var r = MatchScorer.Score(TitleNormalizer.Normalize("Same Title"), 100, row,
            [new MovieCandidate(1, "Other Words", 2001, 150, [tmdbDirector], 1.0)]);
        Assert.Equal(expected, r.Confidence);
    }

    [Fact]
    public void AnyOneOfSeveralTmdbDirectorsMayMatch()
    {
        var r = MatchScorer.Score(TitleNormalizer.Normalize("Same Title"), 100,
            "David Zucker, Jerry Zucker & Jim Abrahams",
            [new MovieCandidate(1, "Other Words", 2001, 150, ["Someone Else", "Jim Abrahams"], 1.0)]);
        Assert.Equal(0.30, r.Confidence);
    }

    [Fact]
    public void FullTieGoesToTheLowestTmdbId()
    {
        var r = Score(Cand(20, true, true, true), Cand(10, true, true, true));
        Assert.Equal(10, r.TmdbId);
    }

    [Fact]
    public void PopularityOrdersEqualScoresAheadOfTmdbIdWithoutChangingTheOutcome()
    {
        var alone = Score(Cand(1, true, true, pop: 5));
        var withRival = Score(Cand(1, true, true, pop: 5), Cand(2, true, true, pop: 500));

        Assert.Equal(2, withRival.TmdbId);
        Assert.Equal(alone.Outcome, withRival.Outcome);
        Assert.Equal(alone.Confidence, withRival.Confidence);
    }

    // runtime + title and director + title + year are both 0.55 in decimal, but as doubles they are
    // 0.55 and 0.5499999999999999. Unless ScoreOne rounds, the first always outranks the second and
    // the popularity / TmdbId tie-breaks never run.
    [Theory]
    [InlineData(1,   500, 1, 2, 2)]   // popularity decides in favour of the float-smaller sum
    [InlineData(500, 1,   1, 2, 1)]   // and in favour of the float-larger one
    [InlineData(1,   1,   2, 1, 1)]   // equal popularity: lowest TmdbId, which is the float-smaller sum
    [InlineData(1,   1,   1, 2, 1)]   // equal popularity: lowest TmdbId, which is the float-larger sum
    public void MathematicallyEqualScoresTieAndPopularityThenDecides(
        double runtimeTitlePop, double directorTitleYearPop,
        int runtimeTitleId, int directorTitleYearId, int expectedId)
    {
        var r = Score(
            Cand(runtimeTitleId, runtime: true, title: true, pop: runtimeTitlePop),
            Cand(directorTitleYearId, director: true, title: true, year: true, pop: directorTitleYearPop));
        Assert.Equal(0.55, r.Confidence);
        Assert.Equal(expectedId, r.TmdbId);
    }

    [Fact]
    public void PopularityNeverOutweighsAHigherScore()
    {
        var r = Score(Cand(1, true, true, pop: 1), Cand(2, true, pop: 1000));
        Assert.Equal(1, r.TmdbId);
    }

    [Theory]
    [InlineData(0,    0)]
    [InlineData(1000, 0)]
    [InlineData(0,    1000)]
    [InlineData(1000, 5000)]
    public void PopularityNeverChangesAnOutcome(double pop, double rivalPop)
    {
        var runtimeOnly = Score(Cand(1, runtime: true, pop: pop), Cand(2, pop: rivalPop));
        Assert.Equal(MatchOutcome.Unmatched, runtimeOnly.Outcome);
        Assert.Equal(0.40, runtimeOnly.Confidence);

        var directorAndTitle = Score(Cand(1, director: true, title: true, pop: pop), Cand(2, pop: rivalPop));
        Assert.Equal(MatchOutcome.Flagged, directorAndTitle.Outcome);
        Assert.Equal(1, directorAndTitle.TmdbId);
        Assert.Equal(0.45, directorAndTitle.Confidence);
    }
}

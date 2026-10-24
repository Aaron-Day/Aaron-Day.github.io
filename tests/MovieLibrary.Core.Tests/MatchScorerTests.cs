using MovieLibrary.Core.Matching;
using MovieLibrary.Core.Text;

namespace MovieLibrary.Core.Tests;

public class MatchScorerTests
{
    private static MovieCandidate C(int id, string title, int year, int runtime, string director,
                                    double pop = 1.0)
        => new(id, title, year, runtime, [director], pop);

    private static MatchResult Score(string title, int runtime, string directors,
                                     params MovieCandidate[] candidates)
        => MatchScorer.Score(TitleNormalizer.Normalize(title), runtime, directors, candidates);

    [Fact]
    public void PicksTheRightHalloweenByRuntimeAndDirector()
    {
        var r = Score("Halloween", 91, "John Carpenter",
            C(948,    "Halloween", 1978, 91,  "John Carpenter"),
            C(1626,   "Halloween", 2007, 109, "Rob Zombie"),
            C(474350, "Halloween", 2018, 106, "David Gordon Green"));

        Assert.Equal(948, r.TmdbId);
        Assert.Equal(MatchOutcome.Accepted, r.Outcome);
        Assert.True(r.Confidence >= 0.75);
    }

    [Fact]
    public void RuntimeWithinTwoMinutesStillCounts()
    {
        var r = Score("Alien", 117, "Ridley Scott", C(348, "Alien", 1979, 116, "Ridley Scott"));
        Assert.Equal(348, r.TmdbId);
        Assert.Equal(MatchOutcome.Accepted, r.Outcome);
    }

    [Fact]
    public void YearHintBreaksATieBetweenIdenticalCandidates()
    {
        var r = Score("The Accountant (2016)", 128, "Gavin O'Connor",
            C(302946, "The Accountant", 2016, 128, "Gavin O'Connor"),
            C(999999, "The Accountant", 2024, 128, "Gavin O'Connor"));
        Assert.Equal(302946, r.TmdbId);
    }

    [Fact]
    public void WeakAgreementIsFlaggedNotAccepted()
    {
        // Title matches, nothing else does.
        var r = Score("Dune", 137, "Denis Villeneuve", C(841, "Dune", 1984, 137, "David Lynch"));
        Assert.Equal(MatchOutcome.Flagged, r.Outcome);
        Assert.InRange(r.Confidence, 0.45, 0.7499);
    }

    [Fact]
    public void NothingPlausibleIsUnmatched()
    {
        var r = Score("Some Obscure Film", 95, "Nobody At All",
            C(1, "Entirely Different", 1950, 200, "Someone Else"));
        Assert.Null(r.TmdbId);
        Assert.Equal(MatchOutcome.Unmatched, r.Outcome);
    }

    [Fact]
    public void EmptyCandidateListIsUnmatched()
    {
        var r = Score("Anything", 100, "Someone");
        Assert.Null(r.TmdbId);
        Assert.Equal(MatchOutcome.Unmatched, r.Outcome);
        Assert.Equal(0d, r.Confidence);
    }
}

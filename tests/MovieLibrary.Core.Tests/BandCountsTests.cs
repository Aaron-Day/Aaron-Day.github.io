using MovieLibrary.Core.Matching;
using MovieLibrary.Core.Models;
using MovieLibrary.Enrich.Reporting;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// The band tally the job summary prints. <see cref="ConfidenceBandTests"/> owns the rule; this owns the
/// counting, which is where a film can go missing or be counted twice.
/// </summary>
public class BandCountsTests
{
    private static MovieRecord Record(int id, double score) => new(
        I: id, K: id == 0 ? "u-00000000" : $"{id}-90", T: "A Film", Y: 1999, Rt: 90,
        P: null, B: null, G: [], Ag: "Drama", Vote: 0d, Dir: [], Ov: null, Tag: null,
        Cast: [], Col: null, Own: null, Mc: score);

    [Fact]
    public void EachFilmLandsInExactlyOneBand()
    {
        var counts = BandCounts.Of([
            Record(807, 0.95), Record(808, 0.75),                 // confident, the second exactly on the threshold
            Record(809, 0.74), Record(810, 0.10), Record(811, 0),  // uncertain
            Record(0, 0), Record(0, 0.99),                        // unmatched, whatever a candidate scored
        ]);

        Assert.Equal(new BandCounts(Confident: 2, Uncertain: 3, Unmatched: 2), counts);
    }

    /// <remarks>
    /// The property that matters when a band is added later: the tally has to account for every film, so a new
    /// Confidence value cannot silently vanish from the owner's page. The default arm counts it as unmatched,
    /// which is the loud end, and this is what makes that visible rather than lost.
    /// </remarks>
    [Fact]
    public void NoFilmIsDroppedAndNoneIsCountedTwice()
    {
        var films = Enumerable.Range(1, 40)
            .Select(i => Record(i % 7 == 0 ? 0 : i, i / 50d))
            .ToList();

        var counts = BandCounts.Of(films);

        Assert.Equal(films.Count, counts.Total);
        Assert.Equal(films.Count(f => ConfidenceBand.Of(f) == Confidence.Confident), counts.Confident);
        Assert.Equal(films.Count(f => ConfidenceBand.Of(f) == Confidence.Uncertain), counts.Uncertain);
        Assert.Equal(films.Count(f => ConfidenceBand.Of(f) == Confidence.Unmatched), counts.Unmatched);
    }

    [Fact]
    public void AnEmptyRunCountsNothingRatherThanThrowing()
        => Assert.Equal(new BandCounts(0, 0, 0), BandCounts.Of([]));

    /// <remarks>
    /// The tally reads <see cref="ConfidenceBand.Of"/> and never its own copy of the accept threshold. A second
    /// copy would let this page call a film confident that the site badges "Needs review", and nobody would
    /// catch it: the two are read months apart by the same person, who would believe the more recent one.
    /// </remarks>
    [Fact]
    public void TheTallyMovesWithTheSharedThresholdRatherThanACopyOfIt()
    {
        var onTheThreshold = Record(807, MatchScorer.AcceptThreshold);
        var justUnder = Record(808, MatchScorer.AcceptThreshold - 0.000001);

        Assert.Equal(new BandCounts(Confident: 1, Uncertain: 1, Unmatched: 0),
                     BandCounts.Of([onTheThreshold, justUnder]));
    }
}

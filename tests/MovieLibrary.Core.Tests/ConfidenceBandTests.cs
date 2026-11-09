using MovieLibrary.Core.Matching;
using MovieLibrary.Core.Models;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// The band rule, table-driven against records written here.
/// <para>
/// The plan asked for these to be measured against the REAL catalogue -- 1,456 records, 1,265 confident, 171
/// uncertain, 20 unmatched. Those numbers are real, and they are deliberately not asserted anywhere. The
/// catalogue is gitignored (<c>src/MovieLibrary.Web/wwwroot/data/</c> and <c>dev-data/</c> both), so a test
/// pinning them would pass on the author's machine, find no file in CI, and then either fail there or -- far
/// worse -- pass over nothing. Rule T3 rules out the obvious escape: there is no runtime skip on xunit 2.9.3.
/// And they are a property of the owner's SHEET rather than of this code: he has 17 override rows to paste and 7
/// rows to move between tabs, each of which moves the figures, so a test pinning 1,265 would go red for a reason
/// that is not a regression. The counts are reported from <c>JobSummary</c> instead, where the catalogue
/// actually exists. What lives here is the logic, which needs no catalogue at all.
/// </para>
/// </summary>
public class ConfidenceBandTests
{
    /// <summary>A record with only the two fields the band rule reads; everything else is inert filler.</summary>
    private static MovieRecord Record(int id, double score) => new(
        I: id, K: id == 0 ? "u-00000000" : $"{id}-90", T: "A Film", Y: 1999, Rt: 90,
        P: null, B: null, G: [], Ag: "Drama", Vote: 0d, Dir: [], Ov: null, Tag: null,
        Cast: [], Col: null, Own: null, Mc: score);

    [Theory]
    [InlineData(0, 0.00, Confidence.Unmatched)]   // the unmatched rows; Mc is 0 even where a candidate scored
    [InlineData(0, 0.40, Confidence.Unmatched)]   // I == 0 wins over any score
    [InlineData(0, 0.95, Confidence.Unmatched)]   // and over a score that would otherwise be Confident
    [InlineData(807, 0.95, Confidence.Confident)]
    [InlineData(807, 0.75, Confidence.Confident)] // exactly on the accept threshold
    [InlineData(807, 0.74, Confidence.Uncertain)]
    [InlineData(807, 0.55, Confidence.Uncertain)] // a peeled-edition film: right id, low score
    [InlineData(807, 0.00, Confidence.Uncertain)] // matched with a zero score is still not Unmatched
    public void BandsSplitOnTheIdFirstAndTheScoreSecond(int id, double mc, Confidence expected)
        => Assert.Equal(expected, ConfidenceBand.Of(Record(id, mc)));

    /// <remarks>
    /// The coupling IS the subject here, not the number. The two cases above that spell 0.75 and 0.74 go green
    /// against a hardcoded literal too; this one does not, because it never names a threshold. Move
    /// <see cref="MatchScorer.AcceptThreshold"/> and a <c>ConfidenceBand</c> holding a literal fails here while
    /// everything else stays green -- which is the drift worth catching, since in production it would surface as
    /// records badged wrongly rather than as anything red.
    /// <c>MatchScorerBoundaryTests.ThresholdsAreThePublishedValues</c> pins the constant's own value.
    /// </remarks>
    [Fact]
    public void TheBandFlipsAtTheScorersOwnAcceptThresholdAndNotAtANumberOfItsOwn()
    {
        Assert.Equal(Confidence.Confident, ConfidenceBand.Of(Record(807, MatchScorer.AcceptThreshold)));
        Assert.Equal(Confidence.Uncertain, ConfidenceBand.Of(Record(807, MatchScorer.AcceptThreshold - 0.01)));
    }

    /// <remarks>
    /// m2. An unset band must not read as the trusted, unbadged one. Nothing orders these by value, so the
    /// declaration order is free to serve this instead.
    /// </remarks>
    [Fact]
    public void TheDefaultBandIsTheLoudValueSoAnUnsetOneCannotPassForAGoodMatch()
    {
        Assert.Equal(Confidence.Unmatched, default(Confidence));
        Assert.NotEqual(Confidence.Confident, default(Confidence));
    }

    [Theory]
    [InlineData(Confidence.Unmatched, "Not matched")]
    [InlineData(Confidence.Uncertain, "Needs review")]
    public void AnExceptionalBandCarriesItsOwnWords(Confidence band, string badge)
        => Assert.Equal(badge, ConfidenceBand.BadgeFor(band));

    /// <remarks>
    /// Deliberate, and the reason is worth a test rather than a comment: the absence of an exceptional marker is
    /// not information conveyed by colour, and a badge on 1,265 of 1,456 records is noise that trains the eye to
    /// ignore the 191 that matter.
    /// </remarks>
    [Fact]
    public void TheOrdinaryCaseCarriesNoBadgeAtAll()
        => Assert.Null(ConfidenceBand.BadgeFor(Confidence.Confident));

    /// <remarks>
    /// m4. "Uncertain match" was jargon from a pipeline the reader does not know exists -- uncertain match to
    /// what? -- and the badge now says what <c>JobSummary</c>'s own heading says, so the build summary and the
    /// thing on screen cannot describe one film in two vocabularies.
    /// </remarks>
    [Fact]
    public void TheMiddleBandUsesTheOwnersWordsAndNotThePipelines()
    {
        var badge = ConfidenceBand.BadgeFor(Confidence.Uncertain);

        Assert.DoesNotContain("ncertain", badge);
        Assert.DoesNotContain("onfiden", badge);
    }
}

using MovieLibrary.Core.Text;

namespace MovieLibrary.Core.Matching;

public sealed record MovieCandidate(
    int TmdbId, string Title, int? Year, int? RuntimeMinutes,
    IReadOnlyList<string> Directors, double Popularity);

public enum MatchOutcome { Accepted, Flagged, Unmatched }

public sealed record MatchResult(int? TmdbId, double Confidence, MatchOutcome Outcome);

public static class MatchScorer
{
    // Weights sum to 0.95, not 1.00: popularity is deliberately NOT part of the score.
    // It orders candidates only, so identical evidence always yields the same outcome.
    // A score of exactly 1.0 therefore means a human override from the Overrides tab.
    private const double RuntimeWeight = 0.40;
    private const double DirectorWeight = 0.30;
    private const double TitleWeight = 0.15;
    private const double YearWeight = 0.10;

    /// <summary>Runtime counts as a match within this many minutes either way.</summary>
    private const int RuntimeToleranceMinutes = 2;

    // Every weight and threshold must stay a multiple of 0.0001, or Math.Round(x, 4) below
    // starts truncating (0.03125 would silently become 0.0312) and the boundary tests lie.
    public const double AcceptThreshold = 0.75;
    public const double FlagThreshold = 0.45;

    public static MatchResult Score(
        NormalizedTitle title, int runtimeMinutes, string rawDirectors,
        IReadOnlyList<MovieCandidate> candidates)
    {
        if (candidates.Count == 0) return new MatchResult(null, 0d, MatchOutcome.Unmatched);

        var wantedDirectors = TitleNormalizer.NameTokens(rawDirectors);

        var best = candidates
            .Select(c => (Candidate: c, Score: ScoreOne(c, title, runtimeMinutes, wantedDirectors)))
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Candidate.Popularity)
            .ThenBy(x => x.Candidate.TmdbId)
            .First();

        // Idempotent given ScoreOne already rounds. Kept deliberately: three float defects
        // have surfaced in this file, and making the threshold depend on a non-local
        // invariant inside ScoreOne is how a fourth arrives. Do not "simplify" this away.
        var rounded = Math.Round(best.Score, 4);

        var outcome = rounded >= AcceptThreshold ? MatchOutcome.Accepted
                    : rounded >= FlagThreshold   ? MatchOutcome.Flagged
                    : MatchOutcome.Unmatched;

        return new MatchResult(
            outcome == MatchOutcome.Unmatched ? null : best.Candidate.TmdbId,
            rounded,
            outcome);
    }

    /// <summary>
    /// Whether a candidate's runtime counts as a match for the sheet's: within the tolerance either way,
    /// inclusive. A candidate with no runtime never agrees. Shared with the pipeline's credits gate so the
    /// two cannot drift apart.
    /// </summary>
    public static bool RuntimeAgrees(int? candidateMinutes, int sheetMinutes)
        => candidateMinutes is { } rt && Math.Abs(rt - sheetMinutes) <= RuntimeToleranceMinutes;

    /// <summary>
    /// Whether a candidate's title names the same film as the sheet's: equal once both are folded.
    /// Shared with the pipeline's credits gate for the same reason as <see cref="RuntimeAgrees"/>.
    /// </summary>
    public static bool TitleAgrees(string candidateTitle, NormalizedTitle sheetTitle)
        => TitleNormalizer.Normalize(candidateTitle).Value == sheetTitle.Value;

    private static double ScoreOne(
        MovieCandidate c, NormalizedTitle title, int runtimeMinutes,
        IReadOnlySet<string> wantedDirectors)
    {
        var score = 0d;

        if (RuntimeAgrees(c.RuntimeMinutes, runtimeMinutes)) score += RuntimeWeight;

        if (c.Directors.Any(d => TitleNormalizer.NameTokens(d) is { Count: > 0 } t
                                 && t.IsSubsetOf(wantedDirectors)))
            score += DirectorWeight;

        if (TitleAgrees(c.Title, title)) score += TitleWeight;

        if (title.YearHint is { } y && c.Year == y) score += YearWeight;

        // Sums of decimal-looking weights are not exact doubles: 0.30 + 0.15 is
        // 0.44999999999999996, and 0.40 + 0.15 differs from 0.30 + 0.15 + 0.10 although both
        // are 0.55. Unrounded, the first silently fell below the Flagged threshold and the
        // second always outranked the third, so the popularity tie-break never ran.
        return Math.Round(score, 4);
    }
}

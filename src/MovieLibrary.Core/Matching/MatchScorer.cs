using MovieLibrary.Core.Text;

namespace MovieLibrary.Core.Matching;

public sealed record MovieCandidate(
    int TmdbId, string Title, int? Year, int? RuntimeMinutes,
    IReadOnlyList<string> Directors, double Popularity);

public enum MatchOutcome { Accepted, Flagged, Unmatched }

public sealed record MatchResult(int? TmdbId, double Confidence, MatchOutcome Outcome);

public static class MatchScorer
{
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

        var rounded = Math.Round(best.Score, 4);

        var outcome = rounded >= AcceptThreshold ? MatchOutcome.Accepted
                    : rounded >= FlagThreshold   ? MatchOutcome.Flagged
                    : MatchOutcome.Unmatched;

        return new MatchResult(
            outcome == MatchOutcome.Unmatched ? null : best.Candidate.TmdbId,
            rounded,
            outcome);
    }

    private static double ScoreOne(
        MovieCandidate c, NormalizedTitle title, int runtimeMinutes,
        IReadOnlySet<string> wantedDirectors)
    {
        var score = 0d;

        if (c.RuntimeMinutes is { } rt && Math.Abs(rt - runtimeMinutes) <= 2) score += 0.40;

        if (c.Directors.Any(d => TitleNormalizer.NameTokens(d) is { Count: > 0 } t
                                 && t.IsSubsetOf(wantedDirectors)))
            score += 0.30;

        if (TitleNormalizer.Normalize(c.Title).Value == title.Value) score += 0.15;

        if (title.YearHint is { } y && c.Year == y) score += 0.10;

        return score;
    }
}

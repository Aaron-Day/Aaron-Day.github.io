using System.Globalization;
using System.Text;

namespace MovieLibrary.Enrich.Reporting;

/// <summary>
/// The Actions job summary: the one page of this tool the owner reads. A healthy run is quiet. Anything that went wrong
/// without stopping the run is on it, and the budget banner is what Task 18's deploy gate has to be able to see.
/// </summary>
public static class JobSummary
{
    private const int MaxReviewLines = 50;

    private static string N(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    public static string Build(JobReport r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Enrichment run").AppendLine();

        if (r.StoppedOnBudget)
            sb.AppendLine("> **INCOMPLETE: the request budget was reached. Do not deploy this catalogue.** It was written " +
                          "for local preview only. Series whose seasons were not fetched show their episodes at season 0, " +
                          "and films not yet looked up carry Sheet data alone. The next run picks up where this one stopped. " +
                          "Raise `max-requests` on a manual dispatch (0 = unlimited) to get through the library faster.")
              .AppendLine();

        if (r.Fixtures)
            sb.AppendLine("> **Fixture run.** Every film and series here is synthetic, built from the Sheet alone. " +
                          "It is for building the site locally and is never deployed.")
              .AppendLine();

        if (r.CacheSaveError is not null)
            sb.AppendLine($"> **The TMDB cache was not saved.** {r.CacheSaveError} The catalogue itself was published " +
                          "and is sound, so it is not withheld. Check the runner's disk and permissions: the next run " +
                          "pays for those responses again, and under the default budget it stops early and produces an " +
                          "incomplete catalogue, which is never deployed.")
              .AppendLine();

        sb.AppendLine($"- Films: **{N(r.Films)}**");
        sb.AppendLine($"- Series: **{N(r.Series)}** ({N(r.TotalEpisodes)} episodes listed, {N(r.OwnedEpisodes)} owned)");
        sb.AppendLine(r.Fixtures
            ? $"- TMDB requests: **{N(r.RequestsUsed)}** (fixture run, none made)"
            : $"- TMDB requests: **{N(r.RequestsUsed)}**" + (r.MaxRequests > 0 ? $" of {N(r.MaxRequests)}" : " (unlimited)"));

        if (r.FilmsWithEmptyCast > 0 || r.CastMembersSkipped > 0)
        {
            sb.AppendLine().AppendLine("### Check these").AppendLine();
            if (r.FilmsWithEmptyCast > 0)
                sb.AppendLine($"- Films with no cast: **{N(r.FilmsWithEmptyCast)}** of {N(r.MatchedFilms)} matched. " +
                              "A few can be real. If this is most of the library, TMDB probably changed its credits " +
                              "response, and the empty casts would be kept for months.");
            if (r.CastMembersSkipped > 0)
                sb.AppendLine($"- Cast members skipped as unreadable: **{N(r.CastMembersSkipped)}**. One odd entry is " +
                              "skipped so it cannot stop a run; a large number means TMDB changed the shape of a cast entry.");
        }

        if (r.ReviewQueue.Count > 0)
        {
            sb.AppendLine().AppendLine($"### Needs review ({N(r.ReviewQueue.Count)})").AppendLine();
            sb.AppendLine("Paste the correct TMDB id into the `Overrides` tab to fix one permanently.").AppendLine();

            foreach (var line in r.ReviewQueue.Take(MaxReviewLines)) sb.AppendLine($"- {line}");
            if (r.ReviewQueue.Count > MaxReviewLines) sb.AppendLine($"- ...and {N(r.ReviewQueue.Count - MaxReviewLines)} more");
        }

        return sb.ToString();
    }

    /// <summary>What a run that ended in a fatal error leaves on the summary page, where the owner will look first.</summary>
    public static string BuildFailure(string headline, string advice)
        => new StringBuilder()
            .AppendLine("## Enrichment run FAILED").AppendLine()
            .AppendLine(headline).AppendLine()
            .AppendLine(advice)
            .ToString();

    /// <summary>Prints the summary, and appends it to the Actions step summary when running there; harmless locally.</summary>
    public static async Task EmitAsync(string markdown, TextWriter console, string? stepSummaryPath, CancellationToken ct)
    {
        console.WriteLine(markdown);

        if (!string.IsNullOrWhiteSpace(stepSummaryPath))
            await File.AppendAllTextAsync(stepSummaryPath, markdown, ct);
    }
}

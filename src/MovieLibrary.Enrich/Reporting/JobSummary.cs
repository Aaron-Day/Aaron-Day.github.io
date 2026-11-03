using System.Globalization;
using System.Text;

namespace MovieLibrary.Enrich.Reporting;

/// <summary>
/// The Actions job summary: the one page of this tool the owner reads. A healthy run is quiet. Anything that went wrong
/// without stopping the run is on it, and the budget banner is what Task 18's deploy gate has to be able to see.
/// </summary>
/// <remarks>
/// The page and the job log carry the same report but not the same text: the page is a fixed-size thing to skim, so its
/// review queue is capped per section, while the log is scrollable and searchable and gets every line. They used to be
/// the same string, so the lines the page dropped existed nowhere at all - not in <c>meta.json</c> (published empty by
/// the owner's decision), not in the log, not in a file.
/// </remarks>
public static class JobSummary
{
    /// <summary>
    /// How many lines of each review section the summary page prints. Per section, never across them: one flat cap over
    /// a films-before-series list meant a long film section printed no series lines at all, on the very run that creates
    /// them. The step summary's own ceiling is 1 MiB, so three sections of this are nothing.
    /// </summary>
    internal const int MaxReviewLinesPerSection = 150;

    private static string N(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>One run's report, in both of the forms it is emitted in.</summary>
    /// <remarks>
    /// Two strings of the same type, so they are named rather than positional: handing the capped page to the console
    /// is exactly the hole this type exists to close, and it would compile.
    /// </remarks>
    public sealed record SummaryText
    {
        /// <summary>Everything, for stdout: the Actions log holds it in full.</summary>
        public required string Console { get; init; }

        /// <summary>The page, with each review section capped at <see cref="MaxReviewLinesPerSection"/>.</summary>
        public required string StepSummary { get; init; }

        /// <summary>Both forms through the caller's redaction, so neither can carry the token.</summary>
        public SummaryText Redacted(Func<string, string> redact)
            => new() { Console = redact(Console), StepSummary = redact(StepSummary) };
    }

    public static SummaryText Build(JobReport r)
        => new() { Console = Build(r, capped: false), StepSummary = Build(r, capped: true) };

    private static string Build(JobReport r, bool capped)
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

        AppendReview(sb, r.Review, capped);

        return sb.ToString();
    }

    /// <summary>
    /// The review queue, as three labelled sections under one heading. Each carries its own count and its own
    /// truncation notice, so a truncated section can only ever hide its own tail.
    /// </summary>
    private static void AppendReview(StringBuilder sb, ReviewQueue queue, bool capped)
    {
        if (queue.Count == 0) return;

        sb.AppendLine().AppendLine($"### Needs review ({N(queue.Count)})").AppendLine();
        sb.AppendLine("Paste the correct TMDB id into the `Overrides` tab to fix one permanently.").AppendLine();

        if (capped)
            sb.AppendLine($"Each section below shows at most {N(MaxReviewLinesPerSection)} lines. **The complete list " +
                          "is in this job's log**, which is scrollable and searchable; it is nowhere else, so read it " +
                          "there before the run ages out.")
              .AppendLine();

        // Order matters and is the owner's: his own failed corrections, then films, then series. It is only the
        // ORDER here - the groups themselves are named, so they cannot be exchanged by accident.
        Section(sb, "Overrides", queue.OverrideNotes, capped);
        Section(sb, "Films", queue.Films, capped);
        Section(sb, "Series", queue.Series, capped);
    }

    /// <summary>One section, printed only when it has lines so a healthy run stays quiet.</summary>
    private static void Section(StringBuilder sb, string label, IReadOnlyList<string> lines, bool capped)
    {
        if (lines.Count == 0) return;

        sb.AppendLine($"**{label} ({N(lines.Count)})**").AppendLine();

        var shown = capped ? Math.Min(lines.Count, MaxReviewLinesPerSection) : lines.Count;
        for (var i = 0; i < shown; i++) sb.AppendLine($"- {lines[i]}");
        if (shown < lines.Count) sb.AppendLine($"- ...and {N(lines.Count - shown)} more, in this job's log");

        sb.AppendLine();
    }

    /// <summary>What a run that ended in a fatal error leaves on the summary page, where the owner will look first.</summary>
    /// <remarks>A failure is a handful of lines, so there is nothing to cap: the page and the log say the same thing.</remarks>
    public static SummaryText BuildFailure(string headline, string advice)
    {
        var text = new StringBuilder()
            .AppendLine("## Enrichment run FAILED").AppendLine()
            .AppendLine(headline).AppendLine()
            .AppendLine(advice)
            .ToString();

        return new SummaryText { Console = text, StepSummary = text };
    }

    /// <summary>
    /// Prints the whole report, and appends the capped page to the Actions step summary when running there; harmless
    /// locally.
    /// </summary>
    public static async Task EmitAsync(SummaryText text, TextWriter console, string? stepSummaryPath, CancellationToken ct)
    {
        console.WriteLine(text.Console);

        if (!string.IsNullOrWhiteSpace(stepSummaryPath))
            await File.AppendAllTextAsync(stepSummaryPath, text.StepSummary, ct);
    }
}

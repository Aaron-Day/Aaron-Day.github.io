using System.Globalization;

namespace MovieLibrary.Enrich.Reporting;

/// <summary>
/// How far a pass has got through its titles. A record with named members rather than a pair of ints: <c>Done</c> and
/// <c>Total</c> are both counts of titles, so positionally they could be exchanged and the line would still read
/// plausibly.
/// </summary>
public sealed record PassProgress
{
    /// <summary>Titles finished, counting from one.</summary>
    public required int Done { get; init; }

    /// <summary>Titles this pass will work through in total, known before the first request.</summary>
    public required int Total { get; init; }
}

/// <summary>
/// The periodic progress line for a long pass, printed to stdout and nowhere else.
/// </summary>
/// <remarks>
/// A first full run is roughly 9,500 requests over 40 to 70 minutes, and it used to print nothing for that whole
/// stretch: a healthy run and one stalled in a 60-second <c>Retry-After</c> loop looked identical from outside, and
/// cancelling mid-enrich is precisely how a spent budget is wasted. One line every <paramref name="every"/> titles
/// and one at the end of the pass, each carrying the requests used so far, which is the number that says whether
/// anything is still moving. It reports through the run's own <c>Say</c>, so the token cannot reach it, and it never
/// touches the job summary.
/// </remarks>
/// <param name="noun">What is being counted, for the line itself: "films", "series".</param>
/// <param name="every">How many titles between lines. Chosen so a real pass produces a readable trickle.</param>
/// <param name="requestsUsed">The run's request count, read at the moment the line is printed.</param>
internal sealed class PassReporter(string noun, int every, Func<int> requestsUsed, Action<string> say)
    : IProgress<PassProgress>
{
    public void Report(PassProgress value)
    {
        if (value.Done <= 0) return;

        // The last title always prints, whatever the total is a multiple of: the end of a pass is worth a line, and
        // without it a 1,457-film pass would stop at 1,400 and go quiet again.
        if (value.Done % every != 0 && value.Done != value.Total) return;

        say($"  {N(value.Done)} of {N(value.Total)} {noun}, {N(requestsUsed())} TMDB requests so far");
    }

    private static string N(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
}

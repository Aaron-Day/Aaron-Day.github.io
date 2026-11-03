namespace MovieLibrary.Enrich.Reporting;

/// <summary>
/// The lines a human has to look at, kept in the three groups the owner reads them in rather than flattened into one
/// list: the owner's own corrections that failed, then the films, then the series.
/// </summary>
/// <remarks>
/// <para><b>Why three lists and not one.</b> The groups are not equally consequential - one wrong series
/// mis-attributes every owned episode row under it, while a flagged film is one card - and they are nothing like the
/// same size. Flattened, ordered films-before-series and then capped, a first full run's dozens of film lines push
/// every series line off the page, on the one run that creates them. Each group is printed under its own label with
/// its own count and its own truncation notice, so a long section can no longer hide a whole category.</para>
/// <para><b>Named, not positional.</b> All three are <c>IReadOnlyList&lt;string&gt;</c>, so a positional constructor
/// would let two of them be exchanged with nothing at all to notice: the lines would simply appear under the wrong
/// label. That is the defect shape this project has already shipped once, with the two same-typed override maps
/// (Task 17 Step G). The members are <c>required</c>, so every construction has to name each group.</para>
/// </remarks>
public sealed record ReviewQueue
{
    /// <summary>Nothing to review: a fixture run, and the shape a run with nothing to report reports.</summary>
    public static readonly ReviewQueue Empty = new() { OverrideNotes = [], Films = [], Series = [] };

    /// <summary>
    /// The owner's own corrections that failed - a drifted override, an unreadable id, a missing Overrides tab. They
    /// come first: they are not a doubtful match, they are a mistake to fix.
    /// </summary>
    public required IReadOnlyList<string> OverrideNotes { get; init; }

    /// <summary>Films that were not matched, or were matched below the accept threshold.</summary>
    public required IReadOnlyList<string> Films { get; init; }

    /// <summary>Series whose name, episode list or season coverage needs a human: the highest-consequence group.</summary>
    public required IReadOnlyList<string> Series { get; init; }

    /// <summary>Lines across all three groups, which is the number the summary heading carries.</summary>
    public int Count => OverrideNotes.Count + Films.Count + Series.Count;
}

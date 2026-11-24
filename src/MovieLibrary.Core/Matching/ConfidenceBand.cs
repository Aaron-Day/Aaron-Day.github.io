using MovieLibrary.Core.Models;

namespace MovieLibrary.Core.Matching;

/// <summary>
/// How far the pipeline trusted a film's TMDB match, in the three bands anything downstream shows.
/// </summary>
/// <remarks>
/// Ordered <see cref="Unmatched"/>, <see cref="Uncertain"/>, <see cref="Confident"/> on purpose, so that
/// <c>default(Confidence)</c> is the LOUD value and not the trusted one. A band left unset would otherwise read
/// as <see cref="Confident"/>: no badge, nothing unusual on screen, and no way to tell it from a record that
/// really was matched well. Reading as <see cref="Unmatched"/> is wrong in a way somebody notices. Nothing
/// orders these by value, so the order costs nothing.
/// </remarks>
public enum Confidence
{
    /// <summary>No match was made at all. <c>MovieRecord.I</c> is 0 and the record carries Sheet data alone.</summary>
    Unmatched,

    /// <summary>Matched, but below <see cref="MatchScorer.AcceptThreshold"/>: a human should look at it.</summary>
    Uncertain,

    /// <summary>Matched at or above <see cref="MatchScorer.AcceptThreshold"/>. The ordinary case, and the quiet one.</summary>
    Confident,
}

/// <summary>
/// The single place that decides which band a record is in, and what to call it.
/// </summary>
/// <remarks>
/// <b>In Core rather than in the web project, and that is load-bearing.</b> The browser badges records with this
/// and <c>MovieLibrary.Enrich</c>'s job summary counts them with it, so both need the same rule;
/// <c>MovieLibrary.Enrich</c> must never reference the web project. Sharing only
/// <see cref="MatchScorer.AcceptThreshold"/> would share the constant and duplicate the RULE -- specifically the
/// <c>I == 0</c> precedence below -- and the failure mode is the owner's build summary disagreeing with the
/// badges he is looking at. Core owns <see cref="MovieRecord"/> and both projects reference Core, so Core is
/// where this belongs.
/// </remarks>
public static class ConfidenceBand
{
    /// <summary>The band a record falls in: its id first, its score second.</summary>
    /// <remarks>
    /// An unmatched record is identified by <c>I == 0</c>, NOT by its score: the pipeline writes <c>Mc: 0</c> for
    /// every unmatched film even when its best candidate scored 0.4, so the score alone cannot tell "no match was
    /// made" from "a poor match was accepted". The id is therefore checked first and wins over any score.
    /// <para>
    /// The threshold is read from <see cref="MatchScorer.AcceptThreshold"/> and is never spelled 0.75 here. A
    /// literal would let this idea of "confident" drift from the pipeline's silently, and the drift would show up
    /// as records badged wrongly rather than as a failing test.
    /// </para>
    /// </remarks>
    public static Confidence Of(MovieRecord m) => Of(m.I, m.Mc);

    /// <summary>The band a series falls in, from the index row a grid is given.</summary>
    /// <remarks>
    /// A series carries the same two fields with the same meanings, so it gets the same answer from the
    /// same code rather than a second copy of the precedence below. The alternative -- a web-side helper
    /// that read <c>I</c> and <c>Mc</c> itself -- is the drift this class exists to prevent, and it would
    /// have drifted silently: both halves would still badge SOMETHING.
    /// </remarks>
    public static Confidence Of(ShowSummary s) => Of(s.I, s.Mc);

    /// <summary>The band a series falls in, from its full record.</summary>
    public static Confidence Of(ShowRecord s) => Of(s.I, s.Mc);

    /// <summary>The rule itself, over the two fields every record shape carries.</summary>
    private static Confidence Of(int id, double score)
        => id == 0 ? Confidence.Unmatched
         : score >= MatchScorer.AcceptThreshold ? Confidence.Confident
         : Confidence.Uncertain;

    /// <summary>The short label for a band, or null where the band carries no badge at all.</summary>
    /// <remarks>
    /// <para>
    /// <b><see cref="Confidence.Confident"/> returns null, and that is deliberate.</b> The normal case carries no
    /// badge, because the absence of an exceptional marker is not information conveyed by colour, and a badge on
    /// 1,265 of 1,456 records is noise that trains the eye to ignore the 191 that matter.
    /// </para>
    /// <para>
    /// "Needs review", not "Uncertain match": the latter is jargon from a pipeline the reader does not know
    /// exists -- uncertain match to WHAT? -- and "Needs review" is both the owner's own mental model and the
    /// heading <c>JobSummary</c> already prints, so a badge and the build summary say the same words.
    /// </para>
    /// </remarks>
    public static string? BadgeFor(Confidence c) => c switch
    {
        Confidence.Unmatched => "Not matched",
        Confidence.Uncertain => "Needs review",
        _ => null,
    };
}

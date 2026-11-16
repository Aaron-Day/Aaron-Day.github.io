namespace MovieLibrary.Core.Identity;

/// <summary>
/// How far a film's runtime may move and still be the same film. Apple re-encodes and the runtime shifts a
/// minute or two, and <c>RuntimeParser.FilmMinutes</c> truncates seconds, so even a sub-minute drift across a
/// minute boundary counts.
/// </summary>
/// <remarks>
/// The one definition of this number in the solution. <c>CacheStore</c>'s plus-or-minus-2-minute alias and the
/// record store's drift rule are the same tolerance for the same reason, and two copies of it would drift apart
/// exactly where nobody is looking: a store entry that stopped binding while the cache went on aliasing.
/// </remarks>
public static class RuntimeDrift
{
    public const int ToleranceMinutes = 2;

    /// <summary>
    /// The offsets to try, <b>nearest first and 0 first</b>. The order is load-bearing: a row that matches an
    /// entry exactly must be reported as an exact bind and never as drift, and where two offsets reach one row
    /// the smaller one is the truer account of what happened.
    /// </summary>
    public static IReadOnlyList<int> Offsets { get; } = [0, -1, 1, -2, 2];
}

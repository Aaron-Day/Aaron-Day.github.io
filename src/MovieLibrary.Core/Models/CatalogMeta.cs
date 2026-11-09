namespace MovieLibrary.Core.Models;

/// <summary>
/// The contents of <c>data/meta.json</c>.
/// </summary>
/// <param name="TotalEpisodeCount">
/// Every episode the library's series carry, owned or not: the sum of <c>ShowRecord.TotalEpisodes</c>. That is TMDB's
/// listing <i>plus</i> season 0 and the synthetic numbered extras - the owned Apple rows TMDB did not list, which
/// <c>TvPipeline</c> keeps rather than drops. So it is not purely "what TMDB lists": owned can never exceed total, and
/// a season-0 row is genuinely owned. <c>CatalogWriter.NumberedSeasons</c> makes the opposite call for the per-series
/// season count, and says there why.
/// </param>
/// <param name="OwnedEpisodeCount">
/// Only the episodes in the library. Optional so existing callers compile; left out of the JSON when null
/// rather than written as a false zero.
/// </param>
public sealed record CatalogMeta(
    string BuiltUtc, int MovieCount, int ShowCount, int TotalEpisodeCount,
    Dictionary<string, string> Genres, string Attribution, string[] ReviewQueue,
    int? OwnedEpisodeCount = null)
{
    /// <summary>
    /// TMDB's API terms fix this sentence and forbid rewording it, so production code owns it: the writer
    /// refuses to publish any other. A typo here is a licence problem, not a cosmetic one.
    /// </summary>
    public const string TmdbAttribution =
        "This website uses TMDB and the TMDB APIs but is not endorsed, certified, or otherwise approved by TMDB.";

    /// <summary>
    /// A safe default for a caller holding a result whose fetch did not produce a catalogue, so that
    /// <see cref="Genres"/>, <see cref="ReviewQueue"/> and <see cref="Attribution"/> are readable with no null
    /// check anywhere.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is not a successful empty catalogue and must never be presented as one.</b> What happened is
    /// carried by the state beside it (<c>CatalogState.Failed</c>), never by the emptiness of this value. The
    /// whole point of pairing a state with a value is that an empty value can no longer mean "it failed".
    /// </para>
    /// <para>
    /// <see cref="BuiltUtc"/> is the empty string rather than a fabricated date. A date that is not this
    /// catalogue's build time is worse than no date, and the empty string renders as nothing rather than as
    /// "0001-01-01". Format it only where the state says the fetch succeeded.
    /// </para>
    /// <para>
    /// <see cref="Attribution"/> is the real sentence and not <c>""</c>. TMDB's terms bind it, and a
    /// licence-bound sentence must not be able to vanish because a fetch failed.
    /// </para>
    /// <para>
    /// A fresh instance per read, not a cached singleton: <see cref="Genres"/> is a mutable
    /// <see cref="Dictionary{TKey,TValue}"/> and <see cref="ReviewQueue"/> a mutable array, so one shared
    /// instance would let any caller edit every other caller's "empty". It is built once per failed fetch.
    /// </para>
    /// </remarks>
    public static CatalogMeta Empty => new(
        BuiltUtc: string.Empty, MovieCount: 0, ShowCount: 0, TotalEpisodeCount: 0,
        Genres: new Dictionary<string, string>(), Attribution: TmdbAttribution, ReviewQueue: []);
}

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
}

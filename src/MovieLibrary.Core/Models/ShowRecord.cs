namespace MovieLibrary.Core.Models;

public sealed record EpisodeRecord(int E, string T, bool O, string? Air, int? Rt, string? S, string? Ov);

public sealed record SeasonRecord(int N, int Owned, int Total, EpisodeRecord[] Eps);

/// <param name="Srt">Where this series sorts, when that is not where <c>T</c> would put it. Null means "sort by <c>T</c>".</param>
/// <param name="Ex">The owner's extra fields from the record store, or null. See <see cref="MovieRecord"/>.</param>
public sealed record ShowRecord(
    int I, string T, string Slug, string? P, int? FirstYear, string Ag,
    SeasonRecord[] Seasons, int OwnedEpisodes, int TotalEpisodes, double Mc,
    string? Srt = null, Dictionary<string, string>? Ex = null)
{
    /// <summary>
    /// How many of this series' seasons are real, numbered seasons.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Season 0 is not a season.</b> It is <c>TvPipeline</c>'s bucket for episodes it could not place --
    /// owned rows that matched nothing, or every row of an unmatched series -- so counting it shows
    /// "6 seasons" for a five-season show.
    /// </para>
    /// <para>
    /// <b>HERE, because two projects have to give the same answer.</b> <c>CatalogWriter</c> counts these
    /// into <see cref="ShowSummary.SeasonCount"/> and the series page counts them again to put the same
    /// number in its byline, and the two had their own copies for exactly one afternoon: the page said
    /// "10 seasons" for the owner's copy of 24 where the tile that linked to it said "9", one click apart,
    /// with nothing on either page to say which was right. A METHOD and not a property, so no serialiser
    /// writes it into <c>tv/{slug}.json</c> as a field that would then be a second place to keep it true.
    /// </para>
    /// </remarks>
    public int NumberedSeasons() => Seasons.Count(season => season.N > 0);
}

/// <summary>
/// One row of <c>shows.json</c>: everything a grid needs about a series and no episodes at all.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the index exists.</b> The 122 series in the live library carry 18,208 episodes between them, which
/// is about 5 MB of <c>tv/{slug}.json</c> and 267 KB for the largest single show. A page that listed the
/// series would have had to download all of it to print 122 titles, so the build writes this projection
/// beside the full records and a show's own file is fetched only when a reader opens that show.
/// </para>
/// <para>
/// <b>It lives in Core because TWO projects have to agree about it.</b> <c>CatalogWriter</c> writes it and
/// the web app reads it; it was private to the writer first, which meant the reader would have declared its
/// own copy of the same shape and nothing would have failed when the two drifted. The same gap is still open
/// for the serialiser's naming policy, and is recorded where that is used.
/// </para>
/// <para>
/// <see cref="SeasonCount"/> counts NUMBERED seasons only, which <see cref="ShowRecord.NumberedSeasons"/>
/// is the one implementation of. Season 0 is NOT the specials, whatever a TMDB response means by it here:
/// in this pipeline it is <c>TvPipeline</c>'s bucket for episodes it could not place -- owned rows that
/// matched nothing, or every row of an unmatched series -- so counting it would show "6 seasons" for a
/// five-season show. 7 of the owner's 122 series have a <see cref="SeasonCount"/> of 0 for that reason and
/// still own episodes.
/// </para>
/// </remarks>
public sealed record ShowSummary(
    int I, string T, string Slug, string? P, int? FirstYear, string Ag,
    int SeasonCount, int OwnedEpisodes, int TotalEpisodes, double Mc);

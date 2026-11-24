namespace MovieLibrary.Core.Models;

public sealed record EpisodeRecord(int E, string T, bool O, string? Air, int? Rt, string? S, string? Ov);

public sealed record SeasonRecord(int N, int Owned, int Total, EpisodeRecord[] Eps);

/// <param name="Srt">Where this series sorts, when that is not where <c>T</c> would put it. Null means "sort by <c>T</c>".</param>
/// <param name="Ex">The owner's extra fields from the record store, or null. See <see cref="MovieRecord"/>.</param>
public sealed record ShowRecord(
    int I, string T, string Slug, string? P, int? FirstYear, string Ag,
    SeasonRecord[] Seasons, int OwnedEpisodes, int TotalEpisodes, double Mc,
    string? Srt = null, Dictionary<string, string>? Ex = null);

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
/// <see cref="SeasonCount"/> counts NUMBERED seasons only. Season 0 is the specials, which TMDB carries for
/// most series and which no reader thinks of as a season, so "9 seasons" means nine numbered ones however
/// many specials sit beside them.
/// </para>
/// </remarks>
public sealed record ShowSummary(
    int I, string T, string Slug, string? P, int? FirstYear, string Ag,
    int SeasonCount, int OwnedEpisodes, int TotalEpisodes, double Mc);

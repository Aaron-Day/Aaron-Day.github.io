namespace MovieLibrary.Core.Models;

public sealed record EpisodeRecord(int E, string T, bool O, string? Air, int? Rt, string? S, string? Ov);

public sealed record SeasonRecord(int N, int Owned, int Total, EpisodeRecord[] Eps);

/// <param name="Srt">Where this series sorts, when that is not where <c>T</c> would put it. Null means "sort by <c>T</c>".</param>
/// <param name="Ex">The owner's extra fields from the record store, or null. See <see cref="MovieRecord"/>.</param>
public sealed record ShowRecord(
    int I, string T, string Slug, string? P, int? FirstYear, string Ag,
    SeasonRecord[] Seasons, int OwnedEpisodes, int TotalEpisodes, double Mc,
    string? Srt = null, Dictionary<string, string>? Ex = null);

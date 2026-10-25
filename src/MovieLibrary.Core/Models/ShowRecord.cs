namespace MovieLibrary.Core.Models;

public sealed record EpisodeRecord(int E, string T, bool O, string? Air, int? Rt, string? S, string? Ov);

public sealed record SeasonRecord(int N, int Owned, int Total, EpisodeRecord[] Eps);

public sealed record ShowRecord(
    int I, string T, string Slug, string? P, int? FirstYear, string Ag,
    SeasonRecord[] Seasons, int OwnedEpisodes, int TotalEpisodes, double Mc);

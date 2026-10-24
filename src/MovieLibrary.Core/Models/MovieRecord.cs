namespace MovieLibrary.Core.Models;

public sealed record CastMember(string N, string C, string? P);

/// <summary>
/// Field names are abbreviated deliberately: this record repeats 1,457 times
/// in movies.json and the short names save roughly a fifth of the payload.
/// </summary>
public sealed record MovieRecord(
    int I, string T, int? Y, int Rt, string? P, string? B,
    int[] G, string Ag, double Vote, string[] Dir,
    string? Ov, string? Tag, CastMember[] Cast,
    string? Col, string? Own, double Mc);

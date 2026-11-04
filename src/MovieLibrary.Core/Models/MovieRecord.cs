namespace MovieLibrary.Core.Models;

public sealed record CastMember(string N, string C, string? P);

/// <summary>
/// Field names are abbreviated deliberately: this record repeats 1,457 times
/// in movies.json and the short names save roughly a fifth of the payload.
/// <para><c>I</c> is the TMDB id and is <c>0</c> for every unmatched film, so it identifies nothing on its own.
/// <c>K</c> is what identifies the record (see <c>RecordKey</c>): the key, the route value and the favourites key.</para>
/// </summary>
public sealed record MovieRecord(
    int I, string K, string T, int? Y, int Rt, string? P, string? B,
    int[] G, string Ag, double Vote, string[] Dir,
    string? Ov, string? Tag, CastMember[] Cast,
    string? Col, string? Own, double Mc);

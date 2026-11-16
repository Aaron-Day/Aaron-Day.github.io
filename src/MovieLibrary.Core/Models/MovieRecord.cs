namespace MovieLibrary.Core.Models;

public sealed record CastMember(string N, string C, string? P);

/// <summary>
/// Field names are abbreviated deliberately: this record repeats 1,457 times
/// in movies.json and the short names save roughly a fifth of the payload.
/// <para><c>I</c> is the TMDB id and is <c>0</c> for every unmatched film, so it identifies nothing on its own.
/// <c>K</c> is what identifies the record (see <c>RecordKey</c>): the key, the route value and the favourites key.</para>
/// </summary>
/// <param name="Srt">
/// Where this title sorts, when that is not where its display title would put it. <b>Null means "sort by
/// <c>T</c>"</b>, which is the overwhelming majority, so the payload grows only for the titles that need it.
/// The pipeline writes it only when <c>SortTitle.From(T)</c> differs from <c>T</c>, or when the record store
/// overrides it. A consumer sorts on <c>Srt ?? T</c> and never on <c>Srt</c> alone.
/// </param>
/// <param name="Ex">
/// The owner's own extra fields, from the record store, copied key-for-key and value-for-value. <b>Null when
/// there are none.</b> Strings only: anything the UI must reason about numerically earns a typed member here
/// instead. Nothing in this project interprets the contents -- the contract is that a key added by hand
/// survives every build in between with no code change.
/// </param>
public sealed record MovieRecord(
    int I, string K, string T, int? Y, int Rt, string? P, string? B,
    int[] G, string Ag, double Vote, string[] Dir,
    string? Ov, string? Tag, CastMember[] Cast,
    string? Col, string? Own, double Mc,
    string? Srt = null, Dictionary<string, string>? Ex = null);

namespace MovieLibrary.Core.Caching;

/// <summary>One cached answer: which TMDB film or series a sheet row resolved to, or that it resolved to none.</summary>
/// <param name="NormalizedTitle">
/// The row's title as <c>NormalizedTitle.Value</c> -- the FOLDED form, which is also what the identity key
/// hashes. Never <c>Cleaned</c>: Cleaned is the readable search string and still carries case, accents and
/// punctuation, so storing it here would fail an alias comparison between two spellings of one title and
/// would not agree with the key the entry is filed under. A cache alias compares this string for equality.
/// </param>
/// <param name="NormalizedDirectors">
/// The row's directors exactly as <c>TitleNormalizer.NormalizeDirectors</c> returns them, which is also what the
/// identity key hashes: lower-cased, accents and punctuation folded away, "and" dropped, name tokens de-duplicated
/// and sorted. The order the sheet lists directors in is Apple's and is not stable, so it must not matter, and
/// sorting is what makes it not matter. A cache alias compares this string for equality.
/// <para>Null means the entry was written before directors were stored; empty means the sheet named none (a series
/// has none). Both are exact-key only: an alias is never made to or from them, because without directors a remake
/// and a re-encode look the same.</para>
/// </param>
public sealed record CacheEntry(
    string Key,
    int? TmdbId,
    double Confidence,
    DateTimeOffset FetchedUtc,
    bool Negative,
    string NormalizedTitle,
    int RuntimeMinutes,
    string? NormalizedDirectors = null);

namespace MovieLibrary.Core.Caching;

public sealed record CacheEntry(
    string Key,
    int? TmdbId,
    double Confidence,
    DateTimeOffset FetchedUtc,
    bool Negative,
    string NormalizedTitle,
    int RuntimeMinutes);

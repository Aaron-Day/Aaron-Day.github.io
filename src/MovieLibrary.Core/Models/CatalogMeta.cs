namespace MovieLibrary.Core.Models;

public sealed record CatalogMeta(
    string BuiltUtc, int MovieCount, int ShowCount, int EpisodeCount,
    Dictionary<string, string> Genres, string Attribution, string[] ReviewQueue);

using System.Text.Json.Serialization;

namespace MovieLibrary.Enrich.Tmdb;

public sealed record TmdbGenre(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name);

/// <summary>One billed cast member, trimmed to what the site shows. Stored in the film's payload.</summary>
public sealed record TmdbCastMember(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("character")] string? Character,
    [property: JsonPropertyName("profile_path")] string? ProfilePath);

/// <summary>Directors (for scoring) and the top-billed cast (for the record), from one credits response.</summary>
public sealed record TmdbCredits(IReadOnlyList<string> Directors, IReadOnlyList<TmdbCastMember> Cast);

/// <summary>A film's details and its top-billed cast from one <c>append_to_response=credits</c> request.</summary>
public sealed record TmdbMovieWithCast(TmdbMovie Movie, IReadOnlyList<TmdbCastMember> Cast);

public sealed record TmdbMovie(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("overview")] string? Overview,
    [property: JsonPropertyName("tagline")] string? Tagline,
    [property: JsonPropertyName("runtime")] int? Runtime,
    [property: JsonPropertyName("release_date")] string? ReleaseDate,
    [property: JsonPropertyName("vote_average")] double VoteAverage,
    [property: JsonPropertyName("poster_path")] string? PosterPath,
    [property: JsonPropertyName("backdrop_path")] string? BackdropPath,
    [property: JsonPropertyName("genres")] IReadOnlyList<TmdbGenre>? Genres = null);

public sealed record TmdbSeries(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("overview")] string? Overview,
    [property: JsonPropertyName("poster_path")] string? PosterPath,
    [property: JsonPropertyName("first_air_date")] string? FirstAirDate,
    [property: JsonPropertyName("in_production")] bool InProduction,
    [property: JsonPropertyName("number_of_seasons")] int NumberOfSeasons);

public sealed record TmdbEpisode(
    [property: JsonPropertyName("episode_number")] int EpisodeNumber,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("overview")] string? Overview,
    [property: JsonPropertyName("air_date")] string? AirDate,
    [property: JsonPropertyName("runtime")] int? Runtime,
    [property: JsonPropertyName("still_path")] string? StillPath);

public sealed record TmdbSeason(
    [property: JsonPropertyName("season_number")] int SeasonNumber,
    [property: JsonPropertyName("episodes")] IReadOnlyList<TmdbEpisode> Episodes);

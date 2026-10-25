using System.Text.Json;
using System.Text.Json.Serialization;
using MovieLibrary.Core.Models;

namespace MovieLibrary.Enrich.Emit;

public sealed class CatalogWriter(string outputRoot)
{
    /// <summary>
    /// Minified: these files are downloaded by every visitor. No <c>NewLine</c> is set, unlike
    /// <c>CacheStore</c> whose indented index is committed: minified output holds no line break at all
    /// (string values escape theirs), so it is byte-identical on Windows and Linux. The default encoder is
    /// kept on purpose: it escapes non-ASCII, apostrophes, ampersands and angle brackets as \uXXXX escapes,
    /// costing about 4% raw and 0.5% gzipped, and keeps TMDB's third-party text inert if a file is ever
    /// inlined into HTML.
    /// </summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Grid-level projection; episodes are deliberately absent.</summary>
    private sealed record ShowIndexEntry(
        int I, string T, string Slug, string? P, int? FirstYear, string Ag,
        int SeasonCount, int OwnedEpisodes, int TotalEpisodes, double Mc);

    /// <summary>
    /// Writes movies.json, shows.json, tv/{slug}.json and meta.json under the output root. Slugs must already be
    /// unique and filename-safe (<c>TvPipeline</c> throws before any request otherwise); this class does not
    /// re-check, so a duplicate would silently overwrite. Files from earlier runs are not deleted: a series that
    /// was renamed or dropped keeps its old tv file until the folder is cleared (CI starts from a clean checkout).
    /// </summary>
    public async Task WriteAsync(
        IReadOnlyList<MovieRecord> movies,
        IReadOnlyList<ShowRecord> shows,
        CatalogMeta meta,
        CancellationToken ct)
    {
        Directory.CreateDirectory(outputRoot);
        Directory.CreateDirectory(Path.Combine(outputRoot, "tv"));

        await WriteJsonAsync("movies.json", movies, ct);

        var index = shows.Select(s => new ShowIndexEntry(
            s.I, s.T, s.Slug, s.P, s.FirstYear, s.Ag,
            NumberedSeasons(s), s.OwnedEpisodes, s.TotalEpisodes, s.Mc)).ToList();

        await WriteJsonAsync("shows.json", index, ct);

        foreach (var show in shows)
            await WriteJsonAsync(Path.Combine("tv", $"{show.Slug}.json"), show, ct);

        await WriteJsonAsync("meta.json", meta, ct);
    }

    /// <summary>
    /// Season 0 is TvPipeline's "unplaced episodes" bucket (owned rows that matched nothing, or every row of
    /// an unmatched series), not a season. Counting it would show "6 seasons" for a five-season show.
    /// </summary>
    private static int NumberedSeasons(ShowRecord show) => show.Seasons.Count(season => season.N > 0);

    private async Task WriteJsonAsync<T>(string relativePath, T payload, CancellationToken ct)
    {
        await using var stream = File.Create(Path.Combine(outputRoot, relativePath));
        await JsonSerializer.SerializeAsync(stream, payload, Options, ct);
    }
}

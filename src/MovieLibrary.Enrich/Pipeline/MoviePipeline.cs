using System.Globalization;
using MovieLibrary.Core.Caching;
using MovieLibrary.Core.Identity;
using MovieLibrary.Core.Matching;
using MovieLibrary.Core.Models;
using MovieLibrary.Core.Parsing;
using MovieLibrary.Core.Text;
using MovieLibrary.Enrich.Caching;
using MovieLibrary.Enrich.Tmdb;

namespace MovieLibrary.Enrich.Pipeline;

public sealed record MoviePipelineResult(
    IReadOnlyList<MovieRecord> Records,
    IReadOnlyList<string> ReviewQueue,
    int RequestsUsed,
    bool StoppedOnBudget);

/// <summary>
/// Resolves every sheet row to a <see cref="MovieRecord"/>: override first, then the cache, then a TMDB
/// search. Every row is emitted, matched or not. An unmatched film comes from sheet data alone with
/// <c>I == 0</c> and <c>Mc == 0</c>; dropping it would silently under-report the library.
/// </summary>
/// <remarks>
/// <para><b>Sequential.</b> <see cref="CacheStore"/> is not thread-safe and <c>Resolve</c> mutates it on an alias hit.</para>
/// <para><b>Order.</b> Rows run in folded-title order (ordinal), not sheet order, so a budgeted run is resumed the
/// same way every time. <see cref="MoviePipelineResult.Records"/> comes out in that order too.</para>
/// <para><b>Budget.</b> If the request budget runs out while a row is being looked up, that row is left
/// unresolved and NOT cached: an exhausted client's empty or partial answer is indistinguishable from
/// "TMDB has no such film", and caching it would suppress the film for 30 days.</para>
/// <para><b>Failures.</b> TMDB failures arrive as <see cref="HttpRequestException"/> and are not caught here; a
/// malformed sheet runtime throws <see cref="FormatException"/>. The cache index lives in memory until the
/// caller saves it, so a caller that wants the work kept across a failure must call
/// <see cref="CacheStore.SaveAsync"/> in a <c>finally</c>.</para>
/// </remarks>
public sealed class MoviePipeline(TmdbClient tmdb, CacheStore cache, Func<DateTimeOffset> clock)
{
    public async Task<MoviePipelineResult> RunAsync(
        IReadOnlyList<SheetRow> rows,
        IReadOnlyDictionary<string, int> overrides,
        CancellationToken ct)
    {
        var records = new List<MovieRecord>(rows.Count);
        var review = new List<string>();
        var now = clock();

        // Stable order so a budgeted run resumes predictably rather than arbitrarily.
        var ordered = rows
            .Select(r => (Row: r, Title: TitleNormalizer.Normalize(r.Title)))
            .OrderBy(x => x.Title.Value, StringComparer.Ordinal)
            .ToList();

        foreach (var (row, title) in ordered)
        {
            ct.ThrowIfCancellationRequested();

            var runtime = RuntimeParser.FilmMinutes(row.Runtime);
            var directors = TitleNormalizer.NormalizeDirectors(row.Directors);
            var key = IdentityKey.ForFilm(title, runtime, directors);

            int? tmdbId = null;
            var confidence = 0d;
            string? reviewNote = null; // why a human should look at this row, when one should

            if (overrides.TryGetValue(title.Value, out var forced))
            {
                tmdbId = forced;
                confidence = 1d;
            }
            else if (cache.Resolve(key, title.Value, runtime, now) is { } hit)
            {
                // On an alias hit this is the SOURCE entry, so its Key and RuntimeMinutes are not
                // ours. Only the answer (TmdbId, Confidence) is meaningful here.
                tmdbId = hit.TmdbId;
                confidence = hit.Confidence;
                if (tmdbId is null) reviewNote = "unmatched";
            }
            else if (string.IsNullOrWhiteSpace(title.Cleaned))
            {
                // Cleaned is empty when a title was nothing but a parenthetical; searching
                // TMDB for an empty string burns a request and returns noise.
                reviewNote = "unmatched";
            }
            else if (tmdb.BudgetExhausted)
            {
                reviewNote = "not searched, request budget spent";
            }
            else
            {
                var candidates = await tmdb.SearchMoviesAsync(title.Cleaned, title.YearHint, ct);
                var enriched = await EnrichCandidatesAsync(candidates, ct);

                // BudgetExhausted flips inside the call that hits the ceiling, so it can only be
                // read AFTER the lookups. An exhausted client returns the same empty or
                // half-enriched result as "TMDB has no such film"; scoring and caching it would
                // write a false negative that suppresses the film for 30 days.
                if (tmdb.BudgetExhausted)
                {
                    reviewNote = "not resolved, request budget spent";
                }
                else
                {
                    var match = MatchScorer.Score(title, runtime, row.Directors, enriched);

                    tmdbId = match.TmdbId;
                    confidence = match.Confidence;

                    cache.Put(new CacheEntry(key, tmdbId, confidence, now,
                                             Negative: tmdbId is null, title.Value, runtime));

                    if (match.Outcome != MatchOutcome.Accepted)
                        reviewNote = $"{match.Outcome} at {confidence.ToString("0.00", CultureInfo.InvariantCulture)}, " +
                                     $"id {tmdbId?.ToString() ?? "none"}";
                }
            }

            records.Add(await BuildRecordAsync(row, title, runtime, tmdbId, confidence, ct));
            if (reviewNote is not null)
                review.Add($"{row.Title} ({row.Runtime}, {row.Directors}) → {reviewNote}");
        }

        return new MoviePipelineResult(records, review, tmdb.RequestsUsed, tmdb.BudgetExhausted);
    }

    private async Task<IReadOnlyList<MovieCandidate>> EnrichCandidatesAsync(
        IReadOnlyList<MovieCandidate> candidates, CancellationToken ct)
    {
        // Runtime and director carry 0.70 of the score but search results hold neither,
        // so the leading few candidates get a details and credits lookup.
        var enriched = new List<MovieCandidate>();

        foreach (var c in candidates.OrderByDescending(c => c.Popularity).Take(3))
        {
            if (tmdb.BudgetExhausted) break;

            var details = await tmdb.GetMovieAsync(c.TmdbId, ct);
            var directors = await tmdb.GetDirectorsAsync(c.TmdbId, ct);
            enriched.Add(c with { RuntimeMinutes = details?.Runtime, Directors = directors });
        }

        return enriched.Count > 0 ? enriched : candidates;
    }

    private async Task<MovieRecord> BuildRecordAsync(
        SheetRow row, NormalizedTitle title, int runtime,
        int? tmdbId, double confidence, CancellationToken ct)
    {
        var details = tmdbId is { } id ? await cache.ReadPayloadAsync<TmdbMovie>($"movies/{id}.json", ct) : null;

        if (details is null && tmdbId is { } fetchId && !tmdb.BudgetExhausted)
        {
            details = await tmdb.GetMovieAsync(fetchId, ct);
            if (details is not null)
                await cache.WritePayloadAsync($"movies/{fetchId}.json", details, ct);
        }

        int? year = details?.ReleaseDate is { Length: >= 4 } d && int.TryParse(d[..4], out var y)
            ? y : title.YearHint;

        return new MovieRecord(
            I: tmdbId ?? 0,
            T: row.Title,
            Y: year,
            Rt: runtime,
            P: details?.PosterPath,
            B: details?.BackdropPath,
            G: [],
            Ag: row.Genre,
            Vote: details?.VoteAverage ?? 0d,
            Dir: row.Directors.Split('&', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
            Ov: details?.Overview,
            Tag: details?.Tagline,
            Cast: [],
            Col: null,
            Own: string.IsNullOrWhiteSpace(row.PurchaseInfo) ? null : row.PurchaseInfo,
            // An unmatched film is always 0, even when its best candidate scored a little: the
            // scorer reports that sub-threshold score, but no match was made.
            Mc: tmdbId is null ? 0d : Math.Round(confidence, 4));
    }
}

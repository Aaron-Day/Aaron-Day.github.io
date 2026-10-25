using System.Globalization;
using System.Text.Json;
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
    bool StoppedOnBudget,
    IReadOnlyDictionary<int, string> Genres);

/// <summary>
/// A film's TMDB details as stored in <c>movies/{id}.json</c>, stamped with when they were fetched so a
/// payload ages out on its own clock. The index entry cannot stand in for it: an override has none, and a
/// payload can outlive the entry that first pointed at it. <c>Movie.Genres</c> and <see cref="Cast"/> are kept here
/// because nothing else retains them: <c>Cast == null</c> means "never recorded" (fetched when a request can be spared),
/// while an empty list means TMDB has none.
/// </summary>
public sealed record StoredMovie(
    DateTimeOffset FetchedUtc, TmdbMovie? Movie, IReadOnlyList<TmdbCastMember>? Cast = null);

/// <summary>
/// Resolves every sheet row to a <see cref="MovieRecord"/>: override first, then the cache, then a TMDB
/// search. Every row is emitted, matched or not. An unmatched film comes from sheet data alone with
/// <c>I == 0</c> and <c>Mc == 0</c>; dropping it would silently under-report the library.
/// </summary>
/// <remarks>
/// <para><b>Sequential.</b> <see cref="CacheStore"/> is not thread-safe and <c>Resolve</c> mutates it on an alias hit.</para>
/// <para><b>Order.</b> Rows run in folded-title order (ordinal), not sheet order, so a budgeted run is resumed the
/// same way every time. <see cref="MoviePipelineResult.Records"/> comes out in that order too.</para>
/// <para><b>Cost.</b> An uncached film costs one search, one details call for each of the three most popular
/// candidates, and one credits call for each of those whose runtime is within tolerance plus every one named like
/// the film (when none agrees and none is named, the most popular): about 5 requests. The matched film's details
/// and its credits (genres, top-billed cast) are reused for its record, not refetched: the cast comes out of the same
/// credits response that gave the directors. A payload past its refresh window, and a film named by an override,
/// take details, genres and cast from one <c>append_to_response=credits</c> request.</para>
/// <para><b>Budget.</b> A budget trip mid-row makes that row's lookup incomplete: an exhausted client's empty or
/// partial answer is indistinguishable from "TMDB has no such film". The scored result is still used for this
/// run, but it is never cached, so the next run redoes the row instead of suppressing the film for 30 days.</para>
/// <para><b>Freshness.</b> A stored payload older than <see cref="RefreshPolicy.Window"/> is refetched, whatever the
/// index says. If it cannot be (budget spent) the stale copy is used for this run rather than a blank card.</para>
/// <para><b>Failures.</b> None of these are caught here. Transport faults, a bad status and a body that is not JSON
/// arrive as <see cref="HttpRequestException"/>. A details body that parses but whose typed members have the wrong
/// shape (<c>genres</c>, <c>belongs_to_collection</c>) throws <see cref="System.Text.Json.JsonException"/> out of
/// <c>Deserialize</c>, unwrapped, because that happens after the request layer has returned. Credits whose
/// <c>cast</c> or <c>crew</c> container has the wrong shape throw <see cref="InvalidDataException"/>, so a changed
/// TMDB response stops the run instead of caching a blank cast; a single unreadable cast member is skipped instead.
/// A malformed sheet runtime throws <see cref="FormatException"/> before any request is made. The cache index lives in
/// memory until the caller saves it, so a caller that wants the work kept across a failure must call
/// <see cref="CacheStore.SaveAsync"/> in a <c>finally</c>.</para>
/// </remarks>
public sealed class MoviePipeline(TmdbClient tmdb, CacheStore cache, Func<DateTimeOffset> clock)
{
    /// <summary>How many top-billed cast members a film keeps.</summary>
    public const int CastLimit = 4;

    public async Task<MoviePipelineResult> RunAsync(
        IReadOnlyList<SheetRow> rows,
        IReadOnlyDictionary<string, int> overrides,
        CancellationToken ct)
    {
        var records = new List<MovieRecord>(rows.Count);
        var review = new List<string>();
        var genreNames = new SortedDictionary<int, string>();   // only ids a record actually carries
        var now = clock();

        // Stable order so a budgeted run resumes predictably rather than arbitrarily.
        // Runtime breaks title ties, so two cuts of one film do not depend on the sheet's row order
        // (Apple's export order is not stable). Parsing it here also means a malformed runtime fails
        // before any request is spent, instead of partway through the run.
        var ordered = rows
            .Select(r => (Row: r, Title: TitleNormalizer.Normalize(r.Title), Runtime: RuntimeParser.FilmMinutes(r.Runtime)))
            .OrderBy(x => x.Title.Value, StringComparer.Ordinal)
            .ThenBy(x => x.Runtime)
            .ToList();

        foreach (var (row, title, runtime) in ordered)
        {
            ct.ThrowIfCancellationRequested();

            var directors = TitleNormalizer.NormalizeDirectors(row.Directors);
            var key = IdentityKey.ForFilm(title, runtime, directors);

            int? tmdbId = null;
            var confidence = 0d;
            TmdbMovie? fresh = null;   // the chosen film's details, when this run's search already fetched them
            IReadOnlyList<TmdbCastMember>? freshCast = null; // and its cast, when the credits gate fetched them
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

                // A flagged match is still waiting for a human, so it must not drop out of the queue
                // the moment it is cached. Accepted matches start at AcceptThreshold; overrides are 1.0.
                if (tmdbId is null) reviewNote = "unmatched";
                else if (confidence < MatchScorer.AcceptThreshold)
                    reviewNote = Describe(MatchOutcome.Flagged, confidence, tmdbId);
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
                var enrichment = await EnrichCandidatesAsync(candidates, title, runtime, ct);
                var match = MatchScorer.Score(title, runtime, row.Directors, enrichment.Candidates);

                tmdbId = match.TmdbId;
                confidence = match.Confidence;
                if (tmdbId is { } matched)
                {
                    enrichment.Details.TryGetValue(matched, out fresh);
                    enrichment.Cast.TryGetValue(matched, out freshCast);
                }

                // BudgetExhausted flips inside the call that hits the ceiling, so it can only be read
                // AFTER the lookups. An exhausted client returns the same empty or half-enriched result
                // as "TMDB has no such film". That result is still used for this run (the requests are
                // already paid for) but must never be cached: it would suppress the film for 30 days.
                var complete = !tmdb.BudgetExhausted;
                if (complete)
                    cache.Put(new CacheEntry(key, tmdbId, confidence, now,
                                             Negative: tmdbId is null, title.Value, runtime));

                if (match.Outcome != MatchOutcome.Accepted)
                    reviewNote = !complete && tmdbId is null
                        ? "not resolved, request budget spent"
                        : Describe(match.Outcome, confidence, tmdbId);
            }

            records.Add(await BuildRecordAsync(row, title, runtime, tmdbId, confidence, fresh, freshCast, genreNames, now, ct));
            if (reviewNote is not null)
                review.Add($"{row.Title} ({row.Runtime}, {row.Directors}) → {reviewNote}");
        }

        return new MoviePipelineResult(records, review, tmdb.RequestsUsed, tmdb.BudgetExhausted, genreNames);
    }

    private static string Describe(MatchOutcome outcome, double confidence, int? tmdbId)
        => $"{outcome} at {confidence.ToString("0.00", CultureInfo.InvariantCulture)}, " +
           $"id {tmdbId?.ToString(CultureInfo.InvariantCulture) ?? "none"}";

    private sealed record Enrichment(
        IReadOnlyList<MovieCandidate> Candidates,
        IReadOnlyDictionary<int, TmdbMovie> Details,
        IReadOnlyDictionary<int, IReadOnlyList<TmdbCastMember>> Cast);

    private async Task<Enrichment> EnrichCandidatesAsync(
        IReadOnlyList<MovieCandidate> candidates, NormalizedTitle title, int sheetRuntime, CancellationToken ct)
    {
        // Runtime and director carry 0.70 of the score but search results hold neither. Details
        // (which carry the runtime) are fetched for the three most popular candidates. Credits
        // (the directors) cost the same again, so they are fetched only where they can still
        // matter: runtime alone rules out most candidates, and director is a confirmation.
        var details = new Dictionary<int, TmdbMovie>();
        var cast = new Dictionary<int, IReadOnlyList<TmdbCastMember>>();
        var enriched = new List<MovieCandidate>();

        foreach (var c in candidates.OrderByDescending(c => c.Popularity).Take(3))
        {
            if (tmdb.BudgetExhausted) break;

            var movie = await tmdb.GetMovieAsync(c.TmdbId, ct);
            if (movie is not null) details[c.TmdbId] = movie;
            enriched.Add(c with { RuntimeMinutes = movie?.Runtime });
        }

        if (enriched.Count == 0) return new Enrichment(candidates, details, cast);

        var wantsCredits = enriched
            .Select(c => MatchScorer.RuntimeAgrees(c.RuntimeMinutes, sheetRuntime))
            .ToArray();

        // Every candidate named exactly like the film (folded on both sides) gets its credits,
        // whether or not its runtime agrees. An edition cut is routinely listed on TMDB at a very
        // different length, a decoy that happens to land within tolerance must not be the only
        // candidate whose director is known, and two films that share a name (a remake) can only be
        // told apart by director. There are at most three candidates here, so this adds at most two
        // requests to the typical one. When nothing agrees and nothing is named, the most popular is
        // the last resort, so a director signal survives a missing or wrong runtime.
        var anyNamed = false;
        for (var i = 0; i < enriched.Count; i++)
        {
            if (!MatchScorer.TitleAgrees(enriched[i].Title, title)) continue;

            wantsCredits[i] = true;
            anyNamed = true;
        }

        if (!anyNamed && !wantsCredits.Contains(true)) wantsCredits[0] = true;

        for (var i = 0; i < enriched.Count; i++)
        {
            if (!wantsCredits[i] || tmdb.BudgetExhausted) continue;

            // The same single request also carries the cast. Keep it: it is paid for, and the stored payload
            // cannot recover it later.
            var credits = await tmdb.GetCreditsAsync(enriched[i].TmdbId, CastLimit, ct);
            enriched[i] = enriched[i] with { Directors = credits?.Directors ?? [] };
            if (credits is not null) cast[enriched[i].TmdbId] = credits.Cast;
        }

        return new Enrichment(enriched, details, cast);
    }

    /// <summary>
    /// The film's details and cast for its record. Details fetched this run are the freshest there are and replace
    /// the stored copy. Otherwise the stored copy is used while it is inside the refresh window, and
    /// refetched (details, genres and cast in one request) once it is not. If it cannot be refetched, a stale
    /// copy still beats a blank card.
    /// </summary>
    /// <remarks>
    /// <c>Cast == null</c> in a payload means "never recorded" (credits skipped by the gate, or a budget trip), and
    /// is fetched once when a request can be spared. <c>[]</c> means TMDB has none, and is not asked for again.
    /// </remarks>
    private async Task<StoredMovie?> LoadDetailsAsync(
        int id, TmdbMovie? fresh, IReadOnlyList<TmdbCastMember>? freshCast, DateTimeOffset now, CancellationToken ct)
    {
        var path = $"movies/{id}.json";
        StoredMovie current;
        var changed = true;

        if (fresh is not null)
        {
            current = new StoredMovie(now, fresh, freshCast);
        }
        else
        {
            StoredMovie? stored;
            try { stored = await cache.ReadPayloadAsync<StoredMovie>(path, ct); }
            catch (JsonException) { stored = null; } // a write cut short: refetch and overwrite it rather than fail every later run

            if (stored?.Movie is not null && !RefreshPolicy.NeedsRefresh(stored.FetchedUtc, now))
            {
                current = stored;
                changed = false;
            }
            else if (!tmdb.BudgetExhausted && await tmdb.GetMovieWithCreditsAsync(id, CastLimit, ct) is { } fetched)
            {
                current = new StoredMovie(now, fetched.Movie, fetched.Cast);
            }
            else
            {
                return stored;
            }
        }

        if (current.Cast is null && !tmdb.BudgetExhausted)
        {
            var credits = await tmdb.GetCreditsAsync(id, CastLimit, ct);
            if (credits is not null || !tmdb.BudgetExhausted) // null and still within budget: TMDB has none
            {
                current = current with { Cast = credits?.Cast ?? [] };
                changed = true;
            }
        }

        if (changed) await cache.WritePayloadAsync(path, current, ct);
        return current;
    }

    private async Task<MovieRecord> BuildRecordAsync(
        SheetRow row, NormalizedTitle title, int runtime,
        int? tmdbId, double confidence, TmdbMovie? fresh, IReadOnlyList<TmdbCastMember>? freshCast,
        SortedDictionary<int, string> genreNames, DateTimeOffset now, CancellationToken ct)
    {
        var stored = tmdbId is { } id ? await LoadDetailsAsync(id, fresh, freshCast, now, ct) : null;
        var details = stored?.Movie;
        var genres = details?.Genres ?? [];
        foreach (var genre in genres) genreNames.TryAdd(genre.Id, genre.Name);
        var collection = details?.BelongsToCollection?.Name?.Trim();

        int? year = details?.ReleaseDate is { Length: >= 4 } d && int.TryParse(d[..4], out var y)
            ? y : title.YearHint;

        return new MovieRecord(
            I: tmdbId ?? 0,
            T: row.Title,
            Y: year,
            Rt: runtime,
            P: details?.PosterPath,
            B: details?.BackdropPath,
            G: [.. genres.Select(g => g.Id)],
            Ag: row.Genre,
            Vote: details?.VoteAverage ?? 0d,
            Dir: row.Directors.Split('&', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
            Ov: details?.Overview,
            Tag: details?.Tagline,
            Cast: [.. (stored?.Cast ?? []).Select(m => new CastMember(m.Name, m.Character ?? string.Empty, m.ProfilePath))],
            Col: string.IsNullOrEmpty(collection) ? null : collection,
            Own: string.IsNullOrWhiteSpace(row.PurchaseInfo) ? null : row.PurchaseInfo,
            // An unmatched film is always 0, even when its best candidate scored a little: the
            // scorer reports that sub-threshold score, but no match was made.
            Mc: tmdbId is null ? 0d : Math.Round(confidence, 4));
    }
}

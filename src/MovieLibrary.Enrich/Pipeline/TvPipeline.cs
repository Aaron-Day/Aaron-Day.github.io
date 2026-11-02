using System.Globalization;
using System.Text.Json;
using MovieLibrary.Core.Caching;
using MovieLibrary.Core.Identity;
using MovieLibrary.Core.Models;
using MovieLibrary.Core.Parsing;
using MovieLibrary.Core.Text;
using MovieLibrary.Enrich.Caching;
using MovieLibrary.Enrich.Tmdb;

namespace MovieLibrary.Enrich.Pipeline;

public sealed record TvPipelineResult(
    IReadOnlyList<ShowRecord> Shows, IReadOnlyList<string> ReviewQueue, bool StoppedOnBudget);

/// <summary>
/// A series' TMDB details as stored in <c>tv/{id}/series.json</c>, stamped with when they were fetched so the
/// payload ages out on its own clock. The index entry cannot stand in for it: it can be refreshed (or lost)
/// while the payload beside it is not.
/// </summary>
public sealed record StoredSeries(DateTimeOffset FetchedUtc, TmdbSeries? Series);

/// <summary>
/// One season's episode list as stored in <c>tv/{id}/season-{n}.json</c>, with its own <c>fetchedUtc</c>. The
/// stamp is what lets a part-fetched series resume season by season, and what stops a season outliving
/// TMDB's six-month limit just because the series entry above it was refreshed.
/// </summary>
public sealed record StoredSeason(DateTimeOffset FetchedUtc, TmdbSeason? Season);

/// <summary>
/// Resolves every series on the Sheet to a <see cref="ShowRecord"/>, once per series and never per episode:
/// a search, a details call, and one call per season, because a season call returns all of its episodes.
/// </summary>
/// <remarks>
/// <para><b>Sequential.</b> <see cref="CacheStore"/> is not thread-safe and <c>Resolve</c> mutates it on an alias
/// hit. Only <c>Resolve</c> and <c>Put</c> are used, and only the returned entry's <c>TmdbId</c> and
/// <c>Confidence</c> are read (an alias hit returns the source entry, whose key and runtime are not ours).</para>
/// <para><b>Order.</b> Series run in name order (ordinal) so a budgeted run resumes the same way every time.
/// Nothing depends on the order of the Sheet rows, which Apple does not keep stable: a series' name is its most
/// common spelling in the Sheet and its genre its most common genre, ties broken ordinally.</para>
/// <para><b>Overrides.</b> A series whose <see cref="OverrideKey"/> is in the overrides map takes that TMDB id
/// without a search, is <c>Mc</c> 1.0 (a human confirmed it, the only way to earn that unchecked) and gets no
/// review line. Only its details (one request, by id) and seasons are fetched. An override beats a cached
/// guess, and is not written to the index.</para>
/// <para><b>Is it the right show?</b> TMDB's first search result is only a guess. The search carries the Sheet
/// name's trailing year as <c>first_air_date_year</c>, and the result's name is folded and compared with the
/// Sheet's. An exact match is <c>Mc</c> 1.0; any other name is 0.5 and goes to the review queue naming both
/// names and the TMDB id. Rows without a series name are skipped and counted in one review line.</para>
/// <para><b>Slugs.</b> Every series' slug is checked before the first request. Two series that share one, or a
/// slug that is empty or starts with a hyphen, throw <see cref="InvalidDataException"/>: they would otherwise
/// overwrite each other's <c>data/tv/{slug}.json</c> without a sound.</para>
/// <para><b>Identity.</b> The cache key folds a trailing year back in, so <c>X</c> and <c>X (2020)</c> are two
/// series rather than one. Payloads are keyed by TMDB id, not slug, so a series that later resolves to a
/// different id cannot read the old id's seasons.</para>
/// <para><b>Seasons.</b> Seasons 1 to <c>number_of_seasons</c> are all fetched and every episode TMDB lists is
/// emitted with an <c>O</c> flag, so the owned-only view and the reveal toggle are both client-side filters.
/// A bare-named row can belong to any season, so the whole series is needed to place it. Season 0 (TMDB's
/// specials) is never fetched.</para>
/// <para><b>Matching.</b> A numbered row claims its episode by number, exactly. A bare-named row claims, by
/// folded name, the first unclaimed episode in season order, and is flagged when the name occurs more than
/// once. Numbered rows claim first. Sheet runtimes are never used. An owned row that claims nothing is kept,
/// never dropped: a numbered one as an extra episode in its own season, any other in season 0, the "unplaced"
/// group.</para>
/// <para><b>Budget.</b> A budget trip mid-lookup leaves that series unresolved: an exhausted client's null is
/// indistinguishable from "TMDB has no such series", so it is never cached as a negative. A trip between
/// seasons keeps the series and the seasons already fetched, each stored with its own stamp, and the next run
/// fetches only the rest.</para>
/// <para><b>Freshness.</b> A stored payload older than <see cref="RefreshPolicy.Window"/> is refetched whatever
/// the index says. If it cannot be (budget spent, or TMDB no longer finds the series) the cached id and the
/// stale copy are kept for this run rather than a blank one, and a lost series is queued for review.
/// A series payload has no by-id endpoint, so refreshing it repeats the search and details calls.</para>
/// <para><b>Failures.</b> None of these are caught here. Transport faults, a bad status and a body that is not JSON
/// arrive as <see cref="HttpRequestException"/>. A series or season body that parses but whose typed members have
/// the wrong shape throws <see cref="System.Text.Json.JsonException"/> out of <c>Deserialize</c>, unwrapped, because
/// that happens after the request layer has returned. This pipeline reads no credits, so the credits-shape
/// <see cref="InvalidDataException"/> that films can raise does not arise here. The cache
/// index lives in memory until the caller saves it, so a caller that wants the work kept across a failure must
/// call <see cref="CacheStore.SaveAsync"/> in a <c>finally</c>.</para>
/// </remarks>
public sealed class TvPipeline(TmdbClient tmdb, CacheStore cache, Func<DateTimeOffset> clock)
{
    private static readonly IReadOnlyDictionary<string, int> NoOverrides = new Dictionary<string, int>();

    /// <summary>
    /// A series has no directors, so it is cached with none and looked up with none. <see cref="CacheStore.Resolve"/>
    /// never aliases a row that has none: a series key is a pure function of its name, so there is no second entry to
    /// alias to, and the one thing an alias could do here is hand a series the id of a film that shares its folded
    /// title and runs two minutes or less.
    /// </summary>
    private const string NoDirectors = "";

    public Task<TvPipelineResult> RunAsync(IReadOnlyList<SheetRow> rows, CancellationToken ct)
        => RunAsync(rows, NoOverrides, ct);

    /// <param name="seriesOverrides">
    /// TMDB ids a human has confirmed, keyed by <see cref="OverrideKey"/>. An overridden series is never
    /// searched, takes the id as given, and is <c>Mc</c> 1.0 with no review line.
    /// </param>
    public async Task<TvPipelineResult> RunAsync(
        IReadOnlyList<SheetRow> rows, IReadOnlyDictionary<string, int> seriesOverrides, CancellationToken ct)
    {
        var now = clock();
        var review = new List<string>();

        var blank = rows.Count(r => string.IsNullOrWhiteSpace(r.Series));
        if (blank > 0) review.Add($"{blank} row(s) have no series name and were skipped");

        var allSeries = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Series))
            .GroupBy(r => r.Series.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => (Name: MostCommon(g.Select(r => r.Series.Trim())),
                          Genre: MostCommon(g.Select(r => r.Genre)),
                          Rows: g.ToList()))
            .OrderBy(s => s.Name, StringComparer.Ordinal)
            .Select(s => (s.Name, Slug: Slug.From(s.Name), s.Genre, s.Rows))
            .ToList();

        // Before a single request is spent: a bad slug means two shows sharing one output file.
        EnsureSlugsAreUsable(allSeries.Select(s => (s.Name, s.Slug)));

        var shows = new List<ShowRecord>(allSeries.Count);

        foreach (var (name, slug, genre, group) in allSeries)
        {
            ct.ThrowIfCancellationRequested();

            var owned = group.Select(r => EpisodeTitleParser.Parse(r.Title)).ToList();
            var series = await ResolveSeriesAsync(name, seriesOverrides, now, ct);
            foreach (var note in series.Notes) review.Add($"{name} → {note}");

            var loaded = series is { Id: { } id, Details: { } details }
                ? await LoadSeasonsAsync(id, details, now, ct)
                : new LoadedSeasons([], false);
            if (loaded.Incomplete) review.Add($"{name} → seasons incomplete, request budget spent");

            var placement = Place(loaded.Seasons, owned, name, review);
            if (placement.Unplaced > 0 && series.Details is not null && !loaded.Incomplete)
                review.Add($"{name} → {placement.Unplaced} owned row(s) not found on TMDB's list");

            shows.Add(new ShowRecord(
                I: series.Id ?? 0,
                T: name,
                Slug: slug,
                P: series.Details?.PosterPath,
                FirstYear: YearOf(series.Details?.FirstAirDate),
                Ag: genre,
                Seasons: placement.Seasons,
                OwnedEpisodes: placement.Seasons.Sum(s => s.Owned),
                TotalEpisodes: placement.Seasons.Sum(s => s.Total),
                Mc: series.Confidence));
        }

        return new TvPipelineResult(shows, review, tmdb.BudgetExhausted);
    }

    /// <summary>
    /// The key a series is looked up under in the overrides map and in the cache: the folded name with a
    /// trailing year folded back in, so <c>X</c> and <c>X (2020)</c> are different series.
    /// </summary>
    public static string OverrideKey(string seriesName) => KeyOf(TitleNormalizer.Normalize(seriesName));

    internal static string KeyOf(NormalizedTitle title)
        => title.YearHint is { } year
            ? $"{title.Value} {year.ToString(CultureInfo.InvariantCulture)}"
            : title.Value;

    private static void EnsureSlugsAreUsable(IEnumerable<(string Name, string Slug)> series)
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (name, slug) in series)
        {
            if (slug.Length == 0 || slug[0] == '-')
                throw new InvalidDataException(
                    $"The series '{name}' has the slug '{slug}', which is empty or starts with a hyphen. " +
                    "Rename it on the Sheet so it carries a real title.");

            if (!seen.TryAdd(slug, name))
                throw new InvalidDataException(
                    $"The series '{seen[slug]}' and '{name}' both have the slug '{slug}', so they would overwrite " +
                    "each other's data/tv file. Rename one of them on the Sheet.");
        }
    }

    private sealed record SeriesLookup(int? Id, TmdbSeries? Details, double Confidence, IReadOnlyList<string> Notes);

    /// <summary>
    /// TMDB's search gives its first result, which is only a guess. The same folded name as the Sheet's is a
    /// match (1.0); any other name is still used but is 0.5 and queued for review, naming both.
    /// </summary>
    private static (double Confidence, IReadOnlyList<string> Notes) Judge(NormalizedTitle sheet, TmdbSeries found)
    {
        if (string.Equals(TitleNormalizer.Normalize(found.Name).Value, sheet.Value, StringComparison.Ordinal))
            return (1d, []);

        return (0.5d, [$"matched TMDB \"{found.Name}\" (id {found.Id}), which is not the same name; override it if wrong"]);
    }

    private static string MostCommon(IEnumerable<string> values)
        => values.GroupBy(v => v, StringComparer.Ordinal)
                 .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
                 .First().Key;

    /// <summary>
    /// Cache first (a fresh negative included), then TMDB. Returns what is known about the series, and the
    /// review notes for a human to look at.
    /// </summary>
    private async Task<SeriesLookup> ResolveSeriesAsync(
        string name, IReadOnlyDictionary<string, int> overrides, DateTimeOffset now, CancellationToken ct)
    {
        var title = TitleNormalizer.Normalize(name);

        // Normalize moves a trailing year into YearHint, leaving "X" and "X (2020)" with one folded Value and so
        // one cache identity. Put the year back so they stay two series.
        var keyed = title with { Value = KeyOf(title) };

        // A human-confirmed id beats the search and the cache alike, and is never second-guessed.
        if (overrides.TryGetValue(keyed.Value, out var forcedId)) return await ResolveOverrideAsync(forcedId, now, ct);

        var key = IdentityKey.ForSeries(keyed);
        var hit = cache.Resolve(key, keyed.Value, 0, NoDirectors, now);

        // A fresh negative is an answer, not a miss. Searching again would spend a request every run.
        if (hit is { TmdbId: null }) return new SeriesLookup(null, null, 0d, ["series not found"]);

        TmdbSeries? stale = null;
        if (hit is { TmdbId: { } cachedId })
        {
            var stored = await ReadAsync<StoredSeries>($"tv/{cachedId}/series.json", ct);
            if (stored?.Series is { } kept)
            {
                if (!RefreshPolicy.NeedsRefresh(stored.FetchedUtc, now))
                {
                    var (confidence, notes) = Judge(title, kept);
                    return new SeriesLookup(cachedId, kept, confidence, notes);
                }

                stale = kept; // aged out: refetch, and fall back to this if TMDB cannot be asked or has lost it
            }
        }

        if (!tmdb.BudgetExhausted)
        {
            var found = await tmdb.SearchSeriesAsync(title.Cleaned, title.YearHint, ct);
            if (found is not null)
            {
                var (confidence, notes) = Judge(title, found);
                cache.Put(new CacheEntry(key, found.Id, confidence, now, Negative: false, keyed.Value, 0, NoDirectors));
                await cache.WritePayloadAsync($"tv/{found.Id}/series.json", new StoredSeries(now, found), ct);
                return new SeriesLookup(found.Id, found, confidence, notes);
            }

            // BudgetExhausted flips inside the call that hits the ceiling, so it can only be read AFTER the
            // call. A refused request returns the same null as "TMDB has no such series" and must never be
            // cached as one: it would suppress the series for 30 days. Neither may an empty answer overwrite
            // an id we already hold (below).
            if (!tmdb.BudgetExhausted && hit is null)
            {
                cache.Put(new CacheEntry(key, null, 0d, now, Negative: true, keyed.Value, 0, NoDirectors));
                return new SeriesLookup(null, null, 0d, ["series not found"]);
            }
        }

        // No fresh answer. Without a known id the series stays unresolved for this run and nothing is written,
        // so the next run redoes it.
        if (hit is not { TmdbId: { } knownId })
            return new SeriesLookup(null, null, 0d, ["not resolved, request budget spent"]);

        // With one, keep it. The index entry is left alone: a refused or empty lookup is not a reason to forget
        // a series TMDB gave us, and the next run asks again.
        var reason = tmdb.BudgetExhausted
            ? (stale is null ? "series details unavailable, request budget spent" : null)
            : $"TMDB no longer finds this series; keeping its cached id {knownId}";
        List<string> reasons = reason is null ? [] : [reason];

        if (stale is null) return new SeriesLookup(knownId, null, hit.Confidence, reasons);

        var (staleConfidence, staleNotes) = Judge(title, stale);
        return new SeriesLookup(knownId, stale, staleConfidence, [.. reasons, .. staleNotes]);
    }

    /// <summary>
    /// An overridden series: the id is a human's, so it is not searched for, not cached in the index and
    /// not judged (<c>Mc</c> 1.0, no review line). Its details are still needed for the poster and the season
    /// count, by id, on the same stamped payload as any other series.
    /// </summary>
    private async Task<SeriesLookup> ResolveOverrideAsync(int id, DateTimeOffset now, CancellationToken ct)
    {
        var path = $"tv/{id}/series.json";
        var stored = await ReadAsync<StoredSeries>(path, ct);

        if (stored?.Series is { } current && !RefreshPolicy.NeedsRefresh(stored.FetchedUtc, now))
            return new SeriesLookup(id, current, 1d, []);

        if (!tmdb.BudgetExhausted && await tmdb.GetSeriesAsync(id, ct) is { } fetched)
        {
            await cache.WritePayloadAsync(path, new StoredSeries(now, fetched), ct);
            return new SeriesLookup(id, fetched, 1d, []);
        }

        // No fresh details. An aged copy still beats none. With nothing at all, the id stands alone: a spent
        // budget will be retried, and an id TMDB does not know is a typo worth telling the user about.
        if (stored?.Series is { } aged) return new SeriesLookup(id, aged, 1d, []);

        return new SeriesLookup(id, null, 1d, [tmdb.BudgetExhausted
            ? "series details unavailable, request budget spent"
            : $"override id {id} is not a series TMDB knows"]);
    }

    private async Task<T?> ReadAsync<T>(string path, CancellationToken ct) where T : class
    {
        try { return await cache.ReadPayloadAsync<T>(path, ct); }
        catch (JsonException) { return null; } // a write cut short: refetch and overwrite rather than fail every later run
    }

    private static int? YearOf(string? date)
        => date is { Length: >= 4 } && int.TryParse(date.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var y)
            ? y : null;

    private sealed record LoadedSeason(int Number, TmdbSeason Season);

    private sealed record LoadedSeasons(IReadOnlyList<LoadedSeason> Seasons, bool Incomplete);

    /// <summary>
    /// Every season TMDB says the series has, each from its own stamped payload while that is fresh and from
    /// TMDB once it is not. <see cref="LoadedSeasons.Incomplete"/> means the budget ran out before a season
    /// that has no copy at all; the next run fetches only that.
    /// </summary>
    private async Task<LoadedSeasons> LoadSeasonsAsync(
        int seriesId, TmdbSeries details, DateTimeOffset now, CancellationToken ct)
    {
        var loaded = new List<LoadedSeason>();
        var incomplete = false;

        for (var number = 1; number <= details.NumberOfSeasons; number++)
        {
            var path = $"tv/{seriesId}/season-{number}.json";
            var stored = await ReadAsync<StoredSeason>(path, ct);

            if (stored?.Season is { } current && !RefreshPolicy.NeedsRefresh(stored.FetchedUtc, now))
            {
                loaded.Add(new LoadedSeason(number, current));
                continue;
            }

            if (!tmdb.BudgetExhausted && await tmdb.GetSeasonAsync(seriesId, number, ct) is { } fetched)
            {
                await cache.WritePayloadAsync(path, new StoredSeason(now, fetched), ct);
                loaded.Add(new LoadedSeason(number, fetched));
                continue;
            }

            // No fresh copy and no way to get one now. An aged copy still beats a blank season. With nothing
            // at all the season is pending when the budget is spent, and simply absent when TMDB said 404.
            if (stored?.Season is { } aged) loaded.Add(new LoadedSeason(number, aged));
            else if (tmdb.BudgetExhausted) incomplete = true;
        }

        return new LoadedSeasons(loaded, incomplete);
    }

    private sealed record Slot(int Season, int Episode, string Name);

    private sealed record Placement(SeasonRecord[] Seasons, int Unplaced);

    /// <summary>
    /// Lays the owned rows over TMDB's episodes. Pure: no requests, no clock.
    /// </summary>
    private static Placement Place(
        IReadOnlyList<LoadedSeason> loaded, IReadOnlyList<ParsedEpisodeTitle> owned,
        string seriesName, List<string> review)
    {
        // Every episode TMDB lists, in season then episode order: the slots an owned row can claim.
        var slots = loaded
            .SelectMany(s => s.Season.Episodes.Select(e =>
                new Slot(s.Number, e.EpisodeNumber, TitleNormalizer.Normalize(e.Name).Value)))
            .ToList();
        var listed = slots.Select(s => (s.Season, s.Episode)).ToHashSet();
        var claimed = new HashSet<(int Season, int Episode)>();
        var unplaced = new List<ParsedEpisodeTitle>();

        // Numbered rows claim first and by number alone, exactly: the name plays no part.
        foreach (var row in owned.Where(o => o.Season is not null))
        {
            var slot = (row.Season!.Value, row.Episode!.Value);
            if (listed.Contains(slot)) claimed.Add(slot);
            else unplaced.Add(row);
        }

        // A bare name is matched against this series' episodes only, never anyone else's.
        foreach (var row in owned.Where(o => o.Season is null).OrderBy(o => o.Name, StringComparer.Ordinal))
        {
            var name = TitleNormalizer.Normalize(row.Name).Value;
            var named = slots.Where(s => string.Equals(s.Name, name, StringComparison.Ordinal)).ToList();
            var free = named.FirstOrDefault(s => !claimed.Contains((s.Season, s.Episode)));

            if (free is null)
            {
                unplaced.Add(row);
                continue;
            }

            claimed.Add((free.Season, free.Episode));
            if (named.Count > 1)
                review.Add($"{seriesName} → \"{row.Name}\" names {named.Count} episodes; " +
                           $"assigned to season {free.Season} episode {free.Episode}");
        }

        var seasons = new SortedDictionary<int, List<EpisodeRecord>>();
        foreach (var (number, season) in loaded)
            seasons[number] = [.. season.Episodes.Select(e => new EpisodeRecord(
                E: e.EpisodeNumber,
                T: e.Name,
                O: claimed.Contains((number, e.EpisodeNumber)),
                Air: e.AirDate,
                Rt: e.Runtime,
                S: e.StillPath,
                Ov: e.Overview))];

        // An owned row that claimed nothing is still owned. A numbered one keeps its season and number.
        foreach (var row in unplaced.Where(o => o.Season >= 1)
                                    .OrderBy(o => o.Season).ThenBy(o => o.Episode)
                                    .ThenBy(o => o.Name, StringComparer.Ordinal))
        {
            if (!seasons.TryGetValue(row.Season!.Value, out var episodes))
                seasons[row.Season.Value] = episodes = [];

            if (episodes.Any(e => e.E == row.Episode)) continue; // a second row for the same episode

            episodes.Add(new EpisodeRecord(row.Episode!.Value, row.Name, true, null, null, null, null));
        }

        // Everything else has no place to hang on: season 0, the "unplaced episodes" group.
        var rest = unplaced.Where(o => o.Season is null or 0)
                           .OrderBy(o => o.Name, StringComparer.Ordinal).ThenBy(o => o.Episode)
                           .Select((o, i) => new EpisodeRecord(i + 1, o.Name, true, null, null, null, null))
                           .ToList();
        if (rest.Count > 0) seasons[0] = rest;

        var records = seasons.Select(kv =>
        {
            var eps = kv.Value.OrderBy(e => e.E).ToArray();
            return new SeasonRecord(kv.Key, eps.Count(e => e.O), eps.Length, eps);
        }).ToArray();

        return new Placement(records, unplaced.Count);
    }
}

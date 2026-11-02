using System.Net;
using MovieLibrary.Core.Models;
using MovieLibrary.Core.Sheets;
using MovieLibrary.Core.Throttling;
using MovieLibrary.Enrich.Caching;
using MovieLibrary.Enrich.Config;
using MovieLibrary.Enrich.Emit;
using MovieLibrary.Enrich.Pipeline;
using MovieLibrary.Enrich.Reporting;
using MovieLibrary.Enrich.Sheets;
using MovieLibrary.Enrich.Tmdb;

namespace MovieLibrary.Enrich.Run;

/// <summary>
/// One run of the tool: read the Sheet, enrich it from TMDB (or synthesise it, in fixture mode), write the static JSON,
/// and report. <c>Main</c> is a shell over this; everything it touches comes in through <see cref="RunServices"/>, so a
/// test runs the whole thing with a stub Sheet, a stub TMDB and a frozen clock. Single use.
/// </summary>
/// <remarks>
/// <para><b>Exit codes.</b> 0 when a catalogue was written, 1 on any fatal error. A budget stop is not an error (it is the
/// normal development path): it exits 0, but the result is never <see cref="RunResult.Deployable"/>, the summary says the
/// catalogue is incomplete, and <c>GITHUB_OUTPUT</c> carries <c>deployable=false</c> for Task 18's gate.</para>
/// <para><b>Any exception out of <c>CatalogWriter.WriteAsync</c> means do not deploy.</b> The publish is ordered and
/// retried but not atomic across files, so a failure that surfaces once the moves have begun can leave the output
/// folder mixing new and stale files. One that surfaces while staging published nothing at all and left the previous
/// build untouched: the writer says which (<c>CatalogWriter.MovesBegan</c>), and only the wording differs.</para>
/// <para><b>The cache is saved in a <c>finally</c>, under no token.</b> Every TMDB response already paid for is in it.
/// It is saved with <see cref="CancellationToken.None"/>: a run that is being cancelled is exactly when the save matters,
/// and a cancelled token would write nothing. It is not saved when it was never loaded and holds nothing (a forced run
/// that died at once would otherwise replace the real index with an empty one), and it is not entered at all when loading
/// failed, so a corrupt index is left as it was for the operator.</para>
/// <para><b>One clock.</b> <see cref="RunServices.Now"/> feeds the TMDB client, both pipelines and <c>meta.builtUtc</c>.</para>
/// </remarks>
public sealed class EnrichRun(EnrichOptions options, RunServices services)
{
    private enum Phase { Starting, Sheet, Cache, Films, Series, Publishing }

    private Phase _phase = Phase.Starting;

    /// <summary>
    /// This run's TMDB client, once there is one. <see cref="FailAsync"/> reads its budget state, so a run that spent
    /// the budget in the film phase and then failed in the series phase does not report <c>stopped_on_budget=false</c>.
    /// A fixture run never builds one and can never stop on the budget.
    /// </summary>
    private TmdbClient? _tmdb;

    /// <summary>
    /// The cache was saved, or had nothing worth saving. Reported for every outcome, the failures that never reach the
    /// cache included: nothing was lost in those, so it stays true.
    /// </summary>
    private bool _cacheSaved = true;

    /// <summary>A fatal error already put into words. Thrown from deep inside the run, reported once, at the top.</summary>
    private sealed class FatalRunException(string headline, string advice) : Exception(headline)
    {
        public string Advice { get; } = advice;
    }

    /// <summary>Everything the publish step needs, from either source.</summary>
    private sealed record Catalogue(
        IReadOnlyList<MovieRecord> Movies, IReadOnlyList<ShowRecord> Shows, IReadOnlyList<string> Review,
        IReadOnlyDictionary<int, string> Genres, int RequestsUsed, bool StoppedOnBudget, int CastMembersSkipped, bool Fixtures);

    public async Task<RunResult> RunAsync(CancellationToken ct)
    {
        try
        {
            return await RunCoreAsync(ct);
        }
        catch (FatalRunException ex)
        {
            return await FailAsync(ex.Message, ex.Advice);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return await FailAsync("Cancelled.",
                "Nothing was published. Responses already fetched were saved to the cache, so a re-run continues from here.");
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // Not the caller stopping the run but an HttpClient timeout: it cancels the client's own token and leaves
            // ct untouched, so the arm above declines it and without this one it reaches the catch-all and prints a
            // stack trace. TmdbClient normalises its own timeouts into HttpRequestException; the Sheet client does not,
            // so the phase picks the words here the same way it does for a request that failed outright.
            return await FailAsync($"Enrichment failed: {DescribeTimeout(_phase == Phase.Sheet)} ({ex.Message})",
                                   "Nothing was written.");
        }
        catch (SheetAccessException ex)
        {
            return await FailAsync($"FATAL: {ex.Message}",
                "The Sheet must stay link-shared (anyone with the link can view) for the anonymous CSV endpoint to work. " +
                "Nothing was written.");
        }
        catch (SheetSchemaException ex)
        {
            return await FailAsync($"FATAL: {ex.Message}",
                "A tab was renamed or its columns changed, so the Sheet no longer has the layout this tool reads. " +
                "Check the tab names and the column headers. Nothing was written.");
        }
        catch (HttpRequestException ex)
        {
            // Task 10 normalises every TMDB failure into HttpRequestException, and the Sheet client lets Google's through.
            // The same exception type means different things in the two phases, so the phase picks the words.
            var reason = _phase == Phase.Sheet ? DescribeSheetFailure(ex) : DescribeTmdbFailure(ex);
            return await FailAsync($"Enrichment failed: {reason} ({ex.Message})", "Nothing was written.");
        }
        catch (CatalogWriter.AttributionException ex)
        {
            // Ahead of the InvalidDataException arm, whose Publishing advice is about the Shows tab: this is the one
            // guard in the writer that no cell on the Sheet can trip or fix.
            return await FailAsync($"FATAL: {ex.Message}",
                "TMDB's terms fix the sentence, so this is a code change to revert, not something to correct on the " +
                "Sheet: pass CatalogMeta.TmdbAttribution. Nothing was written.");
        }
        catch (InvalidDataException ex)
        {
            return await FailAsync($"FATAL: {ex.Message}", _phase switch
            {
                Phase.Cache => "Delete the cache index (or restore it from git) to rebuild it at full request cost. Nothing was written.",
                Phase.Films => "TMDB sent a response in a shape this tool does not expect, and the run stopped on purpose rather " +
                               "than publish a catalogue with silent gaps. Nothing was written.",
                Phase.Series or Phase.Publishing =>
                    "Fix the title on the Shows tab or add an Overrides row, then re-run. Nothing was written.",
                _ => "Nothing was written."
            });
        }
        catch (Exception ex)
        {
            Warn(ex.StackTrace);
            return await FailAsync($"Enrichment failed: {ex.GetType().Name}: {ex.Message}", "Nothing was written.");
        }
    }

    private async Task<RunResult> RunCoreAsync(CancellationToken ct)
    {
        // Known before the Sheet is even fetched, so a missing secret costs nothing.
        if (!options.Fixtures && string.IsNullOrWhiteSpace(options.Token))
            throw new FatalRunException("FATAL: TMDB_TOKEN is not set.",
                "Add it to .env locally, or as an Actions *repository* secret in CI. Codespaces secrets are a separate " +
                "store and are not visible here. Nothing was written.");

        _phase = Phase.Sheet;
        var sheets = new GoogleSheetClient(services.SheetHttp);

        Say($"Fetching sheet {options.SheetId}...");
        var movieRows = await sheets.FetchMoviesAsync(options.SheetId, ct);
        var showRows = await sheets.FetchShowsAsync(options.SheetId, ct);
        Say($"  {movieRows.Count} films, {showRows.Count} episode rows");

        RequireReadableRuntimes(movieRows);

        if (options.Fixtures)
        {
            Say("Fixture mode: synthesising a full catalogue, no TMDB calls.");
            var (movies, shows) = FixtureGenerator.Build(movieRows, showRows);
            var synthetic = new Catalogue(movies, shows, [], FixtureGenerator.Genres,
                                          RequestsUsed: 0, StoppedOnBudget: false, CastMembersSkipped: 0, Fixtures: true);
            return await ConcludeAsync(await PublishAsync(synthetic, ct));
        }

        // FetchOverridesAsync cannot see the sheet rows, so a second call adds a note for every override that matches
        // none. Without it a drifted override says nothing: it simply stops applying.
        var overrides = (await sheets.FetchOverridesAsync(options.SheetId, ct)).WithUnmatchedNotes(movieRows, showRows);
        Say($"  {overrides.Films.Count} film overrides, {overrides.Series.Count} series overrides, {overrides.Notes.Count} notes");

        return await RunAgainstTmdbAsync(movieRows, showRows, overrides, ct);
    }

    /// <summary>
    /// Every Movies row's runtime is checked before the first request. One that cannot be read would otherwise abort the
    /// whole film pass with a bare format error naming no row, and a fixture run would die the same way.
    /// </summary>
    private static void RequireReadableRuntimes(IReadOnlyList<SheetRow> movieRows)
    {
        var bad = movieRows.Where(r => SheetOverrides.Minutes(r.Runtime) is null).ToList();
        if (bad.Count == 0) return;

        throw new FatalRunException(
            $"FATAL: {bad.Count} row(s) on the Movies tab have a runtime that cannot be read: " +
            string.Join("; ", bad.Select(r => $"'{r.Title}' (runtime '{r.Runtime}')")) + ".",
            "A film runtime is written h:mm:ss, such as 1:31:00. Fix the Runtime cell on the Movies tab, then re-run. " +
            "Nothing was spent or written.");
    }

    private async Task<RunResult> RunAgainstTmdbAsync(
        IReadOnlyList<SheetRow> movieRows, IReadOnlyList<SheetRow> showRows, SheetOverrides overrides, CancellationToken ct)
    {
        var cache = new CacheStore(options.CacheRoot);
        var loaded = false;
        if (!options.ForceRefresh)
        {
            _phase = Phase.Cache;
            await cache.LoadAsync(ct);     // before the try: a corrupt index must reach the operator, not be saved over
            loaded = true;
        }

        JobReport report;
        string? saveError;
        var completed = false;
        try
        {
            report = await PublishAsync(await EnrichAsync(cache, movieRows, showRows, overrides, ct), ct);
            completed = true;
        }
        finally
        {
            // Responses already paid for are never thrown away, whatever failed after them.
            saveError = await TrySaveCacheAsync(cache, loaded);
            _cacheSaved = saveError is null;
            if (saveError is not null && !completed) Warn($"ALSO: {saveError}");
        }

        // The catalogue is sound: the writer publishes in order with meta.json last, so a published run published all of
        // it. Losing this run's new cache entries costs requests later, not correctness, and failing here would withhold
        // a good catalogue AND make the re-run pay for every response again. The next run protects itself: refetching
        // under the default budget stops early, and a budget-stopped catalogue is already never deployable.
        if (saveError is not null)
        {
            Warn($"WARNING: {saveError}");
            report = report with { CacheSaveError = saveError };
        }

        return await ConcludeAsync(report);
    }

    /// <summary>Null when saved, or when there was nothing worth saving; otherwise what went wrong.</summary>
    private async Task<string?> TrySaveCacheAsync(CacheStore cache, bool loaded)
    {
        // A forced run starts from an empty index on purpose. If it dies before storing anything, saving would replace the
        // index on disk with nothing.
        if (!loaded && cache.Entries.Count == 0) return null;

        try
        {
            await cache.SaveAsync(CancellationToken.None);
            return null;
        }
        catch (Exception ex)
        {
            return $"the cache could not be saved ({ex.Message}). Every TMDB response this run paid for will be fetched again.";
        }
    }

    private async Task<Catalogue> EnrichAsync(
        CacheStore cache, IReadOnlyList<SheetRow> movieRows, IReadOnlyList<SheetRow> showRows,
        SheetOverrides overrides, CancellationToken ct)
    {
        // Kept on the run as well as here: FailAsync reads its budget state, so a failure that follows a budget stop
        // reports the stop rather than denying it.
        var tmdb = _tmdb = new TmdbClient(
            services.TmdbHttp, options.Token, services.Limiter, new RequestBudget(options.MaxRequests), services.Now);

        var (movieResult, tvResult) = await RunPipelinesAsync(tmdb, cache, movieRows, showRows, overrides, ct);

        return new Catalogue(
            movieResult.Records,
            tvResult.Shows,
            // The owner's own corrections failing come first: they are not a doubtful match, they are a mistake to fix.
            [.. overrides.Notes, .. movieResult.ReviewQueue, .. tvResult.ReviewQueue],
            movieResult.Genres,
            tmdb.RequestsUsed,
            movieResult.StoppedOnBudget || tvResult.StoppedOnBudget,
            tmdb.CastMembersSkipped,
            Fixtures: false);
    }

    /// <summary>
    /// The one place the two override maps are handed to their pipelines. <c>Films</c> and <c>Series</c> are both
    /// <c>IReadOnlyDictionary&lt;string, int&gt;</c>, so exchanging them compiles and then applies no override at all, with
    /// no error anywhere: a swap is a one-line fix here rather than a hunt.
    /// </summary>
    private async Task<(MoviePipelineResult Movies, TvPipelineResult Shows)> RunPipelinesAsync(
        TmdbClient tmdb, CacheStore cache, IReadOnlyList<SheetRow> movieRows, IReadOnlyList<SheetRow> showRows,
        SheetOverrides overrides, CancellationToken ct)
    {
        _phase = Phase.Films;
        var movieResult = await new MoviePipeline(tmdb, cache, services.Now)
            .RunAsync(movieRows, overrides.Films, ct);

        _phase = Phase.Series;
        var tvResult = await new TvPipeline(tmdb, cache, services.Now)
            .RunAsync(showRows, overrides.Series, ct);

        return (movieResult, tvResult);
    }

    /// <summary>Checks the genre table, builds <c>meta.json</c>'s content and writes everything. Returns what to report.</summary>
    /// <remarks>
    /// <c>meta.reviewQueue</c> is published <b>empty on purpose</b>, and the field is kept so the data contract does
    /// not change shape for the web app. The queue names the owner's own problem rows - a title that failed to match,
    /// the runtime his sheet has for it, an override that no longer matches anything - and <c>meta.json</c> is served
    /// publicly from the site, so it is reported where he actually reads it (the Actions job summary, via
    /// <see cref="JobReport.ReviewQueue"/>) and nowhere else. The owner chose this; do not "fix" it by filling it in.
    /// </remarks>
    private async Task<JobReport> PublishAsync(Catalogue c, CancellationToken ct)
    {
        _phase = Phase.Publishing;

        // Films TMDB identified. Both genre checks are about them, and so is the empty-cast count further down.
        var matched = c.Movies.Where(m => m.I != 0).ToList();

        // Each film's G holds genre ids and meta.Genres is how the UI turns them into words. An id with no name is a bare
        // number on the genre facet, and the writer cannot see that: this is the last place it can be caught.
        var unnamed = GenreMap.MissingNames(c.Genres, c.Movies);
        if (unnamed.Count > 0)
            throw new FatalRunException(
                $"FATAL: {unnamed.Count} genre id(s) carried by films have no name: {string.Join(", ", unnamed)}.",
                "The genre facet would show bare numbers, so nothing was written. TMDB sent a genre without a name, or a " +
                "cached film payload lost one.");

        // The check above is structurally unable to fire on an EMPTY table: MoviePipeline.BuildRecordAsync fills the
        // names from the same details.Genres list it reads MovieRecord.G from, so no genres means no ids AND no names,
        // and there is nothing left to compare. An absent or renamed `genres` key is null rather than an error
        // (TmdbModels), so that is the shape a changed TMDB response arrives in: every film's G empty, and the genre
        // facet - one of the three approved layouts - a blank panel site-wide, deployed, with the payloads that caused
        // it cached for five months. Task 14 made the same call for a changed `credits` shape: a field that vanishes
        // stops the run rather than publishing silent gaps.
        // A budget-stopped run is exempt, and must be. A film can be matched without its details ever loading - an
        // Overrides row forces an id without a lookup, and a cache-index hit whose payload file is gone resolves to
        // null - so once the budget is spent there can be matched films that contributed no genre name at all. That is
        // an ordinary budget stop, which Step C calls the normal development path, and failing it would be this guard
        // firing where its own reason does not reach. Nothing is risked by the exemption: a budget-stopped run is
        // never deployable, so a blank facet cannot reach the site through it.
        // Otherwise it cannot false-positive: a fixture run is handed FixtureGenerator.Genres, which is never empty
        // (19 names over the real library), and a run that matched nothing at all has matched.Count 0.
        if (!c.StoppedOnBudget && matched.Count > 0 && c.Genres.Count == 0)
            throw new FatalRunException(
                $"FATAL: {matched.Count} film(s) matched on TMDB, but not one genre name came back.",
                "The genre facet would be empty for the whole library, so nothing was written. TMDB has changed or " +
                "dropped the 'genres' field on a film's details, or every cached film payload lost it.");

        var meta = new CatalogMeta(
            BuiltUtc: services.Now().UtcDateTime.ToString("O"),
            MovieCount: c.Movies.Count,
            ShowCount: c.Shows.Count,
            TotalEpisodeCount: c.Shows.Sum(s => s.TotalEpisodes),
            Genres: GenreMap.ForMeta(c.Genres),
            Attribution: CatalogMeta.TmdbAttribution,
            ReviewQueue: [],                      // deliberately empty: see the remark on this method
            OwnedEpisodeCount: c.Shows.Sum(s => s.OwnedEpisodes));

        try
        {
            await services.Writer.WriteAsync(c.Movies, c.Shows, meta, ct);
        }
        catch (Exception ex) when (ex is InvalidDataException or CatalogWriter.AttributionException
                                   || (ex is OperationCanceledException && ct.IsCancellationRequested))
        {
            throw;   // thrown before anything was staged or published: reported by the top-level handlers
        }
        catch (Exception ex)
        {
            // Both outcomes are "do not deploy", but they send the operator to opposite ends of the problem and only
            // the writer knows which happened: staging is all-or-nothing, and the move phase that follows it is
            // ordered and retried but not atomic. Sending the owner to inspect an output folder that is untouched and
            // coherent wastes the one thing a failed run gives them.
            throw services.Writer.MovesBegan
                ? new FatalRunException(
                    $"FATAL: the catalogue may be only partly published ({ex.Message}).",
                    $"{options.OutputRoot} may now mix new and stale files. Do not deploy; re-run the tool.")
                : new FatalRunException(
                    $"FATAL: the catalogue could not be written ({ex.Message}).",
                    $"Nothing was published: {options.OutputRoot} still holds the previous build, exactly as it was. " +
                    "Fix the cause and re-run the tool.");
        }

        Say($"Wrote {c.Movies.Count} films and {c.Shows.Count} series to {options.OutputRoot}");
        if (c.StoppedOnBudget)
            Say($"Stopped on the {options.MaxRequests}-request budget. The catalogue was written for local preview but is " +
                "INCOMPLETE: series whose seasons were not fetched show episodes at season 0. Raise MAX_REQUESTS_PER_RUN " +
                "(0 = unlimited) and re-run before deploying.");

        return new JobReport(
            Films: c.Movies.Count, Series: c.Shows.Count,
            TotalEpisodes: meta.TotalEpisodeCount, OwnedEpisodes: c.Shows.Sum(s => s.OwnedEpisodes),
            RequestsUsed: c.RequestsUsed, MaxRequests: options.MaxRequests,
            StoppedOnBudget: c.StoppedOnBudget, Fixtures: c.Fixtures,
            ReviewQueue: c.Review,
            MatchedFilms: matched.Count,
            FilmsWithEmptyCast: matched.Count(m => m.Cast.Length == 0),
            CastMembersSkipped: c.CastMembersSkipped);
    }

    // ---- ending the run ------------------------------------------------------------------------------

    /// <summary>
    /// A catalogue was written. It is deployable only when it is complete and real: a budget stop is written for local
    /// preview and exits 0 (the normal development path), and a fixture run is synthetic.
    /// </summary>
    private async Task<RunResult> ConcludeAsync(JobReport report)
    {
        var result = new RunResult(0, report.StoppedOnBudget, Deployable: !report.StoppedOnBudget && !report.Fixtures,
                                   CacheSaved: _cacheSaved);

        await TryAsync(() => JobSummary.EmitAsync(
            Redact(JobSummary.Build(report)), services.Out, services.StepSummaryPath, CancellationToken.None));
        await WriteOutputsAsync(result);
        return result;
    }

    private async Task<RunResult> FailAsync(string headline, string advice)
    {
        Warn(headline);
        Warn(advice);

        // A run that spent the budget in the film phase and then failed in the series phase did stop on the budget.
        var result = new RunResult(1, StoppedOnBudget: _tmdb?.BudgetExhausted ?? false,
                                   Deployable: false, CacheSaved: _cacheSaved);
        await TryAsync(() => JobSummary.EmitAsync(
            Redact(JobSummary.BuildFailure(headline, advice)), TextWriter.Null, services.StepSummaryPath, CancellationToken.None));
        await WriteOutputsAsync(result);
        return result;
    }

    /// <summary>
    /// What the deploy step reads. <c>deployable</c> is the gate: true only for a complete, real catalogue. Written for
    /// every outcome, so a step that finds the file without the key has itself failed to run. <c>cache_saved</c> is not
    /// part of the gate - a catalogue that was published stays deployable - but a runner that cannot save its cache
    /// re-spends the whole budget on every run, and a banner on a page nobody has to read will not catch that.
    /// </summary>
    private async Task WriteOutputsAsync(RunResult result)
    {
        if (string.IsNullOrWhiteSpace(services.OutputFilePath)) return;

        var text = $"deployable={Flag(result.Deployable)}\nstopped_on_budget={Flag(result.StoppedOnBudget)}\n" +
                   $"cache_saved={Flag(result.CacheSaved)}\n";
        await TryAsync(() => File.AppendAllTextAsync(services.OutputFilePath, text, CancellationToken.None));
    }

    private static string Flag(bool value) => value ? "true" : "false";

    /// <summary>
    /// The summary and the outputs file are advice to other tools: failing to write one is a warning, not a verdict.
    /// Every exception, not only the two file ones - anything that escapes here reaches the catch-all, which says
    /// "Nothing was written.", and by this point a whole catalogue may have been published.
    /// </summary>
    private async Task TryAsync(Func<Task> write)
    {
        try { await write(); }
        catch (Exception ex)
        {
            Warn($"WARNING: could not write the job summary or outputs file ({ex.Message}).");
        }
    }

    /// <summary>
    /// Everything this run prints goes through <see cref="Say"/> or <see cref="Warn"/>, so redaction belongs to the
    /// print site instead of being something each caller has to remember. The token has reached a log twice before,
    /// once through a record's <c>ToString</c> and once through <c>JsonSerializer</c>, and these lines are built from
    /// exceptions, file paths and Sheet titles: text from outside this file.
    /// </summary>
    private void Say(string text) => services.Out.WriteLine(Redact(text));

    /// <inheritdoc cref="Say"/>
    private void Warn(string? text) => services.Error.WriteLine(text is null ? null : Redact(text));

    /// <summary>The token is never printed. Logs on a public repository's Actions run are world-readable.</summary>
    private string Redact(string text)
    {
        var token = options.Token.Trim();
        return token.Length == 0 ? text : text.Replace(token, "[redacted]", StringComparison.Ordinal);
    }

    // ---- what a failed request means -----------------------------------------------------------------

    internal static string DescribeTmdbFailure(HttpRequestException ex) => ex.StatusCode switch
    {
        // 401 and 403: the credential is wrong, and retrying will never help.
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
            "TMDB rejected the credential. Check the TMDB_TOKEN Actions secret.",
        // 429 and 5xx are retried; seeing one here means the retries ran out.
        HttpStatusCode.TooManyRequests => "TMDB rate-limited us and retries were exhausted.",
        { } code when (int)code >= 500 => $"TMDB returned {(int)code} and retries were exhausted.",
        { } code => $"TMDB returned {(int)code}.",
        // No status: a transport fault or a malformed body that survived every attempt.
        null => "A network or response-body fault survived five attempts."
    };

    internal static string DescribeSheetFailure(HttpRequestException ex) => ex.StatusCode switch
    {
        { } code => $"Google Sheets answered HTTP {(int)code}. Check SHEET_ID, and that the Sheet is still shared as " +
                    "'Anyone with the link can view'.",
        null => "A network fault stopped the request to Google Sheets."
    };

    /// <summary>
    /// An <see cref="HttpClient"/> timeout is not a failed request: it surfaces as a <see cref="TaskCanceledException"/>
    /// on the client's own token, and neither client sets a timeout, so both carry the 100-second default.
    /// <c>TmdbClient</c> normalises its own into <see cref="HttpRequestException"/> (Task 10); the Sheet client does
    /// not, so a Sheet that is merely slow needs the same advice here that a Google error status gets above, or it falls
    /// to the catch-all and the owner reads a stack trace.
    /// </summary>
    internal static string DescribeTimeout(bool sheet) => sheet
        ? "The request to Google Sheets timed out. Check SHEET_ID, and that the Sheet is still shared as " +
          "'Anyone with the link can view'."
        : "A TMDB request timed out.";
}

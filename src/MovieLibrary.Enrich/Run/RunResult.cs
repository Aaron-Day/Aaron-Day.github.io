namespace MovieLibrary.Enrich.Run;

/// <summary>What a run decided. The CLI turns <see cref="ExitCode"/> into the process exit code; Task 18's deploy gate
/// reads <see cref="Deployable"/>.</summary>
/// <param name="ExitCode">0 when the catalogue was written, 1 on any fatal error.</param>
/// <param name="StoppedOnBudget">The request budget ran out before every lookup finished.</param>
/// <param name="Deployable">
/// Exit 0 <b>and</b> a complete, real catalogue. False for a budget-stopped run (written for local preview only), for a
/// fixture run (synthetic data), and for every failure.
/// </param>
/// <param name="CacheSaved">
/// The TMDB cache was saved, or there was nothing worth saving. It never moves <see cref="Deployable"/>: a catalogue
/// that was published is sound whatever happened to the cache (see <c>EnrichRun.RunAgainstTmdbAsync</c>). It is
/// reported so the workflow can act on it, because a runner that loses its cache loses it on every run and re-spends
/// the whole request budget each time.
/// </param>
public sealed record RunResult(int ExitCode, bool StoppedOnBudget, bool Deployable, bool CacheSaved);

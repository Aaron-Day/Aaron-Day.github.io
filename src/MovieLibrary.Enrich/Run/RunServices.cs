using MovieLibrary.Core.Throttling;
using MovieLibrary.Enrich.Emit;

namespace MovieLibrary.Enrich.Run;

/// <summary>
/// Everything a run touches that is not pure: the two HTTP clients, the clock, the limiter, the writer, the console and
/// the two Actions files. <c>Main</c> builds the real ones (<see cref="EnrichCli"/>); a test builds fakes.
/// </summary>
/// <param name="Now">
/// The one clock of the run. It feeds the TMDB client (and through it the rate limiter), both pipelines' freshness
/// decisions, and <c>meta.builtUtc</c>. It must be non-decreasing: the limiter requires it. Production passes
/// <see cref="Time.MonotonicClock.Now"/>, never <c>DateTimeOffset.UtcNow</c>.
/// </param>
/// <param name="StepSummaryPath"><c>GITHUB_STEP_SUMMARY</c>, when running in Actions.</param>
/// <param name="OutputFilePath"><c>GITHUB_OUTPUT</c>, when running in Actions: where the deploy gate reads <c>deployable</c>.</param>
public sealed record RunServices(
    HttpClient SheetHttp,
    HttpClient TmdbHttp,
    Func<DateTimeOffset> Now,
    SlidingWindowRateLimiter Limiter,
    CatalogWriter Writer,
    TextWriter Out,
    TextWriter Error,
    string? StepSummaryPath = null,
    string? OutputFilePath = null) : IDisposable
{
    public void Dispose()
    {
        SheetHttp.Dispose();
        TmdbHttp.Dispose();
    }
}

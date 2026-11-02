using MovieLibrary.Core.Throttling;
using MovieLibrary.Enrich.Config;
using MovieLibrary.Enrich.Emit;
using MovieLibrary.Enrich.Time;

namespace MovieLibrary.Enrich.Run;

/// <summary>
/// What <c>Program.Main</c> does, in a form a test can call: read the options, build the real services, run, return the
/// exit code. The one thing it cannot do is reach the network from a test, so the HTTP handler is a parameter.
/// </summary>
public static class EnrichCli
{
    /// <param name="handler">Null in production. A test passes a stub, and both HTTP clients use it.</param>
    /// <returns>0 when a catalogue was written, 1 on any fatal error, including bad options.</returns>
    public static async Task<int> MainAsync(
        string[] args, IDictionary<string, string> env, TextWriter stdout, TextWriter stderr,
        CancellationToken ct, HttpMessageHandler? handler = null)
    {
        EnrichOptions options;
        try
        {
            options = EnrichOptions.Load(args, env);
        }
        catch (ArgumentException ex)
        {
            stderr.WriteLine($"FATAL: {ex.Message}");
            return 1;
        }

        using var services = CreateServices(options, env, stdout, stderr, handler);
        var result = await new EnrichRun(options, services).RunAsync(ct);
        return result.ExitCode;
    }

    /// <summary>
    /// What Ctrl+C does, in a form a test can call. The first press asks the run to stop instead of killing it, so the
    /// <c>finally</c> that saves the TMDB cache gets to run: a full seed takes tens of minutes and every response in it
    /// was paid for. A second press kills the process as usual. Returns whether to swallow the press.
    /// </summary>
    /// <remarks>
    /// The <see cref="ObjectDisposedException"/> covers the window between <c>MainAsync</c> returning and the process
    /// exiting, after the <c>using</c> has disposed the source. A press that lands there would otherwise go unhandled
    /// on the handler's own thread and turn a run that had already written <c>$GITHUB_OUTPUT</c> into a crash exit
    /// code. <see cref="CancellationTokenSource.IsCancellationRequested"/> survives disposal; <c>Cancel</c> does not.
    /// </remarks>
    internal static bool RequestStop(CancellationTokenSource cancel)
    {
        var swallow = !cancel.IsCancellationRequested;
        try { cancel.Cancel(); }
        catch (ObjectDisposedException) { /* the run is already over: there is nothing left to stop */ }
        return swallow;
    }

    /// <summary>
    /// The environment a run reads its options from, or null when it could not be read at all. <c>.env</c> is read with
    /// <c>File.ReadAllLines</c>, and in <c>Program</c> that call sits in top-level statements with no handler around
    /// it: a file that is held open or unreadable would crash with a bare stack trace, where every other bad input
    /// gets a <c>FATAL:</c> line and exit 1. Every exception, because an optional local file failing to be read must
    /// not be the loudest thing this tool can do.
    /// </summary>
    /// <param name="read">
    /// Test seam: a file that cannot be read is not portable to arrange, the same reason
    /// <see cref="CatalogWriter"/> has a seam for its moves. Null reads the real <c>.env</c>.
    /// </param>
    internal static Dictionary<string, string>? TryReadEnvironment(
        TextWriter stderr, Func<Dictionary<string, string>>? read = null)
    {
        try
        {
            return (read ?? (() => EnrichOptions.ReadEnvironment()))();
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"FATAL: the environment could not be read ({ex.Message}). A local .env file is " +
                             "optional: fix it or delete it. CI passes real environment variables and needs no file.");
            return null;
        }
    }

    internal static RunServices CreateServices(
        EnrichOptions options, IDictionary<string, string> env, TextWriter stdout, TextWriter stderr,
        HttpMessageHandler? handler = null)
    {
        // MUST be the monotonic clock, not () => DateTimeOffset.UtcNow. The rate limiter requires a non-decreasing source;
        // a backwards NTP step can stall it for the size of the step, and a full seed runs for tens of minutes. Nothing in
        // the type system enforces this. The same clock stamps the pipelines and meta.json, so two freshness decisions in
        // one run cannot disagree.
        var clock = new MonotonicClock();

        return new RunServices(
            SheetHttp: Client("https://docs.google.com/", handler),
            TmdbHttp: Client("https://api.themoviedb.org/", handler),
            Now: clock.Now,
            Limiter: new SlidingWindowRateLimiter(40, TimeSpan.FromSeconds(10)),
            Writer: new CatalogWriter(options.OutputRoot),
            Out: stdout,
            Error: stderr,
            StepSummaryPath: Get(env, "GITHUB_STEP_SUMMARY"),
            OutputFilePath: Get(env, "GITHUB_OUTPUT"));
    }

    // The base address needs its trailing slash: both clients send relative URLs.
    private static HttpClient Client(string baseAddress, HttpMessageHandler? handler)
        => new(handler ?? new HttpClientHandler(), disposeHandler: handler is null) { BaseAddress = new Uri(baseAddress) };

    private static string? Get(IDictionary<string, string> env, string key)
        => env.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
}

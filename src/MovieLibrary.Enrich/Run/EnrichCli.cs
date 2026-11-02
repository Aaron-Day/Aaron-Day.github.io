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

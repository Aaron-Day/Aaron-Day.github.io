using System.Net;
using MovieLibrary.Core.Models;
using MovieLibrary.Enrich.Config;
using MovieLibrary.Enrich.Run;
using MovieLibrary.Enrich.Time;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// <c>Program.Main</c> is a one-line shell over <see cref="EnrichCli"/>, and these tests pin what the shell wires: the real
/// clock is the monotonic one, the limiter is TMDB's 40 requests in 10 seconds, both base addresses end in a slash (the
/// clients resolve relative URLs against them), and the writer writes where the options say. Nothing here can reach the
/// network: the HTTP handler is a stub that fails any request it was not told to expect.
/// </summary>
public class EnrichCliTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static string TempDir() => Path.Combine(Path.GetTempPath(), "ml-cli-" + Guid.NewGuid());

    private static EnrichOptions Options(string outDir)
        => new("sheet-id", "eyJ.stub.token", 40, "cache", outDir, false, false);

    private static RunServices Services(string outDir, IDictionary<string, string>? env = null, StringWriter? stdout = null, StringWriter? stderr = null)
        => EnrichCli.CreateServices(Options(outDir), env ?? new Dictionary<string, string>(),
                                    stdout ?? new StringWriter(), stderr ?? new StringWriter(), new StubHandler(
                                        req => throw new Xunit.Sdk.XunitException($"Unexpected request: {req.RequestUri}")));

    [Fact]
    public void TheRealClockIsTheMonotonicOne_NotTheWallClock()
    {
        using var services = Services(TempDir());

        Assert.IsType<MonotonicClock>(services.Now.Target);
        var first = services.Now();
        Assert.True(services.Now() >= first);
    }

    [Fact]
    public void BothHttpClientsHaveABaseAddressWithATrailingSlash()
    {
        using var services = Services(TempDir());

        Assert.Equal(new Uri("https://docs.google.com/"), services.SheetHttp.BaseAddress);
        Assert.Equal(new Uri("https://api.themoviedb.org/"), services.TmdbHttp.BaseAddress);
    }

    [Fact]
    public void TheLimiterIsFortyRequestsInTenSeconds()
    {
        using var services = Services(TempDir());
        var t = RunHarness.T0;

        for (var i = 0; i < 39; i++) services.Limiter.Record(t);
        Assert.Equal(TimeSpan.Zero, services.Limiter.DelayBefore(t));                  // the fortieth is allowed at once

        services.Limiter.Record(t);
        Assert.Equal(TimeSpan.FromSeconds(10), services.Limiter.DelayBefore(t));       // the forty-first waits a whole window
    }

    [Fact]
    public async Task TheWriterWritesWhereTheOptionsSay()
    {
        var outDir = TempDir();
        try
        {
            using var services = Services(outDir);
            var meta = new CatalogMeta("2026-11-02T10:00:00.0000000Z", 0, 0, 0, new Dictionary<string, string>(),
                                       CatalogMeta.TmdbAttribution, []);

            await services.Writer.WriteAsync([], [], meta, Ct);

            Assert.True(File.Exists(Path.Combine(outDir, "meta.json")));
        }
        finally { if (Directory.Exists(outDir)) Directory.Delete(outDir, true); }
    }

    [Fact]
    public void TheActionsFilesComeFromTheEnvironmentAndAreAbsentWithoutIt()
    {
        var env = new Dictionary<string, string> { ["GITHUB_STEP_SUMMARY"] = "/tmp/summary.md", ["GITHUB_OUTPUT"] = "/tmp/out.txt" };

        using var inActions = Services(TempDir(), env);
        using var local = Services(TempDir());

        Assert.Equal("/tmp/summary.md", inActions.StepSummaryPath);
        Assert.Equal("/tmp/out.txt", inActions.OutputFilePath);
        Assert.Null(local.StepSummaryPath);
        Assert.Null(local.OutputFilePath);
    }

    [Fact]
    public void ABlankActionsVariableCountsAsAbsent()
    {
        var env = new Dictionary<string, string> { ["GITHUB_STEP_SUMMARY"] = "  ", ["GITHUB_OUTPUT"] = "" };

        using var services = Services(TempDir(), env);

        Assert.Null(services.StepSummaryPath);
        Assert.Null(services.OutputFilePath);
    }

    [Fact]
    public async Task DisposingTheServicesDisposesBothHttpClients()
    {
        var services = Services(TempDir());

        services.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => services.SheetHttp.GetAsync("x", Ct));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => services.TmdbHttp.GetAsync("x", Ct));
    }

    [Fact]
    public void TheConsoleIsTheOneThatWasPassedIn()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        using var services = Services(TempDir(), stdout: stdout, stderr: stderr);

        Assert.Same(stdout, services.Out);
        Assert.Same(stderr, services.Error);
    }

    // ---- MainAsync: the shell itself, with a handler that stands in for the network ----------------

    private static StubHandler NoNetwork()
        => new(req => throw new Xunit.Sdk.XunitException($"Unexpected request: {req.RequestUri}"));

    [Fact]
    public async Task ANegativeRequestLimitIsAFatalErrorBeforeAnythingIsFetched()
    {
        var stderr = new StringWriter();
        var handler = NoNetwork();

        var exit = await EnrichCli.MainAsync(["--max-requests", "-5"], new Dictionary<string, string>(),
                                             new StringWriter(), stderr, Ct, handler);

        Assert.Equal(1, exit);
        Assert.Contains("FATAL", stderr.ToString());
        Assert.Contains("request limit", stderr.ToString());
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AMissingTokenOutsideFixtureModeExitsOneWithoutTouchingTheNetwork()
    {
        var stderr = new StringWriter();
        var handler = NoNetwork();

        var exit = await EnrichCli.MainAsync(["--out", TempDir()], new Dictionary<string, string>(),
                                             new StringWriter(), stderr, Ct, handler);

        Assert.Equal(1, exit);
        Assert.Contains("TMDB_TOKEN", stderr.ToString());
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AFixtureRunThroughMainWritesTheCatalogueAndExitsZero()
    {
        var outDir = TempDir();
        var handler = new StubHandler(req => req.RequestUri!.Host == "docs.google.com"
            ? GoogleSheetClientTests.Csv(req.RequestUri.Query.Contains("sheet=Movies", StringComparison.Ordinal)
                ? RunLibrary.Movies(("Alien", "1:56:00", "Ridley Scott", "Sci-Fi"))
                : RunLibrary.Shows(("Season 1, Episode 1: Pilot", "The Office", "Comedy")))
            : throw new Xunit.Sdk.XunitException($"Unexpected request: {req.RequestUri}"));
        try
        {
            var stdout = new StringWriter();

            var exit = await EnrichCli.MainAsync(["--fixtures", "--out", outDir], new Dictionary<string, string>(),
                                                 stdout, new StringWriter(), Ct, handler);

            Assert.Equal(0, exit);
            Assert.True(File.Exists(Path.Combine(outDir, "meta.json")));
            Assert.Contains("Wrote 1 films and 1 series to", stdout.ToString());
            Assert.All(handler.Requests, r => Assert.Equal("docs.google.com", r.RequestUri!.Host));
        }
        finally { if (Directory.Exists(outDir)) Directory.Delete(outDir, true); }
    }

    [Fact]
    public async Task ABudgetStoppedRunStillExitsZeroThroughMain()
    {
        var outDir = TempDir();
        var cacheDir = TempDir();
        var handler = new StubHandler(req =>
        {
            if (req.RequestUri!.Host == "docs.google.com")
                return GoogleSheetClientTests.Csv(req.RequestUri.Query.Contains("sheet=Movies", StringComparison.Ordinal)
                    ? RunLibrary.Movies(("Alien", "1:56:00", "Ridley Scott", "Sci-Fi"), ("Brazil", "2:22:00", "Terry Gilliam", "Sci-Fi"))
                    : req.RequestUri.Query.Contains("sheet=Shows", StringComparison.Ordinal)
                        ? GoogleSheetClientTests.ShowsHeader
                        : GoogleSheetClientTests.OverridesHeader);

            return TestPipeline.Json(TestPipeline.EmptyResults);          // TMDB knows nothing
        });
        try
        {
            var env = new Dictionary<string, string> { ["TMDB_TOKEN"] = "eyJ.stub.token" };

            var exit = await EnrichCli.MainAsync(["--max-requests", "1", "--out", outDir, "--cache", cacheDir], env,
                                                 new StringWriter(), new StringWriter(), Ct, handler);

            Assert.Equal(0, exit);
            Assert.True(File.Exists(Path.Combine(outDir, "meta.json")));
            Assert.True(File.Exists(Path.Combine(cacheDir, "index.json")));
        }
        finally
        {
            foreach (var dir in new[] { outDir, cacheDir }) if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}

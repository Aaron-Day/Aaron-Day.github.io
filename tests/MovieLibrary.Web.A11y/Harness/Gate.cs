using System.Runtime.InteropServices;

namespace MovieLibrary.Web.A11y.Harness;

/// <summary>
/// Whether this machine can run a browser gate at all, and - when it cannot - the exact command that would
/// make it run. Every browser test in this project is attributed <see cref="BrowserFactAttribute"/> or
/// <see cref="BrowserTheoryAttribute"/>, which read <see cref="Unavailable"/> at DISCOVERY time and report the
/// test as Skipped rather than Failed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why skipping matters more here than anywhere else in the suite.</b> A fresh clone has neither a published
/// site nor a browser binary. A browser test that FAILED on such a machine would be a cry-wolf failure
/// indistinguishable from a real conformance defect, and the cure for cry-wolf failures is that somebody deletes
/// the project. So the absence of the harness is never a failure - with one deliberate exception, the CI guard in
/// <c>GateAvailabilityTests</c>, because a gate that silently skips in CI is worse than no gate.
/// </para>
/// <para>
/// <b>The skip mechanism, and why it is not <c>Assert.Skip</c>.</b> Rule T3: <c>Assert.Skip</c> is an xunit v3
/// API; <c>throw SkipException.ForSkip(...)</c> compiles here because the assert library is shared between the
/// two majors, and the v2 engine reports the throw as <b>Failed</b>. <c>[Fact(Skip = "...")]</c> is static, so
/// the condition is applied by overriding <c>FactAttribute.Skip</c>, which xunit v2 declares <c>virtual</c> and
/// reads off the live attribute instance during discovery. That yields a genuine Skipped result.
/// </para>
/// <para>
/// <b>No launch probe.</b> Discovery runs this for every test, so it only looks at environment variables and the
/// filesystem. Launching a browser to find out whether a browser can be launched would cost one process per test
/// case before a single assertion ran.
/// </para>
/// </remarks>
internal static class Gate
{
    /// <summary>A directory holding a published <c>wwwroot</c>. The tests serve it themselves.</summary>
    public const string SiteRootVariable = "A11Y_SITE_ROOT";

    /// <summary>An already-running server to point the browser at instead of serving the site here.</summary>
    public const string BaseUrlVariable = "A11Y_BASE_URL";

    /// <summary>
    /// A Playwright browser channel to use in place of the downloaded Chromium - <c>msedge</c> or <c>chrome</c>.
    /// It exists so that a developer machine with Edge on it needs no 130 MB download to run the gate.
    /// </summary>
    public const string ChannelVariable = "A11Y_BROWSER_CHANNEL";

    public static string? SiteRoot => Value(SiteRootVariable);

    public static string? BaseUrl => Value(BaseUrlVariable);

    public static string? Channel => Value(ChannelVariable);

    /// <summary>True when this process looks like a CI run, where a silent skip is itself a defect.</summary>
    public static bool InCi => Value("CI") is not null || Value("GITHUB_ACTIONS") is not null;

    /// <summary>
    /// Null when the gate can run; otherwise the reason, naming the command that fixes it. Computed on every
    /// read rather than cached, because the server fixture sets <see cref="BaseUrlVariable"/> for nothing and
    /// a cached "unavailable" taken before a fixture ran would be wrong for the rest of the process.
    /// </summary>
    public static string? Unavailable => NoSite() ?? NoBrowser();

    private static string? NoSite()
    {
        if (BaseUrl is not null) return null;

        if (SiteRoot is null)
            return $"No site to scan. Publish the app and point {SiteRootVariable} at it: "
                   + "`dotnet publish src/MovieLibrary.Web -c Release -o release` then "
                   + $"{SiteRootVariable}=release/wwwroot. Or set {BaseUrlVariable} to a server already running "
                   + "the published site at its root path.";

        var index = Path.Combine(SiteRoot, "index.html");
        return File.Exists(index)
            ? null
            : $"{SiteRootVariable} is set to \"{SiteRoot}\", which holds no index.html. It must be a PUBLISHED "
              + "wwwroot: `dotnet publish src/MovieLibrary.Web -c Release -o release` writes one to "
              + "release/wwwroot.";
    }

    private static string? NoBrowser()
    {
        if (Channel is not null) return null;
        if (BrowsersInstalled()) return null;

        var script = Path.Combine("tests", "MovieLibrary.Web.A11y", "bin", "Release", "net10.0", "playwright.ps1");
        return "No browser. Install Chromium once with "
               + $"`pwsh {script} install chromium --with-deps` (build the project in Release first, or the "
               + $"script will not be there), or set {ChannelVariable}=msedge to drive the Edge already on this "
               + "machine and download nothing.";
    }

    /// <summary>
    /// Whether Playwright's own browser cache holds a Chromium build. Mirrors Playwright's own lookup order:
    /// an explicit <c>PLAYWRIGHT_BROWSERS_PATH</c>, else the per-user cache, which differs by platform.
    /// </summary>
    private static bool BrowsersInstalled()
    {
        var root = Value("PLAYWRIGHT_BROWSERS_PATH") ?? DefaultBrowsersPath();

        return root is not null
               && Directory.Exists(root)
               && Directory.EnumerateDirectories(root, "chromium*").Any();
    }

    private static string? DefaultBrowsersPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ms-playwright");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? Join(home, "Library", "Caches", "ms-playwright")
            : Join(home, ".cache", "ms-playwright");
    }

    private static string? Join(string first, params string[] rest) =>
        first.Length == 0 ? null : Path.Combine([first, .. rest]);

    /// <summary>An unset variable and a blank one are the same thing, as everywhere else in this repository.</summary>
    private static string? Value(string name) =>
        Environment.GetEnvironmentVariable(name) is { } raw && raw.Trim().Length > 0 ? raw.Trim() : null;
}

using System.Runtime.InteropServices;
using System.Security;
using System.Text.Json;

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
    /// <summary>
    /// A directory holding a published <c>wwwroot</c>. The tests serve it themselves. A RELATIVE value is
    /// resolved against the repository root and not against the process working directory - see
    /// <see cref="Absolute"/>, which exists for one reason and is not a convenience.
    /// </summary>
    public const string SiteRootVariable = "A11Y_SITE_ROOT";

    /// <summary>An already-running server to point the browser at instead of serving the site here.</summary>
    public const string BaseUrlVariable = "A11Y_BASE_URL";

    /// <summary>
    /// A Playwright browser channel to use in place of the downloaded Chromium - <c>msedge</c> or <c>chrome</c>.
    /// It exists so that a developer machine with Edge on it needs no 130 MB download to run the gate.
    /// </summary>
    public const string ChannelVariable = "A11Y_BROWSER_CHANNEL";

    /// <summary>The file that marks the repository root. See <see cref="RepositoryRoot"/>.</summary>
    private const string SolutionFile = "MovieLibrary.slnx";

    /// <summary>Where the published site is, as an ABSOLUTE path. See <see cref="Absolute"/>.</summary>
    public static string? SiteRoot => Absolute(Value(SiteRootVariable));

    public static string? BaseUrl => Value(BaseUrlVariable);

    public static string? Channel => Value(ChannelVariable);

    /// <summary>True when this process looks like a CI run, where a silent skip is itself a defect.</summary>
    public static bool InCi => Value("CI") is not null || Value("GITHUB_ACTIONS") is not null;

    /// <summary>Null when the gate can run; otherwise the reason, naming the command that fixes it.</summary>
    public static string? Unavailable => NoSite() ?? NoBrowser();

    /// <summary>
    /// A configured path, made absolute. A relative value is resolved against the REPOSITORY ROOT rather
    /// than against the process working directory, and that is the whole of this method's reason to exist.
    /// </summary>
    /// <remarks>
    /// Under <c>dotnet test</c> the working directory is the TEST ASSEMBLY'S OUTPUT FOLDER, not the
    /// workspace. So the obvious reading of <c>A11Y_SITE_ROOT=release/wwwroot</c> - which is the value the
    /// skip message below hands the reader, and was the value the workflow passed - resolved to
    /// <c>tests/MovieLibrary.Web.A11y/bin/Release/net10.0/release/wwwroot</c>. Nothing is ever published
    /// there, so <see cref="NoSite"/> reported no site, every scan SKIPPED, and the gate scanned nothing
    /// while reading as though it had scanned fifteen states. Resolving against the repository root makes
    /// the documented command true and makes the value independent of how the runner chose to invoke the
    /// test host. The workflow passes an absolute path as well, so neither half depends on the other.
    /// <para>
    /// The walk up to <see cref="SolutionFile"/> is the one
    /// <c>MovieLibrary.Core.Tests.RepositoryFileTests.RepoRoot</c> already makes, for the same reason.
    /// </para>
    /// </remarks>
    internal static string? Absolute(string? value)
    {
        if (value is null) return null;
        if (Path.IsPathRooted(value)) return Path.GetFullPath(value);

        return RepositoryRoot() is { } root
            ? Path.GetFullPath(Path.Combine(root, value))
            : Path.GetFullPath(value);
    }

    /// <summary>
    /// The directory holding <see cref="SolutionFile"/>, walked up from this assembly's own location, or
    /// null when there is none above it - a published or copied-out test assembly, for instance.
    /// </summary>
    internal static string? RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, SolutionFile))) dir = dir.Parent;
        return dir?.FullName;
    }

    private static string? NoSite()
    {
        if (BaseUrl is not null) return null;

        if (SiteRoot is null)
            return $"No site to scan. Publish the app and point {SiteRootVariable} at it: "
                   + "`dotnet publish src/MovieLibrary.Web -c Release -o release` then "
                   + $"{SiteRootVariable}=release/wwwroot - a relative value is resolved against the "
                   + $"repository root, so it means what it reads as. Or set {BaseUrlVariable} to a server "
                   + "already running the published site at its root path.";

        var index = Path.Combine(SiteRoot, "index.html");

        // Deliberately NOT "you forgot to publish", which is what this used to say and was the wrong
        // half of the problem: the value can be right and its resolution wrong, so what it RESOLVED to
        // is the part a reader cannot work out for themselves.
        return File.Exists(index)
            ? null
            : $"{SiteRootVariable} resolved to \"{SiteRoot}\", which holds no index.html, so there is "
              + "nothing to scan there. That path has to be a published wwwroot - the directory `dotnet "
              + "publish src/MovieLibrary.Web -c Release -o release` writes to release/wwwroot. If the "
              + "value itself looks right then it is the resolution that is wrong: a relative value is "
              + "resolved against the repository root, which here is "
              + (RepositoryRoot() ?? $"NOT FOUND - no {SolutionFile} above {AppContext.BaseDirectory}")
              + ".";
    }

    /// <summary>Why no browser can be launched, or null when one can.</summary>
    /// <remarks>
    /// A MISMATCHED cache is a different answer from an empty one, and it has to be, because the two used
    /// to be the same answer and the wrong one. A cache left by a different Playwright version satisfies
    /// "some chromium directory exists" but not Playwright's own launch, which throws - from the collection
    /// fixture, which xunit then reports as every browser test FAILED. That is exactly the cry-wolf outcome
    /// this class exists to prevent, so the expected build number is checked and a mismatch SKIPS.
    /// <para>
    /// <b>This must never throw, because it runs at DISCOVERY.</b> xunit reads
    /// <see cref="BrowserFactAttribute.Skip"/> while it enumerates tests, and the collection fixture reads
    /// <see cref="Unavailable"/> again, so an exception from here is not one failed test: with a cache
    /// directory the process may not list, all 29 tests in this project came back Failed. That is the same
    /// cry-wolf outcome the revision check exists to prevent, reached by a different route. A cache or a
    /// <c>browsers.json</c> that exists but cannot be read (an ACL, a path the platform will not open) is a
    /// machine that cannot answer whether a browser is installed, and the honest answer to that is a skip
    /// that says so. The whole body is inside the one handler below for that reason, rather than each read
    /// guarding itself: the next disk read added here is covered without anyone having to remember.
    /// </para>
    /// </remarks>
    private static string? NoBrowser()
    {
        if (Channel is not null) return null;

        try
        {
            return NoCachedBrowser();
        }
        catch (Exception e) when (DiskCannotBeRead(e))
        {
            return "Whether a browser is installed could not be checked, because looking for one on the "
                   + $"disk threw {e.GetType().Name}: {e.Message} Make that location readable "
                   + $"(PLAYWRIGHT_BROWSERS_PATH names the cache when it is set), or set {ChannelVariable}"
                   + "=msedge to drive the Edge already on this machine without consulting the cache at all.";
        }
    }

    /// <summary>
    /// The reads of the disk that mean "this machine will not let me look", as opposed to a defect in this
    /// code. <see cref="UnauthorizedAccessException"/> and <see cref="NotSupportedException"/> derive from
    /// <see cref="SystemException"/> and NOT from <see cref="IOException"/>, so catching IOException alone
    /// misses the two a permissions problem actually produces.
    /// </summary>
    private static bool DiskCannotBeRead(Exception e) =>
        e is IOException or UnauthorizedAccessException or NotSupportedException or SecurityException;

    private static string? NoCachedBrowser()
    {
        var cache = BrowserCache();
        var cached = cache is null
            ? Array.Empty<string?>()
            : Directory.EnumerateDirectories(cache, "chromium*").Select(Path.GetFileName).ToArray();
        var wanted = ExpectedChromiumRevision();

        if (wanted is null)
        {
            // No browsers.json beside the assembly, so there is nothing to compare against and calling it
            // a mismatch would be a guess. Fall back to the loose check this used to make on its own.
            if (cached.Length > 0) return null;
        }
        else if (cached.Contains($"chromium-{wanted}") || cached.Contains($"chromium_headless_shell-{wanted}"))
        {
            return null;
        }
        else if (cached.Length > 0)
        {
            return "The cached Chromium is not the build this Playwright wants, so launching it would throw "
                   + "from the collection fixture and xunit would report every browser test as FAILED. "
                   + $"Microsoft.Playwright here expects revision {wanted}; the cache at \"{cache}\" holds "
                   + $"{string.Join(", ", cached)}. Run `pwsh {InstallScript()} install chromium --with-deps` "
                   + $"to add the build it wants, or set {ChannelVariable}=msedge to drive the Edge already "
                   + "on this machine and download nothing.";
        }

        return "No browser. Install Chromium once with "
               + $"`pwsh {InstallScript()} install chromium --with-deps` (build the project in Release "
               + $"first, or the script will not be there), or set {ChannelVariable}=msedge to drive the "
               + "Edge already on this machine and download nothing.";
    }

    private static string InstallScript() =>
        Path.Combine("tests", "MovieLibrary.Web.A11y", "bin", "Release", "net10.0", "playwright.ps1");

    /// <summary>
    /// The Chromium build number the <c>Microsoft.Playwright</c> in this output expects, read from the
    /// <c>browsers.json</c> its package copies beside the assembly, or null when that file is not there.
    /// </summary>
    private static string? ExpectedChromiumRevision()
    {
        var file = Path.Combine(AppContext.BaseDirectory, ".playwright", "package", "browsers.json");
        if (!File.Exists(file)) return null;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));

            if (!document.RootElement.TryGetProperty("browsers", out var browsers)
                || browsers.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var browser in browsers.EnumerateArray())
                if (browser.ValueKind == JsonValueKind.Object
                    && browser.TryGetProperty("name", out var name)
                    && name.ValueKind == JsonValueKind.String
                    && name.GetString() == "chromium"
                    && browser.TryGetProperty("revision", out var revision)
                    && revision.ValueKind == JsonValueKind.String)
                    return revision.GetString();
        }
        catch (Exception e) when (e is JsonException || DiskCannotBeRead(e))
        {
            return null;   // a file or a shape this cannot read is one it cannot check against
        }

        return null;
    }

    /// <summary>
    /// Playwright's own browser cache directory, by Playwright's own lookup order: an explicit
    /// <c>PLAYWRIGHT_BROWSERS_PATH</c>, else the per-user cache, which differs by platform. Null when the
    /// directory is not there at all.
    /// </summary>
    private static string? BrowserCache()
    {
        var root = Value("PLAYWRIGHT_BROWSERS_PATH") ?? DefaultBrowsersPath();
        return root is not null && Directory.Exists(root) ? root : null;
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

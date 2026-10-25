using MovieLibrary.Enrich.Config;

namespace MovieLibrary.Core.Tests;

public class EnrichOptionsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ml-opts-" + Guid.NewGuid());
    private readonly List<string> _envVars = [];

    public void Dispose()
    {
        foreach (var name in _envVars) Environment.SetEnvironmentVariable(name, null);
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private static EnrichOptions Load(string[] args, params (string Key, string Value)[] env)
        => EnrichOptions.Load(args, env.ToDictionary(e => e.Key, e => e.Value));

    // ---- Load: defaults and precedence -------------------------------------------------

    [Fact]
    public void DefaultsNeedNeitherArgumentsNorEnvironment()
    {
        var o = Load([]);

        Assert.Equal("1gkDwOi5-Gvm48WkYz8noYUb5i6QjfQSDLpEDYV5QlHk", o.SheetId);
        Assert.Equal(EnrichOptions.DefaultSheetId, o.SheetId);
        Assert.Equal(string.Empty, o.Token);
        Assert.Equal(40, o.MaxRequests);
        Assert.Equal("enrich-cache", o.CacheRoot);
        Assert.Equal("src/MovieLibrary.Web/wwwroot/data", o.OutputRoot);
        Assert.False(o.Fixtures);
        Assert.False(o.ForceRefresh);
    }

    [Fact]
    public void FixtureModeWritesToTheGitignoredDevDataFolderNotTheDeployedOne()
        => Assert.Equal("dev-data", Load(["--fixtures"]).OutputRoot);

    [Fact]
    public void AnExplicitOutFolderWinsInBothModes()
    {
        Assert.Equal("elsewhere", Load(["--out", "elsewhere"]).OutputRoot);
        Assert.Equal("elsewhere", Load(["--fixtures", "--out", "elsewhere"]).OutputRoot);
    }

    [Fact]
    public void TheEnvironmentSuppliesTheTokenOutsideFixtureMode()
        => Assert.Equal("eyJ.a.b", Load([], ("TMDB_TOKEN", "eyJ.a.b")).Token);

    [Fact]
    public void FixtureModeDropsAnyTokenItIsHandedSoNoRequestCanAuthenticate()
        // A developer's .env usually holds a real token. Fixture mode must never carry it onward.
        => Assert.Equal(string.Empty, Load(["--fixtures"], ("TMDB_TOKEN", "eyJ.a.b")).Token);

    [Fact]
    public void FlagsOverrideTheEnvironmentForSheetCacheAndBudget()
    {
        var o = Load(["--sheet-id", "flag-sheet", "--cache", "flag-cache", "--max-requests", "7"],
                     ("SHEET_ID", "env-sheet"), ("MAX_REQUESTS_PER_RUN", "99"));

        Assert.Equal("flag-sheet", o.SheetId);
        Assert.Equal("flag-cache", o.CacheRoot);
        Assert.Equal(7, o.MaxRequests);
    }

    [Fact]
    public void TheEnvironmentSuppliesSheetAndBudgetWhenNoFlagIsGiven()
    {
        var o = Load([], ("SHEET_ID", "env-sheet"), ("MAX_REQUESTS_PER_RUN", "99"));

        Assert.Equal("env-sheet", o.SheetId);
        Assert.Equal(99, o.MaxRequests);
    }

    [Theory]
    [InlineData("")]            // an unset Actions variable arrives as an empty string
    [InlineData("forty")]
    [InlineData("4o")]
    public void AnUnreadableBudgetFallsBackToTheSafeDefaultRatherThanFailingTheRun(string raw)
        => Assert.Equal(40, Load([], ("MAX_REQUESTS_PER_RUN", raw)).MaxRequests);

    [Fact]
    public void ZeroIsKeptBecauseTheBudgetTreatsItAsUnlimited()
        => Assert.Equal(0, Load(["--max-requests", "0"]).MaxRequests);

    [Fact]
    public void ForceRefreshComesFromEitherTheFlagOrTheEnvironment()
    {
        Assert.True(Load(["--force-refresh"]).ForceRefresh);
        Assert.True(Load([], ("FORCE_REFRESH", "true")).ForceRefresh);
        Assert.False(Load([], ("FORCE_REFRESH", "false")).ForceRefresh);
        Assert.False(Load([]).ForceRefresh);
    }

    [Theory]
    [InlineData("--sheet-id")]
    [InlineData("--cache")]
    [InlineData("--out")]
    [InlineData("--max-requests")]
    public void AFlagWithNoValueFallsBackToTheDefaultInsteadOfCrashing(string flag)
    {
        var o = Load([flag]);

        Assert.Equal(EnrichOptions.DefaultSheetId, o.SheetId);
        Assert.Equal("enrich-cache", o.CacheRoot);
        Assert.Equal("src/MovieLibrary.Web/wwwroot/data", o.OutputRoot);
        Assert.Equal(40, o.MaxRequests);
    }

    // ---- ReadEnvironment ---------------------------------------------------------------

    private string EnvFile(params string[] lines)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, ".env");
        File.WriteAllLines(path, lines);
        return path;
    }

    private string UniqueName(string stem)
    {
        var name = $"ML_T15_{stem}_{Guid.NewGuid():N}";
        _envVars.Add(name);
        return name;
    }

    [Fact]
    public void ReadsKeyValueLinesFromTheEnvFile()
    {
        var key = UniqueName("PLAIN");
        var values = EnrichOptions.ReadEnvironment(EnvFile($"{key}=plain-value"));

        Assert.Equal("plain-value", values[key]);
    }

    [Fact]
    public void IgnoresBlankLinesCommentsAndLinesWithoutAKey()
    {
        var kept = UniqueName("KEPT");
        var noEquals = UniqueName("NOEQUALS");
        var commented = UniqueName("COMMENTED");
        var values = EnrichOptions.ReadEnvironment(EnvFile(
            "", "   ", "# a comment", $"   # {commented}=never",
            noEquals, "=orphan-value", $"{kept}=yes"));

        Assert.Equal("yes", values[kept]);
        Assert.False(values.ContainsKey(noEquals));
        Assert.DoesNotContain(values.Keys, k => k.Contains(commented, StringComparison.Ordinal));
        Assert.DoesNotContain("orphan-value", values.Values);
    }

    [Fact]
    public void TrimsWhitespaceAndStripsQuotesAroundAValue()
    {
        var a = UniqueName("QUOTED");
        var b = UniqueName("SPACED");
        var values = EnrichOptions.ReadEnvironment(EnvFile($"{a}=\"quoted value\"", $"  {b}  =  spaced  "));

        Assert.Equal("quoted value", values[a]);
        Assert.Equal("spaced", values[b]);
    }

    [Fact]
    public void ValueKeepsEveryEqualsSignAfterTheFirstSoPaddedTokensSurvive()
    {
        var key = UniqueName("PADDED");
        var values = EnrichOptions.ReadEnvironment(EnvFile($"{key}=abc==="));

        Assert.Equal("abc===", values[key]);
    }

    [Fact]
    public void AnEmptyValueIsTreatedAsUnsetNotAsAnEmptyOverride()
    {
        var key = UniqueName("EMPTY");
        var values = EnrichOptions.ReadEnvironment(EnvFile($"{key}=", $"{key}=\"\""));

        Assert.False(values.ContainsKey(key));
    }

    [Fact]
    public void RealEnvironmentVariablesWinSoCiIsNeverShadowedByAStrayFile()
    {
        var key = UniqueName("PRECEDENCE");
        Environment.SetEnvironmentVariable(key, "from-the-process");

        var values = EnrichOptions.ReadEnvironment(EnvFile($"{key}=from-the-file"));

        Assert.Equal("from-the-process", values[key]);
    }

    [Fact]
    public void AMissingEnvFileIsFineAndTheProcessEnvironmentStillComesThrough()
    {
        var key = UniqueName("NOFILE");
        Environment.SetEnvironmentVariable(key, "present");

        var values = EnrichOptions.ReadEnvironment(Path.Combine(_dir, "does-not-exist.env"));

        Assert.Equal("present", values[key]);
    }

    [Fact]
    public void KeysAreCaseInsensitiveLikeWindowsEnvironmentVariables()
    {
        var key = UniqueName("CASE");
        var values = EnrichOptions.ReadEnvironment(EnvFile($"{key}=v"));

        Assert.Equal("v", values[key.ToLowerInvariant()]);
    }
}

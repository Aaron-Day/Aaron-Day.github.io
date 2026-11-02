using System.Text.Json;
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
        Assert.True(Load(["--force-refresh", "--max-requests", "0"]).ForceRefresh);
        Assert.True(Load(["--max-requests", "0"], ("FORCE_REFRESH", "true")).ForceRefresh);
        Assert.False(Load([], ("FORCE_REFRESH", "false")).ForceRefresh);
        Assert.False(Load([]).ForceRefresh);
    }

    /// <summary>
    /// A full refresh of this library needs thousands of requests. Under a budget it re-resolves the first few films
    /// and then replaces the committed index with only those: a forced run never loads the old one, and SaveAsync is a
    /// whole-file replace. There is no flag to force-refresh a subset, so the combination has no use and is refused.
    /// </summary>
    [Theory]
    [InlineData("40")]
    [InlineData("1")]
    public void AForcedRefreshUnderABudgetIsRejectedWhicheverWayTheBudgetArrived(string budget)
    {
        var fromFlag = Assert.Throws<ArgumentException>(() => Load(["--force-refresh", "--max-requests", budget]));
        var fromEnv = Assert.Throws<ArgumentException>(() => Load(["--force-refresh"], ("MAX_REQUESTS_PER_RUN", budget)));

        Assert.Contains("--max-requests 0", fromFlag.Message);      // says how to do what was meant
        Assert.Contains("--max-requests 0", fromEnv.Message);
    }

    [Fact]
    public void TheRefusalCoversTheCiPathWhereNeitherSideIsNamedAtAll()
        // FORCE_REFRESH=true on a manual dispatch, with the budget left at its silent default of 40: the dangerous one.
        => Assert.Contains("--max-requests 0",
               Assert.Throws<ArgumentException>(() => Load([], ("FORCE_REFRESH", "true"))).Message);

    [Fact]
    public void AForcedRefreshWithNoBudgetAtAllIsTheOneLegitimateCombination()
    {
        Assert.True(Load(["--force-refresh", "--max-requests", "0"]).ForceRefresh);
        Assert.True(Load(["--force-refresh", "--max-requests=0"]).ForceRefresh);
        Assert.True(Load(["--force-refresh"], ("MAX_REQUESTS_PER_RUN", "0")).ForceRefresh);
        Assert.Equal(0, Load(["--force-refresh", "--max-requests", "0"]).MaxRequests);
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

    // ---- the token never reaches a log; bad budgets and empty values are caught -------------

    [Fact]
    public void ToStringNeverPrintsTheTokenSoALogLineCannotLeakIt()
    {
        const string token = "eyJhbGciOiJIUzI1NiJ9.live-secret-value.signature";
        var o = Load(["--max-requests", "7", "--cache", "my-cache"], ("TMDB_TOKEN", token), ("SHEET_ID", "my-sheet"));

        var text = o.ToString();

        Assert.DoesNotContain(token, text);
        Assert.DoesNotContain("live-secret-value", text);
        Assert.DoesNotContain("eyJ", text);
        Assert.DoesNotContain("live-secret-value", $"{o}");
        Assert.DoesNotContain("live-secret-value", string.Format("{0}", o));
        Assert.DoesNotContain("live-secret-value", (o with { MaxRequests = 1 }).ToString());
        // and it is still useful for debugging: everything else is there
        Assert.Contains("my-sheet", text);
        Assert.Contains("my-cache", text);
        Assert.Contains("7", text);
        Assert.Contains("<redacted>", text);
    }

    [Fact]
    public void SerialisingTheOptionsNeverWritesTheTokenEither()
    {
        var o = Load([], ("TMDB_TOKEN", "eyJhbGciOiJIUzI1NiJ9.live-secret-value.signature"), ("SHEET_ID", "my-sheet"));

        var outputs = new[]
        {
            JsonSerializer.Serialize(o),
            JsonSerializer.Serialize<object>(o),
            JsonSerializer.Serialize(o, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }),
            JsonSerializer.Serialize(o, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
        };

        Assert.All(outputs, json =>
        {
            Assert.DoesNotContain("live-secret-value", json);
            Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);     // not even the property name
            Assert.Contains("my-sheet", json);                                              // the rest is still written
        });
    }

    [Fact]
    public void ToStringSaysWhenThereIsNoTokenRatherThanPrintingNothing()
    {
        Assert.Contains("<none>", Load(["--fixtures"], ("TMDB_TOKEN", "eyJ.a.b")).ToString());
        Assert.Contains("<none>", Load([]).ToString());
    }

    [Theory]
    [InlineData("--max-requests", "-5")]
    [InlineData("--max-requests=-5", null)]
    [InlineData("--max-requests", "-1")]
    public void ANegativeRequestLimitIsRejectedBecauseTheBudgetWouldReadItAsUnlimited(string first, string? second)
    {
        var ex = Assert.Throws<ArgumentException>(() => Load(second is null ? [first] : [first, second]));

        Assert.Contains("--max-requests 0", ex.Message);       // says how to ask for unlimited on purpose
    }

    [Fact]
    public void ANegativeRequestLimitFromTheEnvironmentIsRejectedToo()
        => Assert.Throws<ArgumentException>(() => Load([], ("MAX_REQUESTS_PER_RUN", "-3")));

    [Fact]
    public void TheEqualsFormOfAFlagIsReadSoAnIntendedZeroIsNotSilentlyFortyInstead()
    {
        var o = Load(["--max-requests=0", "--sheet-id=eq-sheet", "--cache=eq-cache", "--out=eq-out"],
                     ("MAX_REQUESTS_PER_RUN", "99"));

        Assert.Equal(0, o.MaxRequests);
        Assert.Equal("eq-sheet", o.SheetId);
        Assert.Equal("eq-cache", o.CacheRoot);
        Assert.Equal("eq-out", o.OutputRoot);
        Assert.Equal(7, Load(["--max-requests=7"]).MaxRequests);
        Assert.Equal("a=b", Load(["--sheet-id=a=b"]).SheetId);   // only the first = splits
    }

    [Fact]
    public void WhenAFlagIsGivenTwiceTheFirstWinsInEitherForm()
    {
        Assert.Equal(3, Load(["--max-requests", "3", "--max-requests=9"]).MaxRequests);
        Assert.Equal(3, Load(["--max-requests=3", "--max-requests", "9"]).MaxRequests);
    }

    [Theory]
    [InlineData("SHEET_ID")]
    [InlineData("MAX_REQUESTS_PER_RUN")]
    [InlineData("TMDB_TOKEN")]
    [InlineData("FORCE_REFRESH")]
    public void AnEmptyOrBlankEnvironmentVariableIsAbsentNotAnEmptyOverride(string name)
    {
        foreach (var blank in new[] { "", "   " })
        {
            var o = Load([], (name, blank));

            Assert.Equal(EnrichOptions.DefaultSheetId, o.SheetId);     // an unset Actions variable arrives as ""
            Assert.Equal(40, o.MaxRequests);
            Assert.Equal(string.Empty, o.Token);
            Assert.False(o.ForceRefresh);
        }
    }

    [Theory]
    [InlineData("--sheet-id")]
    [InlineData("--cache")]
    [InlineData("--out")]
    public void AnEmptyFlagValueIsAbsentInBothFormsSoTheDefaultAppliesNotAnEmptyPath(string flag)
    {
        foreach (var args in new[] { new[] { flag, "" }, [flag + "="], [flag, "  "] })
        {
            var o = Load(args);

            Assert.Equal(EnrichOptions.DefaultSheetId, o.SheetId);
            Assert.Equal("enrich-cache", o.CacheRoot);
            Assert.Equal("src/MovieLibrary.Web/wwwroot/data", o.OutputRoot);
        }
    }

    [Fact]
    public void AFlagThatOnlyStartsWithAKnownNameIsNotThatFlag()
    {
        var o = Load(["--outdated=zzz", "--cache-old=zzz", "--sheet-identity=zzz", "--max-requests-extra=9"]);

        Assert.Equal("src/MovieLibrary.Web/wwwroot/data", o.OutputRoot);
        Assert.Equal("enrich-cache", o.CacheRoot);
        Assert.Equal(EnrichOptions.DefaultSheetId, o.SheetId);
        Assert.Equal(40, o.MaxRequests);
    }

    [Fact]
    public void AnEmptyFlagValueFallsBackToTheEnvironmentValueNotStraightToTheDefault()
        => Assert.Equal("env-sheet", Load(["--sheet-id="], ("SHEET_ID", "env-sheet")).SheetId);

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

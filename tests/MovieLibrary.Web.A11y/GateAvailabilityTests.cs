using MovieLibrary.Web.A11y.Harness;
using Xunit.Abstractions;

namespace MovieLibrary.Web.A11y;

/// <summary>
/// The tests about the gate itself. None of them is browser-gated, so all of them run everywhere.
/// </summary>
/// <remarks>
/// Rule T2: a green test claims coverage and a skipped test does not, and the two must never be blurred.
/// Every browser test in this project skips on a machine with no published site or no browser, which is
/// right for a fresh clone and wrong for CI - a gate that silently skips in CI is worse than no gate,
/// because the check mark says it ran. These say which case this run is.
/// <para>
/// <b>In the browser collection although none of these drives a browser</b>, because
/// <see cref="TheSkipConditionIsTheGateAndAnExplicitSkipStillWins"/> forces the gate shut by writing to the
/// process environment, and <c>BrowserGate.InitializeAsync</c> reads that same environment. One collection
/// means the fixture is up before any of this runs and nothing can observe the write.
/// </para>
/// </remarks>
[Collection(BrowserCollection.Name)]
public class GateAvailabilityTests(ITestOutputHelper output)
{
    /// <summary>A directory that exists and holds no index.html, for forcing the gate shut.</summary>
    private static readonly string NotASite = AppContext.BaseDirectory;

    /// <summary>
    /// The one place this project is allowed to FAIL for a missing harness. A CI run with no site and no
    /// browser produced no coverage at all, and the job must say so rather than going green on skips.
    /// </summary>
    [Fact]
    public void TheBrowserGateIsNotSilentlySkippedInCi()
    {
        var reason = Gate.Unavailable;
        output.WriteLine(reason is null ? "the gate can run" : "the gate cannot run: " + reason);

        if (!Gate.InCi)
        {
            // Locally, either answer is fine; what is asserted is that the gate has an answer at all.
            Assert.True(reason is null || reason.Length > 0);
            return;
        }

        Assert.True(reason is null,
            "This is a CI run and the browser accessibility gate cannot run, so every scan in this project "
            + "SKIPPED and the job would otherwise have gone green having checked nothing. The reason was: "
            + reason + " See .github/workflows/a11y.yml, which publishes the site and installs the browser "
            + "before this project is tested.");
    }

    /// <summary>
    /// The conditional skip is wired to the gate, asserted directly on the attribute.
    /// </summary>
    /// <remarks>
    /// Rule T3 warns against reaching for a runtime skip and assuming it worked, and this is that warning
    /// taken seriously: Assert.Skip is an xunit v3 API, a thrown SkipException compiles and is reported as
    /// FAILED by the v2 engine, and [Fact(Skip = "...")] is static. The mechanism in use is an override of
    /// FactAttribute.Skip, which xunit v2 declares virtual and reads off the live attribute instance during
    /// discovery - so this test is what proves the override is the thing being read and not dead code.
    /// </remarks>
    [Fact]
    public void TheSkipConditionIsTheGateAndAnExplicitSkipStillWins()
    {
        Assert.Equal(Gate.Unavailable, new BrowserFactAttribute().Skip);
        Assert.Equal(Gate.Unavailable, new BrowserTheoryAttribute().Skip);

        // Those two lines are Assert.Equal(null, null) whenever the gate CAN run, which is exactly the
        // configuration CI runs in - so on their own they prove nothing there. Force the gate shut for the
        // length of the next three and the attribute has to come back with a reason rather than with null.
        // Safe to write to the environment here: this class is in the browser collection, so the fixture
        // has already read everything it reads from it. See the class remarks.
        var site = Environment.GetEnvironmentVariable(Gate.SiteRootVariable);
        var url = Environment.GetEnvironmentVariable(Gate.BaseUrlVariable);

        try
        {
            Environment.SetEnvironmentVariable(Gate.BaseUrlVariable, null);
            Environment.SetEnvironmentVariable(Gate.SiteRootVariable, NotASite);

            var shut = Gate.Unavailable;

            Assert.NotNull(shut);
            Assert.Equal(shut, new BrowserFactAttribute().Skip);
            Assert.Equal(shut, new BrowserTheoryAttribute().Skip);
        }
        finally
        {
            Environment.SetEnvironmentVariable(Gate.SiteRootVariable, site);
            Environment.SetEnvironmentVariable(Gate.BaseUrlVariable, url);
        }

        Assert.Equal("parked by hand", new BrowserFactAttribute { Skip = "parked by hand" }.Skip);
        Assert.Equal("parked by hand", new BrowserTheoryAttribute { Skip = "parked by hand" }.Skip);
    }

    /// <summary>
    /// A relative <c>A11Y_SITE_ROOT</c> resolves against the REPOSITORY ROOT, not the working directory.
    /// </summary>
    /// <remarks>
    /// This is the defect that made the gate scan nothing in CI and never be noticed. The workflow passed
    /// <c>release/wwwroot</c>, the skip message handed the reader the same value, and
    /// <c>Gate.NoSite</c> resolved it with <c>File.Exists</c> against the process working directory - which
    /// under <c>dotnet test</c> is this assembly's own output folder. There is never a published site
    /// there, so every scan skipped and the job reported on fifteen states it had not looked at.
    /// </remarks>
    [Fact]
    public void ARelativeSiteRootResolvesAgainstTheRepositoryRootAndNotTheWorkingDirectory()
    {
        var root = Gate.RepositoryRoot();

        Assert.NotNull(root);   // the assertion below is worthless if the walk silently found nothing
        Assert.True(File.Exists(Path.Combine(root, "MovieLibrary.slnx")),
            $"Gate.RepositoryRoot() returned \"{root}\", which holds no MovieLibrary.slnx.");

        var resolved = Gate.Absolute("release/wwwroot");
        output.WriteLine($"release/wwwroot resolves to {resolved}");

        // The defect, measured rather than described: this is where File.Exists used to look for it.
        output.WriteLine($"the process working directory would have made it "
                         + $"{Path.GetFullPath("release/wwwroot")}");

        Assert.Equal(Path.GetFullPath(Path.Combine(root, "release", "wwwroot")), resolved);

        // And an absolute value is left alone, which is what the workflow now passes.
        Assert.Equal(Path.GetFullPath(root), Gate.Absolute(root));
    }

    /// <summary>
    /// Whatever the gate says, it says how to act on it: a skip reason names the variable or the command
    /// that would make the run happen.
    /// </summary>
    /// <remarks>
    /// A skip message that only says "no browser" costs the next reader the half-hour this assertion saves.
    /// Both branches assert something: available means the reason is absent, unavailable means it is useful.
    /// </remarks>
    [Fact]
    public void TheSkipReasonAlwaysNamesHowToMakeTheGateRun()
    {
        var reason = Gate.Unavailable;

        if (reason is null)
        {
            Assert.NotNull(Gate.SiteRoot ?? Gate.BaseUrl);
            return;
        }

        Assert.True(
            reason.Contains(Gate.SiteRootVariable, StringComparison.Ordinal)
            || reason.Contains(Gate.BaseUrlVariable, StringComparison.Ordinal)
            || reason.Contains("playwright.ps1", StringComparison.Ordinal)
            || reason.Contains(Gate.ChannelVariable, StringComparison.Ordinal),
            "The skip reason does not name a variable or a command that would make the gate run, so a reader "
            + "seeing a skipped suite has nothing to act on. It said: " + reason);
    }
}

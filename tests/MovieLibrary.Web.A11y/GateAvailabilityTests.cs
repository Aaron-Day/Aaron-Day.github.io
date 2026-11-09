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
/// because the check mark says it ran. These three say which case this run is.
/// </remarks>
public class GateAvailabilityTests(ITestOutputHelper output)
{
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

        Assert.Equal("parked by hand", new BrowserFactAttribute { Skip = "parked by hand" }.Skip);
        Assert.Equal("parked by hand", new BrowserTheoryAttribute { Skip = "parked by hand" }.Skip);
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

using System.Reflection;
using System.Text.Json;
using Jint;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// Runs the real inline watchdog from index.html, as shipped, against a small stub of the browser
/// (Harness/watchdog-harness.js) in Jint. Three boot failures must converge on one outcome, and a healthy boot
/// must leave nothing registered. The runtime reveals #blazor-error-ui only when Program.Main throws; a failed
/// platform start (an unhandled rejection) and a runtime script that never loads reveal nothing at all.
/// </summary>
public class BootWatchdogTests
{
    private static readonly string Script = HostPage.WatchdogSource();
    private static readonly string Harness = ReadHarness();

    private static string ReadHarness()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("watchdog-harness.js")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Builds a fresh stub browser, runs the watchdog in it, then runs <paramref name="scenario"/> and returns its result.</summary>
    private static JsonElement Run(string scenario, bool withMutationObserver = true)
    {
        var engine = new Engine();
        engine.Execute(Harness);
        engine.SetValue("SCRIPT", Script);
        var observer = withMutationObserver ? "false" : "true";
        var json = engine.Evaluate(
            $"JSON.stringify((function () {{ var env = makeEnv(SCRIPT, {{ noMutationObserver: {observer} }}); {scenario} }})())").AsString();
        return JsonDocument.Parse(json).RootElement;
    }

    private const string Rejection = "env.win.fire('unhandledrejection', { reason: 'x' }, true);";
    private const string ScriptLoadError =
        "env.win.fire('error', { target: { tagName: 'SCRIPT', src: 'http://localhost/_framework/blazor.webassembly.w3q.js' } }, false);";

    /// <summary>The one state every kind of boot failure must end in.</summary>
    private static void AssertFailureState(JsonElement s)
    {
        Assert.Equal("block", s.GetProperty("bar").GetString());
        Assert.Equal("none", s.GetProperty("spinner").GetString());
        Assert.Equal("none", s.GetProperty("spinnerText").GetString());
        Assert.Equal(JsonValueKind.Null, s.GetProperty("role").ValueKind);   // not a second live region
        Assert.Equal("boot-failure", s.GetProperty("cls").GetString());
        Assert.Equal("-1", s.GetProperty("tabindex").GetString());   // so Dismiss can put focus on it
        var text = s.GetProperty("text").GetString()!;
        Assert.Contains("could not start", text);
        Assert.DoesNotContain("Loading", text);
        Assert.DoesNotContain("connection", text);   // a mid-deploy missing file is as likely as a dropped connection
        Assert.Equal("function", s.GetProperty("reloadWired").GetString());
        Assert.Equal("function", s.GetProperty("dismissWired").GetString());
        // The triggers are spent; only the observer that lets a false alarm heal is still watching.
        Assert.Empty(s.GetProperty("listeners").EnumerateArray());
        Assert.Equal(1, s.GetProperty("observers").GetInt32());
    }

    [Fact]
    public void ItRegistersTwoTriggersAndTwoObserversAndTouchesNothingUntilOneFires()
    {
        var r = Run("env.flush(); return { s: state(env) };").GetProperty("s");

        Assert.Equal(new[] { "error(capture)", "unhandledrejection" },
            r.GetProperty("listeners").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(2, r.GetProperty("observers").GetInt32());
        Assert.Equal("", r.GetProperty("bar").GetString());
        Assert.Equal("status", r.GetProperty("role").GetString());
        Assert.Equal("Loading library", r.GetProperty("text").GetString());
    }

    [Fact]
    public void AnUnhandledRejectionWhileBootingEndsInTheFailureState() =>
        AssertFailureState(Run($"{Rejection} env.flush(); return state(env);"));

    [Fact]
    public void TheRuntimeScriptFailingToLoadEndsInTheSameState() =>
        AssertFailureState(Run($"{ScriptLoadError} env.flush(); return state(env);"));

    [Fact]
    public void TheRuntimeRevealingTheBarItselfEndsInTheSameStateWithoutRevealingItAgain()
    {
        // The path where Program.Main throws: the runtime catches it, logs it, shows the bar, and leaves the spinner.
        var r = Run("env.runtimeReveals(); env.flush(); return { s: state(env), writes: env.displayWrites };");

        AssertFailureState(r.GetProperty("s"));
        Assert.Equal(new[] { "runtime:block" }, r.GetProperty("writes").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void TheWatchdogReplacesTheRuntimesDismissSoFocusDoesNotDropToTheBody()
    {
        var r = Run("env.runtimeReveals(); env.flush(); env.dismissBtn.onclick({ preventDefault: function () {} });" +
                    "return { bar: env.bar.style.display, focusOnStatus: env.activeElement === env.status };");

        Assert.Equal("none", r.GetProperty("bar").GetString());
        Assert.True(r.GetProperty("focusOnStatus").GetBoolean());
    }

    [Fact]
    public void TheRoleIsRemovedBeforeTheTextChangesSoTheAlertIsTheOnlyAnnouncer()
    {
        var log = Run($"{Rejection} env.flush(); return env.log;").EnumerateArray().Select(e => e.GetString()).ToList();

        Assert.Equal(new[] { "removeAttribute:role", "textContent" }, log);
    }

    [Fact]
    public void ASecondTriggerDoesNotRunTheFailureStepsAgain()
    {
        var r = Run($"{ScriptLoadError} {Rejection} env.runtimeReveals(); env.flush(); return env.log;");

        Assert.Equal(new[] { "removeAttribute:role", "textContent" }, r.EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void ErrorsThatAreNotTheRuntimeScriptFailingAreIgnored()
    {
        var r = Run("env.win.fire('error', { target: null, message: 'ordinary script error' }, true);" +
                    "env.win.fire('error', { target: { tagName: 'SCRIPT', src: 'https://cdn.example/other.js' } }, false);" +
                    "env.win.fire('error', { target: { tagName: 'IMG', src: '/blazor.webassembly.png' } }, false);" +
                    "env.flush(); return state(env);");

        Assert.Equal("", r.GetProperty("bar").GetString());
        Assert.Equal(2, r.GetProperty("listeners").GetArrayLength());
    }

    [Fact]
    public void AScriptLoadErrorIsOnlySeenInTheCapturePhase()
    {
        // A resource error does not bubble. If the listener is not capture-phase it never runs for a script element.
        var r = Run($"{ScriptLoadError} return env.listeners();");

        Assert.Empty(r.EnumerateArray());
    }

    [Fact]
    public void AHealthyBootStandsEveryListenerAndObserverDownWithNoEventAtAll()
    {
        var r = Run("env.bootSucceeds(); env.flush(); return state(env);");

        Assert.Empty(r.GetProperty("listeners").EnumerateArray());
        Assert.Equal(0, r.GetProperty("observers").GetInt32());
        Assert.Equal("", r.GetProperty("bar").GetString());
    }

    [Fact]
    public void ARejectionAfterBootIsNotABootFailure()
    {
        // The observer has not delivered yet, so the listener is still registered when the rejection arrives.
        var r = Run($"env.bootSucceeds(); {Rejection} env.flush(); return state(env);");

        Assert.Equal("", r.GetProperty("bar").GetString());
        Assert.Empty(r.GetProperty("listeners").EnumerateArray());
        Assert.Equal(0, r.GetProperty("observers").GetInt32());
    }

    [Fact]
    public void ARejectionAfterBootStandsTheListenersDownEvenWhereThereIsNoMutationObserver()
    {
        // Nothing else would ever remove them there, so the trigger itself has to.
        var r = Run($"env.bootSucceeds(); {Rejection} return state(env);", withMutationObserver: false);

        Assert.Equal("", r.GetProperty("bar").GetString());
        Assert.Empty(r.GetProperty("listeners").EnumerateArray());
    }

    [Fact]
    public void ARuntimeRevealAfterBootIsLeftToTheRuntime()
    {
        var r = Run("env.bootSucceeds(); env.runtimeReveals(); env.flush(); return { s: state(env), writes: env.displayWrites };");

        Assert.Equal("block", r.GetProperty("s").GetProperty("bar").GetString());
        Assert.Equal(new[] { "runtime:block" }, r.GetProperty("writes").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(0, r.GetProperty("s").GetProperty("observers").GetInt32());
    }

    [Fact]
    public void AFalseAlarmHealsWhenBlazorBootsAnyway()
    {
        // An in-app browser's injected script can reject while Blazor is still booting.
        var r = Run($"{Rejection} env.flush(); var during = env.bar.style.display;" +
                    "env.bootSucceeds(); env.flush(); return { during: during, s: state(env) };");

        Assert.Equal("block", r.GetProperty("during").GetString());
        var s = r.GetProperty("s");
        Assert.Equal("none", s.GetProperty("bar").GetString());
        Assert.Empty(s.GetProperty("listeners").EnumerateArray());
        Assert.Equal(0, s.GetProperty("observers").GetInt32());
    }

    [Fact]
    public void ReloadReloadsThePageAndSuppressesTheDefaultNavigationToTheRoot()
    {
        var r = Run($"{Rejection} env.flush(); var prevented = 0;" +
                    "env.reloadBtn.onclick({ preventDefault: function () { prevented++; } });" +
                    "return { reloads: env.reloads(), prevented: prevented };");

        Assert.Equal(1, r.GetProperty("reloads").GetInt32());
        Assert.Equal(1, r.GetProperty("prevented").GetInt32());
    }

    [Fact]
    public void DismissHidesTheBarAndPutsFocusOnTheExplanation()
    {
        var r = Run($"{Rejection} env.flush(); env.dismissBtn.onclick({{ preventDefault: function () {{}} }});" +
                    "return { bar: env.bar.style.display, focusOnStatus: env.activeElement === env.status };");

        Assert.Equal("none", r.GetProperty("bar").GetString());
        Assert.True(r.GetProperty("focusOnStatus").GetBoolean());
    }

    [Theory]
    [InlineData("Rejection")]
    [InlineData("ScriptLoadError")]
    public void WithoutMutationObserverTheTwoEventPathsStillWork(string path)
    {
        var trigger = path == "Rejection" ? Rejection : ScriptLoadError;

        var s = Run($"{trigger} return state(env);", withMutationObserver: false);

        Assert.Equal("block", s.GetProperty("bar").GetString());
        Assert.Equal("none", s.GetProperty("spinner").GetString());
        Assert.Equal(JsonValueKind.Null, s.GetProperty("role").ValueKind);
    }

    [Fact]
    public void TheWatchdogLoadsBeforeTheRuntimeCanFail()
    {
        var all = HostPage.Doc.QuerySelectorAll("script").ToList();
        var runtime = all.Single(s => (s.GetAttribute("src") ?? "").Contains("blazor.webassembly"));

        Assert.True(all.IndexOf(HostPage.WatchdogElement()) < all.IndexOf(runtime), "the watchdog must load before blazor.webassembly.js");
    }

    [Fact]
    public void TheWatchdogUsesNoTimerBecauseASlowPhoneOnAColdCacheIsNotAFailure()
    {
        Assert.DoesNotContain("setTimeout", Script);
        Assert.DoesNotContain("setInterval", Script);
    }
}

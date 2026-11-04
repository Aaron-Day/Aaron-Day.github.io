using System.Text.RegularExpressions;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// The token set and the removed light-theme leftovers, as text. Later tasks consume the tokens by name, so a
/// rename or a dropped token breaks them silently: an undefined var() is just an invalid declaration.
/// </summary>
public class StyleContractTests
{
    private static readonly string AppCss = WebRoot.Read("wwwroot", "css", "app.css");

    private static string Root()
    {
        var m = Regex.Match(AppCss, @":root\s*\{(?<body>[^}]*)\}");
        Assert.True(m.Success, "app.css has no :root block");
        return m.Groups["body"].Value;
    }

    [Theory]
    [InlineData("--bg", "#0b0d12")]
    [InlineData("--panel", "#13161e")]
    [InlineData("--panel2", "#1a1e28")]
    [InlineData("--line", "#262b38")]
    [InlineData("--line-strong", "#657084")]
    [InlineData("--txt", "#e8eaf0")]
    [InlineData("--dim", "#8c93a6")]
    [InlineData("--accent", "#f5c518")]
    [InlineData("--accent2", "#4da3ff")]
    [InlineData("--touch", "44px")]
    public void TheTokenIsDefinedOnRootWithItsMeasuredValue(string name, string value)
    {
        Assert.Matches($@"{Regex.Escape(name)}\s*:\s*{Regex.Escape(value)}\s*;", Root());
    }

    [Fact]
    public void TheThemeIsDeclaredDarkAndAppliedToTheBody()
    {
        Assert.Matches(@"color-scheme\s*:\s*dark\s*;", Root());
        Assert.Matches(@"body\s*\{\s*background\s*:\s*var\(--bg\)\s*;\s*color\s*:\s*var\(--txt\)\s*;\s*\}", AppCss);
    }

    [Fact]
    public void TheFocusRingOutSpecifiesBootstrap()
    {
        // (0,2,0). A bare :focus-visible is (0,1,0) and loses to .form-control:focus and .btn:focus-visible.
        Assert.Matches(@":root\s+:focus-visible\s*\{[^}]*outline\s*:\s*3px solid var\(--accent\)", AppCss);
        Assert.DoesNotContain("#258cfb", AppCss);
    }

    [Theory]
    [InlineData("#0071c1")]       // link blue, 3.82:1 on --bg
    [InlineData("#52585e")]       // attribution grey, 2.70:1 on --bg
    [InlineData("#c02d76")]       // code pink, 3.59:1 on --bg
    [InlineData("lightyellow")]   // the stock error bar
    [InlineData("scroll-padding-top")]
    public void TheLightThemeLeftoverIsGone(string leftover)
    {
        Assert.DoesNotContain(leftover, AppCss);
    }

    [Fact]
    public void TheDismissButtonIsNoLongerPositionedOverTheText()
    {
        // The stock position:absolute overlapped the message and the Reload link at 320px.
        Assert.Matches(@"\.dismiss\s*\{[^}]*margin-left\s*:\s*auto", AppCss);
        Assert.DoesNotMatch(@"\.dismiss\s*\{[^}]*position\s*:\s*absolute", AppCss);
    }

    [Fact]
    public void LinksAndTheAttributionUseTheTokens()
    {
        Assert.Matches(@"(?m)^a\s*\{\s*color\s*:\s*var\(--accent2\)", AppCss);
        // Up to the rule's closing brace at column 0: its comments mention braces, so [^}]* would stop early.
        Assert.Matches(@"(?s)\.attribution p\s*\{.*?color\s*:\s*var\(--txt\).*?
\}", AppCss);
    }

    [Fact]
    public void BootstrapAlreadyDefinesVisuallyHiddenSoThereIsNoLocalCopy()
    {
        Assert.DoesNotContain(".visually-hidden", AppCss.Replace("visually-hidden-focusable", ""));
    }

    [Fact]
    public void NoViewportUnitSitsBelowBodyInTheLayoutCss()
    {
        // A 100dvh on .page would make #app a full viewport tall and push the attribution footer a screenful down.
        foreach (var file in new[] { "MainLayout.razor.css", "NavMenu.razor.css" })
            Assert.DoesNotMatch(@"\d(vh|dvh|svh|lvh)\b", WebRoot.Read("Layout", file));
    }

    [Fact]
    public void TheStockSidebarLayoutIsGoneSoHeaderNeverSitsBesideMain()
    {
        var layoutCss = WebRoot.Read("Layout", "MainLayout.razor.css");
        Assert.DoesNotContain("flex-direction: row", layoutCss);
        Assert.DoesNotContain("min-width: 641px", layoutCss);
        Assert.DoesNotContain("sidebar", layoutCss);
    }

    [Fact]
    public void PressFeedbackOutranksTheScopedToggleRuleAndTheErrorBarsIdRule()
    {
        // (0,2,1) beats the scoped .nav-toggle rule at (0,2,0); the Dismiss button is set at (1,1,0), so it has its own.
        Assert.Matches(@":root :is\(a, button\):active\s*\{\s*background-color\s*:\s*var\(--line\)", AppCss);
        Assert.Matches(@"#blazor-error-ui \.dismiss:active\s*\{\s*background-color\s*:\s*var\(--line\)", AppCss);
        Assert.DoesNotMatch(@"(?m)^a:active, button:active", AppCss);
    }

    [Fact]
    public void AFocusedControlCannotSitUnderTheFixedErrorBar()
    {
        Assert.Matches(@"html\s*\{\s*scroll-padding-bottom\s*:\s*6rem\s*;\s*\}", AppCss);
    }

    [Fact]
    public void TheNavBreakpointsAreMobileFirstSoThereIsNoGapWhereNeitherRuleApplies()
    {
        var css = WebRoot.Read("Layout", "NavMenu.razor.css");

        // max-width: 47.99rem against min-width: 48rem left 0.01rem where the toggle and a visible list both showed.
        Assert.DoesNotContain("max-width", css);
        Assert.Contains("@media (min-width: 48rem)", css);
        Assert.Matches(@"\.nav-links\.collapsed\s*\{\s*display\s*:\s*none", css);
    }
}

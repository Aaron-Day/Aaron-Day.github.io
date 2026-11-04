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

    /// <remarks>
    /// D2. SC 2.4.11 Focus Not Obscured (Minimum), Level AA, new in WCAG 2.2. #blazor-error-ui is
    /// position:fixed at bottom:0, so a control brought into view by a focus move sits under it unless the
    /// scrollport reserves at least the bar's height. The reserve was 6rem, measured from the bar wrapped at
    /// 320px - Reflow's WIDTH. Reflow's other dimension is a 256px viewport HEIGHT, where the bar's own
    /// 40dvh cap is 102.4px and 6rem is 96px, so the mitigation was 6.4px short at precisely the size it
    /// existed for. What this pins is the shape AND one deliberate
    /// asymmetry. One token holds the cap, and html's scroll-padding-bottom is written as max(floor,
    /// that token) because scroll-padding costs nothing: it only moves where a scroll stops.
    /// .attribution's bottom padding deliberately does NOT read the token. That is real layout space on
    /// every page load, 432px of it on a 1080px-tall viewport, and what it would buy is a static sentence
    /// not being overlapped by an element that is display:none unless boot failed, and dismissible when
    /// it is not. 2.4.11 is about a component with focus and does not reach that sentence, and TMDB's
    /// terms ask only that it be displayed, which it is. Harmonising the two values, in either direction,
    /// is the regression this test exists to catch.
    /// What it cannot prove: that a real browser actually keeps the focused control clear. That needs a
    /// viewport and a focus move, and belongs to Task 4b.
    /// </remarks>
    [Fact]
    public void TheScrollReserveReadsTheBarsCapAndTheFooterReserveDeliberatelyDoesNot()
    {
        Assert.Matches(@"--bar-max\s*:\s*40dvh\s*;", Root());
        Assert.Matches(@"#blazor-error-ui\s*\{[^}]*max-height\s*:\s*var\(--bar-max\)", AppCss);
        Assert.Matches(@"html\s*\{\s*scroll-padding-bottom\s*:\s*max\(6rem,\s*var\(--bar-max\)\)\s*;\s*\}", AppCss);
        // The asymmetry is deliberate and is the point of this assertion: scroll-padding reads the cap
        // because it costs nothing, and .attribution does NOT because it is real layout space on every
        // page load -- 432px of it on a 1080px viewport -- bought against an element that is display:none
        // unless boot failed, and dismissible when it is not. SC 2.4.11 is about a focused component and
        // does not reach a static sentence. Harmonising these two values is the regression to catch.
        Assert.Matches(@"(?s)\.attribution\s*\{.*?padding\s*:\s*1rem 1\.5rem 6rem\s*;", AppCss);

        // A bare 6rem in either reserve, or a literal cap that no longer goes through the token, is the
        // bug coming back by the same route it arrived: two numbers for one measurement.
        Assert.DoesNotMatch(@"scroll-padding-bottom\s*:\s*6rem\s*;", AppCss);
        Assert.DoesNotMatch(@"max-height\s*:\s*40dvh", AppCss);
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

    /// <remarks>
    /// D1. SC 1.4.1 Use of Color, Level A. NotFound's "Back to the library" sits inside a sentence of
    /// prose, which is technique failure F73's exact shape: a link distinguished from the text around it
    /// by colour alone needs 3:1 against that text, and --accent2 (#4da3ff) against --txt (#e8eaf0) is
    /// 2.18:1. Nothing looked wrong because Bootstrap's reboot underlines links - which is the problem,
    /// not the defence, because it puts the link's only non-colour cue in a file this project does not own
    /// and would lose to a dropped stylesheet or a link-underline utility. Declaring it here makes it ours.
    /// The second half bans text-decoration: none anywhere in this file, which is stricter than 1.4.1
    /// needs - a standalone control that happens to be an anchor is distinguished by its own box, not by
    /// colour inside running text. It is written this way because the only rule here that could plausibly
    /// want it is one that also lands on the in-prose link, and a blanket ban is a cheap guard with an
    /// obvious escape: when a later task needs one, name the exception in this test the way the motion
    /// rule below names the boot spinner.
    /// Comments are stripped first, so prose that mentions text-decoration cannot trip it.
    /// What this cannot catch: a reset in a component's scoped .razor.css, an inline style, or a Bootstrap
    /// utility class in markup. Those need the rendered page, which is Task 4b.
    /// </remarks>
    [Fact]
    public void AnInProseLinkIsDistinguishedByMoreThanItsColour()
    {
        var css = WithoutComments(AppCss);

        Assert.Matches(@"(?m)^a\s*\{[^}]*text-decoration\s*:\s*underline", css);
        Assert.DoesNotMatch(@"text-decoration\s*:\s*none", css);
    }

    /// <remarks>
    /// D3. The token said "SC 2.5.8" beside the value 44px. 44x44 is SC 2.5.5 Target Size (Enhanced),
    /// Level AAA; SC 2.5.8 Target Size (Minimum) is the Level AA criterion this project is held to and it
    /// is 24x24. Citing 2.5.8 for 44 turns a house rule into a conformance claim, and it is the same
    /// conflation the plan already had corrected once - it had simply survived into the CSS, in the one
    /// file the tests read. The token's VALUE is not the defect and does not change.
    /// </remarks>
    [Fact]
    public void TheTouchTokenNamesTheAaaGoalItAimsAtAndTheAaMinimumTheStandardActuallyRequires()
    {
        var root = Root();

        Assert.Matches(@"2\.5\.5[^*]*AAA", root);     // 44px is the AAA goal
        Assert.Matches(@"2\.5\.8[^*]*24", root);      // 24px is what AA asks for
        Assert.DoesNotMatch(@"minimum size of every standalone control, SC 2\.5\.8", root);
    }

    /// <remarks>
    /// D4. The file's closing comment promised "No transition, animation or transform is used here, so no
    /// prefers-reduced-motion block is needed", while the boot spinner two hundred lines above declares a
    /// transform and a transition. The spinner is a legitimate exception - it is gone before the app
    /// renders, and prefers-reduced-motion cannot help a user staring at a blank page - so the comment now
    /// names it, and this turns the promise into something enforced rather than remembered. Task 6 adds
    /// tile hovers; a promise is only worth the test behind it.
    /// Scope: transition and animation, not transform. A static rotate() is not motion and has nothing for
    /// prefers-reduced-motion to reduce; a rule that flagged it would cry wolf, which is the one thing this
    /// gate must not do. Comments are stripped before the scan, so prose about motion cannot trip it.
    /// What this cannot catch: motion that arrives through a component's scoped .razor.css, an inline
    /// style, or a Bootstrap class. NoViewportUnitSitsBelowBodyInTheLayoutCss covers the scoped files for
    /// its own rule; a stylesheet-wide sweep belongs with the browser pass in Task 4b.
    /// </remarks>
    [Fact]
    public void TheOnlyMotionIsTheBootSpinnersAndAnythingElseNeedsAReducedMotionBlock()
    {
        var motion = Declarations(WithoutComments(AppCss))
            .Where(d => Regex.IsMatch(d.Declaration, @"^(?:transition|animation)[a-z-]*\s*:"))
            .ToList();

        // Positive control. Every assertion below is a loop over `motion`, so an empty `motion` would make
        // this test green while proving nothing at all - which is the failure mode the scan exists to stop.
        Assert.Contains(motion, m => m.Context.Contains(".loading-progress"));

        foreach (var (context, declaration) in motion)
            Assert.True(
                context.Contains("prefers-reduced-motion") || context.Contains(".loading-progress"),
                $"`{declaration};` is declared under `{context}`, which is neither the boot spinner nor a "
                + "@media (prefers-reduced-motion) block. A user who asks their system to reduce motion must "
                + "get a stylesheet that honours it; the boot spinner is the one exception, because it is "
                + "gone before the app renders. Either drop the motion or pair it with a reduced-motion "
                + "block, and say which in app.css's closing comment.");
    }

    private static string WithoutComments(string css) => Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline);

    /// <summary>
    /// Every declaration in a stylesheet, paired with the nested selectors and at-rules it sits under. Not
    /// a CSS parser: it tracks braces and semicolons only, so a semicolon inside a url() value splits that
    /// one value in two. That is harmless for a scan that only reads the start of a declaration, and it is
    /// the reason this is private to one test rather than offered as a utility.
    /// </summary>
    private static IEnumerable<(string Context, string Declaration)> Declarations(string css)
    {
        var open = new List<string>();
        var start = 0;

        for (var i = 0; i < css.Length; i++)
        {
            switch (css[i])
            {
                case '{':
                    open.Add(css[start..i].Trim());
                    start = i + 1;
                    break;
                case '}':
                    if (open.Count > 0) open.RemoveAt(open.Count - 1);
                    start = i + 1;
                    break;
                case ';':
                    yield return (string.Join(" ", open), css[start..i].Trim());
                    start = i + 1;
                    break;
            }
        }
    }
}

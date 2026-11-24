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
    // Two rules consume --bar-max by name -- #blazor-error-ui's max-height and html's
    // scroll-padding-bottom -- so it is pinned by name here with the rest of the set. The D2 test below
    // asserts the same value, but as part of a shape; this one is about the token existing under its name,
    // which is the thing that fails silently, because an undefined var() is just an invalid declaration.
    [InlineData("--bar-max", "40dvh")]
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

    /// <remarks>
    /// Was NoViewportUnitSitsBelowBodyInTheLayoutCss, and read
    /// <c>new[] { "MainLayout.razor.css", "NavMenu.razor.css" }</c> by hand -- the third of the
    /// hand-maintained file lists, and the one whose only cross-reference was a sentence in the motion
    /// rule's remarks that f354e3d deleted, so by the time Task 6 added two component stylesheets nothing
    /// connected the two at all. No live defect: neither new sheet uses a viewport unit. But the rule is
    /// about what sits below <c>body</c>, which is every scoped component sheet rather than the two that
    /// happened to exist when it was written, so it is found by glob like the motion rule beside it.
    /// <para>
    /// app.css is deliberately NOT in the set. It owns --bar-max: 40dvh and the error bar's max-height
    /// reads it, which is the measurement D2 exists to protect; a rule that banned viewport units there
    /// would reject correct CSS, which is rule T5's failure.
    /// </para>
    /// <para>
    /// T1: the only assertion is inside the loop, so the discovered set has a floor and six of the eight
    /// sheets are named (the four web sheets that carry layout or tiles, and both editor sheets).
    /// </para>
    /// </remarks>
    [Fact]
    public void NoViewportUnitSitsBelowBodyInAnyComponentStylesheet()
    {
        // A 100dvh on .page would make #app a full viewport tall and push the attribution footer a screenful down.
        var sheets = WebRoot.FilesWithEditor("*.razor.css").ToList();
        var names = sheets.Select(s => s.Path).ToList();

        Assert.True(sheets.Count >= 8,
            "This rule found " + sheets.Count + " component stylesheets. There are eight: six in the web "
            + "project and two in the editor. Fewer means WebRoot.FilesWithEditor stopped finding them, "
            + "and a rule with no subjects passes over nothing.");

        Assert.Contains("Layout/MainLayout.razor.css", names);
        Assert.Contains("Layout/NavMenu.razor.css", names);
        Assert.Contains("Components/PosterTile.razor.css", names);
        Assert.Contains("Components/PosterGrid.razor.css", names);
        Assert.Contains(WebRoot.EditorPrefix + "Components/RecordForm.razor.css", names);
        Assert.Contains(WebRoot.EditorPrefix + "Components/Worklist.razor.css", names);

        foreach (var (path, text) in sheets)
            Assert.True(!Regex.IsMatch(text, @"\d(vh|dvh|svh|lvh)\b"),
                path + " sets a viewport unit below body. A 100dvh here makes #app a full viewport tall "
                + "and pushes the attribution footer a screenful down. The one viewport measurement this "
                + "project keeps is app.css's --bar-max, which the error bar reads.");
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
    /// not being overlapped by an element that is display:none until something throws. A failed boot is
    /// only one way in and the least interesting one, because the page is then empty; the Blazor runtime
    /// also reveals the bar on ANY unhandled exception during execution, which is the case worth naming,
    /// because it is the one where catalogue data is on screen and the footer has been scrolled to. The
    /// bar is dismissible whenever it appears. 2.4.11 is about a component with focus and does not reach
    /// that sentence, and TMDB's terms ask only that it be displayed, which it is. Harmonising the two
    /// values, in either direction, is the regression this test exists to catch.
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
        // until an unhandled exception reveals it, and dismissible when one does. Not only a failed boot:
        // an exception while the app is running is the case where this overlap can actually be seen, with
        // the catalogue on screen and the footer scrolled to. SC 2.4.11 is about a focused component and
        // does not reach a static sentence. Harmonising these two values is the regression to catch.
        Assert.Matches(@"(?s)\.attribution\s*\{.*?padding\s*:\s*1rem 1\.5rem 6rem\s*;", AppCss);

        // The SCROLL reserve going bare, or a literal cap that no longer reads the token, is the bug
        // coming back by the same route it arrived: two numbers for one measurement. The footer's bare
        // 6rem is not that bug -- it is the deliberate asymmetry, and the assertion three lines above
        // REQUIRES it. Do not read these two as a ban on 6rem anywhere.
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
    /// The second half bans text-decoration: none everywhere EXCEPT at selectors this test names, which
    /// is stricter than 1.4.1 needs - a standalone control that happens to be an anchor is distinguished by
    /// its own box, not by colour inside running text. It is written this way because the only rule that
    /// could plausibly want the reset is one that also lands on the in-prose link.
    /// THE ESCAPE IS NOW USED, which is what the old wording promised would happen. The poster tile became
    /// an anchor, sixty underlined titles in a grid read as sixty broken links, and so .tile-body is named
    /// in the allowed list below -- one SELECTOR and not one file, so another rule in the same sheet
    /// resetting the decoration still fails. The allowance has a live guard of its own: the day .tile-body
    /// stops resetting, Assert.Contains fails and the dead exception has to go.
    /// Comments are stripped first, so prose that mentions text-decoration cannot trip it.
    /// THE BAN NOW READS EVERY STYLESHEET, not just app.css. Its own remark used to say "what this cannot
    /// catch: a reset in a component's scoped .razor.css" -- which was a blind spot nobody could reach
    /// until Task 6 wrote the first two component sheets, and then was simply a hole. A scoped
    /// text-decoration: none on an anchor removes the only non-colour cue that anchor has, whichever file
    /// it is written in. The POSITIVE half stays app.css-only, because that is where the declaration
    /// belongs: one rule for every anchor in the app, in the file this project owns.
    /// What this still cannot catch: an inline style, or a Bootstrap utility class in markup. Those need
    /// the rendered page, which is Task 4b.
    /// </remarks>
    [Fact]
    public void AnInProseLinkIsDistinguishedByMoreThanItsColour()
    {
        Assert.Matches(@"(?m)^a\s*\{[^}]*text-decoration\s*:\s*underline", WithoutComments(AppCss));

        // Stylesheets() is the glob, and its own by-name assertions live in the motion rule, so a walk
        // that stopped matching cannot leave this one quietly looping over an empty set.
        var sheets = Stylesheets();

        Assert.True(sheets.Count >= 10,
            "This rule found " + sheets.Count + " stylesheets. There are ten: app.css, seven scoped "
            + "component and page sheets in the web project and two in the editor. Fewer means the glob stopped finding them.");

        // THE ONE NAMED EXCEPTION, which is the escape the ban's own note promised. A poster tile IS an
        // anchor and must NOT be underlined: sixty underlined titles in a grid read as sixty broken links,
        // and the tile is not distinguished by colour in the first place -- it is a card with artwork in it,
        // its own box is the cue, and PosterTile.razor.css keeps its text at --txt and --dim rather than
        // taking app.css's --accent2. So F73 has no subject there. The exception is to the SELECTOR and not
        // to the file: another rule in the tile's sheet resetting text-decoration still fails, because the
        // exception is matched against the declaration's own selector.
        //
        // It is also why the positive half above stays app.css-only: one rule for every anchor in the app,
        // in the file this project owns, and one named opt-out for the one anchor that is a card.
        var allowed = new[] { ".tile-body" };
        var exceptions = sheets
            .SelectMany(s => Resets(s.Text).Select(selector => (s.Path, Selector: selector)))
            .ToList();

        // T1: the carve-out has to have something in it, or the day .tile-body stops resetting its
        // decoration this list goes empty and the allowance sits here meaning nothing.
        Assert.Contains(exceptions, e => e.Selector == ".tile-body");

        foreach (var (path, selector) in exceptions)
            Assert.True(allowed.Contains(selector, StringComparer.Ordinal),
                path + " resets text-decoration to none on \"" + selector + "\". --accent2 against --txt "
                + "is 2.18:1, so an anchor inside running prose would then be distinguished from it by "
                + "colour alone, which is SC 1.4.1 Use of Color and technique failure F73. A standalone "
                + "control that happens to be an anchor is distinguished by its own box, so if this one is "
                + "meant, add its selector to the allowed list above and say in the sheet why its own box "
                + "is the cue -- as .tile-body does.");
    }

    /// <summary>
    /// Every selector in a stylesheet that resets <c>text-decoration</c> to none, so the ban above can name
    /// its exceptions one selector at a time rather than one file at a time.
    /// </summary>
    /// <remarks>
    /// Comments are stripped first, so prose that mentions the declaration cannot trip it. The selector is
    /// taken as the text between the previous <c>}</c>, <c>{</c> or start of file and the brace that opens
    /// the block, trimmed -- which is enough for this project's flat sheets and would need a real parser
    /// for nesting. A selector list keeps its commas, so an exception has to be named exactly as written.
    /// </remarks>
    private static IReadOnlyList<string> Resets(string css)
    {
        var text = WithoutComments(css);

        return [.. Regex.Matches(text, @"([^{}]*)\{[^{}]*text-decoration\s*:\s*none")
            .Select(m => Regex.Replace(m.Groups[1].Value, @"\s+", " ").Trim())];
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
    /// BOTH GUARD SHAPES ARE ACCEPTED, and an earlier version of this rule accepted only one. It required
    /// the declaration to sit INSIDE a prefers-reduced-motion block, which only the inverted
    /// @media (prefers-reduced-motion: no-preference) form satisfies, so the canonical declaration plus
    /// @media (prefers-reduced-motion: reduce) override failed -- while the failure message told the author
    /// to pair the declaration with a reduced-motion block, which is exactly what it was paired with.
    /// Following the instruction did not make the test pass, and Task 6's first tile hover would have hit
    /// it. The two accepted forms are now: the declaration sits inside a prefers-reduced-motion block of
    /// either polarity, or the declaration's own selector is redeclared under
    /// @media (prefers-reduced-motion: reduce) -- the declare-then-override form, which is what most
    /// authors write and what most linters expect, a blanket reset included.
    /// Scope: transition and animation, not transform. A static rotate() is not motion and has nothing for
    /// prefers-reduced-motion to reduce; a rule that flagged it would cry wolf, which is the one thing this
    /// gate must not do. Comments are stripped before the scan, so prose about motion cannot trip it.
    /// THE SET OF FILES THIS READS IS app.css PLUS EVERY *.razor.css UNDER THE WEB PROJECT, found by glob
    /// rather than by a hand-maintained list, so a stylesheet added in a later task is covered the day it
    /// appears rather than the day someone remembers it. Until Task 6 the only motion in the project could
    /// only be in app.css, because no component had a stylesheet; Task 6 added the first two and every
    /// later task adds more, which is the hole this remark used to admit to and no longer does.
    /// Guards are collected PER FILE, plus app.css's own: a reduce block in app.css is unscoped and does
    /// reach a component's elements, while one component's scoped reduce block cannot guard another's
    /// selectors, because scoped CSS rewrites each file's selectors with its own attribute. Being stricter
    /// than the real cascade would start rejecting correct CSS, which is rule T5's failure.
    /// What this still cannot catch: an inline style, a Bootstrap class, or a stylesheet that is neither
    /// app.css nor a *.razor.css. Nor can it check that an override actually neutralises the motion: a
    /// reduce block naming the selector for any reason satisfies the second form, because deciding
    /// otherwise means resolving the cascade, and a scan that guessed at that would start rejecting
    /// correct CSS -- which is the failure this rule once had.
    /// The rule's own logic is proven below against CSS written for the purpose, because the project
    /// declares motion in one place and has no reduced-motion block at all, so this contract alone cannot
    /// prove it.
    /// </remarks>
    [Fact]
    public void TheOnlyMotionIsTheBootSpinnersAndAnythingElseNeedsAReducedMotionBlock()
    {
        var sheets = Stylesheets();
        var names = sheets.Select(s => s.Path).ToList();

        // A PERMANENT assertion that the glob found what it is supposed to, rather than the usual
        // break-it-and-watch-it-fail exercise, which leaves nothing behind: once the deliberate transition
        // is removed, nothing committed proves the discovery still reaches the component stylesheets, and a
        // later path refactor goes green in silence. Named files, because the count alone would survive one
        // being swapped for another.
        // All five of these can now fail, app.css included. It could not until Stylesheets() stopped
        // prepending that literal path beside the AppCss field: the assertion restated its own input, so a
        // comment saying it "fails loudly if app.css ever leaves the set" was describing the positive
        // control below -- Assert.Contains(..., ".loading-progress") -- and not this line. Both hold now,
        // and they hold different things: this one that the FILE is discovered at that path, that one that
        // its CONTENT reached the scan.
        Assert.Contains("wwwroot/css/app.css", names);
        Assert.Contains("Components/PosterTile.razor.css", names);
        Assert.Contains("Components/PosterGrid.razor.css", names);
        Assert.Contains("Layout/MainLayout.razor.css", names);
        Assert.Contains("Layout/NavMenu.razor.css", names);
        Assert.Contains(WebRoot.EditorPrefix + "Components/RecordForm.razor.css", names);
        Assert.Contains(WebRoot.EditorPrefix + "Components/Worklist.razor.css", names);

        var appGuards = SelectorsRedeclaredUnderReduce(AppCss);
        var all = sheets
            .Select(s => (s.Path,
                          Motion: MotionDeclarations(s.Text),
                          Guarded: SelectorsRedeclaredUnderReduce(s.Text).Concat(appGuards)
                                       .ToHashSet(StringComparer.Ordinal)))
            .ToList();

        // Positive control. Every assertion below loops over the motion found, so a discovery walk that
        // stopped matching would make this test green while proving nothing at all - which is the failure
        // mode the scan exists to stop. It also fails loudly if app.css ever leaves the set.
        Assert.Contains(all.SelectMany(s => s.Motion), m => m.Context.Contains(".loading-progress"));

        foreach (var sheet in all)
        foreach (var declaration in sheet.Motion)
            Assert.True(IsGuardedAgainstReducedMotion(declaration, sheet.Guarded),
                        sheet.Path + ": " + Unguarded(declaration));
    }

    /// <summary>
    /// Every stylesheet this project hand-writes: app.css plus every scoped component sheet, by glob.
    /// WebRoot.Files walks from the solution root and excludes obj and bin, so generated bundles and the
    /// Bootstrap copy under wwwroot/lib are not in it.
    /// </summary>
    /// <remarks>
    /// app.css is FOUND rather than prepended, and that is the whole difference between
    /// <c>Assert.Contains("wwwroot/css/app.css", names)</c> being a gate and being decoration. This helper
    /// used to concatenate that literal path with the AppCss field by hand, so the assertion restated its
    /// own input and could not fail however the file was renamed or moved. Now the glob has to find it at
    /// that path. The pattern is the bare file name on purpose: "*.css" would pull in Bootstrap's copy
    /// under wwwroot/lib, which this project does not write and must not be held to.
    /// </remarks>
    private static IReadOnlyList<(string Path, string Text)> Stylesheets() =>
        WebRoot.FilesWithEditor("app.css", "*.razor.css").ToList();

    /// <remarks>
    /// The motion rule driven against CSS written here, good and bad, because app.css contains one guard
    /// shape and the contract above can only ever prove what this one file happens to contain. The first
    /// case is the pattern the rule used to reject.
    /// </remarks>
    [Fact]
    public void TheMotionRuleAcceptsTheDeclareThenOverrideForm()
    {
        const string css = ".tile { transition: transform .2s ease; }"
                           + "@media (prefers-reduced-motion: reduce) { .tile { transition: none; } }";

        AssertEveryMotionDeclarationIsGuarded(css);
    }

    [Fact]
    public void TheMotionRuleAcceptsTheInvertedNoPreferenceForm()
    {
        const string css = "@media (prefers-reduced-motion: no-preference) "
                           + "{ .tile { transition: transform .2s ease; } }";

        AssertEveryMotionDeclarationIsGuarded(css);
    }

    [Fact]
    public void TheMotionRuleAcceptsABlanketResetUnderReduce()
    {
        // A universal reset under reduce is a common way to write it, and matching selector for selector
        // would reject every declaration in a stylesheet that is in fact fully guarded.
        const string css = ".tile { transition: transform .2s ease; }"
                           + ".card { animation: pulse 1s infinite; }"
                           + "@media (prefers-reduced-motion: reduce) { * { transition: none; } }";

        AssertEveryMotionDeclarationIsGuarded(css);
    }

    [Fact]
    public void TheMotionRuleAcceptsACommaSeparatedOverride()
    {
        const string css = ".tile { transition: transform .2s ease; }"
                           + "@media (prefers-reduced-motion: reduce) { .tile, .card { transition: none; } }";

        AssertEveryMotionDeclarationIsGuarded(css);
    }

    [Theory]
    // Nothing guarding it at all.
    [InlineData(".tile { transition: transform .2s ease; }")]
    // A reduce block, but for another selector: the one the author forgot is the one still moving.
    [InlineData(".tile { transition: transform .2s ease; }"
                + "@media (prefers-reduced-motion: reduce) { .card { transition: none; } }")]
    // Only half of a comma-separated pair is turned off.
    [InlineData(".tile, .card { transition: transform .2s ease; }"
                + "@media (prefers-reduced-motion: reduce) { .tile { transition: none; } }")]
    public void TheMotionRuleStillRejectsMotionNothingTurnsOff(string css)
    {
        var motion = MotionDeclarations(css).First();

        Assert.False(IsGuardedAgainstReducedMotion(motion, SelectorsRedeclaredUnderReduce(css)),
            "accepting both guard shapes is not the same as accepting an unguarded declaration, and this "
            + "one is unguarded: " + Unguarded(motion));
    }

    [Theory]
    [InlineData(".tile:hover { transition: transform .2s ease }")]
    [InlineData(".tile { animation: pulse 1s infinite }")]
    public void TheMotionScanSeesADeclarationWithNoTrailingSemicolon(string css)
    {
        // The semicolon after a block's LAST declaration is optional in CSS, and a scan that yielded only
        // on ';' never saw one that leaves it out -- the likeliest way to write a rule with a single
        // declaration in it. Both of these are real unguarded motion, and both used to go green unseen.
        var motion = Assert.Single(MotionDeclarations(css));

        Assert.False(IsGuardedAgainstReducedMotion(motion, SelectorsRedeclaredUnderReduce(css)),
            "the declaration was found but counted as guarded, which would be a different bug.");
    }

    private static void AssertEveryMotionDeclarationIsGuarded(string css)
    {
        var motion = MotionDeclarations(css);
        var guarded = SelectorsRedeclaredUnderReduce(css);

        Assert.NotEmpty(motion);
        Assert.All(motion, m => Assert.True(IsGuardedAgainstReducedMotion(m, guarded), Unguarded(m)));
    }

    private static string WithoutComments(string css) => Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline);

    private static IReadOnlyList<MotionDeclaration> MotionDeclarations(string css) =>
        Declarations(WithoutComments(css))
            .Where(d => Regex.IsMatch(d.Declaration, @"^(?:transition|animation)[a-z-]*\s*:"))
            .ToList();

    /// <summary>
    /// The selectors a @media (prefers-reduced-motion: reduce) block redeclares. This is what makes the
    /// declare-then-override form checkable: a motion declaration outside the media query is guarded when
    /// its own selector is in here. Selector lists are split on the comma, so a reduce block that turns off
    /// two selectors guards each of them separately, and a universal reset guards everything.
    /// </summary>
    private static IReadOnlySet<string> SelectorsRedeclaredUnderReduce(string css) =>
        Declarations(WithoutComments(css))
            .Where(d => Regex.IsMatch(d.Context, @"prefers-reduced-motion\s*:\s*reduce"))
            .SelectMany(d => SelectorParts(d.Selector))
            .ToHashSet(StringComparer.Ordinal);

    private static IReadOnlyList<string> SelectorParts(string selector) =>
        selector.Split(',')
            .Select(part => Regex.Replace(part, @"\s+", " ").Trim())
            .Where(part => part.Length > 0)
            .ToList();

    private static bool IsGuardedAgainstReducedMotion(MotionDeclaration motion, IReadOnlySet<string> guarded)
    {
        // Form one: inside a prefers-reduced-motion block of either polarity -- the inverted no-preference
        // wrapper, or a reduce block's own `transition: none`.
        if (motion.Context.Contains("prefers-reduced-motion")) return true;

        // The one exception. The boot spinner is gone before the app renders, and prefers-reduced-motion
        // cannot help someone looking at a blank page waiting for a payload.
        if (motion.Context.Contains(".loading-progress")) return true;

        // Form two: declare-then-override. A universal reset covers every selector; otherwise every part
        // of this declaration's own selector list has to be turned off, because the half that is not is
        // the half still moving.
        if (guarded.Contains("*")) return true;

        var parts = SelectorParts(motion.Selector);
        return parts.Count > 0 && parts.All(guarded.Contains);
    }

    private static string Unguarded(MotionDeclaration motion) =>
        $"`{motion.Declaration};` is declared under `{motion.Context}`, which is neither the boot spinner "
        + "nor guarded against prefers-reduced-motion. A user who asks their system to reduce motion must "
        + "get a stylesheet that honours it; the boot spinner is the one exception, because it is gone "
        + "before the app renders. Either drop the motion or guard it, in either of the two forms this "
        + "rule accepts: move the declaration inside @media (prefers-reduced-motion: no-preference), or "
        + $"leave it where it is and redeclare `{motion.Selector}` under "
        + "@media (prefers-reduced-motion: reduce) to turn it off there. Then say which in that "
        + "stylesheet's closing comment.";

    /// <summary>
    /// One declaration, the nested selectors and at-rules it sits under, and the innermost of those on its
    /// own -- which is the selector an override has to name for the declare-then-override form.
    /// </summary>
    private readonly record struct MotionDeclaration(string Context, string Selector, string Declaration);

    /// <summary>
    /// Every declaration in a stylesheet, paired with the context it sits under. Not a CSS parser: it
    /// tracks braces and semicolons only, so a semicolon inside a url() value splits that one value in
    /// two. That is harmless for a scan that only reads the start of a declaration, and it is the reason
    /// this is private to one class rather than offered as a utility.
    /// <para>
    /// A closing brace ends a declaration as well, because the semicolon after the LAST declaration in a
    /// block is optional in CSS. Yielding only on ';' therefore missed every declaration written without
    /// one -- a single-declaration rule with no trailing semicolon was never seen at all, which is the
    /// likeliest way to write one, and the motion scan passed over it with nothing in the output to
    /// suggest it had skipped anything. A more consequential limit than the url() one, which is why it is
    /// fixed behaviour with a regression case rather than a documented quirk:
    /// TheMotionScanSeesADeclarationWithNoTrailingSemicolon.
    /// </para>
    /// </summary>
    private static IEnumerable<MotionDeclaration> Declarations(string css)
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
                    // Before the pop, not after: the pending text belongs to the block that is closing.
                    var last = css[start..i].Trim();
                    if (last.Length > 0) yield return At(open, last);
                    if (open.Count > 0) open.RemoveAt(open.Count - 1);
                    start = i + 1;
                    break;
                case ';':
                    yield return At(open, css[start..i].Trim());
                    start = i + 1;
                    break;
            }
        }
    }

    private static MotionDeclaration At(List<string> open, string declaration) =>
        new(string.Join(" ", open), open.Count == 0 ? "" : open[^1], declaration);


    /// <summary>
    /// The ONE place a focus ring is suppressed, and the file's own statement of why there is no second.
    /// </summary>
    /// <remarks>
    /// SC 2.4.7 Focus Visible, Level AA. app.css:53 declares
    /// <c>#main-content:focus { outline: none; }</c> under the focus-ring rule at :50, and that one is
    /// legitimate: <c>&lt;main tabindex="-1"&gt;</c> is not keyboard operable, so 2.4.7 has no claim on it,
    /// and the move is one the USER asked for by activating the skip link - a ring round the whole of
    /// &lt;main&gt; would only be noise. The declaration at :334 says in prose that the precedent does NOT
    /// extend to <c>.catalog-state</c>, which IS focused programmatically and where the ring is the only
    /// thing telling a sighted user where their keyboard went. This turns both halves into something
    /// enforced rather than remembered: exactly one suppression, and that sentence still in the file to
    /// point a reader at.
    /// <para>
    /// The scan reads EVERY hand-written stylesheet, not just app.css, because a scoped
    /// <c>outline: none</c> in a component sheet removes the ring from that component's controls just as
    /// effectively. Comments are stripped first, so prose about outlines cannot trip it.
    /// </para>
    /// <para>
    /// What this cannot catch: an inline style, or a Bootstrap utility in the markup. Those need the
    /// rendered page, which is <c>MovieLibrary.Web.A11y.FocusVisibleTests</c> - it walks the real tab order
    /// with real keypresses, so <c>:focus-visible</c> applies, and reads the outline the browser painted.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheOnlyFocusRingSuppressionIsTheSkipTargetAndTheFileSaysWhyThereIsNoSecond()
    {
        var sheets = Stylesheets();

        Assert.True(sheets.Count >= 10,
            "This rule found " + sheets.Count + " stylesheets. There are ten: app.css, seven scoped "
            + "component and page sheets in the web project and two in the editor. Fewer means the glob stopped finding them and a missing suppression could "
            + "sit in one of the sheets this never read.");

        var suppressions = sheets
            .SelectMany(sheet => Declarations(WithoutComments(sheet.Text))
                .Where(IsOutlineSuppression)
                .Select(d => new { sheet.Path, d.Selector, d.Declaration }))
            .ToList();

        var only = Assert.Single(suppressions);

        Assert.Equal("wwwroot/css/app.css", only.Path);
        Assert.True(only.Selector == "#main-content:focus",
            "The one permitted focus-ring suppression in this project is `#main-content:focus { outline: "
            + "none; }` at app.css:53, which is legitimate because <main tabindex=\"-1\"> is not keyboard "
            + "operable and SC 2.4.7 Focus Visible (Level AA) has no claim on it. This one is on `"
            + only.Selector + "` in " + only.Path + ": `" + only.Declaration + ";`. app.css:334 already "
            + "states why that precedent does not extend - .catalog-state is focused PROGRAMMATICALLY, by "
            + "the page, because the fetch failed, so the ring is the only thing telling a sighted user "
            + "where their keyboard now is. Read that comment before adding a second.");

        // The failure message above points a reader at a sentence in app.css. If that sentence goes, the
        // message is pointing at nothing, so it is pinned here rather than trusted to survive.
        Assert.Contains("precedent above does not extend to it", AppCss);
    }

    /// <summary>
    /// Whether a declaration turns a focus ring off. <c>outline-offset</c> is deliberately not matched: it
    /// moves a ring, it does not remove one, and <c>outline-offset: 0</c> is a perfectly good value.
    /// </summary>
    /// <remarks>
    /// Anchored at both ends, so <c>outline: 3px solid var(--accent)</c> cannot match on its first token
    /// and <c>outline: none !important</c> cannot escape on its last. The unit group covers
    /// <c>0</c>, <c>0px</c>, <c>0em</c> and <c>0%</c>, which all mean the same thing here.
    /// </remarks>
    private static bool IsOutlineSuppression(MotionDeclaration declaration) =>
        Regex.IsMatch(declaration.Declaration,
            @"^outline(-style|-width)?\s*:\s*(none|0[a-z%]*)(\s*!important)?$");

    /// <summary>
    /// No control's boundary is drawn in the decoration-only token.
    /// </summary>
    /// <remarks>
    /// SC 1.4.11 Non-text Contrast, Level AA - and <b>axe has no rule for it at all</b>, at any tag, so
    /// nothing in Task 4b's scan can see this. app.css's own header states the measurement: <c>--line</c>
    /// is 1.37:1 against <c>--bg</c> and FAILS 1.4.11, so it is for decoration only, and any border that is
    /// a control's sole boundary uses <c>--line-strong</c> at 3.89:1. The file also records that this went
    /// wrong once already: an earlier draft dropped the Try again button's border to <c>--line</c> while it
    /// carried <c>aria-disabled</c>, and with <c>background: transparent</c> that border is the control's
    /// only boundary, so for the whole of a retry the button had none a low-vision reader could find.
    /// Task 8's search input and facet chips are where it creeps back.
    /// <para>
    /// <b>Two tokens that are NOT violations and must not become ones.</b> <c>.attribution</c>'s
    /// <c>border-top</c> and <c>.poster-placeholder</c>'s <c>border</c> both use <c>--line</c> deliberately:
    /// the first is a grouping cue on a footer, the second is a decorative box whose meaning is carried by
    /// the title beside it, and PosterTile.razor.css argues the second out of 1.4.11's scope in writing.
    /// Neither is a control, so neither is a subject here - and <c>.attribution</c>'s has a second job
    /// now: it is the live positive witness that keeps this scan from failing open on a token rename. See
    /// the comment beside it in the body.
    /// </para>
    /// <para>
    /// <b>The blind spot, recorded rather than hidden.</b> A text scan can only see the SELECTOR, so a
    /// control chosen by class - <c>.try-again</c>, <c>.nav-toggle</c>, <c>#blazor-error-ui .dismiss</c> -
    /// is invisible to it unless the rule also carries an interactive state. That is exactly the shape the
    /// one real regression had. The gap is closed in a browser instead, by
    /// <c>MovieLibrary.Web.A11y.ControlBoundaryTests</c>, which reads the computed border of every control
    /// the page actually renders and so does not care how it was selected.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoControlsBoundaryIsDrawnInTheDecorationOnlyToken()
    {
        var sheets = Stylesheets();

        Assert.True(sheets.Count >= 10,
            "This rule found " + sheets.Count + " stylesheets. There are ten: app.css, seven scoped "
            + "component and page sheets in the web project and two in the editor. Fewer means the glob stopped finding them.");

        var borders = sheets
            .SelectMany(sheet => Declarations(WithoutComments(sheet.Text))
                .Where(d => IsABorderDeclaration(d.Declaration) && NamesAControl(d.Selector))
                .Select(d => new { sheet.Path, d.Selector, d.Declaration }))
            .ToList();

        // Rule T1, by NAME and not by count: a count alone survives one subject being swapped for
        // another, and the whole failure this project keeps finding is a rule whose subjects quietly left.
        Assert.Contains(borders, b => b.Selector == ".pager button");
        Assert.Contains(borders, b => b.Selector.Contains("a.active", StringComparison.Ordinal));
        Assert.Contains(borders, b => b.Selector == ".skip-link:focus");

        Assert.True(borders.Count >= 4,
            "This rule found " + borders.Count + " border declaration(s) on a selector that names a "
            + "control. There are at least four - .pager button, .nav-links ::deep a.active, "
            + ".skip-link:focus and .try-again:hover - so fewer means NamesAControl stopped matching and "
            + "the loop below passes over nothing. Found: "
            + string.Join("; ", borders.Select(b => b.Selector + " { " + b.Declaration + " }")));

        // A LIVE POSITIVE WITNESS for the token predicate, and without it this whole scan fails open on a
        // token rename. MEASURED: rename --line to --edge consistently through app.css and the scoped
        // sheets - a behaviour-preserving refactor - and point .try-again's border at the renamed token.
        // UsesTheDecorationToken then matches nothing at all, because it matches the literal string
        // `var(--line)`; the loop below finds zero violations while the floor above is still satisfied,
        // since that floor counts border DECLARATIONS rather than token uses. The result is a green gate
        // over the exact 1.37:1 regression this rule exists to catch. The browser half in
        // ControlBoundaryTests missed the same rename for its own reason and now guards against it too.
        //
        // .attribution's border-top is the witness because it is a real, deliberate, documented and
        // NON-control use of the token - the footer grouping cue argued out of 1.4.11's scope in the
        // remarks above. It cannot be satisfied by a tree in which the token has been renamed away, which
        // is the one property a witness for this failure has to have.
        var witness = sheets
            .SelectMany(sheet => Declarations(WithoutComments(sheet.Text))
                .Where(d => d.Selector == ".attribution" && IsABorderDeclaration(d.Declaration))
                .Select(d => new { sheet.Path, d.Declaration }))
            .ToList();

        Assert.True(witness.Count == 1,
            "This rule found " + witness.Count + " border declaration(s) on `.attribution`, not the one "
            + "app.css carries at :234. That declaration is the live positive witness for the token "
            + "predicate below, so without exactly one of it nothing proves the predicate still matches "
            + "anything in this project. Found: "
            + string.Join("; ", witness.Select(w => w.Path + ": " + w.Declaration)));

        Assert.True(UsesTheDecorationToken(witness[0].Declaration),
            witness[0].Path + ": `.attribution { " + witness[0].Declaration + "; }` is this project's "
            + "one deliberate NON-control use of the decoration token, and UsesTheDecorationToken no "
            + "longer reads it as using one. The predicate therefore matches nothing in the tree as the "
            + "tree now spells it, and the loop below reports a clean pass whatever any control's border "
            + "is set to: the SC 1.4.11 Non-text Contrast (Level AA) scan has stopped running rather "
            + "than started passing. If --line has been renamed, update this predicate and "
            + "MovieLibrary.Web.A11y.ControlBoundaryTests' probe in the same change - both halves of the "
            + "gate miss a rename, and they miss it for different reasons.");

        foreach (var border in borders)
            Assert.True(!UsesTheDecorationToken(border.Declaration),
                border.Path + ": `" + border.Selector + " { " + border.Declaration + "; }` draws a "
                + "control's border in var(--line), which app.css measures at 1.37:1 against --bg and "
                + "records as FAILING SC 1.4.11 Non-text Contrast (Level AA). A border that is a control's "
                + "sole boundary uses var(--line-strong), 3.89:1, which is the token this file reserves "
                + "for exactly that. axe has no non-text-contrast rule, so nothing else in this repository "
                + "will catch it.");
    }

    /// <summary>Whether a declaration sets any part of a border.</summary>
    private static bool IsABorderDeclaration(string declaration) =>
        Regex.IsMatch(declaration, @"^border(-(top|right|bottom|left))?(-color|-width|-style)?\s*:");

    /// <summary>
    /// Whether a declaration's value reads the decoration-only token. The closing parenthesis is part of
    /// the pattern on purpose: <c>var(--line-strong)</c> starts with the same seven characters, and a
    /// prefix match would flag every correct boundary in the project.
    /// </summary>
    private static bool UsesTheDecorationToken(string declaration) =>
        Regex.IsMatch(declaration, @"var\(\s*--line\s*\)");

    /// <summary>
    /// Whether a selector names a control, as far as reading the selector can tell. Element names,
    /// <c>[role=button]</c>, and the interactive pseudo-classes - a rule written for a hover, a focus or a
    /// disabled state is a rule about something a user operates, whatever it is selected by.
    /// </summary>
    /// <remarks>
    /// The leading and trailing character classes stop a bare element name matching inside a longer
    /// identifier: without them <c>.attribution</c> and <c>.nav-links</c> would both look like the element
    /// <c>a</c>. See the class-selector blind spot in the remarks above for what this deliberately cannot
    /// see.
    /// </remarks>
    private static bool NamesAControl(string selector) =>
        Regex.IsMatch(selector,
            @"(?:^|[\s>+~,(])(?:a|button|input|select|textarea|summary)(?:[.#:\[\s>+~,)]|$)")
        || Regex.IsMatch(selector, @"\[role\s*=\s*[""']?button")
        || Regex.IsMatch(selector, @":(?:hover|focus|focus-visible|active|disabled|checked)\b")
        || selector.Contains("[aria-disabled", StringComparison.Ordinal);

    /// <summary>
    /// The three predicates above, over the forms this project actually contains. Rule T4: the rule is
    /// mutated, not the assertion, and a pure function is unit-tested directly - and rule T5: a rule that
    /// rejects the canonical form gets deleted, so every correct shape in the project is pinned as
    /// accepted here before the scan above is allowed to rely on it.
    /// </summary>
    [Theory]
    [InlineData("border: 2px solid var(--line-strong)", true)]
    [InlineData("border-top: 1px solid var(--line)", true)]
    [InlineData("border-color: var(--accent)", true)]
    [InlineData("border-width: 2px", true)]
    [InlineData("border-bottom: 3px solid var(--accent)", true)]
    [InlineData("border-radius: 6px", false)]
    [InlineData("border-top-left-radius: .375rem", false)]
    [InlineData("background: var(--line)", false)]
    [InlineData("outline: 3px solid var(--accent)", false)]
    public void IsABorderDeclarationReadsThePropertyAndNotTheValue(string declaration, bool expected) =>
        Assert.Equal(expected, IsABorderDeclaration(declaration));

    /// <summary>
    /// <c>var(--line-strong)</c> must NOT read as the decoration token: it shares its first seven
    /// characters, and every correct control boundary in the project uses it.
    /// </summary>
    [Theory]
    [InlineData("border: 2px solid var(--line)", true)]
    [InlineData("border-top: 1px solid var( --line )", true)]
    [InlineData("border: 2px solid var(--line-strong)", false)]
    [InlineData("border-color: var(--line-strong)", false)]
    [InlineData("border: 2px solid var(--accent)", false)]
    public void UsesTheDecorationTokenDoesNotMatchTheStrongToken(string declaration, bool expected) =>
        Assert.Equal(expected, UsesTheDecorationToken(declaration));

    /// <summary>
    /// What reading a selector can and cannot tell. The <c>false</c> rows from <c>.try-again</c> down are
    /// the blind spot, asserted rather than hidden: those three ARE controls, this predicate cannot tell,
    /// and <c>MovieLibrary.Web.A11y.ControlBoundaryTests</c> is what covers them. If a later task teaches
    /// the predicate to see them, these rows are what to update.
    /// </summary>
    [Theory]
    [InlineData(".pager button", true)]
    [InlineData("a", true)]
    [InlineData(".nav-links ::deep a.active", true)]
    [InlineData("main p a[href=\"movies\"]", true)]
    [InlineData(".skip-link:focus", true)]
    [InlineData(".try-again:hover", true)]
    [InlineData("[role=\"button\"]", true)]
    [InlineData("input[type=\"search\"]", true)]
    [InlineData(":root :is(a, button):active", true)]
    [InlineData(".attribution", false)]
    [InlineData(".poster-placeholder", false)]
    [InlineData("#blazor-error-ui", false)]
    [InlineData("nav", false)]
    [InlineData(".nav-links", false)]
    [InlineData(".try-again", false)]
    [InlineData(".nav-toggle", false)]
    [InlineData("#blazor-error-ui .dismiss", false)]
    public void NamesAControlSeesElementsAndStatesButNotClasses(string selector, bool expected) =>
        Assert.Equal(expected, NamesAControl(selector));

    /// <summary>
    /// No media query in this project asks which way round the device is.
    /// </summary>
    /// <remarks>
    /// SC 1.3.4 Orientation, Level AA - and this is the cheap half. An orientation media query is the
    /// mechanism by which a stylesheet restricts content to one orientation, usually by hiding the page
    /// and showing a "please rotate your device" panel in its place, and there is no legitimate use of one
    /// in this app: every layout here is driven by the available WIDTH, which is what
    /// <c>(min-width: 48rem)</c> and <c>(max-width: 22.5rem)</c> ask, and a width query says nothing about
    /// which edge is up.
    /// <para>
    /// This is a text scan and so it cannot see an inline style, a utility class or a layout that simply
    /// collapses. <c>MovieLibrary.Web.A11y.OrientationTests</c> rotates the viewport under a booted page
    /// and compares what is on it, which is the half that measures the result.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoStylesheetChangesWhatContentExistsWithTheOrientation()
    {
        var queries = Stylesheets()
            .SelectMany(sheet => Regex.Matches(WithoutComments(sheet.Text), @"@media[^{]*")
                .Select(m => new { sheet.Path, Query = Regex.Replace(m.Value, @"\s+", " ").Trim() }))
            .ToList();

        // Rule T1, and by name rather than by count: the three queries this project has are each pinned,
        // so one of them being deleted or rewritten shows up here instead of quietly shrinking the set the
        // loop below walks. The exact strings also record what each one asks about: the available WIDTH
        // (min-width, max-width), the POINTER (hover: hover) or the colour MODE (forced-colors: active).
        // None of those is satisfied or unsatisfied by a rotation, which is the point; the rule below is
        // about the one query that would be, and it is correct on all of them.
        Assert.Contains(queries, q => q.Query == "@media (hover: hover)");
        Assert.Contains(queries, q => q.Query == "@media (forced-colors: active)");
        Assert.Contains(queries, q => q.Query == "@media (min-width: 48rem)");
        Assert.Contains(queries, q => q.Query == "@media (max-width: 22.5rem)");
        Assert.Contains(queries, q => q.Path.StartsWith(WebRoot.EditorPrefix, StringComparison.Ordinal));

        Assert.True(queries.Count >= 11,
            $"This rule found {queries.Count} media queries outside comments. There are eleven across "
            + "app.css and the component sheets of both projects (three of them in the editor), and later "
            + "tasks only add. Fewer means the discovery stopped working and the loop below passes "
            + "over nothing. Found: " + string.Join("; ", queries.Select(q => q.Path + ": " + q.Query)));

        foreach (var query in queries)
            Assert.True(!query.Query.Contains("orientation", StringComparison.OrdinalIgnoreCase),
                query.Path + ": `" + query.Query + "` branches on the display orientation. SC 1.3.4 "
                + "Orientation (Level AA) says content must not restrict its view and operation to a "
                + "single display orientation, and an orientation media query is how a stylesheet does "
                + "that. Every layout in this project is driven by the available width instead, which is "
                + "what (min-width:) and (max-width:) ask and which says nothing about which edge is up.");
    }

    // ================================================================== the component stylesheets

    private static readonly string TileCss = WebRoot.Read("Components", "PosterTile.razor.css");
    private static readonly string GridCss = WebRoot.Read("Components", "PosterGrid.razor.css");
    private static readonly string FacetCss = WebRoot.Read("Components", "FacetPanel.razor.css");

    /// <remarks>
    /// The clipping properties are banned by name. A two-line clamp holds roughly 30 to 35 characters at
    /// this column width in a semibold face, and real titles go to 49 -- with no link, no title attribute
    /// and no label on the tile, a sighted reader would have no way at all to read the rest, and the
    /// uncertain matches are precisely the records whose titles need reading. Forcing the SC 1.4.12 Text
    /// Spacing overrides makes it strictly worse, because fewer characters then fit on each line.
    /// min-height is required to be in em: the box is reserving two lines of THIS text, so a px floor
    /// stops being two lines the moment the reader changes the size. That is the em unit on its own
    /// merits; it is not what 1.4.12 is about, since a minimum simply grows.
    /// </remarks>
    [Fact]
    public void TheTileTitleWrapsRatherThanClipping()
    {
        var css = WithoutComments(TileCss);

        Assert.DoesNotContain("line-clamp", css);
        Assert.DoesNotContain("text-overflow", css);
        Assert.DoesNotMatch(@"overflow\s*:\s*hidden", css);
        Assert.Matches(@"\.tile-title\s*\{[^}]*min-height\s*:\s*[\d.]+em", css);
        Assert.Matches(@"\.grid\s*\{[^}]*align-items\s*:\s*start", WithoutComments(GridCss));
    }

    /// <remarks>
    /// The greyed state is selected by the attribute, never by :disabled, because the button is never
    /// disabled -- see PosterGrid.Go for why. A :disabled rule here would be dead CSS that quietly
    /// legitimised going back to the attribute. opacity stays at 1: the state is carried by a colour that
    /// passes contrast, and --dim on --panel2 is 5.43:1, which choosing aria-disabled makes load-bearing
    /// rather than spare, because the control is still focusable and so outside SC 1.4.3's exemption for
    /// an inactive component.
    /// <para>
    /// background-color is pinned too, and on this rule rather than only on the enabled one. app.css's
    /// <c>:root :is(a, button):active</c> sets a background-color at (0,2,1) and beats the plain
    /// <c>background</c> on <c>.pager button</c>, so without a background-color here a greyed button lit up
    /// on press while doing nothing at all. Not a conformance defect -- --dim on --line is 4.61:1, past
    /// SC 1.4.3's 4.5:1 -- which is exactly why it needs a test rather than a reviewer noticing.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheGreyedPagerButtonIsSelectedByTheAttributeAndKeepsItsOpacity()
    {
        var css = WithoutComments(GridCss);

        Assert.Matches(@"\.pager button\[aria-disabled=""true""\]\s*\{[^}]*color\s*:\s*var\(--dim\)", css);
        Assert.Matches(@"\.pager button\[aria-disabled=""true""\]\s*\{[^}]*opacity\s*:\s*1", css);
        Assert.Matches(
            @"\.pager button\[aria-disabled=""true""\]\s*\{[^}]*background-color\s*:\s*var\(--panel2\)", css);
        Assert.DoesNotContain(":disabled", css);
    }

    /// <remarks>
    /// The buttons read --touch rather than repeating its value, so a change to the token moves them with
    /// it. Deliberately NOT a conformance assertion about the number: 44x44 is SC 2.5.5 Target Size
    /// (Enhanced), Level AAA, and the criterion this project is held to is SC 2.5.8 Target Size (Minimum),
    /// Level AA, at 24x24. The rendered size is measured in a browser; this only pins that the declaration
    /// is there and that nobody wrote 44px twice.
    /// </remarks>
    [Fact]
    public void ThePagerButtonsReadTheTouchTokenRatherThanRepeatingItsValue()
    {
        var css = WithoutComments(GridCss);

        Assert.Matches(@"\.pager button\s*\{[^}]*min-width\s*:\s*var\(--touch\)", css);
        Assert.Matches(@"\.pager button\s*\{[^}]*min-height\s*:\s*var\(--touch\)", css);
        Assert.DoesNotContain("44px", css);
        Assert.Matches(@"\.pager button\s*\{[^}]*border\s*:\s*2px solid var\(--line-strong\)", css);
    }

    /// <remarks>
    /// Two halves, and which is which matters. SC 1.4.10 Reflow requires the content to be presentable at
    /// 320 CSS px with no two-dimensional scrolling; it does NOT require two columns -- one column is the
    /// canonical Reflow outcome and is fully conformant. The two-column floor is a HOUSE RULE about
    /// density. The breakpoint also has to join cleanly to the auto-fill rule, or a third cramped column
    /// appears on a 360px phone.
    /// </remarks>
    [Fact]
    public void TheNarrowestWidthGetsTwoColumnsWhichIsAHouseRuleAndNotReflow()
    {
        var css = WithoutComments(GridCss);

        Assert.Matches(@"\.grid\s*\{[^}]*grid-template-columns\s*:\s*repeat\(auto-fill,\s*minmax\(7rem,\s*1fr\)\)", css);
        Assert.Matches(@"@media \(max-width:\s*22\.5rem\)\s*\{\s*\.grid\s*\{[^}]*repeat\(2,\s*1fr\)", css);
    }

    [Fact]
    public void TheTileWallKeepsNoListBulletsAndNoDefaultListPadding()
    {
        // list-style: none is what makes role="list" in the markup load-bearing rather than redundant:
        // WebKit drops list semantics from a list styled this way, and again from a grid container.
        var css = WithoutComments(GridCss);

        Assert.Matches(@"\.grid\s*\{[^}]*list-style\s*:\s*none", css);
        Assert.Matches(@"\.grid\s*\{[^}]*padding\s*:\s*0", css);
    }

    // ================================================================ the facet panel's stylesheet

    /// <remarks>
    /// R-O. iOS Safari zooms the page when a field whose COMPUTED font size is under 16px takes focus, and that
    /// zoom breaks reflow. The declaration is max(16px, 1rem) and not 1rem alone, because 1rem is 14px for a
    /// reader who has set a smaller default. Both fields are pinned: the select zooms the same way.
    /// </remarks>
    [Fact]
    public void TheSearchBoxAndTheSelectAreNeverUnderSixteenPixelsSoIosDoesNotZoomOnFocus()
    {
        var css = WithoutComments(FacetCss);

        Assert.Matches(@"input\[type=""search""\],\s*select\s*\{[^}]*font-size\s*:\s*max\(16px,\s*1rem\)", css);
    }

    [Fact]
    public void EveryFacetControlReadsTheTouchTokenAndDrawsItsBoundaryInTheStrongToken()
    {
        var css = WithoutComments(FacetCss);

        // The LABEL is the tap target for a checkbox or a radio, and the field itself for the other two.
        Assert.Matches(@"\.options label\s*\{[^}]*min-width\s*:\s*var\(--touch\)", css);
        Assert.Matches(@"\.options label\s*\{[^}]*min-height\s*:\s*var\(--touch\)", css);
        Assert.Matches(@"input\[type=""search""\],\s*select\s*\{[^}]*min-height\s*:\s*var\(--touch\)", css);
        Assert.Matches(@"input\[type=""search""\],\s*select\s*\{[^}]*border\s*:\s*2px solid var\(--line-strong\)", css);
        Assert.DoesNotContain("44px", css);

        // The native box inside the label is rem, so it grows with the reader's own text size.
        Assert.Matches(@"input\[type=""checkbox""\],\s*input\[type=""radio""\]\s*\{[^}]*width\s*:\s*1\.5rem", css);
    }

    /// <remarks>
    /// The cross-task rule: hover styles live inside @media (hover: hover), because on a touch screen :hover
    /// sticks after the tap and the control stays in its hover state until something else is tapped. Read over
    /// EVERY stylesheet and not only the new one, so the next one added is held to it too. Rule T1: the scan names
    /// the rules it must find, because a parser that stopped seeing :hover would loop over nothing and pass.
    /// </remarks>
    [Fact]
    public void NoHoverRuleSitsOutsideTheHoverMediaQueryInAnyStylesheet()
    {
        var hovers = Stylesheets()
            .SelectMany(sheet => Declarations(WithoutComments(sheet.Text))
                .Where(d => d.Selector.Contains(":hover", StringComparison.Ordinal))
                .Select(d => new { sheet.Path, d.Context, d.Selector }))
            .ToList();

        Assert.Contains(hovers, h => h.Selector == ".try-again:hover");
        Assert.Contains(hovers, h => h.Selector == ".clear-filters:hover");
        Assert.Contains(hovers, h => h.Path == "Components/FacetPanel.razor.css");

        Assert.All(hovers, h => Assert.True(
            h.Context.Contains("@media (hover: hover)", StringComparison.Ordinal),
            h.Path + ": `" + h.Selector + "` is a hover rule outside @media (hover: hover). On a touch screen "
            + ":hover sticks after the tap."));
    }

    [Fact]
    public void ThePanelsStylesheetIsDiscoveredAndTheNoMatchStateReadsTheSameTokensAsTryAgain()
    {
        Assert.Contains("Components/FacetPanel.razor.css", Stylesheets().Select(s => s.Path));

        var css = WithoutComments(AppCss);

        Assert.Matches(@"\.clear-filters\s*\{[^}]*min-width\s*:\s*var\(--touch\)", css);
        Assert.Matches(@"\.clear-filters\s*\{[^}]*min-height\s*:\s*var\(--touch\)", css);
        Assert.Matches(@"\.clear-filters\s*\{[^}]*border\s*:\s*2px solid var\(--line-strong\)", css);
        Assert.Matches(@"\.no-match p\s*\{[^}]*line-height\s*:\s*1\.5", css);
    }
}

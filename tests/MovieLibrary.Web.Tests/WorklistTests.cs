using Microsoft.AspNetCore.Components;
using AngleSharp.Dom;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MovieLibrary.Core.Matching;
using MovieLibrary.Core.Models;
using MovieLibrary.Web.Editor;
using MovieLibrary.Web.Editor.Components;
using MovieLibrary.Web.Editor.Store;

namespace MovieLibrary.Web.Tests;

/// <summary>The worklist rendered directly. Every assertion here is about a row a keyboard or a reader meets.</summary>
public sealed class WorklistTests
{
    private sealed class Region : IStatusRegion
    {
        public List<string> Said { get; } = [];

        public void Status(string text) => Said.Add(text);
    }

    /// <summary>One context with a clock the test owns and a region it can read back.</summary>
    private sealed class Harness : IAsyncDisposable
    {
        public BunitContext Context { get; } = new();
        public ManualTimeProvider Clock { get; } = new();
        public Region Region { get; } = new();

        public Harness()
        {
            Context.JSInterop.Mode = JSRuntimeMode.Loose;
            Context.Services.AddSingleton<TimeProvider>(Clock);
            Context.Services.AddSingleton<IStatusRegion>(Region);
        }

        public IRenderedComponent<Worklist> Render(
            IReadOnlyList<WorklistRow> rows, string? open = null, Action<string>? opened = null, Action? closed = null) =>
            Context.Render<Worklist>(p => p
                .Add(w => w.Rows, rows)
                .Add(w => w.OpenKey, open)
                .Add(w => w.Opened, (string k) => opened?.Invoke(k))
                .Add(w => w.Closed, () => closed?.Invoke()));

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    internal static WorklistRow Row(MovieRecord movie)
    {
        var band = ConfidenceBand.Of(movie);
        return new WorklistRow(movie, RecordLink.IdentityKeyOf(movie), band, ConfidenceBand.BadgeFor(band), null);
    }

    private static MovieRecord Film(int id, string title, int runtime, double mc, string? poster = null) =>
        EditorCatalogueTests.Film(id, title, runtime, mc, "A. Director") with { P = poster };

    // A matched key begins with a digit ("550-139"), which is the common case: 255 of the 285 worklist rows.
    private static readonly WorklistRow Uncertain = Row(Film(550, "Fight Club", 139, 0.5));
    private static readonly WorklistRow Unmatched = Row(Film(0, "Lost Reel", 90, 0));
    private static readonly WorklistRow Second = Row(Film(807, "Se7en", 127, 0.6));

    private static string NameOf(IElement button) =>
        string.Join(' ', button.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static IReadOnlyList<WorklistRow> Three => [Uncertain, Unmatched, Second];

    [Fact]
    public async Task TheListIsAListAndEveryChildIsAnItem()
    {
        await using var h = new Harness();
        var cut = h.Render(Three);

        var list = cut.Find("ul.worklist");
        Assert.Equal("list", list.GetAttribute("role"));
        Assert.Equal(3, list.Children.Length);
        Assert.All(list.Children, c => Assert.Equal("li", c.LocalName));
        Assert.DoesNotContain("grid", list.ClassList);
    }

    [Fact]
    public async Task ACollapsedRowHasExactlyOneFocusableElementAndItIsItsToggle()
    {
        await using var h = new Harness();
        var cut = h.Render(Three);   // no row open: an open one makes the count ~23 for an unrelated reason

        Assert.All(cut.FindAll("ul.worklist > li"), li =>
        {
            var only = Assert.Single(TabStops.Stops(li));
            Assert.Equal("button", only.LocalName);
            Assert.Equal("false", only.GetAttribute("aria-expanded"));
        });
    }

    [Fact]
    public async Task ARowsToggleHasNoLabelOfItsOwnAndItsNameIsItsText()
    {
        await using var h = new Harness();
        var cut = h.Render(Three);

        Assert.All(cut.FindAll("button.worklist-row"), toggle =>
        {
            Assert.False(toggle.HasAttribute("aria-label"));
            Assert.False(toggle.HasAttribute("aria-labelledby"));
        });
    }

    [Fact]
    public async Task TheBandComesBeforeTheTitleInTheDocument()
    {
        await using var h = new Harness();
        var cut = h.Render([Uncertain]);

        var toggle = cut.Find("button.worklist-row");
        var badge = toggle.QuerySelector(".worklist-badge")!;
        var title = toggle.QuerySelector(".worklist-title")!;

        Assert.True((badge.CompareDocumentPosition(title) & DocumentPositions.Following) != 0);
        Assert.StartsWith(ConfidenceBand.BadgeFor(Confidence.Uncertain)!, NameOf(toggle), StringComparison.Ordinal);
    }

    /// <summary>
    /// A pair the real library holds twice: one TMDB film, two cuts, both sheet rows agreeing with TMDB so both
    /// score 0.85 and land in the same band with the same title and year. Only the runtime separates them, which
    /// is why the key folds the runtime in. MovieLibrary.Web.Tests cannot reference the Core test project, so the
    /// pair is copied here; three arbitrary films would make the uniqueness check vacuous for the one case that occurs.
    /// </summary>
    [Fact]
    public async Task EveryRowsToggleIsNamedDifferentlyEvenForTwoCutsOfOneFilm()
    {
        await using var h = new Harness();
        const string title = "The Lord of the Rings: The Fellowship of the Ring";
        var cut = h.Render([Row(Film(120, title, 178, 0.85)), Row(Film(120, title, 228, 0.85)), Uncertain, Unmatched]);

        cut.FindAll("input[name=worklist-band]")[3].Change(true);   // All records: the pair is confident

        var names = cut.FindAll("button.worklist-row").Select(NameOf).ToList();

        Assert.Equal(4, names.Count);
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(names, n => n.Contains(title, StringComparison.Ordinal) && n.Contains("2h 58m", StringComparison.Ordinal));
        Assert.Contains(names, n => n.Contains(ConfidenceBand.BadgeFor(Confidence.Uncertain)!, StringComparison.Ordinal)
                                    && n.Contains("Fight Club", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheOpenRowsToggleSaysTrueAsAStringAndPointsAtSomethingThatExistsEvenWhenTheKeyStartsWithADigit()
    {
        await using var h = new Harness();
        var cut = h.Render(Three, open: Uncertain.Movie.K);

        Assert.Equal("550-139", Uncertain.Movie.K);

        var toggle = cut.FindAll("button.worklist-row").Single(b => b.GetAttribute("aria-expanded") == "true");
        var controls = toggle.GetAttribute("aria-controls");

        Assert.Equal("row-550-139-fields", controls);
        // Looked up by attribute: an id that begins with a digit is a CSS syntax error as a bare selector, which
        // is why the id carries a letter prefix and why this must be tested with a MATCHED key.
        Assert.Single(cut.FindAll($"[id='{controls}']"));
    }

    [Fact]
    public async Task AClosedRowsToggleSaysFalseAsAStringRatherThanOmittingIt()
    {
        await using var h = new Harness();
        var cut = h.Render(Three);

        Assert.All(cut.FindAll("button.worklist-row"), b => Assert.Equal("false", b.GetAttribute("aria-expanded")));
    }

    [Fact]
    public async Task ARecordWithNoPosterEmitsNoImageAtAll()
    {
        await using var h = new Harness();
        var cut = h.Render([Uncertain]);

        Assert.Empty(cut.FindAll("img"));
        Assert.Single(cut.FindAll("span.worklist-thumb[aria-hidden=true]"));
    }

    [Fact]
    public async Task APosterCarriesEmptyAltAndIntegerDimensionsAndNothingStretchesIt()
    {
        await using var h = new Harness();
        var cut = h.Render([Row(Film(550, "Fight Club", 139, 0.5, poster: "/abc.jpg"))]);

        var image = cut.Find("img");

        Assert.True(image.HasAttribute("alt"));
        Assert.Equal(string.Empty, image.GetAttribute("alt"));
        Assert.True(int.TryParse(image.GetAttribute("width"), out _));
        Assert.True(int.TryParse(image.GetAttribute("height"), out _));
        Assert.DoesNotContain(image.GetAttribute("src") ?? "", "null", StringComparison.Ordinal);

        // A20: no ancestor of the poster is a .tile, and the list is not a .grid.
        for (var p = image.ParentElement; p is not null; p = p.ParentElement)
            Assert.DoesNotContain("tile", p.ClassList);
    }

    [Fact]
    public async Task TheBadgeWordsAreCoresAndNotOurs()
    {
        await using var h = new Harness();
        var cut = h.Render(Three);

        foreach (var row in Three)
        {
            var toggle = cut.FindAll("li").Single(li => li.TextContent.Contains(row.Movie.T, StringComparison.Ordinal));
            Assert.Equal(row.Badge, toggle.QuerySelector(".worklist-badge")!.TextContent.Trim());
        }

        Assert.Equal(ConfidenceBand.BadgeFor(Confidence.Unmatched),
            cut.Find(".worklist-badge-unmatched").TextContent.Trim());
    }

    [Fact]
    public async Task TheSearchFieldHasARealLabelAndNotAPlaceholder()
    {
        await using var h = new Harness();
        var cut = h.Render(Three);

        var input = cut.Find("search input[type=search]");
        var label = cut.Find($"label[for='{input.Id}']");

        Assert.False(string.IsNullOrWhiteSpace(label.TextContent));
        Assert.False(input.HasAttribute("placeholder"));
        Assert.Single(cut.FindAll("search"));
        Assert.False(cut.Find("search").HasAttribute("aria-label"));
        Assert.False(cut.Find("search").HasAttribute("aria-labelledby"));
    }

    [Fact]
    public async Task TheCountsSentenceIsComposedFromCoresWordsAndIsPlainText()
    {
        await using var h = new Harness();
        var cut = h.Render(Three);

        var sentence = cut.Find(".worklist-counts");

        Assert.Contains(ConfidenceBand.BadgeFor(Confidence.Unmatched)!, sentence.TextContent, StringComparison.Ordinal);
        Assert.Contains(ConfidenceBand.BadgeFor(Confidence.Uncertain)!, sentence.TextContent, StringComparison.Ordinal);
        Assert.Contains("3 records need attention", sentence.TextContent, StringComparison.Ordinal);

        for (var p = (IElement?)sentence; p is not null; p = p.ParentElement)
        {
            Assert.False(p.HasAttribute("aria-live"));
            Assert.NotEqual("status", p.GetAttribute("role"));
            Assert.NotEqual("alert", p.GetAttribute("role"));
        }

        // Between the search landmark and the list, which is where it is read.
        var all = cut.FindAll("search, p.worklist-counts, ul.worklist").Select(e => e.LocalName + "." + e.ClassName).ToList();
        Assert.Equal(["search.worklist-search", "p.worklist-counts", "ul.worklist"], all);
    }

    [Fact]
    public async Task TheRevealedRegionIsAPlainDivThatTakesFocusAndIsNamedByTheTitle()
    {
        await using var h = new Harness();
        var cut = h.Render(Three, open: Uncertain.Movie.K);

        var panel = cut.Find("[id='row-550-139-fields']");

        Assert.Equal("div", panel.LocalName);
        Assert.False(panel.HasAttribute("role"));
        Assert.Equal("-1", panel.GetAttribute("tabindex"));
        var named = cut.Find($"[id='{panel.GetAttribute("aria-labelledby")}']");
        Assert.Equal("Fight Club", named.TextContent.Trim());
        Assert.Equal(1, h.Context.JSInterop.Invocations.Count(i => i.Identifier.EndsWith("focus", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task OpeningARowClosesAnotherAndRaisesOneOpenedAndNoClosed()
    {
        await using var h = new Harness();
        var opened = new List<string>();
        var closes = 0;
        var cut = h.Render(Three, opened: opened.Add, closed: () => closes++);

        cut.FindAll("button.worklist-row")[0].Click();
        cut.FindAll("button.worklist-row")[1].Click();

        Assert.Equal(2, opened.Count);
        Assert.Equal(0, closes);
        Assert.Single(cut.FindAll("[aria-expanded=true]"));
        Assert.Single(cut.FindAll("[id$='-fields']"));
    }

    [Fact]
    public async Task CloseInsideTheRegionClosesItAndReturnsFocusToItsOwnToggle()
    {
        await using var h = new Harness();
        var cut = h.Render(Three, open: Uncertain.Movie.K);
        var focusesBefore = h.Context.JSInterop.Invocations.Count(i => i.Identifier.EndsWith("focus", StringComparison.Ordinal));

        cut.Find("[id='row-550-139-fields'] button.worklist-close").Click();

        Assert.Empty(cut.FindAll("[aria-expanded=true]"));
        var focuses = h.Context.JSInterop.Invocations.Where(i => i.Identifier.EndsWith("focus", StringComparison.Ordinal)).ToList();
        Assert.Equal(focusesBefore + 1, focuses.Count);

        var target = (ElementReference)focuses[^1].Arguments[0]!;
        var toggle = cut.FindAll("button.worklist-row").Single(b => b.GetAttribute("aria-controls") == "row-550-139-fields");
        Assert.Equal(toggle.GetAttribute("blazor:elementreference"), target.Id);
    }

    [Fact]
    public async Task ManyUnmatchedRecordsRenderBecauseTheKeyIsThePublishedKeyAndNotTheIdWhichIsZero()
    {
        await using var h = new Harness();
        var rows = Enumerable.Range(0, 30).Select(i => Row(Film(0, $"Unknown {i}", 90 + i, 0))).ToList();

        Assert.All(rows, r => Assert.Equal(0, r.Movie.I));

        var cut = h.Render(rows);

        Assert.Equal(30, cut.FindAll("ul.worklist > li").Count);
    }

    /// <summary>The correction nobody had ruled on: filtering out the open row would remove its form from under him.</summary>
    [Fact]
    public async Task AFilterChangeClosesTheOpenRowEvenWhenItIsFilteredOut()
    {
        await using var h = new Harness();
        var closes = 0;
        var cut = h.Render(Three, open: Uncertain.Movie.K, closed: () => closes++);
        Assert.Single(cut.FindAll("[aria-expanded=true]"));

        cut.Find("input[type=search]").Input("Se7en");
        h.Clock.Advance(TimeSpan.FromMilliseconds(Worklist.FilterDebounceMs));

        Assert.Empty(cut.FindAll("[aria-expanded=true]"));
        Assert.Equal(1, closes);
        Assert.Single(cut.FindAll("ul.worklist > li"));

        // Zero expanded rows is true of ANY filtered-out row, so it proves nothing alone: the row is simply not
        // there. The hazard is the key outliving the filter, so bring the row back and it must still be closed.
        cut.Find("input[type=search]").Input("");
        h.Clock.Advance(TimeSpan.FromMilliseconds(Worklist.FilterDebounceMs));

        Assert.Equal(3, cut.FindAll("ul.worklist > li").Count);
        Assert.Empty(cut.FindAll("[aria-expanded=true]"));
        Assert.Empty(cut.FindAll("[id$='-fields']"));
    }

    [Fact]
    public async Task ABandChoiceClosesTheOpenRowToo()
    {
        await using var h = new Harness();
        var cut = h.Render(Three, open: Uncertain.Movie.K);

        cut.FindAll("input[name=worklist-band]")[1].Change(true);

        Assert.Empty(cut.FindAll("[aria-expanded=true]"));
        Assert.Single(cut.FindAll("ul.worklist > li"));
    }

    [Fact]
    public async Task TheCountIsAnnouncedOnceAfterTheTypingStopsAndNeverPerKeystroke()
    {
        await using var h = new Harness();
        var cut = h.Render(Three);

        foreach (var text in new[] { "S", "Se", "Se7" })
        {
            cut.Find("input[type=search]").Input(text);
            h.Clock.Advance(TimeSpan.FromMilliseconds(Worklist.FilterDebounceMs));
        }

        Assert.Empty(h.Region.Said);

        h.Clock.Advance(TimeSpan.FromMilliseconds(Worklist.AnnounceDebounceMs));

        Assert.Equal([Worklist.MatchSentence(1)], h.Region.Said);
    }

    [Fact]
    public async Task LeavingClearsTheRegionAndLeavesNoTimerArmed()
    {
        var h = new Harness();
        var cut = h.Render(Three);
        cut.Find("input[type=search]").Input("S");
        h.Clock.Advance(TimeSpan.FromMilliseconds(Worklist.FilterDebounceMs));
        Assert.True(h.Clock.ArmedTimers > 0);

        await h.DisposeAsync();

        Assert.Equal(string.Empty, h.Region.Said[^1]);
        Assert.Equal(0, h.Clock.ArmedTimers);
    }

    [Fact]
    public async Task TheWorstMatchesSitOnTopAndTheSortCanBeChanged()
    {
        await using var h = new Harness();
        var cut = h.Render([Second, Uncertain, Unmatched]);

        string[] Titles() => cut.FindAll(".worklist-title").Select(t => t.TextContent.Trim()).ToArray();

        Assert.Equal(["Lost Reel", "Fight Club", "Se7en"], Titles());

        cut.Find("select").Change("title");

        Assert.Equal(["Fight Club", "Lost Reel", "Se7en"], Titles());
    }

    [Fact]
    public async Task AFilteredRowThatLeavesHandsFocusToTheRowThatTookItsPlace()
    {
        await using var h = new Harness();
        var cut = h.Render(Three);   // order: Lost Reel, Fight Club, Se7en

        var removed = Uncertain.Movie.K;
        cut.Render(p => p.Add(w => w.Rows, [Unmatched, Second]));
        var moved = await cut.InvokeAsync(() => cut.Instance.FocusRowAfterAsync(removed));

        Assert.True(moved);
        var focus = h.Context.JSInterop.Invocations.Last(i => i.Identifier.EndsWith("focus", StringComparison.Ordinal));
        var target = (ElementReference)focus.Arguments[0]!;
        var toggle = cut.FindAll("button.worklist-row").Single(b => b.TextContent.Contains("Se7en", StringComparison.Ordinal));
        Assert.Equal(toggle.GetAttribute("blazor:elementreference"), target.Id);
    }

    [Fact]
    public async Task WhenTheLastRowGoesFocusFallsBackToThePageAndTheAnswerIsFalse()
    {
        await using var h = new Harness();
        var cut = h.Render([Uncertain]);

        cut.Render(p => p.Add(w => w.Rows, []));

        Assert.False(await cut.InvokeAsync(() => cut.Instance.FocusRowAfterAsync(Uncertain.Movie.K)));
    }

    [Fact]
    public async Task WhenTheRemovedRowWasLastFocusGoesToTheOneBeforeIt()
    {
        await using var h = new Harness();
        var cut = h.Render(Three);   // Lost Reel, Fight Club, Se7en

        cut.Render(p => p.Add(w => w.Rows, [Unmatched, Uncertain]));

        Assert.True(await cut.InvokeAsync(() => cut.Instance.FocusRowAfterAsync(Second.Movie.K)));
        var focus = h.Context.JSInterop.Invocations.Last(i => i.Identifier.EndsWith("focus", StringComparison.Ordinal));
        var toggle = cut.FindAll("button.worklist-row").Single(b => b.TextContent.Contains("Fight Club", StringComparison.Ordinal));
        Assert.Equal(toggle.GetAttribute("blazor:elementreference"), ((ElementReference)focus.Arguments[0]!).Id);
    }

    // ------------------------------------------------------------------ the stylesheet, read from the file
    //
    // Until the project-wide style contracts reach the editor's project, a stylesheet here is bound by nothing
    // else, and a target-size INCOMPLETE is a build break that only a hand-run scan would see. These are the
    // floors, in the shape StyleContractTests uses. They go redundant the day those globs are widened, which is
    // the correct lifecycle for a floor.

    private static string Css() => string.Join(Environment.NewLine, WebRoot.EditorFiles("*.razor.css").Select(File.ReadAllText));

    private static IEnumerable<(string Selector, string Body)> Rules(string css)
    {
        var bare = System.Text.RegularExpressions.Regex.Replace(css, @"/\*.*?\*/", "", System.Text.RegularExpressions.RegexOptions.Singleline);
        foreach (System.Text.RegularExpressions.Match m in
                 System.Text.RegularExpressions.Regex.Matches(bare, @"([^{}@]+)\{([^{}]*)\}"))
            yield return (m.Groups[1].Value.Trim(), m.Groups[2].Value);
    }

    [Fact]
    public void NothingInTheWorklistTruncatesText()
    {
        var css = Css();

        Assert.NotEmpty(css);
        foreach (var banned in new[] { "line-clamp", "text-overflow", "overflow: hidden", "overflow:hidden" })
            Assert.DoesNotContain(banned, css, StringComparison.Ordinal);
    }

    [Fact]
    public void TheThumbnailSetsOneAxisAndLeavesTheOtherAutoWithTheRatioThatDrivesIt()
    {
        var thumb = Rules(Css()).Single(r => r.Selector == ".worklist-thumb").Body;

        Assert.Matches(@"height\s*:\s*auto", thumb);
        Assert.Matches(@"aspect-ratio\s*:\s*2\s*/\s*3", thumb);
        Assert.DoesNotMatch(@"(?<!-)height\s*:(?!\s*auto)", thumb);
    }

    [Fact]
    public void NoWrapIsPermittedOnTheRuntimeTokenAndNowhereElse()
    {
        var holders = Rules(Css()).Where(r => System.Text.RegularExpressions.Regex.IsMatch(r.Body, @"white-space\s*:\s*nowrap"))
            .Select(r => r.Selector).ToList();

        Assert.Equal([".worklist-runtime"], holders);
    }

    [Fact]
    public void TheStylesheetKeepsTheTargetSizeAndViewportAndMotionFloors()
    {
        var css = Css();

        Assert.DoesNotContain("44px", css, StringComparison.Ordinal);
        Assert.Matches(@"min-height\s*:\s*var\(--touch\)", css);
        Assert.DoesNotMatch(@"\d(vh|dvh|svh|lvh|vw)", css);
        Assert.DoesNotMatch(@"position\s*:\s*(sticky|fixed)", css);
        // Declarations only: the closing comment says there is no motion, and must be allowed to.
        Assert.DoesNotMatch("(transition|animation)", string.Join(" ", Rules(css).Select(r => r.Body)));
        Assert.DoesNotContain("orientation", css, StringComparison.Ordinal);

        // Hover only where a pointer can hover: remove the media block and no :hover may remain.
        var outside = System.Text.RegularExpressions.Regex.Replace(
            System.Text.RegularExpressions.Regex.Replace(css, @"/\*.*?\*/", "", System.Text.RegularExpressions.RegexOptions.Singleline),
            @"@media \(hover: hover\)\s*\{[^{}]*\{[^{}]*\}\s*\}", "");
        Assert.DoesNotContain(":hover", outside, StringComparison.Ordinal);
    }

    [Fact]
    public void NoElementInTheEditorCarriesAPositiveTabindex()
    {
        var files = WebRoot.EditorFiles("*.razor").ToList();

        Assert.NotEmpty(files);
        foreach (var file in files)
            Assert.DoesNotMatch(@"tabindex\s*=\s*""[1-9]", File.ReadAllText(file));
    }
}

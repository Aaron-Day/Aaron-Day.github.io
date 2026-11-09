using AngleSharp.Dom;
using System.Globalization;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using MovieLibrary.Core.Models;
using MovieLibrary.Web.Components;
using MovieLibrary.Web.Services;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// The paged grid. Everything here is driven through the component's PARAMETERS rather than by calling its
/// helpers, because the helpers are what an implementer is tempted to test and the parameters are what
/// Task 8 will actually drive.
/// <para>
/// What this file cannot prove: the rendered size of the pager buttons and the column count at 320 CSS px.
/// Both need layout, and both are reported from a browser pass instead -- a bUnit proxy for "focusable" is
/// "carries no disabled attribute", which is what is asserted here and is not the same claim.
/// </para>
/// </summary>
public class PosterGridTests : BunitContext
{
    private readonly ManualTimeProvider clock;

    public PosterGridTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        clock = this.AddAnnouncer();
    }

    private Announcer Spoken() => Services.GetRequiredService<Announcer>();

    /// <summary>
    /// <paramref name="count"/> records, all confident and none with a poster: this file is about paging,
    /// and PosterTileTests owns what a tile does with a record.
    /// </summary>
    private static IReadOnlyList<MovieRecord> Films(int count) =>
        Enumerable.Range(1, count)
            .Select(i => new MovieRecord(i, "k" + i, "Film " + i, 1990, 100, null, null, [], "Drama", 0d,
                                         [], null, null, [], null, null, 1d))
            .ToList();

    /// <summary>
    /// A stand-in for <c>Movies.razor</c>: it owns the page number, adopts what the grid raises, and keeps
    /// a record of every value raised. <paramref name="Adopt"/> false is the parent that refuses to
    /// converge, which is how the clamp's "at most once per distinct out-of-range value" is observable.
    /// </summary>
    private sealed class GridHost : ComponentBase
    {
        [Parameter] public IReadOnlyList<MovieRecord> Items { get; set; } = [];
        [Parameter] public int StartPage { get; set; } = 1;
        [Parameter] public int PageSize { get; set; } = 60;
        [Parameter] public bool Adopt { get; set; } = true;

        public List<int> Raised { get; } = [];

        private int page;
        private bool started;

        protected override void OnParametersSet()
        {
            if (started) return;
            started = true;
            page = StartPage;
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<PosterGrid>(0);
            builder.AddComponentParameter(1, nameof(PosterGrid.Items), Items);
            builder.AddComponentParameter(2, nameof(PosterGrid.Page), page);
            builder.AddComponentParameter(3, nameof(PosterGrid.PageSize), PageSize);
            builder.AddComponentParameter(4, nameof(PosterGrid.PageChanged),
                EventCallback.Factory.Create<int>(this, OnPageChanged));
            builder.CloseComponent();
        }

        private void OnPageChanged(int value)
        {
            Raised.Add(value);
            if (Adopt) page = value;
        }
    }

    private IRenderedComponent<GridHost> RenderGrid(
        int films, int page = 1, int pageSize = 60, bool adopt = true) =>
        Render<GridHost>(p => p
            .Add(x => x.Items, Films(films))
            .Add(x => x.StartPage, page)
            .Add(x => x.PageSize, pageSize)
            .Add(x => x.Adopt, adopt));

    private static string PageLabel(IRenderedComponent<GridHost> cut) =>
        cut.Find("nav.pager span").TextContent;

    private static string Count(IRenderedComponent<GridHost> cut) => cut.Find("p.count").TextContent;

    private static IElement Button(IRenderedComponent<GridHost> cut, string text) =>
        cut.FindAll("nav.pager button").Single(b => b.TextContent.Trim() == text);

    // ============================================================ the page count and the tiles on a page

    /// <remarks>
    /// The page label is asserted rather than the count sentence, because these numbers are below a
    /// thousand and so read the same in every culture. The sentence's formatting has a test of its own.
    /// </remarks>
    [Theory]
    [InlineData(1456, 60, 25)]   // the measured catalogue at the shipped page size
    [InlineData(1, 60, 1)]
    [InlineData(60, 60, 1)]      // exactly full: no second, empty page
    [InlineData(61, 60, 2)]
    public void ThePageCountIsTheCeilingOfTheDivision(int films, int size, int pages)
    {
        Assert.Equal("Page 1 of " + pages, PageLabel(RenderGrid(films, pageSize: size)));
    }

    [Fact]
    public void TheLastPageHoldsTheRemainderAndTheOnesBeforeItHoldAFullPage()
    {
        var cut = RenderGrid(1456, page: 25);

        Assert.Equal("Page 25 of 25", PageLabel(cut));
        Assert.Equal(16, cut.FindAll("ul.grid > li").Count);

        cut = RenderGrid(1456, page: 24);
        Assert.Equal(60, cut.FindAll("ul.grid > li").Count);
    }

    [Fact]
    public void APageShowsTheRecordsItsOffsetSelectsAndNoOthers()
    {
        var cut = RenderGrid(1456, page: 3);

        var titles = cut.FindAll("ul.grid > li .tile-title").Select(e => e.TextContent).ToList();

        Assert.Equal("Film 121", titles[0]);
        Assert.Equal("Film 180", titles[^1]);
    }

    // ============================================================ the list wrapper

    /// <remarks>
    /// Without the list, 60 sibling divs are read as one continuous run -- "Film 1 1990 Film 2 1990..." --
    /// with no item boundaries, no count and no list navigation. role="list" is explicit because WebKit
    /// drops list semantics when list-style is none, and display: grid on the container is the other known
    /// trigger: this element has both. Task 2 made the same call for the drawer's own list.
    /// </remarks>
    [Fact]
    public void TheTileWallIsAListWithOneItemPerTileAndTheCountComesFromTheListItself()
    {
        var cut = RenderGrid(130, page: 1);
        var list = cut.Find("ul.grid");

        Assert.Equal("list", list.GetAttribute("role"));
        Assert.Equal(60, cut.FindAll("ul.grid > li").Count);
        Assert.Equal(60, cut.FindAll("ul.grid > li > .tile").Count);

        // The item count a reader announces is the list's own. Nothing here states it a second time: a
        // label carrying "60 items" would be a number to keep in step with the markup for no benefit.
        Assert.DoesNotContain("aria-label", list.OuterHtml);
        Assert.Empty(cut.FindAll("ul.grid [role]"));
    }

    /// <remarks>
    /// The composite-widget roles are the ones that would promise arrow-key navigation this grid does not
    /// implement. role="list" is a document-structure role and carries no keyboard contract at all, which
    /// is the distinction an earlier draft of the brief collapsed.
    /// </remarks>
    [Fact]
    public void TheGridClaimsNoCompositeWidgetSemanticsAndNothingInItIsATabStop()
    {
        var root = MarkupRules.Parse(RenderGrid(130).Markup);
        var grid = root.QuerySelector("ul.grid")!;

        Assert.Null(root.QuerySelector("[role='grid']"));
        Assert.Null(root.QuerySelector("[aria-rowcount]"));
        Assert.Empty(MarkupRules.InteractiveElements(grid));
        Assert.Empty(grid.QuerySelectorAll("[tabindex]"));
    }

    // ============================================================ the empty list

    /// <remarks>
    /// The whole head is gated, count and pager together. Gating only the count left two dimmed buttons
    /// and the words "Page 1 of 1" asserting a page of titles that does not exist, with no sentence
    /// anywhere to contradict it. Gating on PageCount > 1 instead would dissolve the pager under a filter
    /// while it holds focus, which is the case rule R9 exists for.
    /// <para>
    /// This is also the only observable proof that PageCount is never 0. A naive ceiling division yields 0
    /// over an empty list, and Math.Clamp(1, 1, 0) throws ArgumentException because its minimum would
    /// exceed its maximum -- so a render that completes at all is the assertion.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnEmptyListRendersNoCountSentenceNoPagerAndNoPageOfOne()
    {
        var cut = RenderGrid(0);

        Assert.Empty(cut.FindAll(".grid-head"));
        Assert.Empty(cut.FindAll("p.count"));
        Assert.Empty(cut.FindAll("nav.pager"));
        Assert.Empty(cut.FindAll("ul.grid"));
        Assert.DoesNotContain("Page 1 of", cut.Markup);
        Assert.Equal(string.Empty, cut.Markup.Trim());
    }

    [Fact]
    public void AnEmptyListStillClampsToPageOneRatherThanToPageZero()
    {
        // Nothing renders either way, so the raise is the only observable. With no titles there is exactly
        // one page, so a parent asking for page 7 is told to come back to 1 and a parent already on 1 is
        // told nothing at all.
        Assert.Empty(RenderGrid(0).Instance.Raised);
        Assert.Equal(1, Assert.Single(RenderGrid(0, page: 7).Instance.Raised));
    }

    // ============================================================ the clamp

    /// <remarks>
    /// Task 8 filtering 1,456 titles down to 10 while the parent holds page 12 is the case: without the
    /// clamp the grid renders nothing under the words "Showing 661 to 720 of 10 titles. Page 12 of 1."
    /// The correction raises PageChanged so the parent converges, and announces NOTHING -- a deep link to
    /// page 99 is not a change the user made while reading the page.
    /// </remarks>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(99, 3)]
    [InlineData(4, 3)]
    public void APageOutsideTheRangeRendersTheClampedPageAndRaisesTheClampedNumber(int asked, int clamped)
    {
        var cut = RenderGrid(130, page: asked);   // 130 at 60 a page is 3 pages

        Assert.Equal("Page " + clamped + " of 3", PageLabel(cut));
        Assert.Equal(clamped, Assert.Single(cut.Instance.Raised));
    }

    [Fact]
    public void TheClampAnnouncesNothingSoTheFirstRenderIsSilent()
    {
        RenderGrid(130, page: 99);
        clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(string.Empty, Spoken().StatusText);
    }

    [Fact]
    public void TheComponentNeverWritesToItsOwnPageParameter()
    {
        // The parent refuses to converge, so the only thing that could change Page is the grid itself.
        var cut = RenderGrid(130, page: 99, adopt: false);

        Assert.Equal(99, cut.FindComponent<PosterGrid>().Instance.Page);
        Assert.Equal("Page 3 of 3", PageLabel(cut));
    }

    /// <remarks>
    /// Raising an EventCallback from a parameter hook loops for ever against a parent that recomputes Page
    /// rather than adopting what it is handed, which Task 8's filter state plausibly will. The guard is
    /// one raise per distinct out-of-range value.
    /// </remarks>
    [Fact]
    public void TheCorrectionIsRaisedAtMostOncePerDistinctOutOfRangeValue()
    {
        var cut = RenderGrid(130, page: 99, adopt: false);

        cut.Render();
        cut.Render();
        cut.Render(p => p.Add(x => x.PageSize, 60));

        // The clamped value is what is raised, once, however many renders the parent produces.
        Assert.Equal(3, Assert.Single(cut.Instance.Raised));
    }

    // ============================================================ the pager

    /// <remarks>
    /// Both buttons on every page, and neither ever carries the disabled ATTRIBUTE. Disabling the button
    /// that has focus takes it out of the tab order, so focus falls back to the document body: no focus
    /// indicator anywhere, and the next Tab restarts above the skip link. For someone paging through 25
    /// pages by keyboard that happens twice a visit and is silent.
    /// <para>
    /// "No disabled attribute" is the bUnit PROXY for focusable. Real focusability needs a focus engine
    /// and belongs with the browser pass; this is what can be asserted here, and it is a weaker claim.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(25)]
    public void BothPagerButtonsExistOnEveryPageAndNeitherIsEverDisabled(int page)
    {
        var cut = RenderGrid(1456, page: page);
        var buttons = cut.FindAll("nav.pager button");

        Assert.Equal(2, buttons.Count);
        Assert.All(buttons, b => Assert.Null(b.GetAttribute("disabled")));
        Assert.All(buttons, b => Assert.Equal("button", b.GetAttribute("type")));
        Assert.Equal(new[] { "Previous", "Next" }, buttons.Select(b => b.TextContent.Trim()));
    }

    /// <remarks>
    /// Rendered from the string "true" and never from a bool: Blazor writes a true bool attribute as
    /// present-and-empty and omits a false one, and ARIA reads both of those as undefined. The same trap
    /// the drawer's aria-expanded was written around.
    /// </remarks>
    [Theory]
    [InlineData(1, "true", null)]
    [InlineData(2, null, null)]
    [InlineData(24, null, null)]
    [InlineData(25, null, "true")]
    public void AtABoundTheButtonCarriesAriaDisabledAndNowhereElseDoes(
        int page, string? previous, string? next)
    {
        var cut = RenderGrid(1456, page: page);

        Assert.Equal(previous, Button(cut, "Previous").GetAttribute("aria-disabled"));
        Assert.Equal(next, Button(cut, "Next").GetAttribute("aria-disabled"));
    }

    [Fact]
    public void ASinglePageGreysBothButtonsAndStillRendersThePager()
    {
        var cut = RenderGrid(10);

        Assert.Equal("Page 1 of 1", PageLabel(cut));
        Assert.All(cut.FindAll("nav.pager button"),
            b => Assert.Equal("true", b.GetAttribute("aria-disabled")));
    }

    [Theory]
    [InlineData(1, "Previous")]
    [InlineData(25, "Next")]
    public void PressingAButtonAtItsBoundRaisesNothingAndSaysNothing(int page, string label)
    {
        var cut = RenderGrid(1456, page: page);

        Button(cut, label).Click();
        clock.Advance(TimeSpan.FromHours(1));

        Assert.Empty(cut.Instance.Raised);
        Assert.Equal("Page " + page + " of 25", PageLabel(cut));
        Assert.Equal(string.Empty, Spoken().StatusText);
    }

    [Fact]
    public void PressingNextMovesOnePageAndPreviousMovesBack()
    {
        var cut = RenderGrid(1456, page: 2);

        Button(cut, "Next").Click();
        Assert.Equal("Page 3 of 25", PageLabel(cut));

        Button(cut, "Previous").Click();
        Assert.Equal("Page 2 of 25", PageLabel(cut));

        Assert.Equal(new[] { 3, 2 }, cut.Instance.Raised);
    }

    /// <remarks>
    /// M5: the page number lives in the pager's own indicator and nowhere else. On screen twice is read
    /// twice. The indicator is also the non-colour cue for which bound the user is at, which is what keeps
    /// the greyed button out of SC 1.4.1's way.
    /// </remarks>
    [Fact]
    public void ThePageNumberAppearsOnceAndTheCountSentenceDoesNotRepeatIt()
    {
        var cut = RenderGrid(130, page: 2);

        Assert.Equal("Page 2 of 3", PageLabel(cut));
        Assert.DoesNotContain("Page", Count(cut));
        // Once in the whole head, counted in the text a sighted reader sees rather than per element: the
        // pager landmark and its span both CONTAIN the label, and that is one appearance, not two.
        var head = cut.Find(".grid-head").TextContent;
        Assert.Equal(1, head.Split("Page 2 of 3").Length - 1);
    }

    // ============================================================ what the user hears

    /// <remarks>
    /// Rule R6: a complete sentence including its noun, because the region is atomic and the whole text is
    /// re-read. The range is spoken as "to" rather than as a dash, because how a reader pronounces a dash
    /// between two numbers is not something this page can decide; the visible range keeps the en dash.
    /// </remarks>
    [Fact]
    public void PressingNextAnnouncesThePageTheRangeAndTheTotal()
    {
        // en-US explicitly, because the literal below is an en-US literal. The culture has to be in force
        // before the click, because that is when the sentence is built.
        using var culture = new CultureScope("en-US");

        var cut = RenderGrid(1456, page: 1);

        Button(cut, "Next").Click();
        clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal("Page 2 of 25. Showing 61 to 120 of 1,456 titles.", Spoken().StatusText);
    }

    [Fact]
    public void PressingNextTwiceInsideTheSettleWindowAnnouncesOnlyTheSecondPage()
    {
        var cut = RenderGrid(1456, page: 1);

        Button(cut, "Next").Click();
        Button(cut, "Next").Click();
        clock.Advance(TimeSpan.FromHours(1));

        // Latest wins inside a region, which is right: five quick presses should speak the page the user
        // ended on and not the four they went past.
        Assert.StartsWith("Page 3 of 25.", Spoken().StatusText);
        Assert.DoesNotContain("Page 2 of 25", Spoken().StatusText);
    }

    [Fact]
    public void TheFirstRenderAnnouncesNothingAtAll()
    {
        RenderGrid(1456, page: 1);
        clock.Advance(TimeSpan.FromHours(1));

        // The page title and the route name already cover arrival, and R3 forbids repeating either.
        Assert.Equal(string.Empty, Spoken().StatusText);
    }

    /// <remarks>
    /// AC 10, and P11's correction: the announce and the clear are in the SAME component. Putting the clear
    /// on the page would leave the grid's sentence standing in the region any time the grid were unmounted
    /// while the page lived on, describing a page of tiles that is no longer rendered -- which is the thing
    /// rule R7 exists to prevent.
    /// </remarks>
    [Fact]
    public async Task TheGridClearsTheStatusRegionOnItsOwnDispose()
    {
        var cut = RenderGrid(1456, page: 1);

        Button(cut, "Next").Click();
        clock.Advance(TimeSpan.FromHours(1));
        Assert.NotEqual(string.Empty, Spoken().StatusText);   // a standing statement to clear

        await DisposeComponentsAsync();

        Assert.Equal(string.Empty, Spoken().StatusText);
    }

    [Fact]
    public async Task TheGridRaisesNoNewTextOnDisposeOnlyACancellation()
    {
        var cut = RenderGrid(1456, page: 1);

        Button(cut, "Next").Click();
        await DisposeComponentsAsync();

        // An empty string takes effect at once and cancels anything still waiting out the settle delay, so
        // advancing the clock afterwards must not deliver the message the click armed.
        clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(string.Empty, Spoken().StatusText);
    }

    // ============================================================ the numbers

    /// <remarks>
    /// Blazor WebAssembly takes the culture from the browser and the project does not set
    /// InvariantGlobalization, so a grouped total is correct behaviour rather than a bug to stamp out.
    /// ASSERTED AGAINST LITERALS, not against another ToString("N0") call, which would prove only that the
    /// test and the component agree with each other -- rule T4's point. All three numbers are formatted,
    /// because a grouped total beside a bare range reads "Showing 1381-1440 of 1.456 titles." under de-DE.
    /// </remarks>
    [Theory]
    [InlineData("en-US", "Showing 1,381–1,440 of 1,456 titles.")]
    [InlineData("de-DE", "Showing 1.381–1.440 of 1.456 titles.")]
    public void TheNumbersAreGroupedInTheBrowsersCulture(string culture, string expected)
    {
        using var scope = new CultureScope(culture);

        Assert.Equal(expected, Count(RenderGrid(1456, page: 24)));
    }

    [Theory]
    [InlineData("en-US", "Page 2 of 25. Showing 61 to 120 of 1,456 titles.")]
    [InlineData("de-DE", "Page 2 of 25. Showing 61 to 120 of 1.456 titles.")]
    public void TheAnnouncementReadsTheSameNumbersTheSentenceDoes(string culture, string expected)
    {
        using var scope = new CultureScope(culture);

        var cut = RenderGrid(1456, page: 1);
        Button(cut, "Next").Click();
        clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(expected, Spoken().StatusText);
    }

    /// <remarks>
    /// Rule R6 wants singular and plural. A filter cutting the list to one title is reachable the moment
    /// Task 8 exists, and "of 1 titles" would then be frozen by whichever test pinned only the plural.
    /// </remarks>
    [Theory]
    [InlineData(1, "Showing 1–1 of 1 title.")]
    [InlineData(2, "Showing 1–2 of 2 titles.")]
    public void TheNounAgreesWithTheTotalInBothTheSentenceAndTheAnnouncement(int films, string expected)
    {
        using var scope = new CultureScope("en-US");

        var cut = RenderGrid(films);

        Assert.Equal(expected, Count(cut));

        // The same noun in the spoken copy, reached through a page that is in range so that Go runs.
        var paged = RenderGrid(films + 60, page: 1);
        Button(paged, "Next").Click();
        clock.Advance(TimeSpan.FromHours(1));

        Assert.EndsWith(films + 60 == 1 ? "1 title." : (films + 60) + " titles.", Spoken().StatusText);
    }

    /// <summary>
    /// Sets the culture for one test and puts it back. Only <see cref="CultureInfo.CurrentCulture"/>, never
    /// <c>DefaultThreadCurrentCulture</c>: that one is process-wide and would reach whatever else xunit is
    /// running in parallel. CurrentCulture flows with the execution context, which is what bUnit's
    /// renderer dispatch rides on.
    /// </summary>
    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo previous = CultureInfo.CurrentCulture;

        public CultureScope(string name) => CultureInfo.CurrentCulture = new CultureInfo(name);

        public void Dispose() => CultureInfo.CurrentCulture = previous;
    }
}

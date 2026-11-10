using System.Text.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MovieLibrary.Web.Pages;
using MovieLibrary.Web.Services;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// The films page with its filters, in the real app at /movies, under the real router and layout. The pure
/// filtering is CatalogFilterTests, the address contract is QueryStateTests and the controls are
/// FacetPanelTests; this file is the WIRING: that a choice reaches the list, the address and the announcer, in
/// the right order and on the right clock, and that the address reaches the list the other way.
/// </summary>
/// <remarks>
/// Every test here steps the same fake clock the announcer runs on, so the filter debounce, the announce
/// debounce and the settle delay are one timeline: nothing sleeps and nothing waits "long enough".
/// </remarks>
public class MoviesFilterTests : BunitContext
{
    private const string FocusHelper = "movieLibrary.focusIfUnmoved";

    public MoviesFilterTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    private sealed record Film(string T, int? Y = 1990, int[]? G = null, int I = 1, double Mc = 1,
                               string[]? Dir = null, int Rt = 100);

    /// <summary>
    /// Eight films, alphabetical by construction so that a title-ordered grid reads Alpha to Hotel. They differ
    /// on every axis a facet can see: two unmatched (one id 0 each) and two uncertain, genres in several
    /// combinations, four decades and two with no year.
    /// </summary>
    private static readonly Film[] Eight =
    [
        new("Alpha", 1994, [18, 28], 11, Dir: ["Jane Campion"], Rt: 140),
        new("Bravo", 1999, [35], 12, Rt: 90),
        new("Charlie", 2003, [18], 13, Dir: ["Sofia Coppola"], Rt: 120),
        new("Delta", 1971, [28], 14, Mc: 0.55),
        new("Echo", null, [], 0, Mc: 0, Rt: 80),
        new("Foxtrot", 2008, [35, 18], 15, Mc: 0.55, Rt: 150),
        new("Golf", null, [], 0, Mc: 0, Rt: 110),
        new("Hotel", 1985, [28, 35], 16),
    ];

    private const string Meta =
        """{"genres":{"18":"Drama","28":"Action","35":"Comedy"},"movieCount":8,"attribution":"x","reviewQueue":[]}""";

    private static string Json(IEnumerable<Film> films) => JsonSerializer.Serialize(
        films.Select((f, n) => new
        {
            i = f.I, k = $"k{n}", t = f.T, y = f.Y, rt = f.Rt, g = f.G ?? [], ag = "Drama", vote = 0,
            dir = f.Dir ?? [], cast = Array.Empty<object>(), mc = f.Mc,
        }));

    /// <summary>Films 001 to N, zero-padded so that title order is numeric order.</summary>
    private static IEnumerable<Film> Bulk(int count) =>
        Enumerable.Range(1, count).Select(n => new Film($"Film {n:000}", 1990, [18], n));

    private sealed record Open(IRenderedComponent<MovieLibrary.Web.App> Cut, ManualTimeProvider Clock,
                               BunitNavigationManager Nav, StubCatalog Stub);

    private Open Render(IEnumerable<Film> films, string address = "/movies", string? meta = Meta)
    {
        var (clock, stub) = this.AddAnnouncerAndCatalog(StubCatalog.Json(Json(films)));
        stub.Serves(StubCatalog.Meta, meta is null
            ? StubCatalog.Throws(new HttpRequestException("meta is down"))
            : StubCatalog.Json(meta));

        var cut = this.RenderAppAt(address);
        return new Open(cut, clock, Services.GetRequiredService<BunitNavigationManager>(), stub);
    }

    private Announcer Spoken() => Services.GetRequiredService<Announcer>();

    private static string[] Titles(IRenderedComponent<MovieLibrary.Web.App> cut) =>
        cut.FindAll("ul.grid .tile-title").Select(t => t.TextContent.Trim()).ToArray();

    private static AngleSharp.Dom.IElement Box(IRenderedComponent<MovieLibrary.Web.App> cut, string legend) =>
        cut.FindAll("fieldset").Single(f => f.QuerySelector("legend")!.TextContent.Trim() == legend);

    private static void Tick(IRenderedComponent<MovieLibrary.Web.App> cut, string legend, string label, bool on) =>
        Box(cut, legend).QuerySelectorAll("label").Single(l => l.TextContent.Trim() == label)
            .QuerySelector("input")!.Change(on);

    private void Settle(Open open) => open.Clock.Advance(TimeSpan.FromHours(1));

    // ================================================================ what the page offers

    [Fact]
    public void TheReadyPageOffersTheFiltersBetweenTheStateAndTheCountAndShowsEveryFilm()
    {
        var open = Render(Eight);
        var root = MarkupRules.Parse(open.Cut.Markup);

        var order = root.QuerySelectorAll("h1, .catalog-state, search, p.count, nav.pager, ul.grid")
            .Select(e => e.LocalName + (e.ClassName is { Length: > 0 } c ? "." + c : ""));

        Assert.Equal(new[] { "h1", "div.catalog-state", "search.facets", "p.count", "nav.pager", "ul.grid" }, order);
        Assert.Equal(new[] { "Alpha", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot", "Golf", "Hotel" }, Titles(open.Cut));
        Assert.Equal("The library holds 8 films.", open.Cut.Find(".catalog-state p").TextContent.Trim());
    }

    [Theory]
    [InlineData(CatalogOutcome.Failed)]
    [InlineData(CatalogOutcome.NeverCompletes)]
    public void NeitherTheLoadingNorTheFailedStateOffersAnyFilter(CatalogOutcome outcome)
    {
        this.AddAnnouncer(outcome);
        var cut = this.RenderAppAt("/movies");

        Assert.Empty(cut.FindAll("search"));
        Assert.Empty(cut.FindAll("fieldset"));
        Assert.Empty(cut.FindAll("input"));
        Assert.Empty(cut.FindAll(".no-match"));
    }

    [Fact]
    public void AnEmptyLibraryOffersNoFilterAndNeverBlamesOne()
    {
        var open = Render([]);

        Assert.Equal("The library is empty.", open.Cut.Find(".catalog-state p").TextContent.Trim());
        Assert.Empty(open.Cut.FindAll("search"));
        Assert.Empty(open.Cut.FindAll(".no-match"));
        Assert.DoesNotContain("No titles match", open.Cut.Markup);

        Settle(open);
        Assert.Equal("The library is empty.", Spoken().StatusText);
    }

    /// <remarks>R-C: a failed meta is an empty genre map, and the other facets still work.</remarks>
    [Fact]
    public void AFailedMetaCostsTheGenreFacetAndNothingElse()
    {
        var open = Render(Eight, meta: null);

        Assert.Equal(8, Titles(open.Cut).Length);
        Assert.DoesNotContain(open.Cut.FindAll("legend"), l => l.TextContent.Trim() == "Genre");
        Assert.Equal(new[] { "Match confidence", "Decade" },
                     open.Cut.FindAll("legend").Select(l => l.TextContent.Trim()));

        Tick(open.Cut, "Match confidence", "Not matched", true);
        Assert.Equal(new[] { "Echo", "Golf" }, Titles(open.Cut));
    }

    [Fact]
    public void TheDecadesOnOfferAreTheOnesFilmsAreInAndYearUnknownIsThere()
    {
        var open = Render(Eight);

        Assert.Equal(new[] { "2000s", "1990s", "1980s", "1970s", "Year unknown" },
                     Box(open.Cut, "Decade").QuerySelectorAll("label").Select(l => l.TextContent.Trim()));
    }

    // ================================================================ the facets, through the controls

    [Fact]
    public void EveryFacetStartsOnItsDefaultAndTheDefaultHidesNothing()
    {
        var open = Render(Eight);

        var radio = Box(open.Cut, "Match confidence").QuerySelectorAll("input").Single(r => r.HasAttribute("checked"));
        Assert.Equal("All titles", radio.ParentElement!.TextContent.Trim());
        Assert.Empty(open.Cut.FindAll("fieldset input[type=checkbox][checked]"));
        Assert.Equal(string.Empty, open.Cut.Find("#q").GetAttribute("value") ?? string.Empty);
        Assert.Equal("title", open.Cut.Find("#sort").GetAttribute("value") ?? "title");
    }

    [Theory]
    [InlineData("Uncertain matches", new[] { "Delta", "Foxtrot" })]
    [InlineData("Not matched", new[] { "Echo", "Golf" })]
    [InlineData("All titles", new[] { "Alpha", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot", "Golf", "Hotel" })]
    public void ChoosingAConfidenceBandShowsExactlyThatBandAtOnce(string label, string[] expected)
    {
        var open = Render(Eight);
        Tick(open.Cut, "Match confidence", "Not matched", true);   // away from the default, so All has to come back

        Tick(open.Cut, "Match confidence", label, true);

        Assert.Equal(expected, Titles(open.Cut));
    }

    [Fact]
    public void GenresAreEitherOrAndNarrowEverythingElse()
    {
        var open = Render(Eight);

        Tick(open.Cut, "Genre", "Drama", true);
        Assert.Equal(new[] { "Alpha", "Charlie", "Foxtrot" }, Titles(open.Cut));

        Tick(open.Cut, "Genre", "Comedy", true);
        Assert.Equal(new[] { "Alpha", "Bravo", "Charlie", "Foxtrot", "Hotel" }, Titles(open.Cut));

        Tick(open.Cut, "Match confidence", "Uncertain matches", true);
        Assert.Equal(new[] { "Foxtrot" }, Titles(open.Cut));

        Tick(open.Cut, "Genre", "Comedy", false);
        Tick(open.Cut, "Genre", "Drama", false);
        Tick(open.Cut, "Match confidence", "All titles", true);
        Assert.Equal(8, Titles(open.Cut).Length);
    }

    [Fact]
    public void DecadesAndTheYearUnknownBucketPartitionTheLibrary()
    {
        var open = Render(Eight);

        Tick(open.Cut, "Decade", "1990s", true);
        Assert.Equal(new[] { "Alpha", "Bravo" }, Titles(open.Cut));

        Tick(open.Cut, "Decade", "Year unknown", true);
        Assert.Equal(new[] { "Alpha", "Bravo", "Echo", "Golf" }, Titles(open.Cut));

        Tick(open.Cut, "Decade", "1990s", false);
        Assert.Equal(new[] { "Echo", "Golf" }, Titles(open.Cut));
    }

    [Fact]
    public void SortReordersAndFiltersNothing()
    {
        var open = Render(Eight);

        open.Cut.Find("#sort").Change("year");
        Assert.Equal(new[] { "Foxtrot", "Charlie", "Bravo", "Alpha", "Hotel", "Delta", "Echo", "Golf" }, Titles(open.Cut));

        open.Cut.Find("#sort").Change("runtime");
        Assert.Equal(new[] { "Foxtrot", "Alpha", "Charlie", "Golf", "Delta", "Hotel", "Bravo", "Echo" }, Titles(open.Cut));

        open.Cut.Find("#sort").Change("title");
        Assert.Equal(8, Titles(open.Cut).Length);
        Assert.Equal("Alpha", Titles(open.Cut)[0]);
    }

    // ================================================================ search, and the two debounces

    [Fact]
    public void TypingFiltersAfterTheFilterDebounceAndNotBefore()
    {
        var open = Render(Eight);

        open.Cut.Find("#q").Input("camp");   // Jane Campion directs Alpha
        open.Clock.Advance(TimeSpan.FromMilliseconds(Movies.FilterDebounceMs - 1));
        Assert.Equal(8, Titles(open.Cut).Length);

        open.Clock.Advance(TimeSpan.FromMilliseconds(1));
        open.Cut.WaitForAssertion(() => Assert.Equal(new[] { "Alpha" }, Titles(open.Cut)));
    }

    [Fact]
    public void EveryKeystrokeRestartsTheDebounceSoOnlyTheLastOneApplies()
    {
        var open = Render(Eight);

        open.Cut.Find("#q").Input("c");
        open.Clock.Advance(TimeSpan.FromMilliseconds(100));
        open.Cut.Find("#q").Input("ch");
        open.Clock.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(8, Titles(open.Cut).Length);   // 200ms after the first key, 100ms after the last

        open.Clock.Advance(TimeSpan.FromMilliseconds(50));
        open.Cut.WaitForAssertion(() => Assert.Equal(new[] { "Charlie", "Echo" }, Titles(open.Cut)));   // "ch"
    }

    [Fact]
    public void TheBoxShowsEveryKeystrokeImmediatelyWhateverHasBeenApplied()
    {
        var open = Render(Eight);

        open.Cut.Find("#q").Input("cha");
        open.Cut.Find("#q").Input("char");

        Assert.Equal("char", open.Cut.Find("#q").GetAttribute("value"));
        Assert.Equal(8, Titles(open.Cut).Length);
    }

    [Fact]
    public void ChangingAFacetCarriesTheTextAlreadyTypedWithIt()
    {
        var open = Render(Eight);

        open.Cut.Find("#q").Input("ech");
        Tick(open.Cut, "Match confidence", "Not matched", true);   // before the debounce has fired

        // Not matched alone is Echo and Golf: the typed text is what leaves Echo.
        Assert.Equal(new[] { "Echo" }, Titles(open.Cut));
        Assert.Equal("ech", open.Cut.Find("#q").GetAttribute("value"));
    }

    [Fact]
    public void ASearchOfNothingButSpacesIsNoSearch()
    {
        var open = Render(Eight);

        open.Cut.Find("#q").Input("   ");
        open.Clock.Advance(TimeSpan.FromMilliseconds(Movies.FilterDebounceMs));

        open.Cut.WaitForAssertion(() => Assert.Equal(8, Titles(open.Cut).Length));
        Assert.DoesNotContain("q=", open.Nav.Uri);
    }

    // ================================================================ the address, written

    [Fact]
    public void AChoiceIsWrittenToTheAddressByReplacingTheHistoryEntry()
    {
        var open = Render(Eight);
        var before = open.Nav.History.Count;

        Tick(open.Cut, "Match confidence", "Not matched", true);

        Assert.Equal("movies?conf=unmatched", open.Nav.ToBaseRelativePath(open.Nav.Uri));
        Assert.Equal(before, open.Nav.History.Count);   // replaced in place: a push would have made it longer
        Assert.True(open.Nav.History.First().Options.ReplaceHistoryEntry);
    }

    [Fact]
    public void TypingIsWrittenOnceTheDebounceHasFiredAndAnEmptyBoxDropsTheParameter()
    {
        var open = Render(Eight);

        open.Cut.Find("#q").Input("alp");
        Assert.DoesNotContain("q=", open.Nav.Uri);
        open.Clock.Advance(TimeSpan.FromMilliseconds(Movies.FilterDebounceMs));
        open.Cut.WaitForAssertion(() => Assert.EndsWith("movies?q=alp", open.Nav.Uri));

        open.Cut.Find("#q").Input("");
        open.Clock.Advance(TimeSpan.FromMilliseconds(Movies.FilterDebounceMs));
        open.Cut.WaitForAssertion(() => Assert.Equal("movies", open.Nav.ToBaseRelativePath(open.Nav.Uri)));
        Assert.DoesNotContain("q=", open.Nav.Uri);
    }

    [Fact]
    public void EveryFacetAndTheSortHaveTheirOwnParameter()
    {
        var open = Render(Eight);

        Tick(open.Cut, "Genre", "Drama", true);
        Tick(open.Cut, "Decade", "1990s", true);
        Tick(open.Cut, "Decade", "Year unknown", true);
        Tick(open.Cut, "Match confidence", "Uncertain matches", true);
        open.Cut.Find("#sort").Change("runtime");

        Assert.Equal("movies?genre=18&decade=1990&decade=unknown&conf=uncertain&sort=runtime",
                     open.Nav.ToBaseRelativePath(open.Nav.Uri));
    }

    [Fact]
    public void ClearingEveryChoiceLeavesTheBareAddress()
    {
        var open = Render(Eight);
        Tick(open.Cut, "Genre", "Drama", true);
        Tick(open.Cut, "Match confidence", "Not matched", true);

        Tick(open.Cut, "Genre", "Drama", false);
        Tick(open.Cut, "Match confidence", "All titles", true);

        Assert.Equal("movies", open.Nav.ToBaseRelativePath(open.Nav.Uri));
    }

    // ================================================================ the address, read

    [Fact]
    public void TheAddressIsAFilterAnyoneCanBookmark()
    {
        var open = Render(Eight, "/movies?conf=unmatched");

        Assert.Equal(new[] { "Echo", "Golf" }, Titles(open.Cut));

        var radio = Box(open.Cut, "Match confidence").QuerySelectorAll("input").Single(r => r.HasAttribute("checked"));
        Assert.Equal("Not matched", radio.ParentElement!.TextContent.Trim());
    }

    [Fact]
    public void EveryParameterIsReadOnArrivalAndReachesItsControl()
    {
        // r: Bravo, Charlie, Foxtrot (by title). Drama or Comedy keeps all three; the 1990s or 2000s keeps all
        // three; year order, newest first, is what tells the sort parameter was read.
        var open = Render(Eight, "/movies?q=r&genre=18&genre=35&decade=1990&decade=2000&sort=year");

        Assert.Equal(new[] { "Foxtrot", "Charlie", "Bravo" }, Titles(open.Cut));
        Assert.Equal("r", open.Cut.Find("#q").GetAttribute("value"));
        Assert.Equal(new[] { "Comedy", "Drama" }, Box(open.Cut, "Genre").QuerySelectorAll("input[checked]")
            .Select(c => c.ParentElement!.TextContent.Trim()).Order());
        Assert.Equal(new[] { "1990s", "2000s" }, Box(open.Cut, "Decade").QuerySelectorAll("input[checked]")
            .Select(c => c.ParentElement!.TextContent.Trim()).Order());
        Assert.Equal("year", open.Cut.FindAll("#sort option").Single(o => o.HasAttribute("selected")).GetAttribute("value"));
    }

    [Fact]
    public void AValueTheRouterCannotConvertIsJunkToIgnoreAndNeverAnUnhandledException()
    {
        var open = Render(Eight, "/movies?genre=abc&genre=&page=xyz&conf=bogus&sort=nope&decade=zz&decade=-1&q=%20");

        Assert.Equal(8, Titles(open.Cut).Length);
        Assert.DoesNotContain(open.Cut.FindAll("[role=alert]"), a => a.TextContent.Trim().Length > 0);
    }

    [Fact]
    public void ARepeatedScalarParameterIsStillAFilterAndNotACrash()
    {
        var open = Render(Eight, "/movies?conf=unmatched&conf=uncertain&q=a&q=b&page=1&page=2&sort=year&sort=title");

        Assert.NotEmpty(Titles(open.Cut).Concat(open.Cut.FindAll(".no-match p").Select(p => p.TextContent)));
    }

    /// <remarks>
    /// A genre id the catalogue's meta does not name has no control to untick it, and a filter nobody can see or
    /// remove must not be silently emptying the list.
    /// </remarks>
    [Fact]
    public void AGenreTheMetaDoesNotNameIsNotAppliedBecauseNobodyCouldUntickIt()
    {
        var open = Render(Eight, "/movies?genre=999");

        Assert.Equal(8, Titles(open.Cut).Length);
        Assert.Empty(open.Cut.FindAll(".no-match"));
    }

    [Fact]
    public void WithNoGenreNamesAGenreInTheAddressIsIgnoredAndTheOtherFacetsStillApply()
    {
        var open = Render(Eight, "/movies?genre=18&conf=uncertain", meta: null);

        Assert.Equal(new[] { "Delta", "Foxtrot" }, Titles(open.Cut));
    }

    [Fact]
    public void FollowingALinkToAnotherFilterFollowsItEvenWithTheBoxHalfTyped()
    {
        var open = Render(Eight);
        open.Cut.Find("#q").Input("abc");          // pending, not yet applied

        open.Nav.NavigateTo("/movies?conf=unmatched");

        open.Cut.WaitForAssertion(() => Assert.Equal(new[] { "Echo", "Golf" }, Titles(open.Cut)));
        Assert.Equal(string.Empty, open.Cut.Find("#q").GetAttribute("value") ?? string.Empty);

        // And the half-typed text did not survive to be applied a moment later.
        open.Clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal("movies?conf=unmatched", open.Nav.ToBaseRelativePath(open.Nav.Uri));
    }

    /// <remarks>
    /// The page's own write comes back to it as an address. If it were adopted like a link, the box would
    /// snap back to the text it held a debounce ago and eat what was typed since. An address that says what
    /// the page last wrote -- whatever else it carries -- is that echo, and is left alone.
    /// </remarks>
    [Fact]
    public void TheEchoOfThePagesOwnWriteDoesNotOverwriteWhatIsBeingTyped()
    {
        var open = Render(Eight);
        open.Cut.Find("#q").Input("al");
        open.Clock.Advance(TimeSpan.FromMilliseconds(Movies.FilterDebounceMs));
        open.Cut.WaitForAssertion(() => Assert.EndsWith("movies?q=al", open.Nav.Uri));

        open.Cut.Find("#q").Input("alp");                         // typed since the write, not yet applied
        open.Nav.NavigateTo("/movies?q=al&utm_source=echo", replace: true);   // the same state, arriving late

        Assert.Equal("alp", open.Cut.Find("#q").GetAttribute("value"));
    }

    // ================================================================ the address arriving late

    /// <summary>
    /// Renders with a navigation manager that reports a navigation to the router only when told to, which is the
    /// order a browser gives: the page writes, a keystroke can be dispatched, and THEN the router says the
    /// address changed. See <see cref="DeferredNavigationManager"/>.
    /// </summary>
    private (Open Open, DeferredNavigationManager Nav) RenderWithALateEcho(IEnumerable<Film> films, string address = "/movies")
    {
        var late = new DeferredNavigationManager();
        Services.AddSingleton<NavigationManager>(late);

        var (clock, stub) = this.AddAnnouncerAndCatalog(StubCatalog.Json(Json(films)));
        stub.Serves(StubCatalog.Meta, StubCatalog.Json(Meta));

        var cut = this.RenderAppAt(address);
        late.Discard();

        return (new Open(cut, clock, Services.GetRequiredService<BunitNavigationManager>(), stub), late);
    }

    /// <remarks>
    /// THE INTERLEAVING THAT MAKES SEARCH UNUSABLE IF IT IS WRONG. The page writes q=al, the visitor types one
    /// more letter, and only then does the router report the address. If that report were adopted like a link
    /// the box would snap back to "al" and the letter would be gone -- on every pause in typing.
    /// </remarks>
    [Fact]
    public async Task AnEchoThatArrivesAfterMoreTypingDoesNotEatIt()
    {
        var (open, late) = RenderWithALateEcho(Eight);

        open.Cut.Find("#q").Input("al");
        open.Clock.Advance(TimeSpan.FromMilliseconds(Movies.FilterDebounceMs));
        open.Cut.WaitForAssertion(() => Assert.EndsWith("movies?q=al", late.Uri));

        open.Cut.Find("#q").Input("alp");      // typed after the write...
        await open.Cut.InvokeAsync(late.Deliver);   // ...and before the router reports it

        Assert.Equal("alp", open.Cut.Find("#q").GetAttribute("value"));

        open.Clock.Advance(TimeSpan.FromMilliseconds(Movies.FilterDebounceMs));
        open.Cut.WaitForAssertion(() => Assert.EndsWith("movies?q=alp", late.Uri));
        await open.Cut.InvokeAsync(late.Deliver);
        Assert.Equal(new[] { "Alpha" }, Titles(open.Cut));
        Assert.Equal("alp", open.Cut.Find("#q").GetAttribute("value"));
    }

    /// <remarks>
    /// R-J, with the router's report held back. With the echo delivered at once the address itself puts the page
    /// right, and a page that forgot to reset would be corrected by the very navigation it wrote. Here nothing has
    /// been reported yet, so the reset has to have happened in the page.
    /// </remarks>
    [Theory]
    [InlineData("filter")]
    [InlineData("sort")]
    public void AChangeReturnsToPageOneBeforeTheRouterHasReportedAnything(string change)
    {
        var (open, late) = RenderWithALateEcho(Bulk(130));
        Next(open.Cut);
        Assert.Equal("Page 2 of 3", PageLabel(open.Cut));

        if (change == "filter")
        {
            open.Cut.Find("#q").Input("Film 0");   // 99 films: two pages, so page 2 would still be in range
            open.Clock.Advance(TimeSpan.FromMilliseconds(Movies.FilterDebounceMs));
            open.Cut.WaitForAssertion(() => Assert.Equal("Page 1 of 2", PageLabel(open.Cut)));
        }
        else
        {
            open.Cut.Find("#sort").Change("year");
            Assert.Equal("Page 1 of 3", PageLabel(open.Cut));
        }

        Assert.DoesNotContain("page=", late.Uri);
    }

    [Fact]
    public async Task ThePagersEchoDoesNotEatTheTextEitherAndAFilterEchoDoesNotMovePages()
    {
        var (open, late) = RenderWithALateEcho(Bulk(130));

        Next(open.Cut);                         // writes page=2
        open.Cut.Find("#q").Input("Film 0");    // typed before the echo, not yet applied
        await open.Cut.InvokeAsync(late.Deliver);

        Assert.Equal("Film 0", open.Cut.Find("#q").GetAttribute("value"));
        Assert.Equal("Page 2 of 3", PageLabel(open.Cut));
    }

    [Fact]
    public async Task AFacetsLateEchoDoesNotRevertAFacetChosenSince()
    {
        var (open, late) = RenderWithALateEcho(Eight);

        Tick(open.Cut, "Match confidence", "Not matched", true);     // writes conf=unmatched
        Tick(open.Cut, "Match confidence", "Uncertain matches", true); // writes conf=uncertain before either echo
        await open.Cut.InvokeAsync(late.Deliver);

        Assert.Equal(new[] { "Delta", "Foxtrot" }, Titles(open.Cut));
    }

    [Fact]
    public void AnAddressChangeOverAnEmptyLibraryDoesNotSpeakAboutAFilter()
    {
        var open = Render([]);
        Settle(open);

        open.Nav.NavigateTo("/movies?q=x");
        Settle(open);

        Assert.Equal("The library is empty.", Spoken().StatusText);
    }

    // ================================================================ the page number

    private static string PageLabel(IRenderedComponent<MovieLibrary.Web.App> cut) =>
        cut.Find("nav.pager span").TextContent.Trim();

    private static void Next(IRenderedComponent<MovieLibrary.Web.App> cut) =>
        cut.FindAll("nav.pager button").Single(b => b.TextContent.Trim() == "Next").Click();

    [Fact]
    public void PagingIsWrittenToTheAddress()
    {
        var open = Render(Bulk(130));

        Next(open.Cut);

        Assert.Equal("movies?page=2", open.Nav.ToBaseRelativePath(open.Nav.Uri));
        Assert.Equal("Page 2 of 3", PageLabel(open.Cut));
    }

    [Fact]
    public void APageInTheAddressIsReadOnArrival()
    {
        var open = Render(Bulk(130), "/movies?page=3");

        Assert.Equal("Page 3 of 3", PageLabel(open.Cut));
        Assert.Equal(10, Titles(open.Cut).Length);
    }

    /// <remarks>R-J. Filtering to ten films while on page 2 must not leave page=2 in the address.</remarks>
    [Fact]
    public void ChangingAFilterReturnsToTheFirstPageAndDropsThePageFromTheAddress()
    {
        var open = Render(Bulk(130));
        Next(open.Cut);
        Assert.Equal("Page 2 of 3", PageLabel(open.Cut));

        open.Cut.Find("#q").Input("Film 12");
        open.Clock.Advance(TimeSpan.FromMilliseconds(Movies.FilterDebounceMs));

        open.Cut.WaitForAssertion(() => Assert.Equal(10, Titles(open.Cut).Length));
        Assert.Equal("Showing 1–10 of 10 titles.", open.Cut.Find("p.count").TextContent.Trim());
        Assert.DoesNotContain("page=", open.Nav.Uri);
    }

    /// <remarks>The caller's ruling: sort is not a filter, but re-sorting on page 2 must not show a different 60 films unannounced.</remarks>
    [Fact]
    public void ChangingTheSortAlsoReturnsToTheFirstPage()
    {
        var open = Render(Bulk(130));
        Next(open.Cut);

        open.Cut.Find("#sort").Change("runtime");

        Assert.Equal("Page 1 of 3", PageLabel(open.Cut));
        Assert.DoesNotContain("page=", open.Nav.Uri);
        Assert.Contains("sort=runtime", open.Nav.Uri);
    }

    /// <remarks>
    /// R-J, the other side: the grid heals a page that is out of range, silently, and raises the clamped number.
    /// The page adopts it and writes it, so a deep link to page 99 ends with the address saying page 3.
    /// </remarks>
    [Fact]
    public void ADeepLinkPastTheLastPageIsHealedAndTheAddressSaysSo()
    {
        var open = Render(Bulk(130), "/movies?page=99");

        open.Cut.WaitForAssertion(() => Assert.Equal("Page 3 of 3", PageLabel(open.Cut)));
        Assert.Equal("movies?page=3", open.Nav.ToBaseRelativePath(open.Nav.Uri));

        // Silent: the only thing ever said is the opening count, never a page the visitor did not ask for.
        Settle(open);
        Assert.Equal("The library holds 130 films.", Spoken().StatusText);
    }

    // ================================================================ saying how many

    private string Status(Open open) => open.Cut.Find("#status-announcer").TextContent;

    /// <summary>Lets the opening count be delivered, so what is read afterwards is the filter's own.</summary>
    private void Opened(Open open)
    {
        Settle(open);
        open.Cut.WaitForAssertion(() => Assert.Equal("The library holds 8 films.", Status(open)));
    }

    /// <remarks>
    /// The two numbers, as literals. Every other test here builds its timeline FROM these constants -- and a
    /// constant is inlined into the assembly that reads it, so a test that steps "AnnounceDebounceMs - 1" follows
    /// the constant wherever it is changed to and cannot notice. The brief's figures are the contract: filter at
    /// about 150 ms so the grid feels immediate, announce at about 750 ms so a screen reader user hears the count
    /// once and not a stream of interruptions per keystroke. The gap between them is the point.
    /// </remarks>
    [Fact]
    public void TheTwoDebouncesAreTheBriefsFiguresAndTheGapBetweenThemIsWideEnoughToMatter()
    {
        Assert.Equal(150, Movies.FilterDebounceMs);
        Assert.Equal(750, Movies.AnnounceDebounceMs);
        Assert.True(Movies.AnnounceDebounceMs - Movies.FilterDebounceMs >= 5 * Announcer.SettleDelay.TotalMilliseconds);
    }

    /// <summary>
    /// Moves the clock in small steps, letting the page process whatever each step fired before the next. A timer
    /// callback hands its work to the renderer's dispatcher, so one big Advance would fire a callback and return
    /// before it had run -- and a clock that has already jumped past an announcement's settle delay would never
    /// see it armed. A test of "not yet" needs the page to have done everything it was going to do by then.
    /// </summary>
    private static async Task Step(Open open, int milliseconds)
    {
        for (var elapsed = 0; elapsed < milliseconds; elapsed += 10)
        {
            open.Clock.Advance(TimeSpan.FromMilliseconds(Math.Min(10, milliseconds - elapsed)));
            await open.Cut.InvokeAsync(() => { });
        }
    }

    [Fact]
    public async Task TheCountIsAnnouncedOnTheLongerDebounceAndNotBefore()
    {
        var open = Render(Eight);
        Opened(open);

        Tick(open.Cut, "Match confidence", "Uncertain matches", true);

        // Filtered at once, then silent until the announce debounce AND the settle delay behind it have passed...
        Assert.Equal(new[] { "Delta", "Foxtrot" }, Titles(open.Cut));
        await Step(open, Movies.AnnounceDebounceMs - 1);
        Assert.Equal("The library holds 8 films.", Spoken().StatusText);

        // ...then one polite statement.
        await Step(open, 1 + (int)Announcer.SettleDelay.TotalMilliseconds);
        open.Cut.WaitForAssertion(() => Assert.Equal("2 titles match.", Status(open)));
    }

    [Fact]
    public void ATypingBurstIsOneAnnouncementAboutTheFinalCount()
    {
        var open = Render(Eight);
        Opened(open);
        var said = new List<string>();
        Spoken().Changed += () => said.Add(Spoken().StatusText);

        foreach (var typed in new[] { "c", "ch", "cha", "char" })
        {
            open.Cut.Find("#q").Input(typed);
            open.Clock.Advance(TimeSpan.FromMilliseconds(100));
        }

        Settle(open);

        open.Cut.WaitForAssertion(() => Assert.Equal("1 title matches.", Status(open)));
        Assert.Single(said, s => s.Length > 0);
    }

    [Theory]
    [InlineData("a", "4 titles match.")]
    [InlineData("alpha", "1 title matches.")]
    [InlineData("zzz", "No titles match these filters.")]
    public void SingularPluralAndZeroEachHaveTheirOwnForm(string query, string expected)
    {
        var open = Render(Eight);
        Opened(open);

        open.Cut.Find("#q").Input(query);
        Settle(open);

        open.Cut.WaitForAssertion(() => Assert.Equal(expected, Status(open)));
    }

    /// <remarks>
    /// R-G. The grid announces its own page changes through the same region. A filter that also said "Page 1 of
    /// 3" would be two statements racing for one region, and the visitor would hear one of them, unpredictably.
    /// </remarks>
    [Fact]
    public void AFilterChangeAnnouncesTheCountAndNeverAPage()
    {
        var open = Render(Bulk(130));
        Next(open.Cut);
        Settle(open);
        Assert.StartsWith("Page 2 of 3.", Status(open));

        var said = new List<string>();
        Spoken().Changed += () => said.Add(Spoken().StatusText);

        open.Cut.Find("#q").Input("Film 1");
        Settle(open);

        open.Cut.WaitForAssertion(() => Assert.Equal("31 titles match.", Status(open)));
        Assert.DoesNotContain(said, s => s.Contains("Page", StringComparison.Ordinal));
    }

    /// <remarks>R-L, and one number in one block of text: the same grouping as the grid's head under it.</remarks>
    [Theory]
    [InlineData("en-US", "1,456 titles match.", "Showing 1–60 of 1,456 titles.")]
    [InlineData("de-DE", "1.456 titles match.", "Showing 1–60 of 1.456 titles.")]
    public void TheCountFollowsTheBrowsersCultureLikeTheGridHeadBeneathIt(string culture, string spoken, string head)
    {
        using var scope = new CultureScope(culture);
        var open = Render(Bulk(1456));
        Settle(open);

        open.Cut.Find("#q").Input("Film");
        Settle(open);

        open.Cut.WaitForAssertion(() => Assert.Equal(spoken, Status(open)));
        Assert.Equal(head, open.Cut.Find("p.count").TextContent.Trim());
    }

    [Fact]
    public void ReSortingChangesTheOrderAndSaysNothingBecauseTheCountIsTheSame()
    {
        var open = Render(Eight);
        Opened(open);

        open.Cut.Find("#sort").Change("year");
        Settle(open);

        Assert.Equal("The library holds 8 films.", Status(open));
    }

    [Theory]
    [InlineData("/movies?sort=year")]
    [InlineData("/movies?page=2&sort=runtime")]
    [InlineData("/movies?utm_source=mail")]
    public void FollowingALinkThatLeavesTheSelectionAloneSaysNothing(string link)
    {
        var open = Render(Eight);
        Opened(open);

        open.Nav.NavigateTo(link);
        Settle(open);

        Assert.Equal("The library holds 8 films.", Spoken().StatusText);
    }

    [Fact]
    public void AnAddressThatArrivesWithAFilterSaysTheFilteredCountAndNotTheLibrarysTotal()
    {
        var open = Render(Eight, "/movies?conf=unmatched");
        Settle(open);

        Assert.Equal("2 titles match.", Status(open));
        Assert.Equal("The library holds 8 films.", open.Cut.Find(".catalog-state p").TextContent.Trim());
    }

    [Fact]
    public void FollowingALinkAnnouncesTheNewCount()
    {
        var open = Render(Eight);
        Opened(open);

        open.Nav.NavigateTo("/movies?conf=uncertain");
        Settle(open);

        open.Cut.WaitForAssertion(() => Assert.Equal("2 titles match.", Status(open)));
    }

    /// <remarks>R7: whoever writes a standing statement clears it. A count still waiting out its debounce must not land on the next page.</remarks>
    [Fact]
    public async Task LeavingThePageWithACountPendingSpeaksNothingAtAll()
    {
        var open = Render(Eight);
        Opened(open);

        Tick(open.Cut, "Match confidence", "Uncertain matches", true);   // arms the 750ms announcement
        await DisposeComponentsAsync();
        Settle(open);

        Assert.Equal(string.Empty, Spoken().StatusText);
    }

    [Fact]
    public async Task LeavingThePageWithATypedFilterPendingNeitherFiltersNorWritesTheAddress()
    {
        var open = Render(Eight);
        var uri = open.Nav.Uri;

        open.Cut.Find("#q").Input("alp");
        await DisposeComponentsAsync();
        open.Clock.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(uri, open.Nav.Uri);
    }

    // ================================================================ zero results

    private static void Type(Open open, string text)
    {
        open.Cut.Find("#q").Input(text);
        open.Clock.Advance(TimeSpan.FromMilliseconds(Movies.FilterDebounceMs));
    }

    [Fact]
    public void FilteringToNothingIsATextAndAButtonNotAnEmptyGrid()
    {
        var open = Render(Eight);

        Type(open, "zzz");

        open.Cut.WaitForAssertion(() => Assert.Equal("No titles match these filters.",
                                                     open.Cut.Find(".no-match p").TextContent.Trim()));
        Assert.Empty(open.Cut.FindAll("ul.grid"));
        Assert.Empty(open.Cut.FindAll("nav.pager"));
        Assert.Empty(open.Cut.FindAll("p.count"));
        Assert.Equal("Clear all filters", open.Cut.Find(".no-match button").TextContent.Trim());
        Assert.NotNull(open.Cut.Find("search"));   // the panel stays: the visitor has to be able to widen the search
    }

    /// <remarks>R-I: the focus call lands on an element that cannot take focus unless it carries the attribute.</remarks>
    [Fact]
    public void TheNoMatchParagraphCanTakeFocus()
    {
        var open = Render(Eight);
        Type(open, "zzz");

        open.Cut.WaitForAssertion(() => Assert.Equal("-1", open.Cut.Find(".no-match p").GetAttribute("tabindex")));
    }

    [Fact]
    public void TheNoMatchSentenceIsTheOneConstantSeenAndSpoken()
    {
        var open = Render(Eight);
        Type(open, "zzz");

        open.Cut.WaitForAssertion(() => Assert.Equal(Movies.NoMatch, open.Cut.Find(".no-match p").TextContent.Trim()));
        Assert.Equal(Movies.NoMatch, Movies.CountSentence(0));
    }

    /// <remarks>
    /// R-I and R9. The pager is removed with the grid, so a pager button that held focus takes it with it. When
    /// the focus helper says nobody has moved focus (what a removed button leaves behind), it lands on the
    /// paragraph, and that move IS the announcement (R3): the same words are not also sent to the region.
    /// </remarks>
    [Fact]
    public void WhenFocusWasLostWithThePagerItMovesToTheParagraphAndTheSentenceIsNotSpokenTwice()
    {
        JSInterop.Setup<bool>(FocusHelper, _ => true).SetResult(true);
        var open = Render(Eight);
        Opened(open);

        Type(open, "zzz");
        open.Cut.WaitForAssertion(() => Assert.Single(open.Cut.FindAll(".no-match p")));
        Settle(open);

        var call = JSInterop.Invocations[FocusHelper].Last();
        Assert.Equal(new[] { "#main-content", "#app h1" }, call.Arguments[1]);

        // The region is EMPTY: the grid cleared the opening count on its way out (R7), and the paragraph focus
        // landed on is read as it takes focus, so the same words are not sent to the region as well (R3). The
        // twin of this test, where the helper refuses, is the one that proves the sentence is spoken when
        // nothing else will speak it.
        Assert.Equal(string.Empty, Status(open));
    }

    /// <remarks>
    /// And when it did NOT move -- a visitor typing in the search box still holds focus there -- the sentence is
    /// spoken instead. Stealing focus from a field mid-word is worse than the thing it would fix.
    /// </remarks>
    [Fact]
    public void WhenTheVisitorIsStillTypingFocusStaysPutAndTheSentenceIsSpokenInstead()
    {
        JSInterop.Setup<bool>(FocusHelper, _ => true).SetResult(false);
        var open = Render(Eight);
        Opened(open);

        Type(open, "zzz");
        Settle(open);

        open.Cut.WaitForAssertion(() => Assert.Equal(Movies.NoMatch, Status(open)));
    }

    /// <remarks>
    /// An address that arrives at nothing. FocusOnNavigate has put focus on the h1, which the helper counts as
    /// "the page's own", so it moves on to the paragraph -- and the sentence is then read as it takes focus, and
    /// is not also announced. Where the helper refuses, the announcement is all there is, so it is spoken.
    /// </remarks>
    [Theory]
    [InlineData(true, "")]
    [InlineData(false, "No titles match these filters.")]
    public void AnAddressThatArrivesAtNothingIsSaidOnceWhicheverWayFocusWent(bool helperMoves, string spoken)
    {
        JSInterop.Setup<bool>(FocusHelper, _ => true).SetResult(helperMoves);

        var open = Render(Eight, "/movies?q=zzz");
        Settle(open);

        open.Cut.WaitForAssertion(() => Assert.Equal(spoken, Status(open)));
        Assert.Single(JSInterop.Invocations[FocusHelper]);
    }

    /// <remarks>
    /// The focus call is interop, so the page is suspended at an await while it is in flight, and a render can
    /// arrive in that window. That render's after-render pass must neither ask for focus a second time nor make
    /// the opening announcement the first pass is still deciding about: it would say the sentence that the focus
    /// move, completing a moment later, is about to say as well.
    /// </remarks>
    [Fact]
    public async Task ARenderWhileTheFocusCallIsInFlightNeitherAsksTwiceNorSpeaksTwice()
    {
        var pending = JSInterop.Setup<bool>(FocusHelper, _ => true);
        var open = Render(Eight, "/movies?q=zzz");
        Assert.Single(JSInterop.Invocations[FocusHelper]);          // asked once, and not answered

        open.Cut.Find("#q").Input("zzz");                            // renders the page again, filter unchanged
        Settle(open);
        Assert.Single(JSInterop.Invocations[FocusHelper]);

        pending.SetResult(true);                                     // the move completes
        await open.Cut.InvokeAsync(() => { });
        Settle(open);

        Assert.Single(JSInterop.Invocations[FocusHelper]);
        open.Cut.WaitForAssertion(() => Assert.Equal(string.Empty, Status(open)));
    }

    [Fact]
    public void FocusIsOnlyAskedForOncePerRunOfZeroResultsNotOnEveryKeystrokeWhileItLasts()
    {
        var open = Render(Eight);

        Type(open, "zzz");
        open.Cut.WaitForAssertion(() => Assert.Single(open.Cut.FindAll(".no-match p")));
        Type(open, "zzzz");
        Type(open, "zzzzz");
        open.Cut.WaitForAssertion(() => Assert.Contains("q=zzzzz", open.Nav.Uri));

        Assert.Single(JSInterop.Invocations[FocusHelper]);

        Type(open, "alpha");   // results again...
        open.Cut.WaitForAssertion(() => Assert.Empty(open.Cut.FindAll(".no-match")));
        Type(open, "nope");    // ...and nothing again: a new run
        open.Cut.WaitForAssertion(() => Assert.Single(open.Cut.FindAll(".no-match p")));

        Assert.Equal(2, JSInterop.Invocations[FocusHelper].Count);
    }

    [Fact]
    public void ClearAllFiltersRestoresEverythingKeepsTheSortAndPutsFocusInTheSearchBox()
    {
        var open = Render(Eight, "/movies?q=zzz&conf=unmatched&sort=year");
        Assert.Single(open.Cut.FindAll(".no-match"));

        open.Cut.Find(".no-match button").Click();

        Assert.Equal(8, Titles(open.Cut).Length);
        Assert.Equal("Foxtrot", Titles(open.Cut)[0]);   // still newest first: sort is not a filter
        Assert.Equal(string.Empty, open.Cut.Find("#q").GetAttribute("value") ?? string.Empty);
        Assert.Equal("movies?sort=year", open.Nav.ToBaseRelativePath(open.Nav.Uri));
        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Fact]
    public void ClearAllFiltersAnnouncesTheNewCount()
    {
        var open = Render(Eight, "/movies?q=zzz");
        Settle(open);

        open.Cut.Find(".no-match button").Click();
        Settle(open);

        open.Cut.WaitForAssertion(() => Assert.Equal("8 titles match.", Status(open)));
    }

    // ================================================================ what a filter must not disturb

    [Fact]
    public void QueryOnlyNavigationsNeverReFireTheFocusMoveThatFollowsARouteChange()
    {
        var open = Render(Eight);
        const string FocusOnNavigate = "Blazor._internal.domWrapper.focusBySelector";
        var before = JSInterop.Invocations[FocusOnNavigate].Count;
        Assert.True(before >= 1, "FocusOnNavigate never fired on arrival, so this test would prove nothing.");

        Tick(open.Cut, "Match confidence", "Not matched", true);
        Type(open, "echo");
        open.Cut.WaitForAssertion(() => Assert.Contains("q=echo", open.Nav.Uri));

        Assert.Equal(before, JSInterop.Invocations[FocusOnNavigate].Count);
        Assert.Equal(string.Empty, open.Cut.Find("#route-announcer").TextContent);
    }

    [Fact]
    public void ThereIsStillExactlyOneHeadingAndItsTitleIsInTheFirstRender()
    {
        this.AddAnnouncer(CatalogOutcome.NeverCompletes);
        var cut = this.RenderAppAt("/movies?conf=unmatched&q=x");

        Assert.Equal("Films", cut.Find("h1").TextContent.Trim());
        Assert.Single(cut.FindAll("h1"));
    }

    // ================================================================ leaving the page, and coming back from a failure

    /// <remarks>
    /// Both debounce timers go with the page. The clock counts only the timers that can still fire, and once the
    /// page has cleared its own region and the announcer has dropped what it was holding, nothing should be left.
    /// </remarks>
    [Fact]
    public async Task LeavingThePageLeavesNoTimerBehind()
    {
        var open = Render(Eight);
        Opened(open);

        open.Cut.Find("#q").Input("alp");                                   // arms the filter debounce
        Tick(open.Cut, "Match confidence", "Uncertain matches", true);      // arms the announce debounce
        Assert.True(open.Clock.ArmedTimers >= 2, "the two debounces were never armed, so this proves nothing");

        await DisposeComponentsAsync();

        Assert.Equal(0, open.Clock.ArmedTimers);
    }

    /// <remarks>
    /// A timer that had already queued its callback when the page went cannot be stopped by disposing the timer.
    /// The callback then runs against a page that has been left, and without its guard it would write the address a
    /// second after the visitor navigated somewhere else -- and navigate them back.
    /// </remarks>
    [Fact]
    public async Task ACallbackAlreadyQueuedWhenThePageWentWritesAndSpeaksNothing()
    {
        var open = Render(Eight);
        Opened(open);
        var page = open.Cut.FindComponent<Movies>().Instance;

        open.Cut.Find("#q").Input("alp");
        var before = open.Nav.Uri;
        await DisposeComponentsAsync();

        await Task.Run(page.ApplyTypedQuery);
        page.AnnounceCount();
        Settle(open);

        Assert.Equal(before, open.Nav.Uri);
        Assert.Equal(string.Empty, Spoken().StatusText);
    }

    [Fact]
    public void TheFiltersWorkAfterARetrySucceeds()
    {
        var (clock, stub) = this.AddAnnouncerAndCatalog(
            StubCatalog.Throws(new HttpRequestException("down")), StubCatalog.Json(Json(Eight)));
        stub.Serves(StubCatalog.Meta, StubCatalog.Json(Meta));
        var cut = this.RenderAppAt("/movies");
        Assert.Empty(cut.FindAll("search"));

        cut.Find("button.try-again").Click();

        Assert.Equal(8, Titles(cut).Length);
        Assert.Equal(new[] { "Action", "Comedy", "Drama" },
                     Box(cut, "Genre").QuerySelectorAll("label").Select(l => l.TextContent.Trim()));

        Tick(cut, "Match confidence", "Not matched", true);
        Assert.Equal(new[] { "Echo", "Golf" }, Titles(cut));
    }
}

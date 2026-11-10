using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MovieLibrary.Web.Services;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// The grid where it actually ships: inside the real app, at /movies, under the real layout and the real
/// router. PosterGridTests drives the component through its parameters; this file is about the wiring --
/// that the page reaches the grid at all, in the right state, in the right place, and that a page change
/// reaches the live region the layout owns rather than only the announcer's own property.
/// </summary>
public class MoviesGridTests : BunitContext
{
    public MoviesGridTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    /// <summary>
    /// A published catalogue of <paramref name="count"/> films, in the real wire shape. The titles are zero-padded
    /// because the page sorts by title: "Film 2" sorts after "Film 19", so unpadded numbers would put the 61st film
    /// somewhere in the middle of page one and every assertion about which film is where would be about the sort.
    /// </summary>
    private static string Catalogue(int count) =>
        "[" + string.Join(",", Enumerable.Range(1, count).Select(i =>
            $"{{\"i\":{i},\"k\":\"k{i}\",\"t\":\"Film {i:D4}\",\"y\":1990,\"rt\":100,\"p\":\"/p{i}.jpg\","
            + "\"g\":[],\"ag\":\"Drama\",\"vote\":0,\"dir\":[],\"cast\":[],\"mc\":1}")) + "]";

    private (IRenderedComponent<MovieLibrary.Web.App> Cut, ManualTimeProvider Clock) RenderWith(int films)
    {
        var (clock, _) = this.AddAnnouncerAndCatalog(StubCatalog.Json(Catalogue(films)));
        return (this.RenderAppAt("/movies"), clock);
    }

    private Announcer Spoken() => Services.GetRequiredService<Announcer>();

    [Theory]
    [InlineData(CatalogOutcome.Failed)]
    [InlineData(CatalogOutcome.NeverCompletes)]
    public void NeitherTheLoadingNorTheFailedStateRendersAGrid(CatalogOutcome outcome)
    {
        this.AddAnnouncer(outcome);
        var cut = this.RenderAppAt("/movies");

        Assert.Empty(cut.FindAll("ul.grid"));
        Assert.Empty(cut.FindAll("nav.pager"));
        Assert.Empty(cut.FindAll("p.count"));

        // And the heading and the state container are untouched in both: they sit outside every branch.
        Assert.Equal("Films", cut.Find("h1").TextContent.Trim());
        Assert.Single(cut.FindAll(".catalog-state p"));
    }

    [Fact]
    public void TheReadyStateRendersTheGridWithoutDisturbingWhatTask5Owns()
    {
        var (cut, _) = RenderWith(61);

        Assert.Single(cut.FindAll("ul.grid"));
        Assert.Single(cut.FindAll("nav.pager"));
        Assert.Equal(60, cut.FindAll("ul.grid > li").Count);

        // Task 5's one stable container, its one paragraph, and the one heading: all still exactly one.
        Assert.Single(cut.FindAll("h1"));
        Assert.Single(cut.FindAll(".catalog-state"));
        Assert.Single(cut.FindAll(".catalog-state p"));
        Assert.Equal("The library holds 61 films.", cut.Find(".catalog-state p").TextContent.Trim());
    }

    /// <remarks>
    /// The pager above the tiles is the whole fix for paging leaving the viewport at the tail of the new
    /// page, so the order is asserted rather than left to the markup's shape.
    /// </remarks>
    [Fact]
    public void TheReadingOrderIsTheHeadingTheStateTheCountThePagerAndThenTheTiles()
    {
        var (cut, _) = RenderWith(61);
        var root = MarkupRules.Parse(cut.Markup);

        var order = root.QuerySelectorAll("h1, .catalog-state, p.count, nav.pager, ul.grid")
            .Select(e => e.LocalName + (e.ClassName is { Length: > 0 } c ? "." + c : ""))
            .ToList();

        Assert.Equal(
            new[] { "h1", "div.catalog-state", "p.count", "nav.pager", "ul.grid" },
            order);
    }

    /// <remarks>
    /// AC 9 through the real layout, which is the half PosterGridTests cannot reach: the text has to land
    /// in the element a screen reader is listening to, not merely in the announcer's own property. Follows
    /// LiveRegionTests -- click, advance the clock the test owns, read #status-announcer.
    /// </remarks>
    [Fact]
    public void PressingNextSpeaksThroughTheLayoutsStatusRegionAndTheOtherTwoStaySilent()
    {
        var (cut, clock) = RenderWith(61);

        // Flush the count the page announces on reaching Ready, so what is read below is this task's.
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal("The library holds 61 films.", cut.Find("#status-announcer").TextContent);

        cut.FindAll("nav.pager button").Single(b => b.TextContent.Trim() == "Next").Click();
        clock.Advance(TimeSpan.FromHours(1));

        cut.WaitForAssertion(() =>
            Assert.Equal("Page 2 of 2. Showing 61 to 61 of 61 titles.",
                         cut.Find("#status-announcer").TextContent));

        // One channel, not three. A page change is not a failure, so the alert region stays empty; and the
        // route region is empty because the route announcer is deliberately silent on a first load, which
        // a page change must not change.
        Assert.Equal(string.Empty, cut.Find("#alert-region").TextContent);
        Assert.Equal(string.Empty, cut.Find("#route-announcer").TextContent);
    }

    [Fact]
    public void PagingShowsTheNextSetOfTitlesAndThePageAdoptsTheNumberTheGridRaised()
    {
        var (cut, _) = RenderWith(61);

        Assert.Equal("Film 0001", cut.FindAll("ul.grid > li .tile-title")[0].TextContent);

        cut.FindAll("nav.pager button").Single(b => b.TextContent.Trim() == "Next").Click();

        Assert.Equal("Page 2 of 2", cut.Find("nav.pager span").TextContent);
        Assert.Equal("Film 0061", Assert.Single(cut.FindAll("ul.grid > li .tile-title")).TextContent);
    }

    /// <remarks>
    /// Both components clear the region on the way out -- the page clears the count it set, the grid clears
    /// the page sentence it set -- and an empty string is a cancellation, so two of them are harmless. What
    /// must not survive is either statement, because the announcer is a scoped singleton and the layout
    /// outlives the page: "Page 2 of 2..." sitting in the region is readable in browse mode long after the
    /// films are gone.
    /// </remarks>
    [Fact]
    public async Task NothingTheGridSaidSurvivesTheRouteAway()
    {
        var (cut, clock) = RenderWith(61);

        cut.FindAll("nav.pager button").Single(b => b.TextContent.Trim() == "Next").Click();
        clock.Advance(TimeSpan.FromHours(1));
        Assert.StartsWith("Page 2 of 2.", Spoken().StatusText);

        await DisposeComponentsAsync();
        clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(string.Empty, Spoken().StatusText);
        Assert.Equal(string.Empty, Spoken().AlertText);
    }

    /// <remarks>
    /// ONE NUMBER, ONE GROUPING, read off the page as a visitor sees it. Movies.razor built its Ready
    /// sentence with CultureInfo.InvariantCulture while PosterGrid's head follows the browser, so a de-DE
    /// visitor read "The library holds 1,456 films." two lines above "Showing 1-60 of 1.456 titles." The
    /// brief's M6 ruled that taking the culture from the browser is correct behaviour rather than a bug,
    /// and acceptance criterion 11 requires 1.456 under de-DE with a test behind it, so the invariant half
    /// was the half to drop.
    /// <para>
    /// THIS TEST IS THE POINT OF THE FIX. Every other page-sentence assertion in the suite sits at 1, 2 or
    /// 61 films and sets no culture, so dropping the argument was test-neutral in both directions: nothing
    /// went red, and nothing pinned the new behaviour either. 1,456 is the measured catalogue and the
    /// smallest total in this project that has a group separator at all, and the two sentences are
    /// asserted TOGETHER, because the defect was never in either number on its own.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("en-US", "The library holds 1,456 films.", "Showing 1–60 of 1,456 titles.")]
    [InlineData("de-DE", "The library holds 1.456 films.", "Showing 1–60 of 1.456 titles.")]
    public void TheSentenceAndTheGridHeadGroupTheSameNumberTheSameWay(
        string culture, string sentence, string head)
    {
        using var scope = new CultureScope(culture);

        var (cut, _) = RenderWith(1456);

        Assert.Equal(sentence, cut.Find(".catalog-state p").TextContent.Trim());
        Assert.Equal(head, cut.Find("p.count").TextContent.Trim());
    }

    /// <remarks>
    /// The shipped page size, pinned where it is actually set. 60 makes 25 pages of the measured 1,456
    /// records with 16 on the last, and the grid's own default is the same number so that a caller who
    /// forgets the parameter does not get a different library.
    /// </remarks>
    [Fact]
    public void ThePageHoldsSixtyTiles()
    {
        var (cut, _) = RenderWith(130);

        Assert.Equal(60, cut.FindAll("ul.grid > li").Count);
        Assert.Equal("Page 1 of 3", cut.Find("nav.pager span").TextContent);
    }
}

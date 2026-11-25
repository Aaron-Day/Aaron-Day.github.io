using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MovieLibrary.Web.Components;
using MovieLibrary.Web.Services;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// The owner's own marks on a record page: the same star the wall carries, and his score beside it.
/// </summary>
/// <remarks>
/// <b>Both kinds of record, side by side, because the two pages are supposed to be one page twice.</b>
/// SC 3.2.4 Consistent Identification is about a control that does the same thing being identified the
/// same way, and the cheapest way for that to break is a second implementation on the second page. The two
/// pages share the components, and these tests are what would notice if one of them stopped.
/// </remarks>
public class RecordPageMarksTests : BunitContext
{
    public RecordPageMarksTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    private PreferenceStore Store => Services.GetRequiredService<PreferenceStore>();

    private FakePreferenceStorage Storage => Services.GetRequiredService<FakePreferenceStorage>();

    /// <summary>Two lists the owner made in some earlier visit, as the JSON that visit would have left.</summary>
    private const string TwoLists =
        "{\"v\":1,\"lists\":[{\"id\":\"l1\",\"name\":\"Christmas\"},{\"id\":\"l2\",\"name\":\"Rainy Sunday\"}]}";

    /// <param name="document">
    /// What this browser already holds, seeded BEFORE the first render: the page reads it in
    /// OnParametersSetAsync, so a document set afterwards would arrive too late to be on the page.
    /// </param>
    private (IRenderedComponent<MovieLibrary.Web.App> Cut, ManualTimeProvider Clock) At(
        string address, string? document = null)
    {
        var clock = this.AddAnnouncer();

        if (document is not null) Storage.Json = document;

        return (this.RenderAppAt(address), clock);
    }

    /// <remarks>
    /// The section is FIRST, which is the part a sighted owner notices and the part someone navigating by
    /// heading notices most: everything below it is TMDB's words about the film and does not change.
    /// </remarks>
    [Theory]
    [InlineData(AnnouncerTestExtensions.FilmAddress)]
    [InlineData(AnnouncerTestExtensions.ShowAddress)]
    public void EveryRecordPageOffersTheStarAndTheScoreUnderOneHeadingFirst(string address)
    {
        var (cut, _) = At(address);

        Assert.Equal("Your library", cut.FindAll(".page-section h2")[0].TextContent.Trim());

        var section = cut.FindAll(".page-section")[0];

        Assert.NotNull(section.QuerySelector("button.tile-fav"));
        Assert.Equal("Your rating out of 10", section.QuerySelector("fieldset legend")!.TextContent.Trim());
    }

    /// <remarks>
    /// The star's name is the control's name wherever it appears, including on a page that is already about
    /// one title: the same words on the wall and on the page is what makes it one control to a reader, and
    /// a name that shrank to "Favourite" here would be a second control that happens to look the same.
    /// </remarks>
    [Fact]
    public void TheStarOnAFilmPageIsNamedTheWayTheWallNamesIt()
    {
        var (cut, _) = At(AnnouncerTestExtensions.FilmAddress);

        Assert.Equal("Favourite, Se7en, 1995", cut.Find("button.tile-fav").TextContent.Trim());
        Assert.Equal("false", cut.Find("button.tile-fav").GetAttribute("aria-pressed"));
    }

    [Fact]
    public async Task AStarPressedOnAFilmPageIsSavedAgainstThatFilmsKey()
    {
        var (cut, _) = At(AnnouncerTestExtensions.FilmAddress);

        await cut.Find("button.tile-fav").ClickAsync();

        Assert.True(Store.IsFavourite(TitleKind.Film, AnnouncerTestExtensions.FilmKey));
        Assert.Equal("true", cut.Find("button.tile-fav").GetAttribute("aria-pressed"));
    }

    [Fact]
    public async Task AStarPressedOnASeriesPageIsSavedAgainstThatSeriesSlug()
    {
        var (cut, _) = At(AnnouncerTestExtensions.ShowAddress);

        await cut.Find("button.tile-fav").ClickAsync();

        Assert.True(Store.IsFavourite(TitleKind.Series, AnnouncerTestExtensions.ShowSlug));
        Assert.False(Store.IsFavourite(TitleKind.Film, AnnouncerTestExtensions.ShowSlug));
    }

    [Theory]
    [InlineData(AnnouncerTestExtensions.FilmAddress)]
    [InlineData(AnnouncerTestExtensions.ShowAddress)]
    public async Task AScoreChosenOnARecordPageIsSavedForThatRecord(string address)
    {
        var (cut, _) = At(address);

        await cut.FindAll("fieldset input[type=radio]")[8].ChangeAsync(new ChangeEventArgs());

        Assert.Equal(1, Storage.Writes);
        Assert.Contains("\"rate\":7", Store.Export(), StringComparison.Ordinal);
    }

    /// <remarks>
    /// The sentence a refused save puts on a record page is the SAME constant the walls use, in the page's
    /// own state container, and spoken once through the announcer the layout owns. One cause, one sentence,
    /// wherever the owner met it.
    /// </remarks>
    [Theory]
    [InlineData(AnnouncerTestExtensions.FilmAddress, ".film-state p")]
    [InlineData(AnnouncerTestExtensions.ShowAddress, ".show-state p")]
    public async Task ARefusedSaveOnARecordPageSaysSoInBothChannels(string address, string paragraphs)
    {
        var (cut, clock) = At(address);
        Storage.Refuse = true;

        await cut.Find("button.tile-fav").ClickAsync();
        clock.Advance(Announcer.SettleDelay);

        Assert.Equal("false", cut.Find("button.tile-fav").GetAttribute("aria-pressed"));
        Assert.Contains(
            FavouriteButton.CouldNotSave,
            cut.FindAll(paragraphs).Select(p => p.TextContent.Trim()));
        Assert.Equal(FavouriteButton.CouldNotSave, cut.Find("#alert-region").TextContent);
    }

    /// <remarks>
    /// ONCE, however many marks are refused: the alert region accumulates, so an unguarded handler would
    /// hand it the same sentence twice and a reader would hear it twice. The star and the score are two
    /// different controls reporting one cause, which is exactly the case a per-control flag would miss.
    /// </remarks>
    [Fact]
    public async Task TheRefusalIsSaidOnceEvenWhenTheStarAndTheScoreAreBothRefused()
    {
        var (cut, clock) = At(AnnouncerTestExtensions.FilmAddress);
        Storage.Refuse = true;

        await cut.Find("button.tile-fav").ClickAsync();
        await cut.FindAll("fieldset input[type=radio]")[8].ChangeAsync(new ChangeEventArgs());
        clock.Advance(Announcer.SettleDelay);

        Assert.Equal(FavouriteButton.CouldNotSave, cut.Find("#alert-region").TextContent);
        Assert.Single(cut.FindAll(".film-state p"), p => p.TextContent.Trim() == FavouriteButton.CouldNotSave);
    }

    /// <remarks>
    /// A record page keeps its component instances when the router moves from one title to another, so this
    /// is the navigation that would leave one film's star -- and one film's score -- on another film's page.
    /// The name is read as well as the state, because a stale NAME is the same defect one attribute over:
    /// it would tell a screen-reader reader they were starring Se7en while looking at another film.
    /// </remarks>
    [Fact]
    public async Task MovingFromOneFilmToAnotherShowsTheSecondFilmsMarks()
    {
        var (cut, _) = At(AnnouncerTestExtensions.FilmAddress);

        await cut.Find("button.tile-fav").ClickAsync();
        Assert.Equal("true", cut.Find("button.tile-fav").GetAttribute("aria-pressed"));

        Services.GetRequiredService<NavigationManager>().NavigateTo("/movies/601-88");

        Assert.Equal("false", cut.Find("button.tile-fav").GetAttribute("aria-pressed"));
        Assert.Equal("Favourite, The Peeled Edition, 1982", cut.Find("button.tile-fav").TextContent.Trim());
        Assert.True(Store.IsFavourite(TitleKind.Film, AnnouncerTestExtensions.FilmKey));   // still starred
    }

    // ============================================================ the lists

    /// <remarks>
    /// <b>A SECOND section, under its own heading, and second in the order.</b> Membership is not a third
    /// control about this one title: a list is a set that exists across the whole library and is edited on
    /// a page of its own, so it gets a heading a reader can navigate to. Second rather than first because
    /// the star and the score are about the record in front of him and the lists are about everything else.
    /// </remarks>
    [Theory]
    [InlineData(AnnouncerTestExtensions.FilmAddress)]
    [InlineData(AnnouncerTestExtensions.ShowAddress)]
    public void EveryRecordPageOffersItsListsUnderASecondHeading(string address)
    {
        var (cut, _) = At(address, TwoLists);

        Assert.Equal("Lists", cut.FindAll(".page-section h2")[1].TextContent.Trim());

        var section = cut.FindAll(".page-section")[1];

        Assert.Equal(
            "Lists this title is in", section.QuerySelector("fieldset legend")!.TextContent.Trim());
        Assert.Equal(
            ["Christmas", "Rainy Sunday"],
            section.QuerySelectorAll("label").Select(l => l.TextContent.Trim()));
    }

    /// <remarks>
    /// With no lists the section is still there and still has its heading, because a reader who has never
    /// made a list is the reader who most needs to be told where lists are made. What it holds instead is a
    /// sentence with a link to that page -- never an empty group, which is a tab stop that leads nowhere.
    /// </remarks>
    [Theory]
    [InlineData(AnnouncerTestExtensions.FilmAddress)]
    [InlineData(AnnouncerTestExtensions.ShowAddress)]
    public void WithNoListsTheSectionIsStillThereAndPointsAtThePageWhereListsAreMade(string address)
    {
        var (cut, _) = At(address);
        var section = cut.FindAll(".page-section")[1];

        Assert.Equal("Lists", section.QuerySelector("h2")!.TextContent.Trim());
        Assert.Empty(section.QuerySelectorAll("fieldset"));
        Assert.Equal("lists", section.QuerySelector("p a")!.GetAttribute("href"));
    }

    /// <remarks>
    /// Filed under the record's own key -- the published key for a film, the slug for a series -- and under
    /// the LIST'S ID rather than its name, so that renaming a list does not rewrite every title in it.
    /// </remarks>
    [Theory]
    [InlineData(AnnouncerTestExtensions.FilmAddress, TitleKind.Film)]
    [InlineData(AnnouncerTestExtensions.ShowAddress, TitleKind.Series)]
    public async Task AMembershipTickedOnARecordPageIsSavedForThatRecord(string address, TitleKind kind)
    {
        var (cut, _) = At(address, TwoLists);

        var key = kind == TitleKind.Film
            ? AnnouncerTestExtensions.FilmKey
            : AnnouncerTestExtensions.ShowSlug;

        await cut.FindAll("fieldset input[type=checkbox]")[1].ChangeAsync(new ChangeEventArgs { Value = true });

        Assert.True(Store.IsInList(kind, key, "l2"));
        Assert.False(Store.IsInList(kind, key, "l1"));
        Assert.Contains("\"l2\"", Store.Export(), StringComparison.Ordinal);
    }

    /// <remarks>
    /// The third control reporting the one cause, and the same guard: the alert region accumulates, so a
    /// page that spoke per control would say the same sentence three times for one full browser.
    /// </remarks>
    [Fact]
    public async Task ARefusedMembershipSaysSoInBothChannelsAndOnlyOnce()
    {
        var (cut, clock) = At(AnnouncerTestExtensions.FilmAddress, TwoLists);
        Storage.Refuse = true;

        await cut.Find("button.tile-fav").ClickAsync();
        await cut.FindAll("fieldset input[type=checkbox]")[0].ChangeAsync(new ChangeEventArgs { Value = true });
        clock.Advance(Announcer.SettleDelay);

        Assert.False(cut.FindAll("fieldset input[type=checkbox]")[0].HasAttribute("checked"));
        Assert.Equal(FavouriteButton.CouldNotSave, cut.Find("#alert-region").TextContent);
        Assert.Single(cut.FindAll(".film-state p"), p => p.TextContent.Trim() == FavouriteButton.CouldNotSave);
    }
}

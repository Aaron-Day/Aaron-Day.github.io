using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MovieLibrary.Web.Components;
using MovieLibrary.Web.Services;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// The star where it actually ships: on both walls, inside the real app, under the real layout.
/// </summary>
/// <remarks>
/// <b>FavouriteButtonTests drives the control through its parameters; this file is about the wiring</b> --
/// that both walls reach it at all, that each tile's star is the star for THAT title, and that a browser
/// refusing to save reaches a sentence on screen and the one region in the app that speaks. The two walls
/// are asserted side by side on purpose: a control that differed between them would be SC 3.2.4 Consistent
/// Identification failing in the one place a reader would certainly meet it.
/// </remarks>
public class FavouritesOnTheWallsTests : BunitContext
{
    public FavouritesOnTheWallsTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    /// <summary>A published catalogue of three films, in the real wire shape.</summary>
    private const string ThreeFilms =
        "[{\"i\":807,\"k\":\"807-110\",\"t\":\"Se7en\",\"y\":1995,\"rt\":110,\"p\":\"/a.jpg\",\"g\":[],"
        + "\"ag\":\"Thriller\",\"vote\":8.4,\"dir\":[],\"cast\":[],\"mc\":1},"
        + "{\"i\":120,\"k\":\"120-178\",\"t\":\"Fellowship\",\"y\":2001,\"rt\":178,\"p\":null,\"g\":[],"
        + "\"ag\":\"Fantasy\",\"vote\":8.4,\"dir\":[],\"cast\":[],\"mc\":1},"
        + "{\"i\":0,\"k\":\"u-d2f1c6e2\",\"t\":\"An Unlisted Film\",\"y\":null,\"rt\":101,\"p\":null,"
        + "\"g\":[],\"ag\":\"Documentary\",\"vote\":0,\"dir\":[],\"cast\":[],\"mc\":0}]";

    private Announcer Spoken() => Services.GetRequiredService<Announcer>();

    private PreferenceStore Store => Services.GetRequiredService<PreferenceStore>();

    private FakePreferenceStorage Storage => Services.GetRequiredService<FakePreferenceStorage>();

    private (IRenderedComponent<MovieLibrary.Web.App> Cut, ManualTimeProvider Clock) Films()
    {
        var (clock, _) = this.AddAnnouncerAndCatalog(StubCatalog.Json(ThreeFilms));
        return (this.RenderAppAt("/movies"), clock);
    }

    private (IRenderedComponent<MovieLibrary.Web.App> Cut, ManualTimeProvider Clock) Series()
    {
        var clock = this.AddAnnouncer();
        return (this.RenderAppAt("/tv"), clock);
    }

    /// <remarks>
    /// One star per tile, and the NAMES are what make them usable: in a screen reader's controls list the
    /// name is the only thing to choose by, so three controls named "Favourite" would be three identical
    /// rows. The yearless film is in the fixture because it is the shape that would have produced a
    /// trailing comma or a "0".
    /// </remarks>
    [Fact]
    public void EveryTileOnTheFilmsWallCarriesItsOwnStarNamedForItsOwnTitle()
    {
        var (cut, _) = Films();

        Assert.Equal(cut.FindAll("ul.grid > li").Count, cut.FindAll("button.tile-fav").Count);
        Assert.Equal(
            ["Favourite, An Unlisted Film", "Favourite, Fellowship, 2001", "Favourite, Se7en, 1995"],
            cut.FindAll("button.tile-fav").Select(b => b.TextContent.Trim()));
    }

    /// <remarks>
    /// THE SAME CONTROL ON THE OTHER WALL, from the same component, which is what makes the two identical
    /// by construction rather than by inspection. The fixture series are one per confidence band, so this
    /// also says a star appears on a series the build could not match.
    /// </remarks>
    [Fact]
    public void EveryTileOnTheSeriesWallCarriesItsOwnStarToo()
    {
        var (cut, _) = Series();

        var tiles = cut.FindAll("ul.grid > li").Count;

        Assert.True(tiles > 0, "the series fixture rendered no tiles, so this rule has no subject.");
        Assert.Equal(tiles, cut.FindAll("button.tile-fav").Count);
        Assert.All(cut.FindAll("button.tile-fav"),
            b => Assert.StartsWith("Favourite, ", b.TextContent.Trim(), StringComparison.Ordinal));
    }

    /// <remarks>
    /// The press reaches the owner's document under the PUBLISHED KEY and not the TMDB id, which is 0 on
    /// the unmatched film in this very fixture -- an id-keyed document would file every unmatched record
    /// under one entry and star them together.
    /// </remarks>
    [Fact]
    public async Task AStarPressedOnTheFilmsWallIsSavedAgainstThatFilmsKey()
    {
        var (cut, _) = Films();

        await cut.FindAll("button.tile-fav")[2].ClickAsync();   // Se7en: the wall is in title order

        Assert.True(Store.IsFavourite(TitleKind.Film, "807-110"));
        Assert.Equal(1, Storage.Writes);
        Assert.Equal("true", cut.FindAll("button.tile-fav")[2].GetAttribute("aria-pressed"));
    }

    /// <remarks>
    /// A series is keyed by the SLUG the build wrote, which is the same string its own page is routed by --
    /// read off the tile's href here rather than typed again, so the fixture cannot drift away from it.
    /// </remarks>
    [Fact]
    public async Task AStarPressedOnTheSeriesWallIsSavedAgainstThatSeriesSlug()
    {
        var (cut, _) = Series();
        var href = cut.FindAll("ul.grid > li")[0].QuerySelector("a.tile-body")!.GetAttribute("href");
        var slug = href!["tv/".Length..];

        await cut.FindAll("button.tile-fav")[0].ClickAsync();

        Assert.True(Store.IsFavourite(TitleKind.Series, slug));
        Assert.False(Store.IsFavourite(TitleKind.Film, slug));
        Assert.Equal(1, Storage.Writes);
    }

    /// <remarks>
    /// <b>THE SILENT LOSS THIS WHOLE PATH EXISTS TO PREVENT.</b> Storage blocked for the site, or a full
    /// quota: the star goes back, and the page says why in its own state container -- the stable element
    /// this page already speaks from -- and in the one region in the app that speaks. Both channels, from
    /// the one constant, because a seen sentence and a spoken one that differ are two failures.
    /// </remarks>
    [Fact]
    public async Task TheFilmsWallSaysSoWhenTheBrowserRefusesToSaveAndTakesTheStarBack()
    {
        var (cut, clock) = Films();
        Storage.Refuse = true;

        await cut.FindAll("button.tile-fav")[2].ClickAsync();
        clock.Advance(Announcer.SettleDelay);

        Assert.Equal("false", cut.FindAll("button.tile-fav")[2].GetAttribute("aria-pressed"));
        Assert.Contains(
            FavouriteButton.CouldNotSave,
            cut.FindAll(".catalog-state p").Select(p => p.TextContent.Trim()));
        Assert.Equal(FavouriteButton.CouldNotSave, cut.Find("#alert-region").TextContent);
    }

    [Fact]
    public async Task TheSeriesWallSaysTheSameSentenceThroughTheSameTwoChannels()
    {
        var (cut, clock) = Series();
        Storage.Refuse = true;

        await cut.FindAll("button.tile-fav")[0].ClickAsync();
        clock.Advance(Announcer.SettleDelay);

        Assert.Contains(
            FavouriteButton.CouldNotSave,
            cut.FindAll(".tv-state p").Select(p => p.TextContent.Trim()));
        Assert.Equal(FavouriteButton.CouldNotSave, cut.Find("#alert-region").TextContent);
    }

    /// <remarks>
    /// <b>ONCE, however many stars are pressed after it.</b> The alert region ACCUMULATES -- that is what
    /// keeps two failed fetches from racing -- so an unguarded handler would hand it the same sentence
    /// three times and the reader would hear it three times, the third saying nothing the first did not.
    /// The cause is the device, not the press.
    /// </remarks>
    [Fact]
    public async Task TheRefusalIsSaidOnceHoweverManyStarsAreRefused()
    {
        var (cut, clock) = Films();
        Storage.Refuse = true;

        foreach (var star in cut.FindAll("button.tile-fav")) await star.ClickAsync();

        clock.Advance(Announcer.SettleDelay);

        Assert.Equal(FavouriteButton.CouldNotSave, cut.Find("#alert-region").TextContent);
        Assert.Single(cut.FindAll(".catalog-state p"), p => p.TextContent.Trim() == FavouriteButton.CouldNotSave);
    }

    /// <remarks>
    /// <b>THE STARS ARE RIGHT ON THE FIRST READY RENDER</b>, which is the whole reason the store is read in
    /// OnInitialized rather than awaited after the fetch. The alternative is sixty aria-pressed values
    /// flipping after the paint -- a change of state, announced by nothing, that the reader did not cause.
    /// The read is scripted as a SYNCHRONOUS one here (InProcess), which is what a browser really does.
    /// </remarks>
    [Fact]
    public void AStarredFilmIsAlreadyPressedWhenTheWallFirstPaints()
    {
        var (clock, _) = this.AddAnnouncerAndCatalog(StubCatalog.Json(ThreeFilms));
        Storage.Json = "{\"v\":1,\"films\":{\"807-110\":{\"i\":807,\"fav\":true}}}";
        Storage.InProcess = true;

        var cut = this.RenderAppAt("/movies");

        Assert.Equal(
            ["false", "false", "true"],
            cut.FindAll("button.tile-fav").Select(b => b.GetAttribute("aria-pressed")));
        Assert.Equal(0, Storage.Writes);
    }
}

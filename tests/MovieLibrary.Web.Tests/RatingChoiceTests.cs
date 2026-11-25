using Microsoft.AspNetCore.Components;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MovieLibrary.Web.Components;
using MovieLibrary.Web.Models;
using MovieLibrary.Web.Services;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// The owner's own score: twelve radios, one tab stop, and a way back to nothing.
/// </summary>
/// <remarks>
/// <b>Three shapes were refused and this file is where that stays refused.</b> A slider
/// (<c>type="range"</c>) has no visible value and asks for a precise drag, which SC 2.5.7 Dragging
/// Movements wants an alternative to; a spinner (<c>type="number"</c>) accepts "7.5" and "banana" and so
/// needs validation, an error and a region to announce it; and twelve ungrouped radios would each take
/// focus, turning one control into twelve tab stops. The assertions below are what make those shapes fail
/// a test rather than merely disagree with a comment.
/// </remarks>
public class RatingChoiceTests : BunitContext
{
    private const string Film = "807-110";
    private const int FilmId = 807;

    private readonly FakePreferenceStorage storage;

    public RatingChoiceTests() => this.storage = this.AddPreferences();

    private PreferenceStore Store => Services.GetRequiredService<PreferenceStore>();

    private IRenderedComponent<RatingChoice> Rating(
        string key = Film, TitleKind kind = TitleKind.Film, int tmdbId = FilmId,
        EventCallback refused = default) =>
        Render<RatingChoice>(p => p
            .Add(x => x.Kind, kind)
            .Add(x => x.Key, key)
            .Add(x => x.TmdbId, tmdbId)
            .Add(x => x.Refused, refused));

    private static IReadOnlyList<string> Labels(IRenderedComponent<RatingChoice> cut) =>
        cut.FindAll("label").Select(l => l.TextContent.Trim()).ToList();

    /// <remarks>
    /// The legend is the group's name and it carries the SCALE, which is why it is visible here where the
    /// facet panel's legends are not: those sit inside a disclosure whose summary already names them.
    /// </remarks>
    [Fact]
    public void TheGroupIsAFieldsetWithAVisibleLegendThatSaysWhatTheScaleIs()
    {
        var cut = Rating();

        Assert.Equal("Your rating out of 10", cut.Find("fieldset legend").TextContent.Trim());
        Assert.Empty(cut.FindAll("legend.visually-hidden"));
    }

    /// <remarks>
    /// <b>ONE TAB STOP, and the shared name is the whole of it.</b> Radios that do not share a name are
    /// not a group: each would take focus on its own, so this one control would add twelve stops to a page
    /// that already counts them (the browser gate's tab walk has a budget), and the arrow keys would do
    /// nothing. Read as "one distinct name across twelve inputs" so that a missing name cannot pass.
    /// </remarks>
    [Fact]
    public void TheTwelveRadiosShareOneNameAndSoAreOneGroupAndOneTabStop()
    {
        var radios = Rating().FindAll("input[type=radio]");

        Assert.Equal(12, radios.Count);
        Assert.Single(radios.Select(r => r.GetAttribute("name")).Distinct());
        Assert.DoesNotContain(null, radios.Select(r => r.GetAttribute("name")));
    }

    /// <remarks>
    /// The scale is TMDB's own -- the same units as the score the tile shows -- and the integers are
    /// written out so that a change to the ends of the scale, or a half-point step, fails here.
    /// </remarks>
    [Fact]
    public void TheOptionsAreNotRatedFirstAndThenEveryWholeNumberFromZeroToTen()
    {
        Assert.Equal(
            ["Not rated", "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "10"],
            Labels(Rating()));
    }

    /// <remarks>
    /// <b>"Not rated" exists because otherwise there is no way back.</b> A reader who scored something by
    /// accident could change the score but never clear it, and clearing site data is not an undo. First in
    /// the group because it is the state every title starts in, and because the arrow keys wrap -- so it is
    /// one key away from 10.
    /// </remarks>
    [Fact]
    public void AnUnratedTitleHasNotRatedChosenAndNoNumberChosen()
    {
        var cut = Rating();

        Assert.Equal("Not rated", cut.Find("input[type=radio]:checked").ParentElement!.TextContent.Trim());
        Assert.Single(cut.FindAll("input[type=radio]:checked"));
    }

    [Fact]
    public async Task ChoosingAScoreSavesItOnceAndShowsItAsChosen()
    {
        var cut = Rating();

        await cut.FindAll("input[type=radio]")[8].ChangeAsync(new ChangeEventArgs());   // Not rated, 0..10

        Assert.Equal(7, Store.RatingOf(TitleKind.Film, Film));
        Assert.Equal(1, storage.Writes);
        Assert.Equal("7", cut.Find("input[type=radio]:checked").ParentElement!.TextContent.Trim());
    }

    /// <remarks>
    /// <b>ZERO IS A SCORE.</b> The owner may think a film is worth nothing, and that is a judgement he made
    /// -- not the absence of one. Null clears, 0 records; <c>PreferenceStoreTests</c> pins the same rule
    /// from the store's side, and this is the control that would collapse the two.
    /// </remarks>
    [Fact]
    public async Task ZeroIsAScoreAndNotTheSameThingAsNotRated()
    {
        var cut = Rating();

        await cut.FindAll("input[type=radio]")[1].ChangeAsync(new ChangeEventArgs());

        Assert.Equal(0, Store.RatingOf(TitleKind.Film, Film));
        Assert.Equal("0", cut.Find("input[type=radio]:checked").ParentElement!.TextContent.Trim());
    }

    [Fact]
    public async Task ChoosingNotRatedClearsAScoreThatWasThere()
    {
        var cut = Rating();

        await cut.FindAll("input[type=radio]")[8].ChangeAsync(new ChangeEventArgs());
        await cut.FindAll("input[type=radio]")[0].ChangeAsync(new ChangeEventArgs());

        Assert.Null(Store.RatingOf(TitleKind.Film, Film));
        Assert.Equal("Not rated", cut.Find("input[type=radio]:checked").ParentElement!.TextContent.Trim());
    }

    /// <remarks>
    /// Both shapes are refused by a house rule and both are what a reader would reach for first, so their
    /// absence is asserted rather than left to a comment. A select would be a third candidate; it is not
    /// forbidden, but a group of radios shows every option at once and a select hides eleven of twelve.
    /// </remarks>
    [Fact]
    public void TheScoreIsNeverASliderAndNeverASpinner()
    {
        var cut = Rating();

        Assert.Empty(cut.FindAll("input[type=range], input[type=number]"));
    }

    /// <remarks>
    /// The saved score is on the FIRST render, for the reason <c>FavouriteButtonTests</c> gives about sixty
    /// stars: a control that read storage after its first paint would change state with nothing the reader
    /// did behind it.
    /// </remarks>
    [Fact]
    public async Task ASavedScoreIsAlreadyChosenOnTheFirstRender()
    {
        storage.Json = "{\"v\":1,\"films\":{\"807-110\":{\"i\":807,\"rate\":9}}}";
        await Store.LoadAsync();

        Assert.Equal("9", Rating().Find("input[type=radio]:checked").ParentElement!.TextContent.Trim());
    }

    /// <remarks>
    /// A record page keeps its component instances when the router moves from one title to another -- the
    /// route is one template -- so a score read once in <c>OnInitialized</c> would show the previous
    /// title's score under the new title's heading.
    /// </remarks>
    [Fact]
    public async Task TheSameInstanceGivenADifferentTitleShowsTheNewTitlesScore()
    {
        storage.Json = "{\"v\":1,\"films\":{\"807-110\":{\"i\":807,\"rate\":9}}}";
        await Store.LoadAsync();

        var cut = Rating();
        Assert.Equal("9", cut.Find("input[type=radio]:checked").ParentElement!.TextContent.Trim());

        cut.Render(p => p.Add(x => x.Key, "u-d2f1c6e2").Add(x => x.TmdbId, 0));

        Assert.Equal(
            "Not rated", cut.Find("input[type=radio]:checked").ParentElement!.TextContent.Trim());
    }

    /// <remarks>
    /// A refused write puts the group back to the score the browser actually holds and tells the page, which
    /// is the same contract the star is under: a control showing a score nothing remembers is the silent
    /// loss this path exists to prevent. The score it goes back to is the PREVIOUS one, not null -- which is
    /// why the fixture scores first and is refused second.
    /// </remarks>
    [Fact]
    public async Task ARefusedWriteTakesTheScoreBackAndTellsThePage()
    {
        var told = 0;
        var cut = Rating(refused: EventCallback.Factory.Create(this, () => told++));

        await cut.FindAll("input[type=radio]")[8].ChangeAsync(new ChangeEventArgs());
        storage.Refuse = true;

        await cut.FindAll("input[type=radio]")[3].ChangeAsync(new ChangeEventArgs());

        Assert.Equal("7", cut.Find("input[type=radio]:checked").ParentElement!.TextContent.Trim());
        Assert.Equal(7, Store.RatingOf(TitleKind.Film, Film));
        Assert.Equal(1, told);
    }

    /// <remarks>
    /// The ends of the scale come from the model rather than from this file, so a change to either end
    /// fails the option list above instead of quietly widening the group.
    /// </remarks>
    [Fact]
    public void TheScaleTheControlOffersIsTheScaleTheDocumentAllows()
    {
        Assert.Equal(0, TitlePreference.MinRating);
        Assert.Equal(10, TitlePreference.MaxRating);
        Assert.Equal(
            TitlePreference.MaxRating - TitlePreference.MinRating + 2,
            Rating().FindAll("input[type=radio]").Count);
    }
}

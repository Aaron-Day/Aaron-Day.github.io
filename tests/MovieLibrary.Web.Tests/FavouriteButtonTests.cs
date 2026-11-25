using Microsoft.AspNetCore.Components;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MovieLibrary.Web.Components;
using MovieLibrary.Web.Services;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// The star. Sixty of them sit on one page, so every rule here is a rule sixty times over.
/// </summary>
/// <remarks>
/// <b>What this file is really guarding is the pressed STATE.</b> A control whose name says what it does
/// and whose <c>aria-pressed</c> says whether it is on satisfies SC 4.1.2 Name, Role, Value; drop either
/// half and what is left reads as an ordinary button that does something unknowable. The two ways to lose
/// the state silently are both pinned below: rendering <c>aria-pressed</c> from a <c>bool</c>, which omits
/// the attribute entirely when false, and moving the state into the NAME, which then changes under a
/// reader who is looking for the control they used last time.
/// </remarks>
public class FavouriteButtonTests : BunitContext
{
    private const string Key = "807-110";
    private const int FilmId = 807;

    private readonly FakePreferenceStorage storage;

    public FavouriteButtonTests() => this.storage = this.AddPreferences();

    private PreferenceStore Store => Services.GetRequiredService<PreferenceStore>();

    private IRenderedComponent<FavouriteButton> Star(
        string key = Key, string title = "Se7en", int? year = 1995, TitleKind kind = TitleKind.Film,
        int tmdbId = FilmId, EventCallback refused = default) =>
        Render<FavouriteButton>(p => p
            .Add(x => x.Kind, kind)
            .Add(x => x.Key, key)
            .Add(x => x.TmdbId, tmdbId)
            .Add(x => x.Title, title)
            .Add(x => x.Year, year)
            .Add(x => x.Refused, refused));

    /// <remarks>
    /// A real button, because a button already answers Enter and Space, takes focus in document order and
    /// reports its own state. type="button" and not the default "submit": there is no form on either wall
    /// today, and a tile that ended up inside one would submit the page on every star.
    /// </remarks>
    [Fact]
    public void TheControlIsARealButtonAndNotAnAnchorOrACheckbox()
    {
        var button = Star().Find("button");

        Assert.Equal("button", button.GetAttribute("type"));
        Assert.Null(button.GetAttribute("role"));
        Assert.Empty(button.QuerySelectorAll("input"));
    }

    /// <remarks>
    /// <b>THE DEFECT THIS EXISTS FOR.</b> Blazor renders a bool-valued attribute by PRESENCE: true emits
    /// <c>aria-pressed</c> and false emits NOTHING. An unstarred star would then be a plain button with no
    /// state in it at all -- and unstarred is the state 1,400 of the 1,456 tiles are in. The value is read
    /// out of the DOM in both states so that a bool can never pass this.
    /// </remarks>
    [Fact]
    public async Task BothStatesAreWrittenOutAsAValueAndTheFalseOneIsNotLeftOffTheElement()
    {
        var cut = Star();

        Assert.Equal("false", cut.Find("button").GetAttribute("aria-pressed"));

        await cut.Find("button").ClickAsync();

        Assert.Equal("true", cut.Find("button").GetAttribute("aria-pressed"));
    }

    /// <remarks>
    /// The name carries the title and the year because sixty controls all named "Favourite" are
    /// indistinguishable in a screen reader's controls list, where the name is the only thing to choose by.
    /// It is REAL TEXT in a visually-hidden span and not an aria-label: three contracts on the tile forbid
    /// an author label, a title attribute and an id, and all three were the obvious way to do this.
    /// </remarks>
    [Fact]
    public void TheNameSaysWhatTheControlDoesAndWhichTitleItIsFor()
    {
        var button = Star().Find("button");

        Assert.Equal("Favourite, Se7en, 1995", button.TextContent.Trim());
        Assert.Equal("Favourite, Se7en, 1995", button.QuerySelector("span.visually-hidden")!.TextContent);
        Assert.Null(button.GetAttribute("aria-label"));
        Assert.Null(button.GetAttribute("title"));
        Assert.Null(button.GetAttribute("id"));
        Assert.Empty(button.QuerySelectorAll("[id]"));
    }

    /// <remarks>
    /// <b>The name does not move with the state</b>, and "Unfavourite" is what it would have become. That
    /// would state the state twice in two different ways, and it would break SC 2.5.3 Label in Name for
    /// voice control: "click Favourite" has to reach this control whether or not the title is starred.
    /// </remarks>
    [Fact]
    public async Task TheNameIsTheSameWordsStarredAndUnstarred()
    {
        var cut = Star();
        var before = cut.Find("button").TextContent.Trim();

        await cut.Find("button").ClickAsync();

        Assert.Equal(before, cut.Find("button").TextContent.Trim());
    }

    /// <remarks>
    /// A record with no year -- thirty of the published films have none -- is named by its title alone
    /// rather than by "Favourite, An Unlisted Film, 0" or a stray trailing comma.
    /// </remarks>
    [Fact]
    public void ATitleWithNoYearIsNamedByItsTitleAlone()
    {
        var cut = Star(key: "u-d2f1c6e2", title: "An Unlisted Film", year: null, tmdbId: 0);

        Assert.Equal("Favourite, An Unlisted Film", cut.Find("button").TextContent.Trim());
    }

    /// <remarks>
    /// A YEAR IS AN IDENTIFIER AND IS NEVER GROUPED. The gate runs under the invariant culture while this
    /// box is en-US, and a German visitor's browser would make a culture-formatted year "1.995" -- so the
    /// three cultures are the three shapes that matter, exactly as <c>ShowTextTests</c> reads them for a
    /// season number.
    /// </remarks>
    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("")]
    public void TheYearInTheNameIsNeverGroupedWhateverTheCultureIs(string culture)
    {
        using var scope = new CultureScope(culture);

        Assert.Equal("Favourite, Se7en, 1995", Star().Find("button").TextContent.Trim());
    }

    /// <remarks>
    /// The icon is hidden so the name is read once rather than twice, and <c>focusable="false"</c> keeps an
    /// SVG out of the tab order in the browsers that once put it there. One path and not two: the hollow
    /// star and the solid star are the same geometry with fill and stroke swapped by the stylesheet, so the
    /// two states cannot drift into different shapes.
    /// </remarks>
    [Fact]
    public void TheIconIsHiddenFromAssistiveTechnologyAndTakesNoFocus()
    {
        var svg = Star().Find("svg");

        Assert.Equal("true", svg.GetAttribute("aria-hidden"));
        Assert.Equal("false", svg.GetAttribute("focusable"));
        Assert.Single(svg.QuerySelectorAll("path"));
        Assert.Equal(string.Empty, svg.TextContent.Trim());
    }

    [Fact]
    public async Task PressingItStarsTheTitleAndSavesItOnce()
    {
        await Star().Find("button").ClickAsync();

        Assert.True(Store.IsFavourite(TitleKind.Film, Key));
        Assert.Equal(1, storage.Writes);
    }

    [Fact]
    public async Task PressingItAgainUnstarsTheTitle()
    {
        var cut = Star();

        await cut.Find("button").ClickAsync();
        await cut.Find("button").ClickAsync();

        Assert.False(Store.IsFavourite(TitleKind.Film, Key));
        Assert.Equal("false", cut.Find("button").GetAttribute("aria-pressed"));
    }

    /// <remarks>
    /// The id is saved beside the key because a published key is NOT permanent -- <c>RecordKey</c>'s own
    /// remarks say so and say what to do about it -- and the recovery walk is worth nothing if the entry
    /// holds no id to recover by.
    /// </remarks>
    [Fact]
    public async Task TheTmdbIdIsSavedBesideTheKeySoAReKeyedRecordCanBeFoundAgain()
    {
        await Star().Find("button").ClickAsync();

        Assert.Equal(FilmId, Store.For(TitleKind.Film, Key).TmdbId);
    }

    /// <remarks>
    /// The two halves of the document are separate maps, so a series whose slug happened to equal a film's
    /// key would still be its own entry. Read from the control's side as well as the store's, because the
    /// control is what chooses which half.
    /// </remarks>
    [Fact]
    public async Task ASeriesIsFiledUnderItsOwnHalfOfTheDocument()
    {
        var cut = Star(key: "the-bureau", title: "The Bureau", year: 2015, kind: TitleKind.Series, tmdbId: 61622);

        await cut.Find("button").ClickAsync();

        Assert.True(Store.IsFavourite(TitleKind.Series, "the-bureau"));
        Assert.False(Store.IsFavourite(TitleKind.Film, "the-bureau"));
    }

    /// <summary>A document this browser already holds, with the one film starred.</summary>
    private const string Starred = "{\"v\":1,\"films\":{\"807-110\":{\"i\":807,\"fav\":true}}}";

    /// <remarks>
    /// <b>The saved state is on the FIRST render.</b> A control that read storage after its first paint
    /// would flip sixty <c>aria-pressed</c> values with nobody having pressed anything -- a change of state,
    /// announced by nothing, that the reader did not cause. The PAGE is what loads the store before its
    /// first render; what this pins is that the control reads from it rather than starting at false and
    /// catching up.
    /// </remarks>
    [Fact]
    public async Task AStarredTitleIsAlreadyPressedOnTheFirstRender()
    {
        storage.Json = Starred;
        await Store.LoadAsync();

        Assert.Equal("true", Star().Find("button").GetAttribute("aria-pressed"));
    }

    /// <remarks>
    /// <b>The tile is recycled.</b> <c>PosterGrid</c> keys its items, so paging or filtering hands THIS
    /// instance a different film -- and a state read once in <c>OnInitialized</c> would leave the previous
    /// film's star on the new film's tile, which is the worst defect available here: it would say the owner
    /// marked something he did not.
    /// </remarks>
    [Fact]
    public async Task TheSameInstanceGivenADifferentTitleShowsTheNewTitlesState()
    {
        storage.Json = Starred;
        await Store.LoadAsync();

        var cut = Star();
        Assert.Equal("true", cut.Find("button").GetAttribute("aria-pressed"));

        cut.Render(p => p
            .Add(x => x.Key, "u-d2f1c6e2")
            .Add(x => x.Title, "An Unlisted Film")
            .Add(x => x.Year, null));

        Assert.Equal("false", cut.Find("button").GetAttribute("aria-pressed"));
    }

    /// <remarks>
    /// <b>A browser that refuses the write must not leave a star on screen.</b> Storage blocked for the
    /// site, or a full quota, and the owner is looking at a mark for something nothing remembers -- the
    /// silent loss this whole path exists to prevent. Three things have to happen together: the control
    /// goes back, the store goes back, and the page is told so that it can say why.
    /// </remarks>
    [Fact]
    public async Task ARefusedWriteTakesTheStarBackAndTellsThePage()
    {
        storage.Refuse = true;
        var told = 0;
        var cut = Star(refused: EventCallback.Factory.Create(this, () => told++));

        await cut.Find("button").ClickAsync();

        Assert.Equal("false", cut.Find("button").GetAttribute("aria-pressed"));
        Assert.False(Store.IsFavourite(TitleKind.Film, Key));
        Assert.Equal(1, told);
    }

    /// <remarks>
    /// The sentence names the storage as per-device in the same breath as the failure: a reader told a
    /// change was not saved needs to know that nothing on a server was lost either, and this app has no
    /// server. Pinned as a value because every other control that writes the same document reports through
    /// it, and a reader should meet one sentence for one cause rather than three paraphrases of it.
    /// </remarks>
    [Fact]
    public void TheRefusalSentenceSaysWhatFailedAndWhereTheStateLives()
    {
        Assert.Equal(
            "This browser would not save the change. Your favourites and ratings are stored in this "
            + "browser only, and it is full or has storage turned off.",
            FavouriteButton.CouldNotSave);
    }
}

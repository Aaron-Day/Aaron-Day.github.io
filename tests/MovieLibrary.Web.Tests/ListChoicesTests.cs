using Microsoft.AspNetCore.Components;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MovieLibrary.Web.Components;
using MovieLibrary.Web.Services;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// Which of the owner's lists a title is in: a checkbox each, or a sentence when he has none.
/// </summary>
/// <remarks>
/// <b>Two shapes were refused and this file is where that stays refused.</b> A
/// <c>&lt;select multiple&gt;</c> is the control the platform makes hardest to use -- a scroll inside a
/// scroll on a touch screen, and Ctrl-click or Ctrl-arrow with a keyboard, where a plain click silently
/// drops every other choice -- and an EMPTY FIELDSET is a group a reader can tab into and do nothing with.
/// The assertions below are what make either shape fail a test rather than merely disagree with a comment.
/// <para>
/// The document is seeded as JSON a previous visit would have written, because that is what the real
/// control reads: a test that called the store's own CreateListAsync would be asserting over a document
/// this build happened to write in the same session, where the risk is a document written months ago.
/// </para>
/// </remarks>
public class ListChoicesTests : BunitContext
{
    private const string Film = "807-110";
    private const int FilmId = 807;
    private const string Series = "the-bureau";

    /// <summary>Two lists, and the film is in the second of them.</summary>
    private const string TwoLists =
        "{\"v\":1,\"lists\":[{\"id\":\"l1\",\"name\":\"Christmas\"},{\"id\":\"l2\",\"name\":\"Rainy Sunday\"}],"
        + "\"films\":{\"807-110\":{\"i\":807,\"lists\":[\"l2\"]}}}";

    private readonly FakePreferenceStorage storage;

    public ListChoicesTests() => this.storage = this.AddPreferences();

    private PreferenceStore Store => Services.GetRequiredService<PreferenceStore>();

    private async Task<IRenderedComponent<ListChoices>> ChoicesAsync(
        string key = Film, TitleKind kind = TitleKind.Film, int tmdbId = FilmId,
        EventCallback refused = default)
    {
        await Store.LoadAsync();

        return Render<ListChoices>(p => p
            .Add(x => x.Kind, kind)
            .Add(x => x.Key, key)
            .Add(x => x.TmdbId, tmdbId)
            .Add(x => x.Refused, refused));
    }

    private static IReadOnlyList<string> Options(IRenderedComponent<ListChoices> cut) =>
        cut.FindAll("label").Select(l => l.TextContent.Trim()).ToList();

    // ============================================================ with no lists

    /// <remarks>
    /// <b>No group at all, and that is the decision this test holds.</b> An empty fieldset is a tab stop
    /// that leads to nothing: a reader arrives in a group, hears its name, and finds no options. The
    /// sentence takes its place and names the page where lists are made, so the reader who wanted a list
    /// learns where to get one instead of meeting a dead control.
    /// </remarks>
    [Fact]
    public async Task WithNoListsThereIsNoGroupAndASentenceSaysWhereListsAreMade()
    {
        var cut = await ChoicesAsync();

        Assert.Empty(cut.FindAll("fieldset"));
        Assert.Empty(cut.FindAll("input"));

        var sentence = cut.Find("p");
        var link = sentence.QuerySelector("a")!;

        Assert.Equal("lists", link.GetAttribute("href"));

        // IN PROSE, not a bare link: SC 1.4.1 and failure F73 are about a link a reader picks out of
        // surrounding text, and app.css underlines every a for that reason. A link that was the whole
        // paragraph would be distinguished by its position instead, and the underline rule would stop
        // covering anything here.
        Assert.True(sentence.TextContent.Trim().Length > link.TextContent.Trim().Length + 10,
            "The no-lists sentence is now the link and nothing else: " + sentence.TextContent.Trim());
    }

    // ============================================================ with lists

    /// <remarks>
    /// The legend is the group's accessible name and a screen reader reads it with every option, so it says
    /// what the SET is for rather than repeating the heading. Hidden, because the page's own h2 above it
    /// says Lists in the same words and two visible labels for one group is repetition.
    /// </remarks>
    [Fact]
    public async Task TheGroupIsAFieldsetWhoseLegendSaysWhatTheSetIsFor()
    {
        storage.Json = TwoLists;
        var cut = await ChoicesAsync();

        var legend = cut.Find("fieldset legend");

        Assert.Equal("Lists this title is in", legend.TextContent.Trim());
        Assert.Contains("visually-hidden", legend.GetAttribute("class") ?? string.Empty, StringComparison.Ordinal);
    }

    /// <remarks>
    /// One checkbox per list, in the order the owner made them, each named by the list's own name. NEVER a
    /// multi-select: see the class remarks. The names are the owner's words, so they are read off the
    /// seeded document rather than written out as expectations of what a list is called.
    /// </remarks>
    [Fact]
    public async Task EveryListIsOneCheckboxNamedByItsOwnNameAndInTheOrderTheyWereMade()
    {
        storage.Json = TwoLists;
        var cut = await ChoicesAsync();

        Assert.Equal(["Christmas", "Rainy Sunday"], Options(cut));
        Assert.Equal(2, cut.FindAll("input[type=checkbox]").Count);
        Assert.Empty(cut.FindAll("select"));
        Assert.Empty(cut.FindAll("input[type=radio]"));
    }

    /// <remarks>
    /// <b>The house pattern, and the reason it is pinned per control rather than once.</b> Each input is
    /// INSIDE its own label, so no id and no for= appear -- the facet panel's checkboxes are built the same
    /// way. Generated ids would be the one thing on a record page that has to stay unique across a cast
    /// list and a facts table, and the failure of a clash is silent: the label points at the wrong control
    /// and a reader clicking the words gets the other list.
    /// </remarks>
    [Fact]
    public async Task EveryCheckboxIsWrappedByItsOwnLabelWithNoIdAndNoForAttribute()
    {
        storage.Json = TwoLists;
        var cut = await ChoicesAsync();

        var labels = cut.FindAll("label");
        Assert.Equal(2, labels.Count);

        foreach (var label in labels)
        {
            Assert.Null(label.GetAttribute("for"));
            Assert.NotNull(label.QuerySelector("input[type=checkbox]"));
        }

        foreach (var box in cut.FindAll("input[type=checkbox]"))
            Assert.Null(box.GetAttribute("id"));
    }

    /// <remarks>
    /// The saved memberships are on the FIRST render, for the reason the star's own tests give: a control
    /// that read storage after its first paint would change state with nothing the reader did behind it.
    /// </remarks>
    [Fact]
    public async Task TheSavedMembershipsAreAlreadyCheckedOnTheFirstRender()
    {
        storage.Json = TwoLists;
        var cut = await ChoicesAsync();

        var boxes = cut.FindAll("input[type=checkbox]");

        Assert.False(boxes[0].HasAttribute("checked"));
        Assert.True(boxes[1].HasAttribute("checked"));
    }

    /// <remarks>
    /// A record page keeps its component instances when the router moves from one title to another, so
    /// memberships read once in <c>OnInitialized</c> would show the previous title's under the new title's
    /// heading. The second title is in NEITHER list, so the stale state would be visible as a checked box.
    /// </remarks>
    [Fact]
    public async Task TheSameInstanceGivenADifferentTitleShowsTheNewTitlesMemberships()
    {
        storage.Json = TwoLists;
        var cut = await ChoicesAsync();

        Assert.True(cut.FindAll("input[type=checkbox]")[1].HasAttribute("checked"));

        cut.Render(p => p.Add(x => x.Key, "u-d2f1c6e2").Add(x => x.TmdbId, 0));

        Assert.DoesNotContain(cut.FindAll("input[type=checkbox]"), b => b.HasAttribute("checked"));
    }

    // ============================================================ what reaches storage

    /// <remarks>
    /// The write is what separates a working control from one that keeps the change in RAM and loses it on
    /// reload, so it is asserted in the document the browser holds and not in the component's own state.
    /// The id and never the NAME: renaming a list must not rewrite every entry that mentions it.
    /// </remarks>
    [Fact]
    public async Task CheckingABoxFilesTheTitleUnderThatListsIdAndSavesOnce()
    {
        storage.Json = TwoLists;
        var cut = await ChoicesAsync();

        var before = storage.Writes;
        await cut.FindAll("input[type=checkbox]")[0].ChangeAsync(new ChangeEventArgs { Value = true });

        Assert.Equal(before + 1, storage.Writes);
        Assert.Contains("\"l1\"", storage.Json!, StringComparison.Ordinal);
        Assert.True(Store.IsInList(TitleKind.Film, Film, "l1"));
        Assert.True(Store.IsInList(TitleKind.Film, Film, "l2"));
    }

    /// <remarks>
    /// Taking a title out is the other half, and it is the half that a set-only implementation passes
    /// without doing: the entry has to come back without that id while keeping the other one.
    /// </remarks>
    [Fact]
    public async Task UncheckingABoxTakesTheTitleOutOfThatListAndLeavesTheOthers()
    {
        storage.Json = TwoLists;
        var cut = await ChoicesAsync();

        await cut.FindAll("input[type=checkbox]")[0].ChangeAsync(new ChangeEventArgs { Value = true });
        await cut.FindAll("input[type=checkbox]")[1].ChangeAsync(new ChangeEventArgs { Value = false });

        Assert.True(Store.IsInList(TitleKind.Film, Film, "l1"));
        Assert.False(Store.IsInList(TitleKind.Film, Film, "l2"));
    }

    /// <remarks>
    /// A series is filed in the series half of the document, which is the collision this store's two maps
    /// exist to prevent: nothing stops a slug reading like a published key, and one map would move a
    /// membership from a film to a series without a sound.
    /// </remarks>
    [Fact]
    public async Task ASeriesIsFiledInItsOwnHalfOfTheDocument()
    {
        storage.Json = TwoLists;
        var cut = await ChoicesAsync(Series, TitleKind.Series, 60000);

        await cut.FindAll("input[type=checkbox]")[0].ChangeAsync(new ChangeEventArgs { Value = true });

        Assert.True(Store.IsInList(TitleKind.Series, Series, "l1"));
        Assert.False(Store.IsInList(TitleKind.Film, Series, "l1"));

        // The TMDB id is saved beside the key, as RecordKey's own remarks require: a published key is a
        // function of the id and the runtime, so it MOVES, and without the id the membership would become a
        // membership of nothing with nothing on screen saying so.
        Assert.Contains("\"i\":60000", storage.Json!, StringComparison.Ordinal);
    }

    /// <remarks>
    /// A refused write puts the box back and tells the page, which is the contract both other marks are
    /// under: a checked box the browser does not remember is the silent loss this path exists to prevent.
    /// Both directions, because a revert that only handled "was unchecked" would leave a removal showing as
    /// gone while the document still held it.
    /// </remarks>
    [Fact]
    public async Task ARefusedWriteTakesTheBoxBackAndTellsThePage()
    {
        var told = 0;
        storage.Json = TwoLists;

        var cut = await ChoicesAsync(refused: EventCallback.Factory.Create(this, () => told++));
        storage.Refuse = true;

        await cut.FindAll("input[type=checkbox]")[0].ChangeAsync(new ChangeEventArgs { Value = true });

        Assert.Equal(1, told);
        Assert.False(cut.FindAll("input[type=checkbox]")[0].HasAttribute("checked"));

        await cut.FindAll("input[type=checkbox]")[1].ChangeAsync(new ChangeEventArgs { Value = false });

        Assert.Equal(2, told);
        Assert.True(cut.FindAll("input[type=checkbox]")[1].HasAttribute("checked"));
    }

    /// <remarks>
    /// <b>It speaks nothing.</b> A checkbox reports its own new state, so an utterance would be the same
    /// fact a second time -- and on a record page the one region is already carrying the heading, the route
    /// announcement and the refusal sentence. <c>SourceContractTests</c> scans the component for an
    /// injected announcer; this is the same rule from the rendered side, where an announcer resolved
    /// through DI and called would show up as text in a region.
    /// </remarks>
    [Fact]
    public async Task NothingIsAnnouncedWhenAMembershipChanges()
    {
        storage.Json = TwoLists;

        // The announcer alone, and NOT AddAnnouncer: that helper registers a store of its own over this
        // test's, so the document seeded above would not be the one the control reads and the group would
        // render empty -- a test that passed while asserting nothing.
        Services.AddSingleton(new Announcer(new ManualTimeProvider()));

        var cut = await ChoicesAsync();
        await cut.FindAll("input[type=checkbox]")[0].ChangeAsync(new ChangeEventArgs { Value = true });

        var announcer = Services.GetRequiredService<Announcer>();

        Assert.Equal(string.Empty, announcer.StatusText);
        Assert.Equal(string.Empty, announcer.AlertText);
        Assert.Equal(string.Empty, announcer.RouteText);
    }
}

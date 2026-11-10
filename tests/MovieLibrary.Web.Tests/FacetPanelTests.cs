using AngleSharp.Dom;
using Bunit;
using MovieLibrary.Core.Matching;
using MovieLibrary.Web.Components;
using MovieLibrary.Web.Models;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// The panel on its own: the controls it renders, how each is named, what each raises. The page tests prove the
/// wiring and the filtering; this file is about the markup contract of the controls themselves, which is the
/// half the proven rules in <see cref="MarkupRules"/> can say something about.
/// </summary>
public class FacetPanelTests : BunitContext
{
    private static readonly GenreOption[] Genres =
        [new(28, "Action"), new(35, "Comedy"), new(18, "Drama")];

    private readonly List<FilterState> raised = [];
    private readonly List<string> typed = [];

    public FacetPanelTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    private IRenderedComponent<FacetPanel> Panel(
        FilterState? state = null, string typedQuery = "", IReadOnlyList<GenreOption>? genres = null,
        IReadOnlyList<int>? decades = null, bool unknownYear = true) =>
        Render<FacetPanel>(p => p
            .Add(x => x.State, state ?? FilterState.Default)
            .Add(x => x.TypedQuery, typedQuery)
            .Add(x => x.Genres, genres ?? Genres)
            .Add(x => x.Decades, decades ?? [2000, 1990, 1980])
            .Add(x => x.HasUnknownYear, unknownYear)
            .Add(x => x.QueryInput, (string text) => typed.Add(text))
            .Add(x => x.StateChanged, (FilterState next) => raised.Add(next)));

    private static IElement Root(IRenderedComponent<FacetPanel> cut) => MarkupRules.Parse(cut.Markup);

    private static IElement Group(IRenderedComponent<FacetPanel> cut, string legend) =>
        cut.FindAll("fieldset").Single(f => f.QuerySelector("legend")!.TextContent.Trim() == legend);

    private static string[] Labels(IElement group) =>
        group.QuerySelectorAll("label").Select(l => l.TextContent.Trim()).ToArray();

    // ================================================================ the search box

    [Fact]
    public void TheSearchBoxIsALabelledSearchInputInsideTheSearchLandmark()
    {
        var cut = Panel();

        var input = cut.Find("search input#q");
        Assert.Equal("search", input.GetAttribute("type"));
        Assert.Equal("search", input.GetAttribute("enterkeyhint"));
        Assert.Equal("Search titles and directors", cut.Find("label[for=q]").TextContent.Trim());
        Assert.Equal("Search titles and directors", MarkupRules.AccessibleName(Root(cut).QuerySelector("#q")!));
    }

    /// <remarks>A placeholder is not a label: SC 1.3.1 and 3.3.2, and it is gone the moment the visitor types.</remarks>
    [Fact]
    public void NoControlReliesOnAPlaceholderForItsName()
    {
        var cut = Panel();

        Assert.Empty(cut.FindAll("[placeholder]"));
    }

    [Fact]
    public void ThereIsNoFormBecauseThereIsNothingForEnterToSubmit()
    {
        Assert.Empty(Panel().FindAll("form"));
    }

    /// <remarks>
    /// The box shows what has been TYPED, which trails the applied query by the filter debounce. Rendering the
    /// applied one would hand the next keystroke the page's older text, and the caret goes with it.
    /// </remarks>
    [Fact]
    public void TheSearchBoxShowsWhatWasTypedAndNotWhatHasBeenApplied()
    {
        var cut = Panel(state: FilterState.Default with { Query = "ab" }, typedQuery: "abcd");

        Assert.Equal("abcd", cut.Find("#q").GetAttribute("value"));
    }

    [Fact]
    public void EveryKeystrokeIsRaisedWithTheWholeText()
    {
        var cut = Panel();

        cut.Find("#q").Input("a");
        cut.Find("#q").Input("ab");

        Assert.Equal(new[] { "a", "ab" }, typed);
        Assert.Empty(raised);   // typing is not a facet change: the page debounces it
    }

    // ================================================================ confidence

    [Fact]
    public void ConfidenceIsOneRadioGroupWithTheThreeChoicesInOrder()
    {
        var group = Group(Panel(), "Match confidence");

        Assert.Equal(new[] { "All titles", "Uncertain matches", "Not matched" }, Labels(group));

        var radios = group.QuerySelectorAll("input").ToList();
        Assert.All(radios, r => Assert.Equal("radio", r.GetAttribute("type")));
        Assert.Single(radios.Select(r => r.GetAttribute("name")).Distinct());
    }

    [Theory]
    [InlineData(null, "All titles")]
    [InlineData(Confidence.Uncertain, "Uncertain matches")]
    [InlineData(Confidence.Unmatched, "Not matched")]
    public void ExactlyTheChosenBandIsChecked(Confidence? band, string expected)
    {
        var group = Group(Panel(FilterState.Default with { Band = band }), "Match confidence");

        var checkedLabels = group.QuerySelectorAll("input")
            .Where(r => r.HasAttribute("checked")).Select(r => r.ParentElement!.TextContent.Trim());

        Assert.Equal(new[] { expected }, checkedLabels);
    }

    [Fact]
    public void ChoosingABandRaisesTheStateWithThatBand()
    {
        var cut = Panel();

        cut.FindAll("input[name=conf]")[2].Change(true);
        cut.FindAll("input[name=conf]")[1].Change(true);
        cut.FindAll("input[name=conf]")[0].Change(true);

        Assert.Equal(new Confidence?[] { Confidence.Unmatched, Confidence.Uncertain, null },
                     raised.Select(s => s.Band));
    }

    // ================================================================ genre

    [Fact]
    public void GenreIsACheckboxGroupNamedByTheMetasNamesAndNeverByTheirIds()
    {
        var group = Group(Panel(), "Genre");

        Assert.Equal(new[] { "Action", "Comedy", "Drama" }, Labels(group));
        Assert.All(group.QuerySelectorAll("input"), c => Assert.Equal("checkbox", c.GetAttribute("type")));
        Assert.DoesNotMatch(@"\b(28|35|18)\b", group.TextContent);
    }

    /// <remarks>
    /// R-C's empty-map case. With no names there is no genre control at all: not a bare id, not an empty
    /// fieldset with a dangling legend, and not an error -- and the other facets are untouched.
    /// </remarks>
    [Fact]
    public void WithNoGenreNamesThereIsNoGenreFieldsetAtAllAndTheRestStillRender()
    {
        var cut = Panel(genres: []);

        Assert.DoesNotContain(cut.FindAll("legend"), l => l.TextContent.Trim() == "Genre");
        Assert.Equal(new[] { "Match confidence", "Decade" },
                     cut.FindAll("fieldset").Select(f => f.QuerySelector("legend")!.TextContent.Trim()));
        Assert.All(cut.FindAll("fieldset"), f => Assert.NotEmpty(f.QuerySelectorAll("input")));
        Assert.NotNull(cut.Find("#q"));
        Assert.NotNull(cut.Find("#sort"));
    }

    [Fact]
    public void OnlyTheChosenGenresAreChecked()
    {
        var group = Group(Panel(FilterState.Default.WithGenre(35, true).WithGenre(18, true)), "Genre");

        var checkedLabels = group.QuerySelectorAll("input")
            .Where(c => c.HasAttribute("checked")).Select(c => c.ParentElement!.TextContent.Trim());

        Assert.Equal(new[] { "Comedy", "Drama" }, checkedLabels);
    }

    [Fact]
    public void TickingAndUntickingAGenreRaisesTheStateWithAndWithoutIt()
    {
        var cut = Panel(FilterState.Default.WithGenre(18, true));

        cut.FindAll("fieldset").Single(f => f.QuerySelector("legend")!.TextContent == "Genre")
            .QuerySelectorAll("input")[1].Change(true);   // Comedy
        Assert.Equal(new[] { 18, 35 }, raised[^1].Genres);

        cut.FindAll("fieldset").Single(f => f.QuerySelector("legend")!.TextContent == "Genre")
            .QuerySelectorAll("input")[2].Change(false);  // Drama, which was on
        Assert.Empty(raised[^1].Genres);
    }

    // ================================================================ decade

    [Fact]
    public void EachDecadeHasACheckboxLabelledWithItsYearsAndNoGroupSeparator()
    {
        var group = Group(Panel(), "Decade");

        Assert.Equal(new[] { "2000s", "1990s", "1980s", "Year unknown" }, Labels(group));
    }

    [Fact]
    public void YearUnknownIsOnlyOfferedWhenAFilmHasNoYear()
    {
        Assert.DoesNotContain("Year unknown", Labels(Group(Panel(unknownYear: false), "Decade")));
    }

    [Fact]
    public void WithNothingToChooseThereIsNoDecadeFieldsetEither()
    {
        var cut = Panel(decades: [], unknownYear: false);

        Assert.DoesNotContain(cut.FindAll("legend"), l => l.TextContent.Trim() == "Decade");
    }

    [Fact]
    public void ADecadeAndYearUnknownRaiseTheirOwnChoices()
    {
        var cut = Panel();

        cut.FindAll("fieldset").Single(f => f.QuerySelector("legend")!.TextContent == "Decade")
            .QuerySelectorAll("input")[1].Change(true);                  // 1990s
        Assert.Equal(new[] { 1990 }, raised[^1].Decades);
        Assert.False(raised[^1].YearUnknown);

        cut.FindAll("fieldset").Single(f => f.QuerySelector("legend")!.TextContent == "Decade")
            .QuerySelectorAll("input")[3].Change(true);                  // Year unknown
        Assert.True(raised[^1].YearUnknown);
    }

    // ================================================================ sort

    [Fact]
    public void SortIsASelectWithAVisibleLabelAndThreeOrders()
    {
        var cut = Panel();

        Assert.Equal("Sort by", cut.Find("label[for=sort]").TextContent.Trim());
        Assert.Equal("Sort by", MarkupRules.AccessibleName(Root(cut).QuerySelector("#sort")!));
        Assert.False(cut.Find("#sort").HasAttribute("multiple"));

        var options = cut.FindAll("#sort option").Select(o => (o.GetAttribute("value")!, o.TextContent.Trim()));
        Assert.Equal(new[] { ("title", "Title"), ("year", "Year, newest first"), ("runtime", "Runtime, longest first") },
                     options);
    }

    [Theory]
    [InlineData(SortOrder.Title, "title")]
    [InlineData(SortOrder.Year, "year")]
    [InlineData(SortOrder.Runtime, "runtime")]
    public void TheSelectShowsTheSortInForce(SortOrder sort, string selected)
    {
        var cut = Panel(FilterState.Default with { Sort = sort });

        Assert.Equal(selected, cut.Find("#sort").GetAttribute("value") ?? cut.FindAll("#sort option")
            .Single(o => o.HasAttribute("selected")).GetAttribute("value"));
    }

    [Fact]
    public void ChoosingASortRaisesTheStateWithThatSort()
    {
        var cut = Panel();

        cut.Find("#sort").Change("runtime");
        cut.Find("#sort").Change("year");

        Assert.Equal(new[] { SortOrder.Runtime, SortOrder.Year }, raised.Select(s => s.Sort));
    }

    // ================================================================ what the panel must not contain

    /// <remarks>
    /// R-A, as a contract on the rendered markup. Every control is native. Nothing here is a button wearing a
    /// pressed state, a multiple select or a slider (a two-handle slider fails SC 2.5.7 Dragging Movements, new
    /// in WCAG 2.2), and nothing speaks: the count goes through the announcer, once.
    /// </remarks>
    [Fact]
    public void EveryControlIsNativeAndThePanelDeclaresNoLiveRegion()
    {
        var cut = Panel();

        Assert.Empty(cut.FindAll("[aria-pressed]"));
        Assert.Empty(cut.FindAll("select[multiple]"));
        Assert.Empty(cut.FindAll("input[type=range]"));
        Assert.Empty(cut.FindAll("[role=button]"));
        Assert.Empty(cut.FindAll("button"));
        Assert.Empty(cut.FindAll("[aria-live], [role=status], [role=alert], [role=log]"));
    }

    [Fact]
    public void EveryControlHasAnAccessibleNameAndNoneNestsAnother()
    {
        var root = Root(Panel());

        var controls = MarkupRules.InteractiveElements(root);

        // T1: the panel offers a search box, three radios, three genres, four decade boxes and a select.
        Assert.Equal(1 + 3 + 3 + 4 + 1, controls.Count);
        Assert.Empty(MarkupRules.EveryInteractiveElementHasAnAccessibleName(root));
        Assert.Empty(MarkupRules.NoFocusableWidgetIsNestedInsideAnother(root));
        Assert.Empty(MarkupRules.EveryIdReferenceResolves(root));
        Assert.Equal(2, MarkupRules.FailableIdReferences(root).Count);   // label[for=q] and label[for=sort]
    }

    [Fact]
    public void EveryFieldsetHasALegend()
    {
        var cut = Panel();

        Assert.All(cut.FindAll("fieldset"), f => Assert.NotNull(f.QuerySelector(":scope > legend")));
        Assert.Equal(3, cut.FindAll("fieldset").Count);
    }

    // ================================================================ focus

    [Fact]
    public async Task FocusSearchAsyncFocusesTheSearchBox()
    {
        var cut = Panel();

        await cut.InvokeAsync(() => cut.Instance.FocusSearchAsync());

        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Fact]
    public async Task ABrokenFocusCallIsSwallowedBecauseAnEscapeWouldRevealTheErrorBar()
    {
        JSInterop.SetupVoid("Blazor._internal.domWrapper.focus", _ => true)
            .SetException(new Microsoft.JSInterop.JSException("the element has gone"));
        var cut = Panel();

        await cut.InvokeAsync(() => cut.Instance.FocusSearchAsync());   // does not throw
    }
}

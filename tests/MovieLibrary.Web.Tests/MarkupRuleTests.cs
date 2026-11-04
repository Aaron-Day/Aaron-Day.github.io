using AngleSharp.Dom;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// The markup rules driven against markup written here by hand, good and bad. This file is the reason
/// MarkupContractTests is worth anything.
/// <para>
/// The app has two pages, one image and four controls, so most of these rules loop over an element set
/// that is empty or has one member in it. A loop over an empty collection PASSES, and in the output it
/// reads exactly like a loop that checked something -- and the usual proof, breaking an assertion to watch
/// it fail, cannot detect the difference, because the mutation passes too and the exercise then certifies
/// a vacuous test as proven. Separating the rule from the discovery is what fixes that: here the rule
/// meets markup chosen to exercise it, so its logic is proven whatever the app currently contains.
/// </para>
/// <para>
/// The negative cases matter as much as the positive ones. Every rule below has at least one test for
/// correct markup it must NOT flag, because the failure mode of an accessibility gate is not missing a
/// defect: it is crying wolf once too often and being switched off.
/// </para>
/// </summary>
public class MarkupRuleTests
{
    private static IElement Markup(string html) => MarkupRules.Parse(html);

    private static void Flags(IReadOnlyList<string> violations, string mustMention)
    {
        var message = Assert.Single(violations);
        Assert.Contains(mustMention, message, StringComparison.OrdinalIgnoreCase);
    }

    private static void Allows(IReadOnlyList<string> violations) =>
        Assert.True(violations.Count == 0,
            "the rule flagged markup that is correct:\n" + string.Join("\n", violations));

    // ------------------------------------------------------------------ headings

    [Theory]
    [InlineData("<p>no heading here</p>", "no <h1> at all")]
    [InlineData("<h1>One</h1><h1>Two</h1>", "2 <h1> elements")]
    public void TheOneHeadingRuleCatchesBothNoneAndTwo(string html, string mentions) =>
        Flags(MarkupRules.ExactlyOneTopLevelHeading(Markup(html)), mentions);

    [Fact]
    public void TheOneHeadingRuleAllowsExactlyOne() =>
        Allows(MarkupRules.ExactlyOneTopLevelHeading(Markup("<h1>Films</h1><h2>Recent</h2>")));

    [Fact]
    public void TheHeadingOrderRuleCatchesASkippedLevel() =>
        Flags(MarkupRules.HeadingLevelsNeverSkipALevel(Markup("<h1>Films</h1><h3>Recent</h3>")), "skipping <h2>");

    [Fact]
    public void TheHeadingOrderRuleAllowsComingBackUpALevel()
    {
        // h1 h2 h3 h2 is a correct outline: the second h2 closes the h3's section and opens a sibling of
        // the first h2. Only a downward jump of more than one invents a level that does not exist.
        Allows(MarkupRules.HeadingLevelsNeverSkipALevel(
            Markup("<h1>A</h1><h2>B</h2><h3>C</h3><h2>D</h2>")));
    }

    [Fact]
    public void TheHeadingOrderRuleIgnoresAHeadingNoReaderCanReach() =>
        Allows(MarkupRules.HeadingLevelsNeverSkipALevel(
            Markup("<h1>A</h1><div aria-hidden=\"true\"><h4>decorative</h4></div>")));

    [Fact]
    public void TheFocusableHeadingRuleCatchesABareH1() =>
        Flags(MarkupRules.TopLevelHeadingIsExplicitlyFocusable(Markup("<h1>Films</h1>")), "tabindex");

    [Fact]
    public void TheFocusableHeadingRuleIsSatisfiedByMinusOneAndNotByZero()
    {
        Allows(MarkupRules.TopLevelHeadingIsExplicitlyFocusable(Markup("<h1 tabindex=\"-1\">Films</h1>")));
        // tabindex="0" would put a heading in the tab order, which is a Tab stop that does nothing.
        Flags(MarkupRules.TopLevelHeadingIsExplicitlyFocusable(Markup("<h1 tabindex=\"0\">Films</h1>")), "tabindex");
    }

    // ------------------------------------------------------------------ the document title

    [Theory]
    [InlineData("", "empty")]
    [InlineData("   ", "empty")]
    [InlineData("Films", "does not end with")]
    public void TheTitleRuleCatchesATitleThatDoesNotNameTheSite(string title, string mentions) =>
        Assert.Contains(mentions,
            string.Join(" ", MarkupRules.TitleDescribesTheRouteItself(title, "Film and TV library")),
            StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void TheTitleRuleCatchesTheHostPageTitleShowingThrough()
    {
        // This is the case a presence check cannot see, and the reason the rule is not a presence check:
        // index.html has a static title, so a page that forgets PageTitle still HAS one.
        var violations = MarkupRules.TitleDescribesTheRouteItself("Film and TV library", "Film and TV library");

        Assert.Contains(violations, v => v.Contains("sets no PageTitle of its own", StringComparison.Ordinal));
    }

    [Fact]
    public void TheTitleRuleAllowsARouteThatNamesItselfAndTheSite() =>
        Allows(MarkupRules.TitleDescribesTheRouteItself("Films - Film and TV library", "Film and TV library"));

    // ------------------------------------------------------------------ images

    [Fact]
    public void TheAltRuleCatchesAMissingAttribute() =>
        Flags(MarkupRules.EveryImageHasAnAltAttribute(Markup("<img src=\"poster.jpg\" width=\"1\" height=\"1\">")),
              "no alt attribute");

    [Fact]
    public void TheAltRuleAcceptsAnEmptyAltBecauseThatIsHowADecorativeImageIsMarked()
    {
        // alt="" and a missing alt look identical in a browser and read as opposites to a screen reader:
        // one prunes the image from the tree, the other makes the reader announce the file name.
        Allows(MarkupRules.EveryImageHasAnAltAttribute(
            Markup("<img src=\"tmdb-logo.svg\" alt=\"\" width=\"489\" height=\"35\">")));
    }

    [Theory]
    [InlineData("<img src=\"a.png\" alt=\"\">")]
    [InlineData("<img src=\"a.png\" alt=\"\" width=\"489\">")]
    [InlineData("<img src=\"a.png\" alt=\"\" width=\"100%\" height=\"35\">")]
    [InlineData("<img src=\"a.png\" alt=\"\" width=\"-1\" height=\"35\">")]
    public void TheImageSizeRuleNeedsTwoValidIntegersAndNotAPercentage(string html) =>
        Flags(MarkupRules.EveryImageHasExplicitWidthAndHeight(Markup(html)), "width or height");

    [Fact]
    public void TheImageSizeRuleAllowsTwoIntegers() =>
        Allows(MarkupRules.EveryImageHasExplicitWidthAndHeight(
            Markup("<img src=\"a.svg\" alt=\"\" width=\"489\" height=\"35\">")));

    // ------------------------------------------------------------------ nested widgets

    [Theory]
    [InlineData("<button type=\"button\">Save <a href=\"/help\">help</a></button>")]
    [InlineData("<a href=\"/x\">Open <button type=\"button\">Close</button></a>")]
    [InlineData("<div role=\"checkbox\" tabindex=\"0\"><input type=\"text\"></div>")]
    public void TheNestedWidgetRuleCatchesAControlInsideAControl(string html) =>
        Flags(MarkupRules.NoFocusableWidgetIsNestedInsideAnother(Markup(html)), "nested inside another");

    [Fact]
    public void TheNestedWidgetRuleAllowsTheImplicitLabelPattern()
    {
        // The canonical form, and Task 8's filters will be full of it. HTML permits a label's own labeled
        // control inside it; a rule that flagged this would be unusable from the first filter onwards.
        Allows(MarkupRules.NoFocusableWidgetIsNestedInsideAnother(
            Markup("<label><input type=\"checkbox\"> Watched</label>")));
    }

    [Theory]
    [InlineData("<details><summary>More</summary><p>Detail</p></details>")]
    [InlineData("<video controls><a href=\"film.mp4\">Download the film</a></video>")]
    [InlineData("<audio controls><a href=\"t.mp3\">Download</a></audio>")]
    [InlineData("<main tabindex=\"-1\"><button type=\"button\">Save</button></main>")]
    [InlineData("<button type=\"button\"><span tabindex=\"-1\">x</span> Dismiss</button>")]
    [InlineData("<button type=\"button\"><span aria-hidden=\"true\">x</span> Dismiss</button>")]
    public void TheNestedWidgetRuleAllowsTheNestingsThatAreCorrect(string html) =>
        Allows(MarkupRules.NoFocusableWidgetIsNestedInsideAnother(Markup(html)));

    [Fact]
    public void TheNestedWidgetRuleLeavesCompositeWidgetsAlone()
    {
        // A composite widget legitimately owns focusable children. Putting listbox, grid, tablist or menu
        // in the outer set would make the rule fail on correct ARIA, which is the one thing it must not do.
        Allows(MarkupRules.NoFocusableWidgetIsNestedInsideAnother(
            Markup("<div role=\"tablist\"><button type=\"button\" role=\"tab\">One</button></div>")));
        Allows(MarkupRules.NoFocusableWidgetIsNestedInsideAnother(
            Markup("<select><option>One</option></select>")));
    }

    // ------------------------------------------------------------------ accessible names

    [Theory]
    [InlineData("<button type=\"button\"><svg aria-hidden=\"true\"></svg></button>")]
    [InlineData("<a href=\"/x\"><img src=\"i.png\" alt=\"\" width=\"1\" height=\"1\"></a>")]
    [InlineData("<button type=\"button\"><span aria-hidden=\"true\">x</span></button>")]
    [InlineData("<input type=\"text\">")]
    public void TheAccessibleNameRuleCatchesAControlWithNothingToAnnounce(string html) =>
        Flags(MarkupRules.EveryInteractiveElementHasAnAccessibleName(Markup(html)), "no accessible name");

    [Theory]
    [InlineData("<button type=\"button\" aria-label=\"Dismiss error\"><span aria-hidden=\"true\">x</span></button>")]
    [InlineData("<button type=\"button\"><svg aria-hidden=\"true\"></svg><span class=\"visually-hidden\">Menu</span></button>")]
    [InlineData("<a href=\"/x\">Back to the library</a>")]
    [InlineData("<a href=\"/x\"><img src=\"i.png\" alt=\"Open the film\" width=\"1\" height=\"1\"></a>")]
    [InlineData("<label for=\"q\">Search</label><input type=\"text\" id=\"q\">")]
    [InlineData("<label>Search <input type=\"text\"></label>")]
    [InlineData("<span id=\"lbl\">Search</span><input type=\"text\" aria-labelledby=\"lbl\">")]
    [InlineData("<input type=\"submit\" value=\"Apply\">")]
    [InlineData("<input type=\"submit\">")]
    [InlineData("<button type=\"button\" title=\"Dismiss\"></button>")]
    [InlineData("<input type=\"text\" placeholder=\"Title or director\">")]
    public void TheAccessibleNameRuleFindsANameEveryWayTheAlgorithmOffersOne(string html) =>
        Allows(MarkupRules.EveryInteractiveElementHasAnAccessibleName(Markup(html)));

    [Fact]
    public void TheAccessibleNameRuleCountsVisuallyHiddenTextBecauseAReaderDoes()
    {
        // The NavMenu toggle's whole name is its visually-hidden span. A name computation that dropped it
        // would report the real app's own correct markup as a nameless button.
        var toggle = Markup("<button type=\"button\"><span class=\"visually-hidden\">Menu</span></button>")
            .QuerySelector("button")!;

        Assert.Equal("Menu", MarkupRules.AccessibleName(toggle));
        Assert.Equal("", MarkupRules.TextOf(toggle, visibleOnly: true));
    }

    [Fact]
    public void TheAccessibleNameRulePrefersAriaLabelledByOverEverythingElse()
    {
        var control = Markup("<span id=\"real\">Director</span>"
                             + "<input type=\"text\" id=\"q\" aria-labelledby=\"real\" aria-label=\"ignored\" "
                             + "placeholder=\"also ignored\">")
            .QuerySelector("input")!;

        Assert.Equal("Director", MarkupRules.AccessibleName(control));
    }

    // ------------------------------------------------------------------ label in name

    [Theory]
    [InlineData("<button type=\"button\" aria-label=\"Submit\">Send</button>")]
    [InlineData("<a href=\"/x\" aria-label=\"Films\">Back to the library</a>")]
    public void TheLabelInNameRuleCatchesALabelThatDropsTheVisibleWords(string html) =>
        Flags(MarkupRules.AnAriaLabelContainsTheVisibleTextItReplaces(Markup(html)), "does not contain it");

    [Fact]
    public void TheLabelInNameRuleIgnoresTextThatIsNotVisible()
    {
        // The pattern this rule is most likely to be wrong about. SC 2.5.3 is about text presented
        // VISUALLY: a visually-hidden span is in the accessible name and is not on the screen, so a naive
        // "the visible text must appear in the label" rule reports this correct button as a defect the
        // moment it gains a label. The dismiss button is the same shape with an aria-hidden glyph.
        Allows(MarkupRules.AnAriaLabelContainsTheVisibleTextItReplaces(Markup(
            "<button type=\"button\" aria-label=\"Open the menu\">"
            + "<span class=\"visually-hidden\">Menu</span></button>")));

        Allows(MarkupRules.AnAriaLabelContainsTheVisibleTextItReplaces(Markup(
            "<button type=\"button\" aria-label=\"Dismiss error\">"
            + "<span aria-hidden=\"true\">x</span></button>")));
    }

    [Theory]
    [InlineData("<button type=\"button\" aria-label=\"apply filters\">Apply filters</button>")]
    [InlineData("<button type=\"button\" aria-label=\"Apply filters now\">Apply filters:</button>")]
    [InlineData("<button type=\"button\" aria-label=\"Apply  filters\">Apply\n filters</button>")]
    public void TheLabelInNameRuleNormalisesCasePunctuationAndWhitespace(string html) =>
        Allows(MarkupRules.AnAriaLabelContainsTheVisibleTextItReplaces(Markup(html)));

    [Fact]
    public void TheLabelInNameRuleComparesOnlyTheVisibleHalfOfAMixedControl()
    {
        // Visible text AND a visually-hidden clarification on the same control -- a common idiom, and the
        // case that proves the COMPARISON has to use the visible half and not merely the subject filter.
        // The accessible name contains the words on the screen; it does not contain the hidden ones and is
        // not required to. Without this test, changing visibleOnly to false in the comparison survives,
        // because every other labelled control in the suite has no visible text at all and so never
        // reaches the comparison. Found by mutating the rule, which is the only way it could be found.
        Allows(MarkupRules.AnAriaLabelContainsTheVisibleTextItReplaces(MarkupRules.Parse(
            "<button type=\"button\" aria-label=\"Apply filters\">Apply filters"
            + "<span class=\"visually-hidden\">to the film list</span></button>")));
    }

    [Fact]
    public void TheNestedWidgetCarveOutAppliesToALabelThatIsAlsoDeclaredAWidget()
    {
        // The implicit-label carve-out is not reachable through a plain <label>, which has no widget role
        // and so is never an outer element. This is the case that makes it live: an explicit role turns the
        // label into a leaf widget, and its own labeled control inside it is still correct markup.
        Allows(MarkupRules.NoFocusableWidgetIsNestedInsideAnother(
            Markup("<label role=\"button\"><input type=\"checkbox\"> Watched</label>")));

        // ...and the carve-out is its own control only. A second control in there is a real nesting.
        Flags(MarkupRules.NoFocusableWidgetIsNestedInsideAnother(
                  Markup("<label role=\"button\"><input type=\"checkbox\"> Watched "
                         + "<a href=\"/help\">help</a></label>")),
              "nested inside another");
    }

    [Fact]
    public void TheSkipLinkIsNotTreatedAsVisibleTextWhileItIsOffScreen()
    {
        // The skip link carries .visually-hidden-focusable on itself rather than on a child, so a rule that
        // only inspected descendants would read "Skip to main content" as text on the screen. Nothing in
        // parsed markup has focus, so the unfocused state is the only one this half can see; the focused
        // state, where the text really is visible, is Task 4b's.
        var skip = MarkupRules.Parse(
                "<a class=\"skip-link visually-hidden-focusable\" href=\"#main-content\">Skip to main content</a>")
            .QuerySelector("a")!;

        Assert.Equal("Skip to main content", MarkupRules.AccessibleName(skip));
        Assert.Equal("", MarkupRules.TextOf(skip, visibleOnly: true));
    }

    [Fact]
    public void TheLabelInNameRuleDoesNotApplyToAControlWithNoLabelAtAll() =>
        Assert.Empty(MarkupRules.ElementsWhoseLabelReplacesVisibleText(
            Markup("<button type=\"button\">Apply filters</button>")));

    // ------------------------------------------------------------------ tab order

    [Theory]
    [InlineData("1")]   // the value people actually write, and the boundary: > 0, not > 1
    [InlineData("3")]
    [InlineData("32767")]
    public void ThePositiveTabIndexRuleCatchesEveryPositiveValueIncludingOne(string tabindex) =>
        Flags(MarkupRules.NoElementHasAPositiveTabIndex(
                  Markup("<button type=\"button\" tabindex=\"" + tabindex + "\">Go</button>")),
              "positive tabindex");

    [Theory]
    [InlineData("<main tabindex=\"-1\">content</main>")]
    [InlineData("<div tabindex=\"0\">widget</div>")]
    public void ThePositiveTabIndexRuleAllowsZeroAndMinusOne(string html) =>
        Allows(MarkupRules.NoElementHasAPositiveTabIndex(Markup(html)));

    // ------------------------------------------------------------------ id references

    [Theory]
    [InlineData("<input type=\"text\" aria-labelledby=\"gone\">")]
    [InlineData("<input type=\"text\" aria-describedby=\"gone\">")]
    [InlineData("<div role=\"group\" aria-owns=\"gone\"></div>")]
    [InlineData("<label for=\"gone\">Search</label>")]
    public void TheIdReferenceRuleCatchesADanglingReference(string html) =>
        Flags(MarkupRules.EveryIdReferenceResolves(Markup(html)), "no element with that id");

    [Fact]
    public void TheIdReferenceRuleSplitsTheSpaceSeparatedListInsteadOfReadingItAsOneId()
    {
        // The trap. Every one of these attributes takes a list, so an implementation that looked up the
        // whole attribute value as a single id would report correct markup as broken on its first use --
        // and would also miss the real case below, where only the second id is missing.
        Allows(MarkupRules.EveryIdReferenceResolves(Markup(
            "<span id=\"a\">Title</span><span id=\"b\">required</span>"
            + "<input type=\"text\" aria-labelledby=\"a b\">")));

        Flags(MarkupRules.EveryIdReferenceResolves(Markup(
                  "<span id=\"a\">Title</span><input type=\"text\" aria-labelledby=\"a gone\">")),
              "gone");
    }

    [Fact]
    public void TheIdReferenceRuleAllowsACollapsedControlToPointAtSomethingNotYetInTheDom()
    {
        // axe deliberately passes this: when aria-expanded="false" the controlled element may legitimately
        // not have been rendered yet. Task 4b runs axe over the same markup, so this half carves out the
        // same case -- a rule stricter here than there is a rule that disagrees with the other half of its
        // own gate.
        Allows(MarkupRules.EveryIdReferenceResolves(Markup(
            "<button type=\"button\" aria-expanded=\"false\" aria-controls=\"drawer\">Menu</button>")));
    }

    [Fact]
    public void TheIdReferenceRuleStillFailsAnExpandedControlPointingNowhere()
    {
        // The other side of the exception: once the control says it is expanded, the thing it controls has
        // to be there. Without this the carve out above would amount to never checking aria-controls.
        Flags(MarkupRules.EveryIdReferenceResolves(Markup(
                  "<button type=\"button\" aria-expanded=\"true\" aria-controls=\"drawer\">Menu</button>")),
              "drawer");
    }

    [Fact]
    public void TheIdReferenceRuleResolvesTheRealNavToggleAgainstItsDrawer() =>
        Allows(MarkupRules.EveryIdReferenceResolves(Markup(
            "<button type=\"button\" aria-expanded=\"true\" aria-controls=\"nav-links\">Menu</button>"
            + "<ul class=\"nav-links\" id=\"nav-links\" role=\"list\"><li><a href=\"movies\">Films</a></li></ul>")));

    [Fact]
    public void TheFailableReferenceCountLeavesOutTheOneTheRuleCanNeverFailOn()
    {
        // The difference the contract's coverage count has to be built on. This markup is NavMenu's shape
        // on a first render, and it is one id reference the rule is incapable of failing: counting it
        // reported the contract as covered over a subject set that was entirely carved out.
        var markup = Markup(
            "<button type=\"button\" aria-expanded=\"false\" aria-controls=\"drawer\">Menu</button>");

        Assert.Single(MarkupRules.IdReferences(markup));
        Assert.Empty(MarkupRules.FailableIdReferences(markup));
    }

    [Fact]
    public void TheFailableReferenceCountKeepsEveryReferenceTheRuleCanStillFailOn()
    {
        // The other side: the carve-out is narrow -- one attribute, one state. An expanded control, a
        // label's for and a description are all failable, which is what makes the skip expire when Task 8
        // adds the filter controls rather than being permanent.
        var markup = Markup(
            "<button type=\"button\" aria-expanded=\"true\" aria-controls=\"drawer\">Menu</button>"
            + "<label for=\"q\">Search</label>"
            + "<input type=\"text\" id=\"q\" aria-describedby=\"hint\">");

        Assert.Equal(3, MarkupRules.FailableIdReferences(markup).Count);
    }
}

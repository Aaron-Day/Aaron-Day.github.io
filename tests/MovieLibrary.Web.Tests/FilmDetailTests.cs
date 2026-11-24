using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MovieLibrary.Web.Pages;
using MovieLibrary.Web.Services;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// The film detail page: its four states, what it renders about a film, and what it says.
/// <para>
/// FOUR STATES AND NOT THREE, which is the difference between this page and the films page. Loading, Ready
/// with a film, Ready with NO film, and Failed. The third is the one worth the extra machinery: a key with
/// no film behind it is a wrong ADDRESS, not a fetch that failed, so it offers no Try again -- no number of
/// retries will resolve it -- and a page that could not tell the two apart would show a reader a button
/// that cannot help.
/// </para>
/// <para>
/// Rendered through <c>App</c>, like the rest of this suite: the live regions, the landmarks and the drawer
/// all live in MainLayout, and the announcer's text can only be read where that layout is mounted.
/// </para>
/// <para>
/// WHAT bUNIT CANNOT SHOW. There is no layout and no accessibility tree here, so where focus actually lands
/// is asserted as the interop CALLS that implement it, and whether a reader speaks the container it lands on
/// is a device question. It also cannot show that a message was raised AFTER the render carrying its state
/// rather than before: its renderer commits in the same clock instant as the call. Both limits are the ones
/// <c>MoviesStateTests</c> records for the same machinery, and neither is re-litigated here.
/// </para>
/// </summary>
public class FilmDetailTests : BunitContext
{
    private const string FocusHelper = "movieLibrary.focusIfUnmoved";

    /// <summary>What <c>ElementReference.FocusAsync()</c> invokes, which is the page's other focus call.</summary>
    private const string BuiltInFocus = "Blazor._internal.domWrapper.focus";

    /// <summary>A key that is a real shape and is in no catalogue, so the lookup legitimately finds nothing.</summary>
    private const string MissingKey = "999-1";

    public FilmDetailTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    private void FocusHelperAnswers(bool moved) =>
        JSInterop.Setup<bool>(FocusHelper, _ => true).SetResult(moved);

    /// <summary>The film this catalogue holds every field for. See AnnouncerTestExtensions.</summary>
    private (IRenderedComponent<MovieLibrary.Web.App> Cut, ManualTimeProvider Clock) RenderFilm(
        string address = AnnouncerTestExtensions.FilmAddress, CatalogOutcome outcome = CatalogOutcome.Ready)
    {
        var clock = this.AddAnnouncer(outcome);
        return (this.RenderAppAt(address), clock);
    }

    /// <summary>
    /// A COLD arrival: the catalogue is fetched asynchronously, so a render commits while the page is still
    /// loading and the cold branch of the announcement is reached. See <c>StubCatalog.JsonAfterYielding</c>
    /// for why this is not the same as <see cref="RenderFilm"/>, and for why it is preferred to a Gate where
    /// the test does not need to observe the middle of the fetch.
    /// </summary>
    private (IRenderedComponent<MovieLibrary.Web.App> Cut, ManualTimeProvider Clock) RenderColdFilm(
        string address = AnnouncerTestExtensions.FilmAddress)
    {
        var (clock, stub) = this.AddAnnouncerAndCatalog(
            AnnouncerTestExtensions.ColdResponder(CatalogOutcome.Ready));
        stub.Serves(StubCatalog.Meta, AnnouncerTestExtensions.MetaResponder(CatalogOutcome.Ready));

        return (this.RenderAppAt(address), clock);
    }

    /// <summary>
    /// Wait for a cold arrival to finish: the film's state has landed AND the after-render that announces it
    /// has run.
    /// </summary>
    /// <remarks>
    /// The flush is the part that matters. Markup is published DURING a render batch and the
    /// OnAfterRenderAsync callbacks run at the END of the same dispatcher work item, so a heading assertion
    /// can pass on the test's thread while the announcement has not been raised yet -- and the clock advance
    /// that followed would then run over an empty region. A no-op queued on the renderer's dispatcher cannot
    /// run until that work item has finished, so awaiting one is a flush and not a sleep. Three tests here
    /// failed intermittently, and whichever of a near-identical pair ran second, before this existed.
    /// </remarks>
    private static async Task SettledAsync(IRenderedComponent<MovieLibrary.Web.App> cut, string heading)
    {
        cut.WaitForAssertion(() => Assert.Equal(heading, Heading(cut)));
        await cut.InvokeAsync(() => { });
    }

    private Announcer Spoken() => Services.GetRequiredService<Announcer>();

    private Microsoft.AspNetCore.Components.NavigationManager Nav() =>
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();

    private static string Heading(IRenderedComponent<MovieLibrary.Web.App> cut) =>
        cut.Find("main h1").TextContent.Trim();

    private static string Sentence(IRenderedComponent<MovieLibrary.Web.App> cut) =>
        cut.Find(".film-state p").TextContent.Trim();

    private static IReadOnlyList<AngleSharp.Dom.IElement> TryAgain(IRenderedComponent<MovieLibrary.Web.App> cut) =>
        cut.FindAll("button.try-again");

    /// <summary>A dl's pairs as term-and-value, in document order.</summary>
    private static List<(string Term, string Value)> Facts(IRenderedComponent<MovieLibrary.Web.App> cut, int which) =>
        [.. cut.FindAll("dl.film-facts")[which].QuerySelectorAll("div")
            .Select(row => (row.QuerySelector("dt")!.TextContent.Trim(),
                            row.QuerySelector("dd")!.TextContent.Trim()))];

    // ============================================================ the heading, in every state

    /// <remarks>
    /// The heading and the title are in the first render and outside every state branch, which is the same
    /// claim <c>MoviesStateTests</c> makes for the films page and for the same two reasons: FocusOnNavigate
    /// fires once after the first render and never retries, and the browser gate's boot signal is
    /// "#app main#main-content h1". A heading that appeared only once the film arrived would cost both.
    /// The tabindex is a feature dependency, not a conformance claim: <c>FocusAsync()</c> does not add it and
    /// fails silently without it, which is how the successful-retry move would break with nothing going red.
    /// </remarks>
    [Theory]
    [InlineData(CatalogOutcome.NeverCompletes, FilmDetail.LoadingVisible)]
    [InlineData(CatalogOutcome.Failed, FilmDetail.UnavailableHeading)]
    [InlineData(CatalogOutcome.Ready, "Se7en")]
    public void TheHeadingIsInTheFirstRenderAndSaysWhatThisStateKnows(CatalogOutcome outcome, string expected)
    {
        var (cut, _) = RenderFilm(outcome: outcome);

        var h1 = cut.Find("main h1");
        Assert.Equal(expected, h1.TextContent.Trim());
        Assert.Equal("-1", h1.GetAttribute("tabindex"));
    }

    [Fact]
    public void AKeyNoFilmHasIsItsOwnHeadingAndNotTheFailureOne()
    {
        var (cut, _) = RenderFilm("/movies/" + MissingKey);

        // Ready with no film. "Film unavailable" would blame the fetch for an address that is simply wrong,
        // and "Se7en" would be a page naming a film it is not showing.
        Assert.Equal(FilmDetail.NotFoundHeading, Heading(cut));
        Assert.Equal(FilmDetail.NoSuchFilm, Sentence(cut));
    }

    /// <remarks>
    /// SC 2.4.2 Page Titled, Level A, and the exact strings rather than the shape -- which is what
    /// <c>MarkupContractTests.EveryRouteSetsADocumentTitleOfItsOwn</c> leaves to a test like this one. The
    /// title is built from the heading, so a state whose heading changed and whose title did not would be a
    /// tab saying one thing and a page saying another.
    /// </remarks>
    [Theory]
    [InlineData(AnnouncerTestExtensions.FilmAddress, "Se7en - Film and TV library")]
    [InlineData("/movies/" + MissingKey, "Film not found - Film and TV library")]
    public void TheDocumentTitleIsTheHeadingAndTheSiteName(string address, string expected)
    {
        // The services first and the HeadOutlet second: bUnit seals its provider the moment anything is
        // resolved from it, which rendering does, so a registration after the first render throws.
        this.AddAnnouncer();
        var head = Render<Microsoft.AspNetCore.Components.Web.HeadOutlet>();

        Assert.NotNull(this.RenderAppAt(address));
        head.WaitForAssertion(() => Assert.Equal(expected, head.Find("title").TextContent));
    }

    // ============================================================ the one stable container

    /// <remarks>
    /// ONE container and ONE paragraph in every state, never sibling branches that swap nodes: replacing the
    /// node would destroy the one a screen reader's virtual cursor may be sitting on while the film loads,
    /// and this page's whole middle state is a wait.
    /// <para>
    /// The fourth state's paragraph is the film's BYLINE rather than a sentence, which is why the container
    /// can be the same element in all four without an empty paragraph anywhere: a detail page puts the year,
    /// the runtime and the rating under its title regardless.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(CatalogOutcome.NeverCompletes)]
    [InlineData(CatalogOutcome.Failed)]
    [InlineData(CatalogOutcome.Ready)]
    public void OneStableContainerHoldsTheStateInEveryState(CatalogOutcome outcome)
    {
        var (cut, _) = RenderFilm(outcome: outcome);

        var container = Assert.Single(cut.FindAll(".film-state"));
        Assert.Equal("-1", container.GetAttribute("tabindex"));
        Assert.Single(cut.FindAll(".film-state > p"));
    }

    /// <remarks>
    /// The byline's parts and their order, which is what a reader meets immediately after the title. No
    /// separator CHARACTER between them: the gap is a flex gap, because a middot is read inconsistently and
    /// moving it into generated content does not hide it from the accessibility tree.
    /// </remarks>
    [Fact]
    public void TheBylineIsTheFilmsOwnNumbersAndCarriesNoSeparatorCharacter()
    {
        var (cut, _) = RenderFilm();

        Assert.Equal(
            new[] { "1995", "1h 50m", "8.4/10" },
            cut.FindAll(".film-byline > span").Select(s => s.TextContent.Trim()));

        // The parent's own text is the spans' text and nothing else: no punctuation anywhere between them.
        Assert.Equal("19951h 50m8.4/10", cut.Find(".film-byline").TextContent.Trim());
    }

    // ============================================================ the artwork

    /// <remarks>
    /// Both images are decorative and both reserve their box. alt="" rather than a description: the title is
    /// the h1 immediately above, and a backdrop is set dressing with no information in it at all. PRESENT and
    /// empty -- a missing alt makes a reader read the file name out, and the two look identical in a browser.
    /// </remarks>
    [Theory]
    [InlineData("img.film-backdrop", "https://image.tmdb.org/t/p/w1280/se7en-wide.jpg", "1280", "720")]
    [InlineData("img.film-poster", "https://image.tmdb.org/t/p/w342/se7en.jpg", "342", "513")]
    public void TheArtworkIsPrunedFromTheAccessibilityTreeAndReservesItsBox(
        string selector, string src, string width, string height)
    {
        var (cut, _) = RenderFilm();
        var img = cut.Find(selector);

        Assert.Equal("", img.GetAttribute("alt"));
        Assert.Equal(src, img.GetAttribute("src"));
        Assert.Equal(width, img.GetAttribute("width"));
        Assert.Equal(height, img.GetAttribute("height"));
    }

    /// <remarks>
    /// A record with no artwork renders NO img for it, and the two cases differ on purpose: the poster falls
    /// back to a placeholder box, because a detail page with a blank column reads as broken, and an absent
    /// backdrop renders nothing at all, because a placeholder for set dressing is set dressing for set
    /// dressing. Never an img with an empty src -- that renders broken-image chrome and some browsers expose
    /// the URL as text -- which is the "simplification" this guards against rather than a case the page meets.
    /// </remarks>
    [Fact]
    public void AFilmWithNoArtworkGetsAPlaceholderPosterAndNoBackdropAtAll()
    {
        var (cut, _) = RenderFilm("/movies/601-88");

        Assert.Empty(cut.FindAll("img.film-poster"));
        Assert.Empty(cut.FindAll("img.film-backdrop"));
        Assert.Single(cut.FindAll("span.film-poster-placeholder"));

        var placeholder = cut.Find("span.film-poster-placeholder");
        Assert.Equal("true", placeholder.GetAttribute("aria-hidden"));
        Assert.Equal("", placeholder.TextContent);
    }

    // ============================================================ the sections

    /// <remarks>
    /// The outline a reader navigates by, in order and at one level. PLAIN div AND h2, never &lt;section&gt;:
    /// a named section becomes a region landmark, and four of them would file these beside Banner, Navigation
    /// and Main in a landmark list, which is not what a landmark list is for. Unnamed, a section maps to
    /// generic and buys nothing. <c>MarkupContractTests.HeadingLevelsNeverSkipALevel</c> is live because of
    /// these, and <c>TheHeadingOrderContractHasTheSubjectsTheDetailPageGaveIt</c> counts them.
    /// </remarks>
    [Fact]
    public void TheSectionsAreHeadingsAtOneLevelAndNotLandmarks()
    {
        var (cut, _) = RenderFilm();

        Assert.Equal(
            new[] { "Overview", "Cast", "Details", "Additional information" },
            cut.FindAll(".film-section h2").Select(h => h.TextContent.Trim()));

        Assert.Empty(cut.FindAll("main section"));
        Assert.Empty(cut.FindAll("main h3"));
    }

    /// <remarks>
    /// A section with nothing to put in it is ABSENT and not an empty heading. A heading over nothing tells
    /// someone navigating by heading that content exists which does not, and the Details section is the one
    /// that always appears because every record has a match to report.
    /// </remarks>
    [Fact]
    public void ASectionWithNothingInItIsNotRenderedAtAll()
    {
        var (cut, _) = RenderFilm("/movies/601-88");

        Assert.Equal(new[] { "Details" }, cut.FindAll(".film-section h2").Select(h => h.TextContent.Trim()));
    }

    [Fact]
    public void TheOverviewAndTheTaglineAreProseAndNeitherIsAHeading()
    {
        var (cut, _) = RenderFilm();

        Assert.Equal("Seven deadly sins. Seven ways to die.", cut.Find("p.film-tagline").TextContent.Trim());
        Assert.Equal(
            "Two detectives hunt a killer who uses the seven deadly sins as his motives.",
            cut.Find(".film-section p").TextContent.Trim());

        // Not emphasis, not a heading, not a blockquote: a studio's sentence is a sentence. <em> would claim
        // a stress the words do not have, and a reader hears that claim.
        Assert.Empty(cut.FindAll("main em, main i, main blockquote"));
    }

    // ============================================================ the cast

    /// <remarks>
    /// role="list" is declared by hand because CSS removes the markers, and Safari with VoiceOver strips
    /// list semantics from a list whose markers are gone -- the same reason PosterGrid declares it. Without
    /// it a reader is not told how many people are in the cast or where in it they are.
    /// </remarks>
    [Fact]
    public void TheCastIsAListAndSaysSoEvenWithItsMarkersRemoved()
    {
        var (cut, _) = RenderFilm();
        var list = cut.Find("ul.film-cast");

        Assert.Equal("list", list.GetAttribute("role"));
        Assert.Equal(3, cut.FindAll("ul.film-cast > li").Count);

        // No label on the list. The count a reader announces is the list's own, and "3 items" in a label
        // would be a number to keep in step with the markup for no benefit.
        Assert.False(list.HasAttribute("aria-label"));
        Assert.False(list.HasAttribute("aria-labelledby"));
    }

    /// <remarks>
    /// "as" IS A REAL TEXT NODE. "Brad Pitt as Mills" reads as a sentence; "Brad Pitt -- Mills" reads as
    /// "Brad Pitt" and then, depending on the reader's punctuation level, either nothing or "dash". The
    /// three members are the three branches: a photo and a character, a character and no photo, and the
    /// EMPTY character string the pipeline writes when it has none -- which must render the person alone and
    /// never "An Extra as".
    /// </remarks>
    [Fact]
    public void EachCastMemberReadsAsASentenceAndNeverAsAnOrphanedAs()
    {
        var (cut, _) = RenderFilm();

        Assert.Equal(
            new[] { "Brad Pitt", "Morgan Freeman", "An Extra" },
            cut.FindAll("ul.film-cast .film-cast-name").Select(e => e.TextContent.Trim()));

        // Two roles for three people, and each begins with the word rather than a separator.
        Assert.Equal(
            new[] { "as Mills", "as Somerset" },
            cut.FindAll("ul.film-cast .film-cast-role").Select(e => e.TextContent.Trim()));
    }

    /// <remarks>
    /// A portrait is decorative for the reason the poster is: the person's name is the text beside it. The
    /// member with no profile path gets the placeholder box rather than an img with an empty src, and the
    /// one with no character gets no photo element of its own kind by accident -- so the counts are asserted
    /// together, because "one img and two placeholders" is the shape and either number alone is not.
    /// </remarks>
    [Fact]
    public void APortraitIsDecorativeAndAMissingOneIsAPlaceholderRatherThanAnEmptyImage()
    {
        var (cut, _) = RenderFilm();
        var photos = cut.FindAll("ul.film-cast img");

        var photo = Assert.Single(photos);
        Assert.Equal("", photo.GetAttribute("alt"));
        Assert.Equal("https://image.tmdb.org/t/p/w185/pitt.jpg", photo.GetAttribute("src"));
        Assert.Equal("185", photo.GetAttribute("width"));
        Assert.Equal("278", photo.GetAttribute("height"));
        Assert.Equal("lazy", photo.GetAttribute("loading"));

        Assert.Equal(2, cut.FindAll("ul.film-cast span.film-portrait-placeholder").Count);
        Assert.All(cut.FindAll("ul.film-cast span.film-portrait-placeholder"),
                   p => Assert.Equal("true", p.GetAttribute("aria-hidden")));
    }

    /// <remarks>
    /// No truncation, in C# or in CSS, and no link. A character name cut short cannot be recovered -- there
    /// is no title attribute, label or link here that could carry the rest -- and a cast member who was a
    /// link would put a stop in the tab order for an address this site does not serve.
    /// </remarks>
    [Fact]
    public void NothingInTheCastIsALinkOrALabelOrATooltip()
    {
        var (cut, _) = RenderFilm();
        var list = cut.Find("ul.film-cast");

        Assert.Empty(list.QuerySelectorAll("a, button, [tabindex], [title], [aria-label], [aria-labelledby]"));
    }

    // ============================================================ the facts

    /// <remarks>
    /// Each pair is wrapped in a div, which is valid inside a dl and is what lets the pair be a grid row
    /// WITHOUT display: contents -- Chrome and Safari both used to drop a display: contents element from the
    /// accessibility tree, taking the dt/dd relationship with it. The rows are asserted as pairs for that
    /// reason: a dl whose terms and values were siblings in a flat list would pass a count of each.
    /// </remarks>
    [Fact]
    public void TheDetailsAreTermAndValuePairsAndEveryPairIsItsOwnGroup()
    {
        var (cut, _) = RenderFilm();

        Assert.Equal(
            [("Director", "David Fincher"),
             ("Genres", "Thriller"),
             ("Category in the library", "Thriller"),
             ("Collection", "The Se7en Collection"),
             ("How it was bought", "Bought 2019"),
             ("Match", "Matched, 95%")],
            Facts(cut, 0));
    }

    /// <remarks>
    /// A row with nothing behind it is ABSENT and never padded with "Unknown": a value of "Unknown" is the
    /// page stating that the library knows the answer is unknown, when the truth is that the catalogue has
    /// no field for it. The Match row is the one that always appears, because every record has a match to
    /// report -- including "no match at all", which is the band the whole page exists to surface.
    /// </remarks>
    [Fact]
    public void AFactWithNothingBehindItIsAbsentRatherThanPaddedWithAPlaceholder()
    {
        var (cut, _) = RenderFilm("/movies/u-d2f1c6e2");

        Assert.Equal([("Category in the library", "Documentary"), ("Match", "Not matched")], Facts(cut, 0));
    }

    /// <remarks>
    /// The band's own words, from <c>ConfidenceBand</c> and never retyped, so the page, the tile's badge and
    /// the build summary the owner reads cannot drift apart. An unmatched record's score is 0 and means
    /// nothing -- the pipeline writes 0 whatever the best candidate scored -- so it is not shown; the other
    /// two carry it, because "Needs review" without a number gives the owner nothing to judge by.
    /// </remarks>
    [Theory]
    [InlineData(AnnouncerTestExtensions.FilmAddress, "Matched, 95%")]
    [InlineData("/movies/601-88", "Needs review, 55%")]
    [InlineData("/movies/u-d2f1c6e2", "Not matched")]
    public void TheMatchRowCarriesTheBandsOwnWords(string address, string expected)
    {
        var (cut, _) = RenderFilm(address);

        Assert.Equal(expected, Facts(cut, 0).Single(f => f.Term == "Match").Value);
    }

    /// <remarks>
    /// The owner's own fields are a CONTRACT: a key added by hand to the record store survives every build
    /// in between with no code change, and nothing in this project interprets the contents. So this asserts
    /// only the things the page decides -- the order, and which rows exist.
    /// <para>
    /// ORDINAL ORDER AND NOT THE DICTIONARY'S. <c>Dictionary&lt;,&gt;</c> enumerates in an order no
    /// documentation promises, and rows that moved between two loads of the same film would be SC 3.2.3
    /// Consistent Navigation's problem in miniature. The fixture's keys are deliberately out of order so
    /// that a page which sorted nothing would fail this rather than pass it by luck.
    /// </para>
    /// <para>
    /// A BLANK KEY OR A BLANK VALUE IS DROPPED. A dt with no text is a term a reader is told exists and
    /// cannot read, and a dd with no text is the same from the other side; both pass every automated check
    /// and say nothing. The fixture carries one of each.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheOwnersOwnFieldsAreSortedAndTheBlankOnesAreDropped()
    {
        var (cut, _) = RenderFilm();

        Assert.Equal([("Case", "keep case"), ("Shelf", "B3")], Facts(cut, 1));
    }

    [Fact]
    public void AFilmWithNoExtraFieldsRendersNoAdditionalInformationSection()
    {
        var (cut, _) = RenderFilm("/movies/601-88");

        Assert.Single(cut.FindAll("dl.film-facts"));
        Assert.DoesNotContain("Additional information",
                              cut.Find("main").TextContent, StringComparison.Ordinal);
    }

    // ============================================================ the failure, and the retry

    /// <remarks>
    /// ONE constant for the visible paragraph and for the alert, so the seen and the spoken copies cannot
    /// drift. Asserted as the same STRING and not as two literals that happen to match today.
    /// </remarks>
    [Fact]
    public void AFailedFetchSaysTheSameWordsOnScreenAndAloud()
    {
        FocusHelperAnswers(false);
        var (cut, clock) = RenderFilm(outcome: CatalogOutcome.Failed);

        clock.Advance(Announcer.SettleDelay);

        Assert.Equal(FilmDetail.CouldNotLoad, Sentence(cut));
        Assert.Equal(Sentence(cut), Spoken().AlertText);
        Assert.Single(TryAgain(cut));
    }

    /// <remarks>
    /// THE FOCUS MOVE IS THE PRIMARY CHANNEL AND THE ALERT IS THE FALLBACK, which is one report for one
    /// failure. A reader lands on the container and hears its text as it lands, so raising the alert as well
    /// would say the same sentence twice -- once from the focus move and again a settle delay later when the
    /// assertive region interrupts with identical words. Driven from both answers of the helper, because the
    /// branch is which channel ran and not whether either did.
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheFailureIsReportedOnceByWhicheverChannelRan(bool focusMoves)
    {
        FocusHelperAnswers(focusMoves);
        var (cut, clock) = RenderFilm(outcome: CatalogOutcome.Failed);

        clock.Advance(Announcer.SettleDelay);

        Assert.NotNull(cut);
        Assert.Single(JSInterop.Invocations[FocusHelper]);
        Assert.Equal(focusMoves ? "" : FilmDetail.CouldNotLoad, Spoken().AlertText);
    }

    /// <remarks>
    /// The keepers are the shared list, not a copy: a page that kept its own would hand focus to a reader on
    /// one route and refuse it on the next with nothing on screen to say which was the odd one.
    /// <c>MoviesFilterTests</c> pins the same pair reaching the same helper from the films page.
    /// </remarks>
    [Fact]
    public void TheFocusMoveAsksTheSharedKeeperListAndNotACopyOfIt()
    {
        FocusHelperAnswers(true);
        var (cut, _) = RenderFilm(outcome: CatalogOutcome.Failed);

        Assert.NotNull(cut);
        var call = Assert.Single(JSInterop.Invocations[FocusHelper]);
        Assert.Equal(new[] { "#main-content", "#app h1" }, call.Arguments[1]);
    }

    /// <remarks>
    /// THE BUTTON IS THE DIFFERENCE BETWEEN A FAILURE AND A WRONG ADDRESS, and it is the whole reason
    /// <c>CatalogService.GetFilmAsync</c> reports Ready-with-null rather than folding it into Failed. A
    /// retry over an address no number of fetches will resolve is a button that cannot help, and a reader
    /// who presses it learns nothing except that it did nothing.
    /// </remarks>
    [Theory]
    [InlineData("/movies/" + MissingKey, 0)]
    [InlineData(AnnouncerTestExtensions.FilmAddress, 0)]
    public void OnlyAFailedFetchOffersARetry(string address, int buttons)
    {
        var (cut, _) = RenderFilm(address);

        Assert.Equal(buttons, TryAgain(cut).Count);
    }

    /// <remarks>
    /// aria-disabled, never the disabled ATTRIBUTE: disabling the focused element blurs it and drops focus
    /// to &lt;body&gt;, which is the same defect the focus move on failure exists to fix, at the other end.
    /// The click still arrives while a retry is in flight and is ignored in C# instead.
    /// </remarks>
    [Fact]
    public void TheRetryButtonIsNeverTheDisabledAttribute()
    {
        FocusHelperAnswers(true);
        var (cut, _) = RenderFilm(outcome: CatalogOutcome.Failed);

        var button = cut.Find("button.try-again");
        Assert.False(button.HasAttribute("disabled"));
        Assert.Null(button.GetAttribute("aria-disabled"));   // not retrying yet
    }

    // ============================================================ what the page says, and when

    /// <remarks>
    /// THE NAME IS SAID ONLY WHERE A READER HAS NOT ALREADY HEARD IT, and these two tests are the pair that
    /// makes that condition falsifiable. On a COLD arrival the fetch is genuinely in flight, a render
    /// commits while the heading still reads "Loading film", FocusOnNavigate has already landed on it, and
    /// the polite region is the only channel that will ever name the film.
    /// </remarks>
    [Fact]
    public async Task AColdArrivalSaysTheFilmsNameBecauseTheHeadingSaidLoadingWhenFocusLanded()
    {
        var (cut, clock) = RenderColdFilm();

        await SettledAsync(cut, "Se7en");
        clock.Advance(Announcer.SettleDelay);

        Assert.Equal("Se7en", Spoken().StatusText);
        Assert.Equal("", Spoken().AlertText);
    }

    /// <remarks>
    /// The middle of the cold arrival, which the test above cannot show because by the time its render call
    /// returns the fetch has finished. A held fetch is the only way to read the page WHILE it is loading,
    /// and what matters there is that nothing has been said yet: an announcement raised before the state
    /// arrived would be the loading copy spoken over the focus move that had just read the same words off
    /// the heading.
    /// </remarks>
    [Fact]
    public void NothingIsSaidWhileTheFilmIsStillLoading()
    {
        var gate = new StubCatalog.Gate();
        var (clock, stub) = this.AddAnnouncerAndCatalog(
            gate.Holding(AnnouncerTestExtensions.Responder(CatalogOutcome.Ready)));
        stub.Serves(StubCatalog.Meta, AnnouncerTestExtensions.MetaResponder(CatalogOutcome.Ready));

        var cut = this.RenderAppAt(AnnouncerTestExtensions.FilmAddress);

        Assert.Equal(FilmDetail.LoadingVisible, Heading(cut));
        Assert.Equal(FilmDetail.LoadingVisible, Sentence(cut));
        Assert.Empty(TryAgain(cut));

        clock.Advance(Announcer.SettleDelay);
        Assert.Equal("", Spoken().StatusText);
        Assert.Equal("", Spoken().AlertText);

        gate.Open();
    }

    /// <remarks>
    /// The other half, and the common case: arriving from a tile on the films page, where the catalogue is
    /// already in memory. The page is Ready on its FIRST render, so FocusOnNavigate lands on a heading that
    /// already holds the film's name and a reader hears it from the focus move. Announcing as well would be
    /// the same words twice for one event -- rule R3, which forbids announcing the text of whatever focus
    /// has just moved to.
    /// <para>
    /// This is also the test that would fail if the guard were removed, which is the point of writing it:
    /// the cold case above passes either way.
    /// </para>
    /// </remarks>
    [Fact]
    public void AWarmArrivalSaysNothingBecauseTheFocusMoveAlreadyReadTheName()
    {
        var (cut, clock) = RenderFilm();

        Assert.Equal("Se7en", Heading(cut));
        clock.Advance(Announcer.SettleDelay);

        Assert.Equal("", Spoken().StatusText);
        Assert.Equal("", Spoken().AlertText);
    }

    /// <remarks>
    /// A SUCCESSFUL RETRY MOVES FOCUS AND SAYS NOTHING. The Try again button the reader was standing on has
    /// gone, so focus would otherwise drop to &lt;body&gt;; landing on the heading, which now holds the
    /// film's name, reports the success in the words on screen. An announcement after it would be the same
    /// name twice, which is the R3 case again and is why the focus move claims the report.
    /// </remarks>
    [Fact]
    public async Task ASuccessfulRetryMovesFocusToTheHeadingAndDoesNotAlsoAnnounce()
    {
        FocusHelperAnswers(false);
        var (clock, stub) = this.AddAnnouncerAndCatalog(
            StubCatalog.Throws(new HttpRequestException("the stub was asked to fail")),
            AnnouncerTestExtensions.Responder(CatalogOutcome.Ready));
        stub.Serves(StubCatalog.Meta, AnnouncerTestExtensions.MetaResponder(CatalogOutcome.Ready));

        var cut = this.RenderAppAt(AnnouncerTestExtensions.FilmAddress);
        clock.Advance(Announcer.SettleDelay);
        Assert.Equal(FilmDetail.CouldNotLoad, Spoken().AlertText);

        await cut.Find("button.try-again").ClickAsync(new());
        cut.WaitForAssertion(() => Assert.Equal("Se7en", Heading(cut)));
        clock.Advance(Announcer.SettleDelay);

        // The heading, outright: the control that had focus no longer exists, so there is nothing to ask
        // the keeper list about. FocusAsync() goes through a different interop identifier from the helper.
        Assert.NotEmpty(JSInterop.Invocations[BuiltInFocus]);

        // R5: the failure has ended as far as the reader is concerned, and nothing new is said.
        Assert.Equal("", Spoken().AlertText);
        Assert.Equal("", Spoken().StatusText);
    }

    // ============================================================ one film replaced by another

    /// <remarks>
    /// THE ROUTER REUSES THIS COMPONENT when one film's address replaces another's, which is why the fetch
    /// is in <c>OnParametersSetAsync</c> and not in <c>OnInitializedAsync</c>: with it in OnInitialized the
    /// second film would never be looked up and the page would show the first for ever. There is no link
    /// between two films on the page yet, so this is reached by navigation -- and by a back button, which is
    /// the same thing and is how a reader will actually meet it.
    /// </remarks>
    [Fact]
    public void ReplacingOneFilmsAddressWithAnothersLooksUpTheOtherFilm()
    {
        var (cut, _) = RenderFilm();
        var nav = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();

        Assert.Equal("Se7en", Heading(cut));

        nav.NavigateTo("/movies/601-88");
        cut.WaitForAssertion(() => Assert.Equal("The Peeled Edition", Heading(cut)));

        // And the new film's own facts, not the old one's: a page that re-read only the heading would pass
        // the assertion above while leaving Se7en's cast and collection on screen.
        Assert.Empty(cut.FindAll("ul.film-cast"));
        Assert.DoesNotContain("The Se7en Collection", cut.Find("main").TextContent, StringComparison.Ordinal);
    }

    /// <remarks>
    /// A second film is a second thing the reader needs telling about, so the announcement guard resets with
    /// the key. Without the reset the first film would claim the report for every film after it.
    /// </remarks>
    [Fact]
    public async Task ASecondFilmGetsItsOwnAnnouncementRatherThanInheritingTheFirstsSilence()
    {
        var (cut, clock) = RenderColdFilm();
        await SettledAsync(cut, "Se7en");
        clock.Advance(Announcer.SettleDelay);
        Assert.Equal("Se7en", Spoken().StatusText);

        // The catalogue is in memory now, so the second film arrives warm -- which is exactly the case the
        // guard silences. What must NOT survive is the first film's name standing in the region over the
        // second film's page.
        Nav().NavigateTo("/movies/601-88");
        await SettledAsync(cut, "The Peeled Edition");
        clock.Advance(Announcer.SettleDelay);

        Assert.Equal("", Spoken().StatusText);
    }

    // ============================================================ what the page leaves behind

    /// <remarks>
    /// R7: clear what this page set, and raise nothing new. Without it a film's name sits in
    /// #status-announcer, readable in browse mode, long after the reader has left the film -- the announcer
    /// is a scoped singleton and MainLayout survives navigation.
    /// <para>
    /// #route-announcer is deliberately NOT cleared, and this is the test that would fail if it were:
    /// RouteAnnouncer writes the next page's name during the same navigation, and a clear from this page's
    /// Dispose would land after it on any order where the component is disposed last, dropping the name of
    /// the page the reader has just arrived at.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task LeavingAFilmClearsWhatItSaidAndLeavesTheNextPagesNameAlone()
    {
        var (cut, clock) = RenderColdFilm();
        await SettledAsync(cut, "Se7en");
        clock.Advance(Announcer.SettleDelay);
        Assert.Equal("Se7en", Spoken().StatusText);

        Nav().NavigateTo("/movies");
        clock.Advance(Announcer.SettleDelay);

        Assert.Equal("", Spoken().AlertText);
        Assert.Equal("Films", Spoken().RouteText);
        Assert.DoesNotContain("Se7en", Spoken().StatusText, StringComparison.Ordinal);
    }
}

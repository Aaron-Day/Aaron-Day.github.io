using System.Text.RegularExpressions;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Microsoft.Extensions.DependencyInjection;
using MovieLibrary.Core.Models;
using MovieLibrary.Web.Models;
using MovieLibrary.Web.Pages;
using MovieLibrary.Web.Services;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// The films page's three states, and the twelve things Task 5 promised about them.
/// <para>
/// Rendered through <c>App</c>, like the rest of this suite: the live regions, the landmarks and the drawer
/// all live in MainLayout, and the announcer's text can only be read where that layout is mounted.
/// </para>
/// <para>
/// WHAT bUNIT CANNOT SHOW, so that a green run here is not mistaken for more than it is. It cannot show that
/// a message was raised AFTER the render that carries its state rather than before: its renderer commits in
/// the same clock instant as the call, so a settle timer armed early and one armed late fall due together.
/// The announcer's own remarks say so. That ordering is pinned as source text at the bottom of this file and
/// is on the device list for a browser with a MutationObserver. It cannot show where focus actually lands
/// either -- there is no layout and no accessibility tree here -- so the focus contract is asserted as the
/// calls that implement it, and whether a reader speaks the container it lands on is Task 9's to find out on
/// real hardware.
/// </para>
/// </summary>
public class MoviesStateTests : BunitContext
{
    private const string FocusHelper = "movieLibrary.focusIfUnmoved";

    /// <summary>
    /// What <c>ElementReference.FocusAsync()</c> invokes, which is how a test can break the OTHER focus call
    /// the page makes. bUnit's own <c>VerifyFocusAsyncInvoke()</c> watches this same identifier.
    /// </summary>
    private const string BuiltInFocus = "Blazor._internal.domWrapper.focus";

    public MoviesStateTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    /// <summary>
    /// Makes the focus helper answer. The matcher overload is required: the identifier-only Setup
    /// overload matches only an invocation with NO arguments, so it would silently not apply and Loose
    /// mode would hand back false -- which reads in a test exactly like a refused focus move.
    /// </summary>
    private void FocusHelperAnswers(bool moved) =>
        JSInterop.Setup<bool>(FocusHelper, _ => true).SetResult(moved);

    /// <summary>Renders the real app at /movies with the catalogue behaving as asked, and returns the clock.</summary>
    private (IRenderedComponent<MovieLibrary.Web.App> Cut, ManualTimeProvider Clock) RenderWith(CatalogOutcome outcome)
    {
        var clock = this.AddAnnouncer(outcome);
        return (this.RenderAppAt("/movies"), clock);
    }

    private (IRenderedComponent<MovieLibrary.Web.App> Cut, ManualTimeProvider Clock, StubCatalog Catalog) RenderScripted(
        params Func<Task<HttpResponseMessage>>[] responders)
    {
        var (clock, stub) = this.AddAnnouncerAndCatalog(responders);
        return (this.RenderAppAt("/movies"), clock, stub);
    }

    private Announcer Spoken() => Services.GetRequiredService<Announcer>();

    private static string Sentence(IRenderedComponent<MovieLibrary.Web.App> cut) =>
        cut.Find(".catalog-state p").TextContent.Trim();

    private static IReadOnlyList<AngleSharp.Dom.IElement> TryAgain(IRenderedComponent<MovieLibrary.Web.App> cut) =>
        cut.FindAll("button.try-again");

    // ============================================================ criterion 2: the title, the heading, one container

    /// <remarks>
    /// Criterion 2. FocusOnNavigate fires once after the first render and never retries, so a heading behind
    /// a state branch would silently never receive focus -- and movies.json arrives long after that render.
    /// Asserted in all three states, because "outside any state branch" is exactly the claim.
    /// </remarks>
    [Theory]
    [InlineData(CatalogOutcome.Ready)]
    [InlineData(CatalogOutcome.Failed)]
    [InlineData(CatalogOutcome.NeverCompletes)]
    public void TheHeadingIsInTheFirstRenderWhateverTheFetchDid(CatalogOutcome outcome)
    {
        var (cut, _) = RenderWith(outcome);

        var h1 = cut.Find("h1");
        Assert.Equal("Films", h1.TextContent.Trim());
        Assert.Equal("-1", h1.GetAttribute("tabindex"));
    }

    [Theory]
    [InlineData(CatalogOutcome.Ready)]
    [InlineData(CatalogOutcome.Failed)]
    [InlineData(CatalogOutcome.NeverCompletes)]
    public void OneStableContainerHoldsTheStateAndItIsTheSameSingleParagraphEveryTime(CatalogOutcome outcome)
    {
        var (cut, _) = RenderWith(outcome);

        // One container, one paragraph, in every state. Three sibling branches that swapped nodes would
        // destroy the node a screen reader's virtual cursor may be sitting on.
        Assert.Single(cut.FindAll(".catalog-state"));
        Assert.Single(cut.FindAll(".catalog-state p"));
        Assert.Equal("-1", cut.Find(".catalog-state").GetAttribute("tabindex"));
    }

    [Fact]
    public void TheDocumentTitleIsSetEvenWhenTheFetchFailed()
    {
        this.AddAnnouncer(CatalogOutcome.Failed);   // before anything resolves a service from the provider

        var head = Render<HeadOutlet>();
        this.RenderAppAt("/movies");

        head.WaitForAssertion(() =>
            Assert.Equal("Films - Film and TV library", head.Find("title").TextContent));
    }

    // ============================================================ the three sentences, verbatim

    [Fact]
    public void LoadingShowsTheLoadingCopyAndNoButton()
    {
        var (cut, _) = RenderWith(CatalogOutcome.NeverCompletes);

        Assert.Equal("Loading films…", Sentence(cut));
        Assert.Empty(TryAgain(cut));
    }

    [Fact]
    public void FailedShowsTheOneFailureSentenceAndARealButton()
    {
        var (cut, _) = RenderWith(CatalogOutcome.Failed);

        Assert.Equal(Movies.CouldNotLoad, Sentence(cut));
        Assert.Equal(
            "The film library could not be loaded. Check your connection and try again.",
            Movies.CouldNotLoad);

        var button = Assert.Single(TryAgain(cut));
        Assert.Equal("button", button.GetAttribute("type"));
        Assert.Equal("Try again", button.TextContent.Trim());
    }

    [Fact]
    public void ReadyShowsTheCountWithItsNoun()
    {
        var (cut, _) = RenderWith(CatalogOutcome.Ready);

        Assert.Equal("The library holds 3 films.", Sentence(cut));
        Assert.Empty(TryAgain(cut));
    }

    [Fact]
    public void OneFilmIsSingular()
    {
        var (cut, _, _) = RenderScripted(StubCatalog.Json(
            """[{"i":807,"k":"807-110","t":"Se7en","rt":110,"g":[],"ag":"Thriller","vote":8.4,"dir":[],"cast":[],"mc":0.95}]"""));

        Assert.Equal("The library holds 1 film.", Sentence(cut));
    }

    /// <remarks>
    /// Criterion 2. A successful fetch of nothing is not a failure and is not a filtered-out grid either. It
    /// must never reach Task 8's "No titles match these filters.", which would be false twice over: no filter
    /// is involved and nothing is being hidden.
    /// </remarks>
    [Fact]
    public void AnEmptyLibraryGetsItsOwnSentenceAndNotACountOfZero()
    {
        var (cut, _, _) = RenderScripted(StubCatalog.Json("[]"));

        Assert.Equal("The library is empty.", Sentence(cut));
        Assert.Equal(Movies.EmptyLibrary, Sentence(cut));
        Assert.DoesNotContain("0", Sentence(cut), StringComparison.Ordinal);
        Assert.DoesNotContain("filter", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(TryAgain(cut));
    }

    // ============================================================ criterion 6: the loading announcement

    [Fact]
    public void APageStillLoadingAfterItsFirstRenderSaysSo()
    {
        var (_, clock) = RenderWith(CatalogOutcome.NeverCompletes);

        clock.Advance(Announcer.SettleDelay);

        Assert.Equal("Loading films", Spoken().StatusText);
        Assert.Equal("", Spoken().AlertText);
    }

    /// <remarks>
    /// Criterion 6, and the premise it rests on. On a return visit the catalogue is cached, the task handed
    /// back is already complete, ComponentBase produces no intermediate render, and the page is Ready on its
    /// FIRST render -- so the loading branch never runs and nothing is ever said about a page that had
    /// already loaded. This asserts the premise, because it is the part that could change: the page never
    /// shows the loading copy at all when the task is already done.
    /// </remarks>
    [Fact]
    public void APageThatWasReadyOnItsFirstRenderNeverEntersTheLoadingState()
    {
        var (cut, clock) = RenderWith(CatalogOutcome.Ready);

        Assert.Equal("The library holds 3 films.", Sentence(cut));

        clock.Advance(Announcer.SettleDelay);

        Assert.Equal("The library holds 3 films.", Spoken().StatusText);
        Assert.DoesNotContain("Loading", Spoken().StatusText, StringComparison.Ordinal);
    }

    /// <remarks>
    /// "At most once per visit". A second call would empty the region at once and only refill it a settle
    /// delay later, so reading the region immediately after extra renders is what distinguishes one call
    /// from two: if it had been raised again this would be the empty string.
    /// </remarks>
    [Fact]
    public void TheLoadingAnnouncementIsRaisedOnceHoweverManyTimesThePageRenders()
    {
        var (cut, clock) = RenderWith(CatalogOutcome.NeverCompletes);
        clock.Advance(Announcer.SettleDelay);
        Assert.Equal("Loading films", Spoken().StatusText);

        cut.FindComponent<Movies>().Render();
        cut.FindComponent<Movies>().Render();

        Assert.Equal("Loading films", Spoken().StatusText);
    }

    // ============================================================ criterion 9: no count while loading or failed

    [Theory]
    [InlineData(CatalogOutcome.Failed)]
    [InlineData(CatalogOutcome.NeverCompletes)]
    public void NoCountIsShownOrSpokenWhileLoadingOrFailed(CatalogOutcome outcome)
    {
        var (cut, clock) = RenderWith(outcome);

        clock.Advance(Announcer.SettleDelay);

        // A count is a claim about the library, and neither of these states knows anything about it.
        Assert.DoesNotMatch(@"\d", Sentence(cut));
        Assert.DoesNotContain("holds", Sentence(cut), StringComparison.Ordinal);
        Assert.DoesNotContain("holds", Spoken().StatusText, StringComparison.Ordinal);
    }

    // ============================================================ criterion 3: one sentence, seen and spoken

    [Fact]
    public void TheFailureIsSpokenInExactlyTheWordsItIsShownIn()
    {
        var (cut, clock) = RenderWith(CatalogOutcome.Failed);

        clock.Advance(Announcer.SettleDelay);

        Assert.Equal(Sentence(cut), Spoken().AlertText);
        Assert.Equal(Movies.CouldNotLoad, Spoken().AlertText);
    }

    /// <remarks>
    /// R11, as far as the DELIVERED text can show it: the sentence arrives whole and once, with nothing joined
    /// on to it.
    /// <para>
    /// This cannot stand in for a count of how often the page alerted, and an earlier remark here claimed it
    /// could -- it said the region accumulates duplicates, so a second alert would show up as two sentences.
    /// It does not: <c>Announcer.Publish</c> drops an exact duplicate joining a pending message
    /// (<c>PendingParts.Contains</c>), and the class remarks say so. A page alerting twice with the same words
    /// is invisible here however the dispatcher schedules it, which is why the guard has
    /// <see cref="OneFailureIsReportedOnceHoweverManyTimesThePageRenders"/> below -- a region READ IMMEDIATELY
    /// after extra renders, where a second call shows as the empty string.
    /// </para>
    /// </remarks>
    [Fact]
    public void OneFailureIsOneSentenceAndNotTwo()
    {
        var (_, clock) = RenderWith(CatalogOutcome.Failed);

        clock.Advance(Announcer.SettleDelay);

        Assert.Equal(Movies.CouldNotLoad, Spoken().AlertText);
        Assert.Single(Regex.Matches(Spoken().AlertText, Regex.Escape(Movies.CouldNotLoad)));
    }

    /// <remarks>
    /// The falsifying test the <c>failureReported</c> guard did not have, and it is deterministic where an
    /// assertion on the delivered text is not. Technique borrowed from
    /// <see cref="TheLoadingAnnouncementIsRaisedOnceHoweverManyTimesThePageRenders"/>: a second
    /// <c>Alert</c> empties the region SYNCHRONOUSLY and only refills it a settle delay later, so reading the
    /// region immediately after extra renders is what distinguishes one call from two. If the guard is
    /// neutralised this is the empty string. Nothing here depends on dispatcher scheduling.
    /// </remarks>
    [Fact]
    public void OneFailureIsReportedOnceHoweverManyTimesThePageRenders()
    {
        var (cut, clock) = RenderWith(CatalogOutcome.Failed);
        clock.Advance(Announcer.SettleDelay);
        Assert.Equal(Movies.CouldNotLoad, Spoken().AlertText);

        cut.FindComponent<Movies>().Render();
        cut.FindComponent<Movies>().Render();

        Assert.Equal(Movies.CouldNotLoad, Spoken().AlertText);
    }

    /// <remarks>
    /// The same for the count, whose <c>countAnnounced</c> guard had no falsifying test either. A second
    /// <c>Status</c> empties the region at once, so a repeat shows here as the empty string.
    /// </remarks>
    [Fact]
    public void TheCountIsAnnouncedOnceHoweverManyTimesThePageRenders()
    {
        var (cut, clock) = RenderWith(CatalogOutcome.Ready);
        clock.Advance(Announcer.SettleDelay);
        Assert.Equal("The library holds 3 films.", Spoken().StatusText);

        cut.FindComponent<Movies>().Render();
        cut.FindComponent<Movies>().Render();

        Assert.Equal("The library holds 3 films.", Spoken().StatusText);
    }

    /// <remarks>
    /// <b>The failure is spoken once, by ONE channel.</b> The focus move is the primary one: a screen reader
    /// reads the contents of a focused unnamed <c>tabindex="-1"</c> container, and this container's entire text
    /// is the failure sentence -- so a move that happens has already said it. An <c>Alert</c> raised beside it
    /// is the same words again, interrupting the first reading a settle delay later, which is the hazard R3
    /// exists for and is total overlap here rather than partial.
    /// <para>
    /// Both halves in one theory, and the page is rendered TWICE in each, because the obvious implementation
    /// of this ordering has a trap in it: the "already moved" flag is sticky, so on a second render of the same
    /// failure "no move happened on this pass" is true again and a late alert reintroduces the double speech.
    /// A second, separate "this failure has been reported" flag is what makes the count hold across renders.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AFailureSpeaksExactlyOnceByEitherChannelAndNeverByBoth(bool focusMoves)
    {
        FocusHelperAnswers(focusMoves);

        var (cut, clock) = RenderWith(CatalogOutcome.Failed);
        cut.FindComponent<Movies>().Render();
        clock.Advance(Announcer.SettleDelay);

        // The move happened, so the container's contents are the utterance and the assertive region stays
        // silent. It did not, so the region is the utterance. Never both, and never one of them twice.
        Assert.Equal(focusMoves ? "" : Movies.CouldNotLoad, Spoken().AlertText);

        // And the sentence is on screen either way, which is what the focus move reads out.
        Assert.Equal(Movies.CouldNotLoad, Sentence(cut));
    }

    /// <remarks>
    /// Criterion 8 survives the reordering: a retry that fails again must not be met with silence. Focus is on
    /// the Try again button by then, the button is not a keeper, the helper refuses, and the alert fires because
    /// no move happened. (Where the browser does not focus a button on click the move happens instead and the
    /// container is read -- still one utterance. See the remarks on Movies.FocusKeepers.)
    /// </remarks>
    [Fact]
    public async Task ARetryThatFailsAgainIsSpokenRatherThanMetWithSilence()
    {
        FocusHelperAnswers(false);
        var (cut, clock, _) = RenderScripted(
            AnnouncerTestExtensions.Responder(CatalogOutcome.Failed),
            AnnouncerTestExtensions.Responder(CatalogOutcome.Failed));

        clock.Advance(Announcer.SettleDelay);
        await cut.Find("button.try-again").ClickAsync(new MouseEventArgs());
        clock.Advance(Announcer.SettleDelay);

        Assert.Equal(Movies.CouldNotLoad, Spoken().AlertText);
        Assert.Equal(Movies.CouldNotLoad, Sentence(cut));
    }

    // ============================================================ criterion 4: no live region in page content

    [Theory]
    [InlineData(CatalogOutcome.Ready)]
    [InlineData(CatalogOutcome.Failed)]
    [InlineData(CatalogOutcome.NeverCompletes)]
    public void NothingInsideMainSpeaksForItself(CatalogOutcome outcome)
    {
        var (cut, _) = RenderWith(outcome);

        // The RENDERED half of SourceContractTests.NoComponentOutsideTheLayoutDeclaresALiveRegion, which can
        // only read source text: this sees the attributes as they reach the DOM, computed ones included. The
        // announcer is the only speaking channel, and the deterministic channel for the failure is the focus
        // move, not a second region.
        var main = cut.Find("main");

        Assert.Empty(main.QuerySelectorAll("[aria-live]"));
        Assert.Empty(main.QuerySelectorAll("[role='alert'],[role='status'],[role='log']"));
    }

    // ============================================================ criterion 7: the focus contract

    /// <remarks>
    /// Criterion 7, first transition, and C4's reason for it. SC 4.1.3 Status Messages (Level AA) applies
    /// only to messages that do NOT receive focus, so moving focus to the failure text takes the message out
    /// of that criterion's scope rather than depending on a live region this project has already recorded as
    /// unreliable on iOS VoiceOver and TalkBack. The helper is in index.html because the guard needs
    /// document.activeElement, which Blazor cannot read.
    /// </remarks>
    [Fact]
    public void AnInitialFailureTakesFocusToTheFailureText()
    {
        FocusHelperAnswers(true);

        RenderWith(CatalogOutcome.Failed);

        JSInterop.VerifyInvoke(FocusHelper, 1);
    }

    /// <remarks>
    /// The guard, which is the whole reason the move goes through JavaScript rather than through
    /// ElementReference.FocusAsync(). The KEEPERS are the places the page itself puts focus; anything else
    /// means the user moved it, and the move is refused. The Try again button is deliberately not among them,
    /// which is what makes criterion 7's second transition work -- a failed retry leaves focus on the button,
    /// because the button is not a keeper and the helper refuses.
    /// What this cannot prove: that the refusal actually happens. That is the JavaScript's job and belongs on
    /// the device list with a real drawer open.
    /// </remarks>
    [Fact]
    public void TheFocusMoveIsOfferedOnlyThePlacesThePageItselfPutFocus()
    {
        FocusHelperAnswers(true);

        RenderWith(CatalogOutcome.Failed);

        var keepers = Assert.IsType<string[]>(
            JSInterop.Invocations[FocusHelper].Single().Arguments[1]);

        Assert.Equal(new[] { "#main-content", "#app h1" }, keepers);
        Assert.DoesNotContain("try-again", string.Join(" ", keepers), StringComparison.Ordinal);
    }

    [Fact]
    public void TheFocusMoveIsNotRepeatedOnceItHasHappened()
    {
        FocusHelperAnswers(true);
        var (cut, _) = RenderWith(CatalogOutcome.Failed);

        cut.FindComponent<Movies>().Render();
        cut.FindComponent<Movies>().Render();

        JSInterop.VerifyInvoke(FocusHelper, 1);
    }

    /// <remarks>
    /// The other half: a refused move leaves the flag alone, so a user who had the drawer open when the fetch
    /// failed is not denied the move for ever. It is offered again on the next render, and refused again for
    /// as long as their focus is still theirs.
    /// </remarks>
    [Fact]
    public void ARefusedFocusMoveIsOfferedAgainRatherThanGivenUpOn()
    {
        FocusHelperAnswers(false);
        var (cut, _) = RenderWith(CatalogOutcome.Failed);

        cut.FindComponent<Movies>().Render();

        JSInterop.VerifyInvoke(FocusHelper, 2);
    }

    /// <remarks>
    /// Criterion 7, third transition. The button is gone on a successful retry, so without a deliberate move
    /// focus drops to the body. It goes to the h1 -- which is why that heading declares tabindex="-1": the
    /// attribute is a dependency of this feature, not a conformance claim, and may be asserted as one.
    /// </remarks>
    [Fact]
    public async Task ASuccessfulRetryTakesFocusToTheHeadingBecauseTheButtonHasGone()
    {
        FocusHelperAnswers(true);
        var (cut, _, _) = RenderScripted(
            AnnouncerTestExtensions.Responder(CatalogOutcome.Failed),
            AnnouncerTestExtensions.Responder(CatalogOutcome.Ready));

        await cut.Find("button.try-again").ClickAsync(new MouseEventArgs());

        Assert.Equal("The library holds 3 films.", Sentence(cut));
        Assert.Empty(TryAgain(cut));
        JSInterop.VerifyFocusAsyncInvoke();
    }

    /// <remarks>
    /// The focus call is awaited, and a render can arrive while it is suspended -- the announcer delivers a
    /// message from a timer callback, MainLayout re-renders on it, and this page re-renders with it. Without
    /// a guard that render would see the "already moved" flag still false and issue a second focus call, and
    /// the page would fight itself for focus. The handler is deliberately left without a result so the first
    /// call really is in flight for the duration of this test.
    /// </remarks>
    [Fact]
    public void ARenderArrivingWhileTheFocusCallIsInFlightDoesNotIssueASecondOne()
    {
        var pending = JSInterop.Setup<bool>(FocusHelper, _ => true);
        var (cut, _) = RenderWith(CatalogOutcome.Failed);

        JSInterop.VerifyInvoke(FocusHelper, 1);

        cut.FindComponent<Movies>().Render();

        JSInterop.VerifyInvoke(FocusHelper, 1);

        pending.SetResult(true);
    }

    // ============================================================ criterion 8: the retry in flight

    /// <remarks>
    /// Criterion 8. Pressing the button on a slow phone has to do something visible, so the page goes back to
    /// the loading copy while the second fetch is in the air. The gate holds the response so the middle of
    /// the retry can be read at all: without it the stub answers inside the click and only the end state is
    /// ever observable.
    /// </remarks>
    [Fact]
    public async Task ARetryInFlightShowsTheLoadingCopy()
    {
        var gate = new StubCatalog.Gate();
        var (cut, _, _) = RenderScripted(
            AnnouncerTestExtensions.Responder(CatalogOutcome.Failed),
            gate.Holding(AnnouncerTestExtensions.Responder(CatalogOutcome.Ready)));

        var click = cut.Find("button.try-again").ClickAsync(new MouseEventArgs());

        Assert.Equal(Movies.LoadingVisible, Sentence(cut));

        gate.Open();
        await click;

        Assert.Equal("The library holds 3 films.", Sentence(cut));
    }

    /// <remarks>
    /// Criterion 8, and the reason it is spelled out as a rule: the disabled ATTRIBUTE blurs the element it is
    /// put on, so a user who pressed Try again would lose focus to the body at the exact moment they were
    /// told to wait -- the same defect criterion 7 fixes at the other end. aria-disabled says the same thing
    /// to a reader and moves nothing, and the click that still arrives is ignored in the handler instead.
    /// </remarks>
    [Fact]
    public async Task TheButtonStaysAndIsNeverGivenTheDisabledAttributeWhileARetryIsInFlight()
    {
        var gate = new StubCatalog.Gate();
        var (cut, _, _) = RenderScripted(
            AnnouncerTestExtensions.Responder(CatalogOutcome.Failed),
            gate.Holding(AnnouncerTestExtensions.Responder(CatalogOutcome.Failed)));

        var before = cut.Find("button.try-again");
        Assert.Null(before.GetAttribute("aria-disabled"));

        var click = before.ClickAsync(new MouseEventArgs());

        var during = Assert.Single(TryAgain(cut));
        Assert.Equal("true", during.GetAttribute("aria-disabled"));
        Assert.False(during.HasAttribute("disabled"));

        gate.Open();
        await click;

        Assert.Null(Assert.Single(TryAgain(cut)).GetAttribute("aria-disabled"));
    }

    /// <remarks>
    /// The click the aria-disabled button still receives. A second fetch while the first is in the air would
    /// leave two awaits racing to write <c>films</c>, and the loser would overwrite the winner.
    /// </remarks>
    [Fact]
    public async Task AReClickWhileTheRetryIsInFlightIsIgnoredRatherThanStartingASecondFetch()
    {
        var gate = new StubCatalog.Gate();
        var (cut, _, stub) = RenderScripted(
            AnnouncerTestExtensions.Responder(CatalogOutcome.Failed),
            gate.Holding(AnnouncerTestExtensions.Responder(CatalogOutcome.Ready)));

        var first = cut.Find("button.try-again").ClickAsync(new MouseEventArgs());
        var second = cut.Find("button.try-again").ClickAsync(new MouseEventArgs());

        // Deliberately NOT awaited: a handler that ignored the click has already returned, and one that
        // started a second fetch would still be sitting on the gate -- so the broken version fails here in
        // milliseconds instead of hanging the suite on a task nothing will ever complete.
        Assert.True(second.IsCompleted,
            "The second click started work of its own. Two awaits then race to write films, and whichever "
            + "finishes last wins -- so a retry that succeeded can be overwritten by one that failed.");

        Assert.Equal(2, stub.RequestsFor(StubCatalog.Movies));   // the first fetch and this retry, not three

        gate.Open();
        await first;
        await second;
    }

    // ============================================================ criterion 10: a state, not a toast

    /// <remarks>
    /// Criterion 10. A message that cleared itself would be a content-set time limit on reading it --
    /// SC 2.2.1 Timing Adjustable, Level A -- and an auto-retry would engage the same criterion. Ten minutes
    /// is far past any plausible toast lifetime, and the clock is the test's so none of it is spent waiting.
    /// </remarks>
    [Fact]
    public void TheFailureNeverDismissesItselfAndNothingRetriesOnItsOwn()
    {
        var (cut, clock, stub) = RenderScripted(AnnouncerTestExtensions.Responder(CatalogOutcome.Failed));

        clock.Advance(TimeSpan.FromMinutes(10));
        cut.FindComponent<Movies>().Render();

        Assert.Equal(Movies.CouldNotLoad, Sentence(cut));
        Assert.Single(TryAgain(cut));
        Assert.Equal(1, stub.RequestsFor(StubCatalog.Movies));
    }

    /// <remarks>
    /// The same for the loading copy, which is the other thing a timeout would be tempting on: a page that
    /// gave up after n seconds and showed the failure would be inventing a failure that has not happened.
    /// </remarks>
    [Fact]
    public void AFetchThatIsSimplySlowIsNeverTurnedIntoAFailureByTheClock()
    {
        var (cut, clock, stub) = RenderScripted(AnnouncerTestExtensions.Responder(CatalogOutcome.NeverCompletes));

        clock.Advance(TimeSpan.FromMinutes(10));
        cut.FindComponent<Movies>().Render();

        Assert.Equal(Movies.LoadingVisible, Sentence(cut));
        Assert.Empty(TryAgain(cut));
        Assert.Equal(1, stub.RequestsFor(StubCatalog.Movies));
    }

    // ============================================================ criterion 11: on the way out

    /// <remarks>
    /// Criterion 11 and R7. Both calls are cancellations: they take effect at once and speak nothing.
    /// Without them "The library holds 3 films." sits in the status region, readable in browse mode, long
    /// after the films are gone -- the announcer is a scoped singleton and MainLayout outlives the page.
    /// </remarks>
    [Fact]
    public async Task LeavingThePageClearsBothRegionsAtOnce()
    {
        var (_, clock) = RenderWith(CatalogOutcome.Ready);
        clock.Advance(Announcer.SettleDelay);
        Assert.Equal("The library holds 3 films.", Spoken().StatusText);

        await DisposeComponentsAsync();

        Assert.Equal(string.Empty, Spoken().StatusText);
        Assert.Equal(string.Empty, Spoken().AlertText);
    }

    /// <remarks>
    /// And nothing new is raised on the way out: a farewell sentence would be delivered on the page the user
    /// has just arrived at, by a region that page is still mounted next to.
    /// </remarks>
    [Fact]
    public async Task NothingNewIsSpokenAfterThePageHasGone()
    {
        var (_, clock) = RenderWith(CatalogOutcome.Failed);

        await DisposeComponentsAsync();
        clock.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal(string.Empty, Spoken().StatusText);
        Assert.Equal(string.Empty, Spoken().AlertText);
    }

    /// <remarks>
    /// Criterion 11's other half, and the one with a real cost if it is got wrong. The fetch task is shared
    /// and cached by the service, so cancelling it on the way out would turn one user's navigation away into
    /// a Failed result for the NEXT visit to this page. The gate keeps the fetch in flight across the
    /// dispose, which is the only state in which cancelling it could do that damage.
    /// </remarks>
    [Fact]
    public async Task LeavingThePageMidFetchDoesNotSpoilTheCatalogueForTheNextVisit()
    {
        var gate = new StubCatalog.Gate();
        var (_, _, stub) = RenderScripted(gate.Holding(AnnouncerTestExtensions.Responder(CatalogOutcome.Ready)));

        await DisposeComponentsAsync();
        gate.Open();

        var again = await Services.GetRequiredService<CatalogService>().GetMoviesAsync();

        Assert.Equal(CatalogState.Ready, again.State);
        Assert.Equal(3, again.Value.Count);
        Assert.Equal(1, stub.RequestsFor(StubCatalog.Movies));   // the same task, not a second fetch
    }

    // ============================================================ criterion 12: no exception path escapes

    /// <remarks>
    /// Criterion 12, the first half: every way a fetch can go wrong lands on Failed, and none of them leaves
    /// the page on the loading copy for ever. The second half -- that no fetch failure reaches
    /// #blazor-error-ui -- cannot be read here at all: that bar lives in index.html, outside the render tree,
    /// and the runtime reveals it for an unhandled exception. What IS proved is the condition that reveals
    /// it: bUnit rethrows an exception that escapes a component, so a render that returns a sentence at all
    /// is a render that raised nothing. HostPageTests owns the bar's own markup.
    /// </remarks>
    [Theory]
    [InlineData("dropped connection")]
    [InlineData("captive portal")]
    [InlineData("server error")]
    [InlineData("no content")]
    [InlineData("json null")]
    [InlineData("asynchronous throw")]
    public void NoWayOfFailingLeavesThePageLoadingOrEscapesToTheErrorBar(string how)
    {
        var (cut, clock, _) = RenderScripted(how switch
        {
            "dropped connection" => StubCatalog.Throws(new HttpRequestException("the network went away")),
            "captive portal" => StubCatalog.Html(),
            "server error" => StubCatalog.Status(System.Net.HttpStatusCode.InternalServerError),
            "no content" => StubCatalog.NoContent(),
            "json null" => StubCatalog.Json("null"),
            _ => StubCatalog.ThrowsAfterYielding(new HttpRequestException("the network went away mid-body")),
        });

        // A failure that arrives after a yield reaches the page one render later, which is what it does in a
        // browser. The wait is bounded; a page that stayed on the loading copy fails here by timing out.
        cut.WaitForAssertion(() => Assert.Equal(Movies.CouldNotLoad, Sentence(cut)));

        Assert.NotEqual(Movies.LoadingVisible, Sentence(cut));
        Assert.Single(TryAgain(cut));

        // R7: whoever writes a standing statement clears it. After an asynchronous failure the polite region
        // was armed with the loading line by the Loading render, and must not still be saying it.
        clock.Advance(Announcer.SettleDelay);
        Assert.Equal("", Spoken().StatusText);
    }

    /// <remarks>
    /// The focus helper is the one call this page makes into JavaScript, and a missing or broken one must not
    /// take the page down with it. An escape here would reveal #blazor-error-ui -- which carries an assertive
    /// role of its own -- on a page already reporting a failure through the announcer: two assertive sources
    /// for one failure, which is the thing this project forbids outright.
    /// </remarks>
    [Fact]
    public void ABrokenFocusHelperIsSwallowedRatherThanTakingThePageDown()
    {
        JSInterop.Setup<bool>(FocusHelper, _ => true)
            .SetException(new JSException("movieLibrary.focusIfUnmoved is not a function"));

        var (cut, clock) = RenderWith(CatalogOutcome.Failed);
        clock.Advance(Announcer.SettleDelay);

        Assert.Equal(Movies.CouldNotLoad, Sentence(cut));
        Assert.Equal(Movies.CouldNotLoad, Spoken().AlertText);   // the failure is still announced
    }

    /// <remarks>
    /// The mirror of the test above, for the OTHER focus call. The successful retry moves focus with
    /// <c>ElementReference.FocusAsync()</c>, which is interop just as much as the helper is -- it invokes
    /// <c>Blazor._internal.domWrapper.focus</c> -- and it was the one of the two that had no guard. An escape
    /// would reveal <c>#blazor-error-ui</c> with its own assertive role, on a page that has just RECOVERED from
    /// a failure, so the user would be told about a failure that no longer exists. Both calls now go through
    /// one guard, and this is the half that proves the second of them is inside it.
    /// </remarks>
    [Fact]
    public async Task ABrokenFocusCallOnTheSuccessfulRetryIsSwallowedToo()
    {
        FocusHelperAnswers(false);
        JSInterop.SetupVoid(BuiltInFocus, _ => true).SetException(new JSException("the element has gone"));

        var (cut, clock, _) = RenderScripted(
            AnnouncerTestExtensions.Responder(CatalogOutcome.Failed),
            AnnouncerTestExtensions.Responder(CatalogOutcome.Ready));

        await cut.Find("button.try-again").ClickAsync(new MouseEventArgs());

        // The retry succeeded, the page says so, and the count is still announced: the broken focus call cost
        // the user their place on the page and nothing else.
        Assert.Equal("The library holds 3 films.", Sentence(cut));
        clock.Advance(Announcer.SettleDelay);
        Assert.Equal("The library holds 3 films.", Spoken().StatusText);
    }

    // ============================================================ M11: the strings themselves

    /// <remarks>
    /// Three full stops are read out as three full stops by some readers and silently by others. The
    /// ellipsis character is one glyph with one meaning, and the spoken copy drops it either way.
    /// </remarks>
    [Fact]
    public void TheLoadingCopyUsesTheEllipsisCharacterRatherThanThreeFullStops()
    {
        Assert.EndsWith("…", Movies.LoadingVisible, StringComparison.Ordinal);
        Assert.DoesNotContain("...", Movies.LoadingVisible, StringComparison.Ordinal);
    }

    /// <remarks>
    /// The two loading strings differ in punctuation ONLY. That is deliberate and is not the drift criterion
    /// 3 forbids: an ellipsis is noise read aloud. Anything else differing between them would be drift.
    /// </remarks>
    [Fact]
    public void TheSpokenLoadingCopyDiffersFromTheVisibleOneInPunctuationOnly()
    {
        Assert.Equal(Movies.LoadingSpoken, Movies.LoadingVisible.TrimEnd('…', '.', ' '));
        Assert.NotEqual(Movies.LoadingVisible, Movies.LoadingSpoken);
    }

    /// <remarks>
    /// An empty library is not an empty filter result. Task 8 adds "No titles match these filters.", which
    /// would be a false statement here -- no filter is involved and nothing is being hidden.
    /// </remarks>
    [Fact]
    public void TheEmptyLibrarySentenceDoesNotBlameAFilterThatDoesNotExistYet()
    {
        Assert.Equal("The library is empty.", Movies.EmptyLibrary);
        Assert.DoesNotContain("filter", Movies.EmptyLibrary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("match", Movies.EmptyLibrary, StringComparison.OrdinalIgnoreCase);
    }

    // ============================================================ the catalogue stub the whole suite shares

    /// <remarks>
    /// Not about the page. <c>AnnouncerTestExtensions</c> is where every bUnit test in this suite registers
    /// its catalogue, and it scripted movies.json alone -- so the first Task 6 or 7 component to call
    /// <c>GetMetaAsync()</c> would have hit the stub's unscripted-path guard, had the refusal swallowed by
    /// <c>CatalogService</c>'s broad catch, and rendered a FAILED meta (no build date, no genre names) with
    /// every test in the suite still green. An outcome now covers both documents.
    /// <para>
    /// This is also what makes the meta script a failable subject rather than a decorative one, rule T1:
    /// nothing else in this suite reads meta yet, so without this test the responder could be deleted with
    /// nothing going red.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(CatalogOutcome.Ready, CatalogState.Ready)]
    [InlineData(CatalogOutcome.Failed, CatalogState.Failed)]
    public async Task TheSharedCatalogueStubAnswersForMetaAndNotOnlyForMovies(
        CatalogOutcome outcome, CatalogState expected)
    {
        this.AddAnnouncer(outcome);

        var meta = await Services.GetRequiredService<CatalogService>().GetMetaAsync();

        Assert.Equal(expected, meta.State);
        Assert.Equal(CatalogMeta.TmdbAttribution, meta.Value.Attribution);   // m3, on both paths
        if (expected == CatalogState.Ready) Assert.Equal(3, meta.Value.MovieCount);   // the same three films
    }

    /// <remarks>
    /// The other door into the helper, and the reason it needs its own test: a test that scripts its own
    /// movies responses -- a retry -- goes through <c>AddAnnouncerAndCatalog</c>, whose meta script
    /// <c>AddAnnouncer</c> overwrites. Without this the default is unfalsifiable (T1): deleting it leaves the
    /// theory above green, because that one re-scripts meta itself.
    /// </remarks>
    [Fact]
    public async Task AHandScriptedCatalogueStillAnswersForMeta()
    {
        var (_, stub) = this.AddAnnouncerAndCatalog(AnnouncerTestExtensions.Responder(CatalogOutcome.Ready));

        var meta = await Services.GetRequiredService<CatalogService>().GetMetaAsync();

        Assert.Equal(CatalogState.Ready, meta.State);
        Assert.Empty(stub.Unscripted);
    }

    /// <remarks>
    /// The "assert none occurred" half of the stub's unscripted-path guard, pointed at the page rather than at
    /// the service. Today the page fetches movies.json and nothing else, so this is a guard rather than a
    /// finding: the moment Task 6 or 7 adds a second document to a render, an unscripted one shows up here
    /// instead of arriving as a silent Failed through <c>CatalogService</c>'s broad catch.
    /// </remarks>
    [Theory]
    [InlineData(CatalogOutcome.Ready)]
    [InlineData(CatalogOutcome.Failed)]
    [InlineData(CatalogOutcome.NeverCompletes)]
    public void RenderingThePageAsksForNothingTheStubWasNotScriptedFor(CatalogOutcome outcome)
    {
        var (_, stub) = this.AddAnnouncerAndCatalog(AnnouncerTestExtensions.Responder(outcome));
        stub.Serves(StubCatalog.Meta, AnnouncerTestExtensions.MetaResponder(outcome));

        this.RenderAppAt("/movies");

        Assert.Empty(stub.Unscripted);
    }

    // ============================================================ criterion 5: WHERE the announcements are raised

    /// <summary>
    /// The text of one member's body, braces balanced, so a contract can say what a method does NOT contain.
    /// </summary>
    /// <remarks>
    /// Deliberately naive: it counts every brace, including any inside a string literal or a comment. The
    /// four members it is used on contain none, and <c>TheBodyMatcherReadsBalancedBracesAndNotJustTheFirstOne</c>
    /// below pins the behaviour it does have. Something cleverer would need a C# parser to prove a property
    /// that a brace count already proves.
    /// <para>
    /// This is here because bUnit cannot show the thing criterion 5 is about. Its renderer commits in the
    /// same clock instant as the call, so a message raised BEFORE the render that carries its state and one
    /// raised after fall due together, and no assertion on the delivered text can tell them apart. The
    /// ordering is a property of the source, so it is asserted on the source.
    /// </para>
    /// </remarks>
    internal static string MemberBody(string source, string signature)
    {
        var at = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(at >= 0,
            "\"" + signature + "\" is not in the source any more. This contract is about where that member "
            + "raises its announcements, so a renamed or resignatured member needs the contract updated, not "
            + "deleted: it would otherwise pass by finding nothing.");

        var open = source.IndexOf('{', at + signature.Length);
        Assert.True(open >= 0, "\"" + signature + "\" has no body.");

        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source[(open + 1)..i];
        }

        Assert.Fail("\"" + signature + "\" has an unbalanced body, which would not compile.");
        return string.Empty;
    }

    private static string Page() => WebRoot.Read("Pages", "Movies.razor");

    /// <remarks>
    /// Criterion 5 and rule R4. A message raised before the first render starts its settle timer while the
    /// clearing render is still queued, so the real gap becomes "delay minus handler time" and collapses on
    /// exactly the slow devices the gap exists for.
    /// </remarks>
    [Fact]
    public void TheFetchMethodAnnouncesNothingAtAll()
    {
        var body = MemberBody(Page(), "protected override async Task OnInitializedAsync()");

        Assert.Contains("await FetchAsync()", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Announcer.", body, StringComparison.Ordinal);

        // Task 8 moved the awaits into one method both the first load and the retry call, so the contract
        // follows them there: that method is the fetch now, and it must say nothing either.
        var fetch = MemberBody(Page(), "private async Task<CatalogResult<IReadOnlyList<MovieRecord>>> FetchAsync()");

        Assert.Contains("Catalog.GetMoviesAsync()", fetch, StringComparison.Ordinal);
        Assert.DoesNotContain("Announcer.", fetch, StringComparison.Ordinal);
    }

    /// <remarks>
    /// Criterion 8's second half, as source text, because it is the half no rendered assertion can reach:
    /// <c>Alert("")</c> and the next <c>Alert(sentence)</c> must never be raised in the same synchronous
    /// handler. The second call would see the region already empty, mark the clear observed, and so prevent
    /// RenderCommitted re-anchoring the settle delay -- and on a device slow enough to flush both mutations
    /// in one frame the repeated identical sentence is no DOM change, so nothing is spoken. The user retries,
    /// it fails again, and they hear silence.
    /// </remarks>
    [Fact]
    public void TheClickHandlerOnlyEverCancelsAndNeverSpeaks()
    {
        var body = MemberBody(Page(), "private async Task TryAgain()");

        Assert.Equal(["Announcer.Alert(\"\")"], AnnouncerCalls(body));
    }

    /// <remarks>Criterion 11: two cancellations on the way out, and nothing new.</remarks>
    [Fact]
    public void DisposeClearsBothRegionsAndRaisesNothing()
    {
        var body = MemberBody(Page(), "public void Dispose()");

        Assert.Equal(["Announcer.Status(\"\")", "Announcer.Alert(\"\")"], AnnouncerCalls(body));
        Assert.DoesNotContain("Cancel", body, StringComparison.Ordinal);   // never the shared fetch
    }

    /// <remarks>
    /// The other side of the same contract: the announcements with words in them all live in the one place
    /// that runs after the render carrying their state. Without this half, moving a call out of
    /// OnAfterRenderAsync and into the handler would satisfy every other test on this page.
    /// </remarks>
    [Fact]
    public void EverySentenceThisPageSpeaksIsRaisedAfterTheRenderThatCarriesIt()
    {
        var page = Page();
        var after = MemberBody(page, "protected override async Task OnAfterRenderAsync(bool firstRender)");

        Assert.Equal(
            ["Announcer.Status(LoadingSpoken)", "Announcer.Status(\"\")", "Announcer.Alert(CouldNotLoad)",
             "Announcer.Status(OpeningSentence)"],   // the clear is the Failed arm's R7 duty
            AnnouncerCalls(after));

        // Task 8 adds ONE more place that speaks: the count after a filter change. It is not a second way to
        // skip the render, it is later than one by construction -- it runs from a timer armed by the change
        // and fires AnnounceDebounceMs after the last of them -- and CountsAreOnlyEverRaisedFromTheTimer
        // below pins that nothing else calls it.
        var count = MemberBody(page, "internal void AnnounceCount()");
        Assert.Equal(["Announcer.Status(sentence)"], AnnouncerCalls(count));

        // THE STAR'S REFUSAL IS THE ONE SENTENCE RAISED FROM A HANDLER, and it is allowed there for a
        // reason the retry's is not. What this contract is really protecting is the clear-then-speak pair:
        // Alert("") followed in the same synchronous handler by Alert(sentence) would mark the clear
        // observed before any render committed, and a repeated identical sentence is then no DOM change at
        // all -- the reader hears silence. This handler raises no clear and speaks ONCE EVER, guarded by a
        // field that never goes back to false, so there is no second identical utterance for a settle
        // window to swallow. It cannot wait for a render either: nothing re-renders on a storage refusal
        // except the paragraph this same press puts on screen, which lands in the very next render pass.
        var refused = MemberBody(page, "private void OnStorageRefused()");
        Assert.Equal(["Announcer.Alert(FavouriteButton.CouldNotSave)"], AnnouncerCalls(refused));

        // And they are raised nowhere else: every call in the file is in one of the members above, the
        // TryAgain cancellation, or Dispose's two.
        Assert.Equal(
            AnnouncerCalls(page).Count,
            AnnouncerCalls(after).Count + AnnouncerCalls(count).Count + AnnouncerCalls(refused).Count + 1 + 2);
    }

    /// <remarks>
    /// The count is the one statement this page makes from a place other than OnAfterRenderAsync, and it is
    /// safe there only because it runs on a debounce timer: a handler that called it directly would announce
    /// per keystroke, and the announcer restarts its settle delay on every call, so nothing would ever be
    /// delivered. One definition, one reference, and the reference is the timer's callback.
    /// </remarks>
    [Fact]
    public void CountsAreOnlyEverRaisedFromTheDebounceTimer()
    {
        var page = Page();

        Assert.Equal(2, Regex.Matches(page, @"\bAnnounceCount\b").Count);   // its definition, and one use

        var scheduler = MemberBody(page, "private void ScheduleCountAnnouncement()");
        Assert.Contains("InvokeAsync(AnnounceCount)", scheduler, StringComparison.Ordinal);
        Assert.Contains("Time.CreateTimer", scheduler, StringComparison.Ordinal);
    }

    /// <remarks>
    /// Criterion 7's ordering, third transition. Focus first, then the count: landing on the h1 announces
    /// "Films, heading level 1", which does not tell the user the retry worked, and R3 forbids announcing the
    /// text of whatever focus has just moved to. So the count follows as its own statement.
    /// </remarks>
    [Fact]
    public void TheSuccessfulRetryFocusesBeforeItAnnounces()
    {
        var body = MemberBody(Page(), "protected override async Task OnAfterRenderAsync(bool firstRender)");

        var focus = body.IndexOf("await FocusOutright(heading)", StringComparison.Ordinal);
        var count = body.IndexOf("Announcer.Status(OpeningSentence)", StringComparison.Ordinal);

        Assert.True(focus >= 0 && count >= 0, "both calls have to be present for their order to mean anything");
        Assert.True(focus < count,
            "The count is announced before focus moves to the heading. A reader then speaks the count, the "
            + "focus move interrupts it, and the user is told \"Films, heading level 1\" instead.");
    }

    /// <summary>Every <c>Announcer.X(arg)</c> call in some source text, in the order they appear.</summary>
    private static IReadOnlyList<string> AnnouncerCalls(string source) =>
        Regex.Matches(source, @"Announcer\.(?:Status|Alert|Route)\([^)]*\)").Select(m => m.Value).ToList();

    /// <remarks>
    /// T4 on the matcher itself. A brace matcher that stopped at the first closing brace would end every body
    /// at its first nested block, and all five contracts above would then be asserting about a fragment --
    /// passing, and proving nothing.
    /// </remarks>
    [Fact]
    public void TheBodyMatcherReadsBalancedBracesAndNotJustTheFirstOne()
    {
        const string source = "void A() { if (x) { y(); } z(); }\nvoid B() { w(); }";

        Assert.Equal(" if (x) { y(); } z(); ", MemberBody(source, "void A()"));
        Assert.Equal(" w(); ", MemberBody(source, "void B()"));
    }

    /// <remarks>
    /// And it fails loudly on a signature it cannot find, rather than handing back an empty body that every
    /// "does not contain" assertion above would be satisfied by. Renaming a member has to break these tests.
    /// </remarks>
    [Fact]
    public void TheBodyMatcherRefusesASignatureItCannotFind()
        => Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => MemberBody("void A() { }", "void Gone()"));

    /// <remarks>
    /// T4 on the call scan: it must see a call with an argument, which the real contracts depend on, and must
    /// not match a mention of the word in a comment or a different type's method.
    /// </remarks>
    [Theory]
    [InlineData("Announcer.Status(\"\");", "Announcer.Status(\"\")")]
    [InlineData("Announcer.Alert(CouldNotLoad);", "Announcer.Alert(CouldNotLoad)")]
    [InlineData("await Thing.Announcer.Route(x);", "Announcer.Route(x)")]
    public void TheAnnouncerCallScanFindsACallWithItsArgument(string source, string expected)
        => Assert.Equal([expected], AnnouncerCalls(source));

    [Theory]
    [InlineData("// the announcer is the only speaking channel")]
    [InlineData("Announcer.Changed += OnChanged;")]
    [InlineData("private Announcer Spoken() => null;")]
    [InlineData("Other.Status(\"12 films\");")]
    public void TheAnnouncerCallScanDoesNotCryWolf(string source)
        => Assert.Empty(AnnouncerCalls(source));
}

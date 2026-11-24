using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MovieLibrary.Web.Pages;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// The series page: its four states, what it renders about a series, and the one condition that changes
/// nearly all of it.
/// <para>
/// <b>THE TRUST CONDITION IS WHAT THIS CLASS IS MOSTLY ABOUT.</b> A series whose match scored below the
/// accept threshold may carry a different series' episode list, so the page drops every denominator and
/// every "Missing" mark and lists only the episodes the owner has. That is one boolean -- <c>Trusted</c> --
/// read in five places, and a regression in any one of them puts a claim about the owner's library on screen
/// on the authority of a match nobody trusts. Both sides of it are rendered here, from two fixture series
/// that differ in nothing else that matters.
/// </para>
/// <para>
/// Rendered through <c>App</c>, like the rest of this suite: the live regions, the landmarks and the drawer
/// live in MainLayout, so a page rendered on its own would be missing most of what it is judged against.
/// </para>
/// <para>
/// The focus moves and the announcements are NOT re-litigated here. <c>FilmDetailTests</c> pins the same
/// four-state machinery -- the interop call on a failure, the outright move after a successful retry, the
/// polite region armed once and cleared on arrival -- and this page runs the same code through the same
/// helper. What is here is what is different about a SERIES: seasons, episodes, and the trust condition.
/// </para>
/// </summary>
public class ShowDetailTests : BunitContext
{
    public ShowDetailTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    private IRenderedComponent<MovieLibrary.Web.App> Render(
        string slug = AnnouncerTestExtensions.ShowSlug, CatalogOutcome outcome = CatalogOutcome.Ready)
    {
        this.AddAnnouncer(outcome);
        return this.RenderAppAt("/tv/" + slug);
    }

    private static IReadOnlyList<string> Text(IRenderedComponent<MovieLibrary.Web.App> cut, string selector)
        => cut.FindAll(selector).Select(e => e.TextContent.Trim()).ToList();

    /// <summary>The seasons in the order the page renders them, each with its episode rows.</summary>
    private static IReadOnlyList<IElement> Seasons(IRenderedComponent<MovieLibrary.Web.App> cut)
        => cut.FindAll("details.season").ToList();

    /// <summary>
    /// One string per episode row, each the row's spans in DOM order joined by a space -- so an assertion
    /// reads as the row reads, and WHERE a mark sits in the row is part of what is asserted. The whole
    /// TextContent cannot be used: Razor's own indentation lands between the spans.
    /// </summary>
    private static IReadOnlyList<string> Rows(IElement season)
        => season.QuerySelectorAll("li")
            .Select(li => string.Join(" ", li.QuerySelectorAll("span").Select(s => s.TextContent.Trim())))
            .ToList();

    /// <remarks>
    /// The byline's three facts and the ABSENCE of a fourth. A confident match renders no match span at all
    /// -- <c>ConfidenceBand.BadgeFor</c> returns null for it -- and a page that printed "Matched" there
    /// would be telling the reader something the other two bands' own words make unnecessary.
    /// </remarks>
    [Fact]
    public void TheBylineNamesTheYearTheSeasonsAndTheEpisodesWithTheirTotal()
    {
        var cut = Render();

        Assert.Equal("The Bureau", cut.Find("h1").TextContent);
        Assert.Equal(["2015", "2 seasons", "7 of 9 episodes"], Text(cut, ".show-byline span"));
        Assert.Empty(cut.FindAll(".show-match"));
    }

    /// <remarks>
    /// <b>The defect this exists for was on screen.</b> The page counted <c>Seasons.Length</c> and the tile
    /// that linked to it counted numbered seasons only, so the owner's copy of 24 was "10 seasons" on one
    /// page and "9 seasons" one click away, with nothing on either to say which was right. The fixture
    /// carries THREE season records of which two are numbered, so a page that counts records fails here.
    /// </remarks>
    [Fact]
    public void TheSeasonCountLeavesOutTheUnplacedBucketEverywhereItIsStated()
    {
        var cut = Render();

        Assert.Equal(3, Seasons(cut).Count);
        Assert.Contains("2 seasons", Text(cut, ".show-byline span"));
        Assert.Contains("2 seasons", Text(cut, ".facts dd"));
    }

    /// <remarks>
    /// The build writes season 0 FIRST and this page renders it last, so the order is changed in C# where
    /// the DOM order and the reading order stay the same thing. Changing it in CSS instead would put a
    /// keyboard reader through the disclosures in one order and a sighted reader in another.
    /// </remarks>
    [Fact]
    public void TheUnplacedBucketIsCalledSeasonUnknownAndComesAfterTheNumberedSeasons()
    {
        var cut = Render();

        Assert.Equal(
            ["Season 1, 3 of 4 episodes", "Season 2, 2 of 2 episodes", "Season unknown, 2 of 2 episodes"],
            Text(cut, "details.season > summary"));
    }

    /// <remarks>
    /// "Missing" is VISIBLE TEXT at the end of the row. Not a colour (SC 1.4.1 Use of Color), not an icon
    /// with no text (SC 1.1.1) and not an attribute a sighted reader cannot see. The assertion is on the
    /// row's whole text in order, because where the mark sits is the part a reader scanning numbers and
    /// titles depends on.
    /// </remarks>
    [Fact]
    public void AnEpisodeTheOwnerDoesNotHaveIsMarkedMissingAtTheEndOfItsRow()
    {
        var cut = Render();
        var season1 = Seasons(cut)[0];

        Assert.Equal(
            ["1 Pilot", "2 The Second", "3 The Missing One Missing", "4 The Fourth"],
            Rows(season1));

        // One, from the one unowned episode in the fixture: a page that marked every row, or none, passes
        // the row assertion above on a different fixture and fails here.
        Assert.Single(cut.FindAll(".episode-missing"));
        Assert.Equal("Missing", cut.Find(".episode-missing").TextContent);
    }

    /// <remarks>
    /// Where the match IS trusted the list is the whole season, owned or not, because the page's subject is
    /// then the series and the gap in the library is the thing worth seeing.
    /// </remarks>
    [Fact]
    public void EveryEpisodeOfASeasonIsListedWhereTheMatchIsTrusted()
    {
        var cut = Render();

        Assert.Equal([4, 2, 2], Seasons(cut).Select(s => Rows(s).Count));
    }

    /// <remarks>
    /// <b>The untrusted side of the one condition.</b> Both of these are the same defect seen twice: a
    /// denominator states a gap in the owner's library, and a "Missing" mark names an episode he may never
    /// have had, each on the authority of a match that scored below the accept threshold. The fixture's
    /// season 1 holds owned and unowned episodes together, so a page that merely dropped the MARK would
    /// still leave the unowned rows here with nothing to tell them apart.
    /// </remarks>
    [Fact]
    public void AnUncertainMatchListsOnlyWhatTheOwnerHasAndMarksNothingMissing()
    {
        var cut = Render(AnnouncerTestExtensions.UncertainShowSlug);

        Assert.Equal(["1 Owned One", "3 Owned Two"], Rows(Seasons(cut)[0]));
        Assert.Empty(Rows(Seasons(cut)[1]));
        Assert.Empty(cut.FindAll(".episode-missing"));
    }

    /// <remarks>
    /// The counts an untrusted page MAY state: how many episodes the owner has, with no total beside them,
    /// in the byline, in the season names and in the Details list -- the three places the trusted form
    /// appears -- and the band's own words in the same breath so the shorter form has a reason.
    /// </remarks>
    [Theory]
    [InlineData(AnnouncerTestExtensions.UncertainShowSlug, "Needs review", "2 episodes", "Season 1, 2 episodes")]
    [InlineData(AnnouncerTestExtensions.UnmatchedShowSlug, "Not matched", "3 episodes", "Season unknown, 3 episodes")]
    public void AnUntrustedMatchDropsEveryTotalAndSaysWhyInTheBandsOwnWords(
        string slug, string badge, string episodes, string firstSeason)
    {
        var cut = Render(slug);

        Assert.Equal(badge, cut.Find(".show-match").TextContent);
        Assert.Contains(episodes, Text(cut, ".show-byline span"));
        Assert.Contains(episodes, Text(cut, ".facts dd"));
        Assert.Equal(firstSeason, Text(cut, "details.season > summary")[0]);
        Assert.DoesNotContain(" of ", string.Join(" | ", Text(cut, ".show-byline span")));
    }

    /// <remarks>
    /// The unmatched shape the real library has: no year and no poster, and every episode the owner holds
    /// in the unplaced bucket, so there is no numbered season to report and no Seasons row in the facts.
    /// </remarks>
    [Fact]
    public void AnUnmatchedSeriesRendersWhatItHasAndClaimsNothingItDoesNot()
    {
        var cut = Render(AnnouncerTestExtensions.UnmatchedShowSlug);

        Assert.Equal("An Unlisted Series", cut.Find("h1").TextContent);
        Assert.Equal(["3 episodes", "Not matched"], Text(cut, ".show-byline span"));
        Assert.Empty(cut.FindAll("img.show-poster"));
        Assert.Equal(["Genre", "Episodes"], Text(cut, ".facts dt"));
        Assert.Single(Seasons(cut));
    }

    /// <remarks>
    /// <b>The fourth state</b>, which no series in the owner's library is in today. Without it the page
    /// would render a heading, a byline and the Details list with a silent gap where the seasons belong. No
    /// "Seasons" heading either: a heading over nothing is a section that is not there.
    /// </remarks>
    [Fact]
    public void ASeriesWithNoSeasonsSaysSoAndRendersNoSeasonsHeading()
    {
        var cut = Render(AnnouncerTestExtensions.BareShowSlug);

        Assert.Equal(ShowDetail.NoEpisodeList, cut.Find(".no-episodes").TextContent);
        Assert.Empty(Seasons(cut));
        Assert.Equal(["Details"], Text(cut, "h2"));
        Assert.Equal(["Genre", "First aired"], Text(cut, ".facts dt"));
    }

    /// <remarks>
    /// A slug no series answers is a wrong ADDRESS and not a fetch that failed, so it offers no Try again:
    /// no number of retries will resolve it, and a button that cannot help is worse than none. The heading
    /// is the claim the page can stand behind -- this address, not the library.
    /// </remarks>
    [Fact]
    public void AnAddressNoSeriesAnswersSaysSoAndOffersNoTryAgain()
    {
        var cut = Render("a-slug-no-series-has");

        Assert.Equal(ShowDetail.NotFoundHeading, cut.Find("h1").TextContent);
        Assert.Equal(ShowDetail.NoSuchShow, cut.Find(".show-state p").TextContent);
        Assert.Empty(cut.FindAll(".try-again"));
        Assert.Empty(Seasons(cut));
    }

    /// <remarks>
    /// And the failure, which is the one that DOES offer the button. "Series unavailable" deliberately does
    /// not say the series does not exist: a failed fetch knows nothing at all about that.
    /// </remarks>
    [Fact]
    public void AFailedFetchSaysSoAndOffersTryAgain()
    {
        var cut = Render(outcome: CatalogOutcome.Failed);

        Assert.Equal(ShowDetail.UnavailableHeading, cut.Find("h1").TextContent);
        Assert.Equal(ShowDetail.CouldNotLoad, cut.Find(".show-state p").TextContent);
        Assert.NotNull(cut.Find("button.try-again"));
    }

    /// <remarks>
    /// SC 3.2.3 Consistent Navigation and SC 3.2.4 Consistent Identification: "Details" and "Additional
    /// information" say the same thing about a series as about a film, at the same level, so a reader who
    /// has learned one page has learned the other. "Seasons" is the only heading with no counterpart,
    /// because a film has no seasons.
    /// </remarks>
    [Fact]
    public void TheHeadingsAreTheFilmPagesHeadingsAtTheSameLevel()
    {
        var series = Render();

        using var film = new BunitContext();
        film.JSInterop.Mode = JSRuntimeMode.Loose;
        film.AddAnnouncer();

        var filmHeadings = film.RenderAppAt(AnnouncerTestExtensions.FilmAddress)
            .FindAll("h2").Select(h => h.TextContent.Trim()).ToList();

        Assert.Equal(["Seasons", "Details", "Additional information"], Text(series, "h2"));
        Assert.Contains("Details", filmHeadings);
        Assert.Contains("Additional information", filmHeadings);

        // No h3 anywhere: a heading per season would put every season in the rotor's heading list with its
        // contents hidden whenever the disclosure is closed, which reads as an empty section to anyone
        // navigating by heading. The seasons are siblings under one heading instead.
        Assert.Empty(series.FindAll("h3"));
    }

    /// <remarks>
    /// <b>A row is content, not a control.</b> The owner's largest series is 494 rows and the biggest the
    /// page can meet is 559; a tab stop apiece would make the keyboard route past this page longer than the
    /// page itself (SC 2.4.3 Focus Order is about a sensible order, and the shortest sensible order here has
    /// nothing in the list at all). The only focusable things in the Seasons section are the disclosures the
    /// browser gives us.
    /// </remarks>
    [Fact]
    public void NothingInAnEpisodeRowIsFocusableAndNoRowCarriesALabel()
    {
        var cut = Render();

        foreach (var season in Seasons(cut))
        {
            Assert.Empty(season.QuerySelectorAll("li a, li button, li input, li select, li [tabindex]"));
            Assert.Empty(season.QuerySelectorAll("li [aria-label], li [aria-labelledby], li [role]"));
        }

        // The disclosures themselves, so the assertion above is not reading an empty section: three summary
        // elements, which are the page's only interactive elements outside the shell.
        Assert.Equal(3, cut.FindAll("details.season > summary").Count);
    }

    /// <remarks>
    /// <c>@key</c> is on the episode NUMBER, so two rows in one season sharing a number would make Blazor's
    /// diff ambiguous -- and the unplaced bucket is where that nearly happened: a fixture that numbered
    /// every unplaced episode 0 looked exactly right on screen. <c>TvPipeline</c> numbers them from 1.
    /// </remarks>
    [Fact]
    public void NoTwoEpisodeRowsInOneSeasonShareTheirNumber()
    {
        var cut = Render();

        foreach (var season in Seasons(cut))
        {
            var numbers = season.QuerySelectorAll(".episode-number").Select(n => n.TextContent).ToList();

            Assert.NotEmpty(numbers);
            Assert.Equal(numbers.Count, numbers.Distinct(StringComparer.Ordinal).Count());
        }
    }

    /// <remarks>
    /// The poster is DECORATIVE: the title is the h1 immediately above it, so alt text here would be the
    /// series named twice. The dimensions are the intrinsic size of a w342 rendition, which is what lets the
    /// browser reserve the box before the TMDB CDN answers -- it matters most under magnification.
    /// </remarks>
    [Fact]
    public void TheSeriesPosterIsDecorativeAndNamesNothing()
    {
        var poster = Render().Find("img.show-poster");

        Assert.Equal("", poster.GetAttribute("alt"));
        Assert.Equal("342", poster.GetAttribute("width"));
        Assert.Equal("513", poster.GetAttribute("height"));
        Assert.Equal("lazy", poster.GetAttribute("loading"));
    }

    /// <remarks>
    /// The owner's own columns from the record store: the part of the page no code here interprets. A series
    /// with none of them renders no section at all rather than an empty list under a heading.
    /// </remarks>
    [Fact]
    public void TheOwnersOwnColumnsAreTheAdditionalInformationSectionAndNothingElseIs()
    {
        var withExtra = Render();

        Assert.Contains("Additional information", Text(withExtra, "h2"));
        Assert.Equal(["Genre", "First aired", "Seasons", "Episodes", "Shelf"], Text(withExtra, ".facts dt"));
        Assert.Contains("C1", Text(withExtra, ".facts dd"));

        using var bare = new BunitContext();
        bare.JSInterop.Mode = JSRuntimeMode.Loose;
        bare.AddAnnouncer();

        var noExtra = bare.RenderAppAt("/tv/" + AnnouncerTestExtensions.UncertainShowSlug);

        Assert.DoesNotContain(
            "Additional information", noExtra.FindAll("h2").Select(h => h.TextContent.Trim()));
    }

    /// <remarks>
    /// <b>Why the page fetches in OnParametersSetAsync and not OnInitializedAsync.</b> Blazor reuses this
    /// component when the router moves between two addresses on the same template, so initialising once
    /// would leave the second series rendering the FIRST one's episodes under its own title -- a page that
    /// is wrong about the owner's library and looks entirely healthy. The link out of a series' own page
    /// does not exist yet; the browser's address bar is enough to reach this.
    /// </remarks>
    [Fact]
    public void MovingFromOneSeriesToAnotherRendersTheSecondSeriesEpisodes()
    {
        var cut = Render();
        Assert.Equal("The Bureau", cut.Find("h1").TextContent);

        Services.GetRequiredService<NavigationManager>()
            .NavigateTo("/tv/" + AnnouncerTestExtensions.UncertainShowSlug);

        Assert.Equal("A Doubtful Series", cut.Find("h1").TextContent);
        Assert.Equal(["1 Owned One", "3 Owned Two"], Rows(Seasons(cut)[0]));
    }
}

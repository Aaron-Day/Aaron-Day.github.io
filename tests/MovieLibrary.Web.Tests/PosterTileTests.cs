using Bunit;
using MovieLibrary.Core.Models;
using MovieLibrary.Web.Components;
using MovieLibrary.Web.Models;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// The tile. Three of its rules cannot be retrofitted and so are each pinned here: every tile carries
/// visible text, the action slot is a sibling of the text block rather than inside it, and truncation
/// happens in CSS or not at all.
/// <para>
/// What this file cannot prove: anything needing layout. Whether a long title is READABLE at the narrowest
/// column is a browser question; what is provable here is that the whole string is in the DOM and that the
/// stylesheet clips nothing, which is what StyleContractTests.TheTileTitleWrapsRatherThanClipping covers.
/// </para>
/// </summary>
public class PosterTileTests : BunitContext
{
    /// <summary>
    /// A record with every field filled, so a test can vary the one it is about. The defaults are a
    /// confident match; <see cref="Unmatched"/> is the other end of the range.
    /// </summary>
    private static MovieRecord Film(
        string title = "Se7en", int? year = 1995, string? poster = "/se7en.jpg", int id = 807,
        string key = "807-110") =>
        new(id, key, title, year, 110, poster, "/se7en-bd.jpg", [80, 9648], "Thriller", 8.4, ["David Fincher"],
            "Two detectives.", "Seven deadly sins.", [new CastMember("Brad Pitt", "Mills", "/bp.jpg")],
            null, "Blu-ray", 0.95);

    /// <summary>
    /// What a real unmatched row looks like: TMDB id 0, no poster, no year, and Vote 0d. THIRTY of the
    /// 1,488 published records are this shape -- counted from the published movies.json, where every record
    /// with no rating is also one with no id -- and they are the records the owner wants to be able to SEE,
    /// so a tile that rendered them as an empty box, or that invented a score for them, would defeat the
    /// point of the page.
    /// </summary>
    private static MovieRecord Unmatched() =>
        new(0, "u-d2f1c6e2", "An Unlisted Film", null, 101, null, null, [], "Documentary", 0d, [],
            null, null, [], null, null, 0d);

    private IRenderedComponent<PosterTile> RenderTile(MovieRecord film) =>
        Render<PosterTile>(p => p.Add(x => x.Tile, TileView.ForFilm(film)));

    [Theory]
    [InlineData("Se7en")]
    [InlineData("The Lord of the Rings: The Fellowship of the Ring")]
    [InlineData("Birdman or (The Unexpected Virtue of Ignorance)")]
    public void TheTitleIsVisibleTextAndIsNeverShortened(string title)
    {
        var cut = RenderTile(Film(title: title));

        // The WHOLE title, character for character. Cutting it in C# would destroy the accessible name
        // permanently, and the two long ones here are the real titles that a two-line clamp would cut.
        Assert.Equal(title, cut.Find(".tile-title").TextContent);
    }

    /// <remarks>
    /// "NOTHING ELSE PRETENDS TO EXIST" IS ABOUT THE ABSENT FIELDS, not about the whole tile. Since Task 7
    /// this record also renders a "Not matched" badge, which is a real piece of information about the record
    /// rather than a placeholder standing in for data it does not have -- so the name still holds, but it
    /// reads looser than it did and the badge is deliberately not in the list below.
    /// <c>DegradedRecordTests</c> owns the badge's own assertions; this test owns the three absences.
    /// </remarks>
    [Fact]
    public void AnUnmatchedRecordStillRendersItsTitleAndNothingElsePretendsToExist()
    {
        var cut = RenderTile(Unmatched());

        Assert.Equal("An Unlisted Film", cut.Find(".tile-title").TextContent);
        Assert.Empty(cut.FindAll("img"));                      // no poster path, so no image at all
        Assert.Single(cut.FindAll("span.poster-placeholder"));
        Assert.Empty(cut.FindAll(".tile-year"));               // no year, so no empty year element

        // THE RATING, which is the absence most likely to be lost. Vote is 0d on every unmatched record,
        // so an unguarded tile prints "0.0/10" and states that a film nobody looked up scored zero -- about
        // the 30 records of 1,488 that this page exists to surface. The runtime is asserted present in the
        // same breath, because the guard must not take both: Rt is read from the sheet and no published
        // film has one at or below zero.
        Assert.Empty(cut.FindAll(".tile-rating"));
        Assert.Equal("1h 41m", cut.Find(".tile-runtime").TextContent);
    }

    /// <summary>
    /// A rated film shows the score WITH its scale, and a runtime in hours and minutes.
    /// </summary>
    /// <remarks>
    /// "8.4/10" and not a bare "8.4": alone, the number's meaning rests on its position in the tile, which
    /// is SC 1.3.1 Info and Relationships. It cannot be carried by title= or aria-label instead -- both are
    /// banned on this tile, by StandingAuditTests and by the assertion below this one.
    /// </remarks>
    [Theory]
    [InlineData(110, "1h 50m")]
    [InlineData(101, "1h 41m")]
    [InlineData(45, "45m")]       // under the hour, so no leading "0h"
    [InlineData(120, "2h")]       // exactly on the hour, so no trailing "0m"
    [InlineData(1353, "22h 33m")] // the longest published row, and never "1,353"
    public void TheRuntimeReadsAsHoursAndMinutesAndIsNeverGroupFormatted(int minutes, string expected)
    {
        var cut = Render<PosterTile>(p => p.Add(x => x.Tile, TileView.ForFilm(Film() with { Rt = minutes })));

        Assert.Equal(expected, cut.Find(".tile-runtime").TextContent);
    }

    [Fact]
    public void TheRatingCarriesItsOwnScaleAsVisibleText()
    {
        var cut = RenderTile(Film());

        Assert.Equal("8.4/10", cut.Find(".tile-rating").TextContent);
    }

    [Fact]
    public void ThePosterIsPrunedFromTheAccessibilityTreeAndReservesItsBoxBeforeItArrives()
    {
        var img = RenderTile(Film()).Find("img");

        // alt="" rather than alt="@Movie.T": the title is visible text in the same tile, so a description
        // here would announce it twice. PRESENT and empty -- a missing alt makes a reader read the file
        // name, and the two look identical in a browser.
        Assert.Equal("", img.GetAttribute("alt"));
        Assert.Equal("https://image.tmdb.org/t/p/w342/se7en.jpg", img.GetAttribute("src"));
        Assert.Equal("342", img.GetAttribute("width"));
        Assert.Equal("513", img.GetAttribute("height"));
        Assert.Equal("lazy", img.GetAttribute("loading"));
    }

    /// <remarks>
    /// A REGRESSION GUARD rather than coverage, and worth labelling as one: with the image emitted only
    /// inside a pattern match on a non-empty poster path, this assertion cannot fail today. It exists
    /// because the obvious "simplification" is src="@(Movie.P ?? "")", which renders broken-image chrome
    /// and in some browsers exposes the URL as text.
    /// </remarks>
    [Fact]
    public void NoImageIsEverEmittedWithAnEmptySrc()
    {
        foreach (var film in new[] { Film(), Unmatched(), Film(poster: "") })
            Assert.All(RenderTile(film).FindAll("img"),
                img => Assert.False(string.IsNullOrEmpty(img.GetAttribute("src"))));
    }

    [Fact]
    public void AnEmptyPosterPathIsTreatedAsNoPosterRatherThanAsOne()
    {
        var cut = RenderTile(Film(poster: ""));

        Assert.Empty(cut.FindAll("img"));
        Assert.Single(cut.FindAll("span.poster-placeholder"));
    }

    [Fact]
    public void TheYearIsAYearAndNotAFormattedNumber()
    {
        // N0 would render 1,995. Only the grid head's total is formatted.
        Assert.Equal("1995", RenderTile(Film(year: 1995)).Find(".tile-year").TextContent);
    }

    /// <remarks>
    /// C1. No /movies/{key} route exists and RouteAnnouncer maps only "" and "movies", so a link here
    /// would land all 1,456 tiles on the not-found page. Nothing in the tile is interactive in Part A,
    /// which is also why nothing in the grid is a tab stop.
    /// </remarks>
    [Fact]
    public void TheTileRendersExactlyOneLinkAndNothingElseInteractive()
    {
        var root = MarkupRules.Parse(RenderTile(Film()).Markup);

        // ONE anchor, and it is the whole of the tile's interactivity. The ban was on every anchor while
        // there was no /movies/{key} route to point one at; narrowed rather than deleted, as its own note
        // asked for, because what the rule is really about is that a tile is ONE target and not several.
        // Two anchors in a card -- a poster link and a title link, which is the usual way this goes wrong --
        // double the tab stops in the wall and read as two links to the same film.
        var anchor = Assert.Single(root.QuerySelectorAll("a"));

        Assert.Equal("movies/807-110", anchor.GetAttribute("href"));
        Assert.Equal(new[] { anchor }, MarkupRules.InteractiveElements(root));

        // NOTHING ELSE, and [role] stays in the list: the anchor's role is implicit and nothing in here
        // declares one. The list semantics live on the grid's ul and li, and the badge is content rather
        // than a role. If a later task does need a role in here, narrow this to the roles it is about
        // rather than deleting the assertion.
        Assert.Empty(root.QuerySelectorAll("button, input, select, textarea, [tabindex], [role]"));
    }

    /// <remarks>
    /// WHAT THE LINK IS NAMED BY, and in what order, because that is the string read on each of sixty
    /// tiles. A field moved out of the anchor, a field added to it, or the order of two of them swapped
    /// changes what a reader hears, and nothing else in the suite would catch it: the structural tests
    /// above ask where elements are and the text tests ask what each one says, and neither asks what the
    /// link as a whole announces. The poster is alt="" and contributes nothing.
    /// <para>
    /// <b>The PARTS are pinned, not one joined string, and that is a real limit rather than a style
    /// choice.</b> Razor emits these spans with no text between them at all, so <c>TextContent</c> is
    /// "Se7en19951h 50m8.4/10" -- the spaces a reader hears come from the accessible NAME algorithm, which
    /// separates each element child's contribution, and there is no accessibility tree here to compute it.
    /// Asserting a joined string would therefore be asserting a space this DOM does not contain. What this
    /// does hold is the content and the order, which is everything the algorithm is given.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheLinkIsNamedByTheFilmsOwnVisibleText()
    {
        var anchor = RenderTile(Film()).Find("a.tile-body");

        // Document order, which is what QuerySelectorAll returns and what the name algorithm walks.
        Assert.Equal(
            new[] { "Se7en", "1995", "1h 50m", "8.4/10" },
            anchor.QuerySelectorAll(".tile-title, .tile-year, .tile-runtime, .tile-rating")
                  .Select(e => e.TextContent.Trim()));
    }

    /// <remarks>
    /// A degraded record has fewer parts, and the name must still be the film: no year, no rating, and the
    /// badge OUTSIDE the anchor, which is what
    /// <c>DegradedRecordTests.TheBadgeIsASiblingOfTheTextBlockAndNeverInsideIt</c> pins from the other
    /// side. "An Unlisted Film, 1h 41m" and never "An Unlisted Film, 1h 41m, Not matched" -- thirty of the
    /// published records are this shape and they are the ones the owner most wants to open.
    /// </remarks>
    [Fact]
    public void AnUnmatchedFilmsLinkIsNamedByWhatTheRecordActuallyHas()
    {
        var cut = RenderTile(Unmatched());
        var anchor = cut.Find("a.tile-body");

        Assert.Equal(
            new[] { "An Unlisted Film", "1h 41m" },
            anchor.QuerySelectorAll(".tile-title, .tile-year, .tile-runtime, .tile-rating")
                  .Select(e => e.TextContent.Trim()));

        // The badge exists and is NOT in the name. Without this the assertion above would also pass over a
        // tile that had stopped rendering a badge at all, which is a different defect wearing the same green.
        Assert.Equal("Not matched", cut.Find(".badge").TextContent.Trim());
        Assert.Empty(anchor.QuerySelectorAll(".badge"));
    }

    /// <remarks>
    /// The slot's POSITION is the requirement, not its contents. Part B wraps <c>.tile-body</c> -- and
    /// nothing else -- in an anchor, so anything the slot holds has to be outside that element: a
    /// <c>&lt;button&gt;</c> inside an <c>&lt;a&gt;</c> is invalid and unusable. Fixing this later means
    /// moving markup that Task 7 will already have built against.
    /// </remarks>
    [Fact]
    public void ActionSlotContentIsASiblingOfTheTextBlockAndNeverInsideIt()
    {
        var cut = Render<PosterTile>(p => p
            .Add(x => x.Tile, TileView.ForFilm(Film()))
            .Add(x => x.ActionSlot, "<button type=\"button\" id=\"fav\">Favourite</button>"));

        var slotted = cut.Find("#fav");
        var body = cut.Find(".tile-body");

        Assert.False(body.Contains(slotted), "the slot's content is inside .tile-body, which is what Part "
                                             + "B's anchor will wrap -- a button inside an anchor.");
        Assert.Equal(cut.Find(".tile"), slotted.ParentElement);
        Assert.Equal(cut.Find(".tile"), body.ParentElement);
    }

    /// <remarks>
    /// A CONFIDENT film, and since Task 7 that matters: a degraded record's tile has a second child, the
    /// confidence badge, which is content rather than a control and sits in the same outside-.tile-body
    /// position for the same reason. <c>DegradedRecordTests.AConfidentTileRendersNoBadgeAtAll</c> reads
    /// this shape from the other side.
    /// </remarks>
    [Fact]
    public void TheSlotIsEmptyInPartASoTheTileHasNoControlOfItsOwn()
    {
        var cut = RenderTile(Film());

        Assert.Single(cut.FindAll(".tile > *"));                 // .tile-body and nothing beside it
        Assert.Equal("tile-body", cut.Find(".tile > *").ClassName);
    }

    /// <remarks>
    /// No label on the tile, deliberately: the visible title IS the name, and an aria-label would let the
    /// two diverge. There is also nothing here for a label to name -- the tile is not a widget in Part A.
    /// </remarks>
    [Fact]
    public void TheTileCarriesNoAuthorLabelAndNoTitleAttribute()
    {
        var root = MarkupRules.Parse(RenderTile(Film()).Markup);

        Assert.Empty(root.QuerySelectorAll("[aria-label], [aria-labelledby], [title]"));
    }

    /// <remarks>
    /// C1 again, from the other side: the tile must not create an id reference the app has to resolve.
    /// EveryIdReferenceResolvesToAnIdInTheSameRender is skipped precisely because the app has no failable
    /// reference, and 1,456 tiles each carrying one would be a poor way to end that.
    /// </remarks>
    [Fact]
    public void TheTileCreatesNoIdReferenceAndNoIdAtAll()
    {
        var root = MarkupRules.Parse(RenderTile(Film()).Markup);

        Assert.Empty(MarkupRules.IdReferences(root));
        Assert.Empty(root.QuerySelectorAll("[id]"));
    }
}

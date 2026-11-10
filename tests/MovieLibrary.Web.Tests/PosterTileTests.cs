using Bunit;
using MovieLibrary.Core.Models;
using MovieLibrary.Web.Components;

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
    /// What a real unmatched row looks like: TMDB id 0, no poster, no year. Twenty of the 1,456 published
    /// records are this shape, and they are the records the owner wants to be able to SEE, so a tile that
    /// rendered them as an empty box would defeat the point of the page.
    /// </summary>
    private static MovieRecord Unmatched() =>
        new(0, "u-d2f1c6e2", "An Unlisted Film", null, 101, null, null, [], "Documentary", 0d, [],
            null, null, [], null, null, 0d);

    private IRenderedComponent<PosterTile> RenderTile(MovieRecord film) =>
        Render<PosterTile>(p => p.Add(x => x.Movie, film));

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
    public void TheTileRendersNoAnchorAndNothingInteractive()
    {
        var root = MarkupRules.Parse(RenderTile(Film()).Markup);

        // [role] IS IN THE LIST HERE, and it is defensible here in a way it was not in the grid's own list
        // test, where the identical shape banned the role="listitem" PosterGrid's comment instructs a later
        // implementer to add. The tile genuinely carries no role in Part A and no role is pending for it:
        // the list semantics live on the grid's ul and li, the badge Task 7 adds is content rather than a
        // role, and Part B adds an anchor, whose role is implicit. So this is a rule with a subject it can
        // fail on rather than one forbidding correct future code -- but it is the same T5 shape, so if a
        // later task does need a role in here, narrow this to the roles it is about rather than deleting
        // the whole assertion.
        var candidates = root.QuerySelectorAll("a, button, input, select, textarea, [tabindex], [role]")
            .ToList();

        Assert.Empty(candidates);
        Assert.Empty(MarkupRules.InteractiveElements(root));
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
            .Add(x => x.Movie, Film())
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

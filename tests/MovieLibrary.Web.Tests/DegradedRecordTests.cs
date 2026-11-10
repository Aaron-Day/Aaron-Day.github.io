using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Bunit;
using MovieLibrary.Core.Matching;
using MovieLibrary.Core.Models;
using MovieLibrary.Web.Components;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// The degraded records and their confidence badges: the records the pipeline could not match at all, and
/// the ones it matched badly. Seeing them is the owner's whole reason for wanting this view, so they are a
/// first-class state here rather than an edge case, and the fixtures below are built to be genuinely
/// degraded rather than healthy records with a low score written on them.
/// <para>
/// <b>What this file deliberately does NOT assert, and why.</b> The plan's task text asked for
/// <c>AnUnmatchedTileIsStillALink</c> and for the badge text to sit "inside the link". Neither is
/// satisfiable and satisfying either would break the app: there is no <c>/movies/{key}</c> route in Part A
/// and <c>RouteAnnouncer</c>'s table maps only "" and "movies", so an anchor here would land all 1,456
/// tiles on the not-found page. <c>PosterTile.razor</c> records that decision in capitals, and
/// <c>PosterTileTests.TheTileRendersNoAnchorAndNothingInteractive</c> pins it. The worry behind the
/// instruction -- "a tile that is sometimes a link and sometimes inert is worse than a thin detail page" --
/// does not arise, because NO tile is a link in Part A and the treatment is already uniform. The valuable
/// assertion is the inverse one, so it is written instead:
/// <see cref="TheDegradedBranchesAddNoAnchorNoInteractiveElementAndNoLabel"/> makes this task unable to be
/// the one that re-breaks that decision.
/// </para>
/// <para>
/// It also asserts no record COUNTS. "The 17 unmatched films", the 20 of Task 1b and the 1,265-of-1,456
/// in <c>ConfidenceBand</c>'s own remarks describe different sets at different moments, and every one of
/// them moves with the Sheet and the TMDB cache. What is pinned here is the behaviour: every record
/// renders, each degraded band gets its own words, and the confident band gets nothing.
/// </para>
/// </summary>
public class DegradedRecordTests : BunitContext
{
    public DegradedRecordTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddAnnouncer();
    }

    // Expressed through MatchScorer.AcceptThreshold and never as 0.75. A literal here would let these
    // fixtures' idea of "confident" drift from the pipeline's silently, and the drift would show up as
    // tests asserting the wrong band rather than as a failure -- which is the reason ConfidenceBand reads
    // the threshold rather than spelling it.
    private const double Accepted = MatchScorer.AcceptThreshold + 0.20;
    private const double Rejected = MatchScorer.AcceptThreshold - 0.20;

    /// <summary>The ordinary case: a real id, a score at or above the threshold, a poster and a year.</summary>
    private static MovieRecord Confident() =>
        new(807, "807-110", "Se7en", 1995, 110, "/se7en.jpg", "/se7en-bd.jpg", [80, 9648], "Thriller", 8.4,
            ["David Fincher"], "Two detectives.", "Seven deadly sins.",
            [new CastMember("Brad Pitt", "Mills", "/bp.jpg")], null, "Blu-ray", Accepted);

    /// <summary>
    /// A poor match that was still accepted: a REAL TMDB id with a score below the threshold. A peeled
    /// edition -- the right film, matched on thin evidence -- which is why it keeps a year and an Apple
    /// genre while carrying no poster, no overview and no cast. The id is what makes it the Uncertain band
    /// and not the Unmatched one: <c>ConfidenceBand.Of</c> checks the id first, so a score of
    /// <see cref="Rejected"/> on id 0 would be Unmatched however low it went.
    /// </summary>
    private static MovieRecord Uncertain() =>
        new(601, "601-88", "The Peeled Edition", 1982, 88, null, null, [18], "Drama", 6.2,
            [], null, null, [], null, null, Rejected);

    /// <summary>
    /// What a real unmatched row looks like: TMDB id 0, and Sheet data alone. No poster, no year, no
    /// overview, no tagline, no cast, no genre ids -- and <c>Mc</c> 0 even where its best candidate scored
    /// something, which is the pipeline's behaviour and the reason the id has to be checked before the
    /// score. A fixture that gave this record a synthetic year and a four-person cast would render a tile
    /// indistinguishable from a healthy one and every branch below would pass over nothing; Task 6 wrote
    /// that hazard down in <c>PosterTile.razor</c> before this task started.
    /// </summary>
    private static MovieRecord Unmatched() =>
        new(0, "u-d2f1c6e2", "An Unlisted Film", null, 101, null, null, [], "Documentary", 0d,
            [], null, null, [], null, null, 0d);

    private static MovieRecord For(Confidence band) => band switch
    {
        Confidence.Unmatched => Unmatched(),
        Confidence.Uncertain => Uncertain(),
        _ => Confident(),
    };

    private IRenderedComponent<PosterTile> RenderTile(MovieRecord film) =>
        Render<PosterTile>(p => p.Add(x => x.Movie, film));

    /// <summary>The rendered tile, parsed, as the <c>.tile</c> element itself rather than a document body
    /// around it, so that "inside the tile" is a question this file can actually ask.</summary>
    private IElement Tile(MovieRecord film) =>
        MarkupRules.Parse(RenderTile(film).Markup).QuerySelector(".tile")!;

    private static string? BadgeOf(IElement tile) => tile.QuerySelector(".badge")?.TextContent.Trim();

    // ================================================================ the subject set, before the rules

    /// <remarks>
    /// RULE T1, FIRST, because this is the defect this project keeps shipping: a contract green over a
    /// subject set whose members were all absent or all exempt. Everything below loops over or renders
    /// these three records, so if two of them collapsed into one band -- a fixture edited to a score on
    /// the wrong side of the threshold, a band renamed, <c>ConfidenceBand.Of</c>'s id-first precedence
    /// dropped -- the rest of this file would still be green while covering two bands or one. The bands
    /// are read from <c>ConfidenceBand.Of</c> rather than from the method names, because the name is what
    /// a reader trusts and the function is what the app uses.
    /// </remarks>
    [Fact]
    public void EveryConfidenceBandHasOneRealAndDistinguishableSubjectInThisFile()
    {
        var bands = Enum.GetValues<Confidence>();
        Assert.Equal(3, bands.Length);

        var subjects = bands.ToDictionary(b => b, For);

        // Each fixture is in the band it is named for, as ConfidenceBand decides it.
        Assert.All(subjects, s => Assert.Equal(s.Key, ConfidenceBand.Of(s.Value)));

        // And no assertion below can pass by reading the wrong tile: distinct titles and distinct keys.
        Assert.Equal(3, subjects.Values.Select(m => m.T).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(3, subjects.Values.Select(m => m.K).Distinct(StringComparer.Ordinal).Count());

        // The degraded subject is really degraded, so the branches below have something to take.
        var degraded = subjects[Confidence.Unmatched];
        Assert.Equal(0, degraded.I);
        Assert.Null(degraded.Y);
        Assert.Null(degraded.P);
        Assert.Null(degraded.Ov);
        Assert.Empty(degraded.Cast);

        // The uncertain subject is the OTHER shape: a real id, which is the only thing separating it from
        // the one above. Both are below the threshold.
        Assert.NotEqual(0, subjects[Confidence.Uncertain].I);
        Assert.True(subjects[Confidence.Uncertain].Mc < MatchScorer.AcceptThreshold);
    }

    // ================================================================ what each band renders

    [Fact]
    public void AnUnmatchedTileRendersNoImgAtAll()
    {
        var tile = Tile(Unmatched());

        // Not an img with an empty src, and not one pointing at the CDN root: no element at all. An empty
        // src renders broken-image chrome and some browsers expose the URL as text.
        Assert.Empty(tile.QuerySelectorAll("img"));
        Assert.Single(tile.QuerySelectorAll("span.poster-placeholder"));
    }

    /// <remarks>
    /// Was <c>AnUnmatchedTileRendersTheNotMatchedTextInsideTheLink</c>. Same substance, no anchor: see the
    /// class remarks.
    /// </remarks>
    [Fact]
    public void AnUnmatchedTileRendersTheNotMatchedTextInsideTheTile()
    {
        var tile = Tile(Unmatched());
        var badge = Assert.Single(tile.QuerySelectorAll(".badge"));

        Assert.Equal("Not matched", badge.TextContent.Trim());
        Assert.True(tile.Contains(badge), "the badge is not inside the tile at all");
        Assert.Contains("badge-unmatched", badge.ClassList);
    }

    /// <remarks>
    /// Was <c>AnUncertainTileRendersTheUncertainMatchText</c>, which encoded copy this project had already
    /// rejected. "Uncertain match" is jargon from a pipeline the reader does not know exists -- uncertain
    /// match to WHAT? -- while "Needs review" is the owner's own words and the heading
    /// <c>JobSummary</c> already prints, so the badge and the build summary say the same thing.
    /// </remarks>
    [Fact]
    public void AnUncertainTileRendersTheNeedsReviewText()
    {
        var tile = Tile(Uncertain());
        var badge = Assert.Single(tile.QuerySelectorAll(".badge"));

        Assert.Equal("Needs review", badge.TextContent.Trim());
        Assert.Contains("badge-uncertain", badge.ClassList);
    }

    /// <remarks>
    /// <c>ConfidenceBand.BadgeFor</c> returns null for <see cref="Confidence.Confident"/>, which is what
    /// makes this the right assertion rather than "a third badge variant": the absence of an exceptional
    /// marker is not information conveyed by colour, and a badge on the ordinary 1,265 records would train
    /// the eye to ignore the ones that matter.
    /// </remarks>
    [Fact]
    public void AConfidentTileRendersNoBadgeAtAll()
    {
        var tile = Tile(Confident());

        Assert.Empty(tile.QuerySelectorAll(".badge"));
        Assert.Empty(tile.QuerySelectorAll("[class*='badge']"));

        // And the tile still has exactly the one child it had before this task: PosterTileTests'
        // TheSlotIsEmptyInPartASoTheTileHasNoControlOfItsOwn reads the same shape.
        Assert.Single(tile.Children);
        Assert.Equal("tile-body", Assert.Single(tile.Children).ClassName);
    }

    /// <remarks>
    /// The reading order a screen reader walks, which is what the plan's
    /// <c>AnUnmatchedTileAccessibleNameIsTitleThenYearThenStatus</c> was reaching for. It asked for an
    /// ACCESSIBLE NAME, which a Part A tile does not have: with no anchor the tile is not a named widget at
    /// all, so there is nothing for a name to belong to and <c>MarkupRules.AccessibleName</c> would be
    /// asked about a <c>div</c>. The substance survives as the order of the visible text, which is the same
    /// thing a reader meets and the thing Part B's anchor will turn into a name.
    /// <para>
    /// The unmatched row has no year, so its own order is title then status: that absence IS the degraded
    /// branch, and giving the fixture a synthetic year to make a three-part assertion possible is exactly
    /// the vacuity this task was warned about. The three-part order is asserted on the uncertain row,
    /// which has a real year because it has a real match.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(Confidence.Unmatched, "An Unlisted Film", null, "Not matched")]
    [InlineData(Confidence.Uncertain, "The Peeled Edition", "1982", "Needs review")]
    public void ADegradedTilesTextReadsTheTitleThenAnyYearThenTheStatus(
        Confidence band, string title, string? year, string status)
    {
        var tile = Tile(For(band));

        var expected = new List<string> { title };
        if (year is not null) expected.Add(year);
        expected.Add(status);

        Assert.Equal(expected, tile.QuerySelectorAll(".tile-title, .tile-year, .badge")
                                   .Select(e => e.TextContent.Trim()));
    }

    // ================================================================ what no band may introduce

    /// <remarks>
    /// THE INVERSE OF THE ASSERTION THE PLAN ASKED FOR, and the valuable one: this task cannot be the one
    /// that re-breaks the no-anchor decision. <c>PosterTileTests.TheTileRendersNoAnchorAndNothingInteractive</c>
    /// renders one confident film; the badge is a branch it never reaches, so the branches this task adds
    /// get their own pass over all three bands. <c>[role]</c> is in the list for the reason that test gives:
    /// the tile genuinely carries no role in Part A, the badge is content rather than a role, and Part B's
    /// anchor has an implicit one.
    /// </remarks>
    [Fact]
    public void TheDegradedBranchesAddNoAnchorNoInteractiveElementAndNoLabel()
    {
        foreach (var band in Enum.GetValues<Confidence>())
        {
            var tile = Tile(For(band));

            Assert.Empty(tile.QuerySelectorAll("a, button, input, select, textarea, [tabindex], [role]"));
            Assert.Empty(MarkupRules.InteractiveElements(tile));

            // No label either: the badge's words are on screen, and an aria-label would let the visible
            // text and the announced text diverge. No title attribute, which is the same defect with a
            // tooltip on it.
            Assert.Empty(tile.QuerySelectorAll("[aria-label], [aria-labelledby], [title]"));
        }
    }

    /// <remarks>
    /// The fixed slot position, asserted for the badge as
    /// <c>PosterTileTests.ActionSlotContentIsASiblingOfTheTextBlockAndNeverInsideIt</c> asserts it for the
    /// action slot. Part B wraps <c>.tile-body</c> -- and nothing else -- in one anchor to
    /// <c>movies/{K}</c>, so a badge inside <c>.tile-body</c> would become part of that link's accessible
    /// name: every degraded tile would announce as "Se7en, 1995, Needs review, link". Fixing it then means
    /// moving markup Part B has already built against, which is why it is pinned now.
    /// </remarks>
    [Fact]
    public void TheBadgeIsASiblingOfTheTextBlockAndNeverInsideIt()
    {
        foreach (var band in new[] { Confidence.Unmatched, Confidence.Uncertain })
        {
            var tile = Tile(For(band));
            var badge = Assert.Single(tile.QuerySelectorAll(".badge"));
            var body = tile.QuerySelector(".tile-body")!;

            Assert.False(body.Contains(badge),
                         band + "'s badge is inside .tile-body, which is what Part B's anchor will wrap.");
            Assert.Equal(tile, badge.ParentElement);
            Assert.Equal(tile, body.ParentElement);
        }
    }

    // ================================================================ the component consumes Core's rule

    /// <remarks>
    /// <c>ConfidenceBand</c> lives in Core rather than in the web project deliberately: the browser badges
    /// records with it and <c>MovieLibrary.Enrich</c>'s job summary counts them with it, and
    /// <c>MovieLibrary.Enrich</c> must never reference the web project. So the failure this guards against
    /// is not "the badge is wrong" but "the badge and the owner's build summary disagree", which nothing on
    /// screen explains. Two copies of the words, or two copies of the <c>I == 0</c> precedence, are how
    /// that happens.
    /// <para>
    /// Rule T4: this is a scan, so it begins with a positive control. Without one, a comment-stripper that
    /// stopped matching, or a renamed call, would make every <c>DoesNotContain</c> below pass over an empty
    /// string.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheComponentConsumesConfidenceBandRatherThanRestatingItsWordsOrItsRule()
    {
        var code = WithoutComments(WebRoot.Read("Components", "PosterTile.razor"));

        Assert.Contains("ConfidenceBand.Of(Movie)", code, StringComparison.Ordinal);
        Assert.Contains("ConfidenceBand.BadgeFor(", code, StringComparison.Ordinal);

        // The words are Core's. "Uncertain match" was rejected before this task and must not come back
        // through the component either.
        Assert.DoesNotContain("Not matched", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Needs review", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Uncertain match", code, StringComparison.Ordinal);

        // And so is the rule. The threshold is never spelled here, and neither field ConfidenceBand.Of
        // reads is touched: a tile that looked at Mc or I itself would be the second copy of the rule.
        Assert.DoesNotContain("0.75", code, StringComparison.Ordinal);
        Assert.DoesNotContain("AcceptThreshold", code, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\bMovie\.(I|Mc)\b", code);
    }

    /// <summary>Razor and XML-doc comments removed, so a scan reads code and not prose about it.</summary>
    /// <remarks>
    /// Both are needed and for the same reason: this file's own comments quote the badge strings, name the
    /// <c>I == 0</c> precedence and explain why the threshold is not spelled. A scan that read them would
    /// fail on the documentation that exists to stop the defect.
    /// </remarks>
    private static string WithoutComments(string razor) =>
        Regex.Replace(Regex.Replace(razor, @"@\*.*?\*@", "", RegexOptions.Singleline),
                      @"(?m)^[ \t]*///.*$", "");

    /// <remarks>
    /// Rule T4 again, on the stripper itself: the rule is a pure function, so it is mutated directly
    /// rather than through the scan above. The first case is the shape that made the scan necessary -- a
    /// razor comment quoting the very string the scan forbids.
    /// </remarks>
    [Theory]
    [InlineData("@* says \"Not matched\" *@<span>@badge</span>", "<span>@badge</span>")]
    [InlineData("@*\n  Not matched\n*@x", "x")]
    [InlineData("    /// Not matched\n    private int x;", "\n    private int x;")]
    [InlineData("<span>Not matched</span>", "<span>Not matched</span>")]
    public void TheCommentStripperRemovesCommentsAndNothingElse(string input, string expected) =>
        Assert.Equal(expected, WithoutComments(input));

    // ================================================================ the stylesheet

    private static string Css() => WithoutCssComments(WebRoot.Read("Components", "PosterTile.razor.css"));

    private static string WithoutCssComments(string css) =>
        Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline);

    /// <summary>Every rule in the stylesheet, as (selector, declarations), in source order.</summary>
    /// <remarks>
    /// Innermost rules only, which is what is wanted here: the selector group forbids a brace, so a wrapper
    /// such as <c>@media (forced-colors: active)</c> never matches as a rule of its own, and the
    /// <c>.badge</c> rule INSIDE it comes back as an ordinary rule. Rule T6 does not bite -- the body is
    /// taken whole, up to the closing brace, rather than walked semicolon by semicolon, so a final
    /// declaration with no trailing semicolon is still read.
    /// </remarks>
    private static List<(string Selector, string Body)> Rules(string css) =>
        [.. Regex.Matches(css, @"(?<sel>[^{}]+)\{(?<body>[^{}]*)\}")
                 .Select(m => (Selector: Collapse(m.Groups["sel"].Value), Body: m.Groups["body"].Value))];

    /// <summary>Whitespace runs collapsed to one space and trimmed, so two spellings of one selector match.</summary>
    private static string Collapse(string text) => Regex.Replace(text, @"\s+", " ").Trim();

    /// <summary>EVERY rule whose selector mentions this class -- not the first rule whose selector IS it.</summary>
    /// <remarks>
    /// WHY NOT AN EXACT MATCH ON THE FIRST RULE, which is what this file did until the review found it. That
    /// form could not see either of two cues the SC 1.4.1 argument below explicitly names:
    /// <c>.badge-unmatched::before { content: "! " }</c> is a different selector, so it was never examined,
    /// and a SECOND <c>.badge-uncertain { background-image: ... }</c> is a later rule that overrides the
    /// first, so only the first was read. Both were appended to the stylesheet and all 23 tests in this
    /// file stayed green, as did all 77 in <c>StyleContractTests</c>. Matching every rule that mentions the
    /// class catches a pseudo-element, a pseudo-class state, a descendant or compound selector and a
    /// selector list; the duplicate check in the caller catches the second copy.
    /// <para>
    /// The class boundary is explicit on both sides, so <c>badge</c> does not match the variants' rules and
    /// a variant does not match a longer name beginning with it.
    /// </para>
    /// <para>
    /// WHAT IT STILL CANNOT SEE, stated rather than left to be discovered: this is a single-file source
    /// scan, so a cue applied to <c>.badge-unmatched</c> from another stylesheet is invisible to it. The
    /// scoped rules in this file carry a <c>[b-...]</c> attribute and so win every property they declare,
    /// but a property they do NOT declare can still be set from outside -- which is the collision the
    /// comment at the top of the badge rules records for Bootstrap.
    /// </para>
    /// </remarks>
    private static List<(string Selector, string Body)> RulesMentioning(string css, string klass)
    {
        var mentions = new Regex(@"(?<![\w-])\." + Regex.Escape(klass) + @"(?![\w-])");

        return [.. Rules(css).Where(r => mentions.IsMatch(r.Selector))];
    }

    /// <remarks>
    /// BOTH DIRECTIONS, because each one fails silently on its own. A band renamed in Core leaves a styled
    /// class nothing renders; a rule renamed here leaves a rendered class nothing styles. Either way the
    /// badge still appears, in the base rule's colours, and looks like a design choice. The expected pair
    /// is written out rather than derived from the enum, because the thing being pinned is the convention
    /// that the class is the band's own name in lower case -- derive it from the enum and the assertion
    /// restates its own input.
    /// </remarks>
    [Fact]
    public void EveryBadgeVariantTheTileRendersHasARuleAndEveryRuleHasAVariant()
    {
        var rendered = Enum.GetValues<Confidence>()
            .SelectMany(band => Tile(For(band)).QuerySelectorAll(".badge"))
            .SelectMany(badge => badge.ClassList)
            .Where(c => c.StartsWith("badge-", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        var styled = Regex.Matches(Css(), @"\.(?<name>badge-[a-z-]+)")
            .Select(m => m.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(new[] { "badge-uncertain", "badge-unmatched" }, rendered);
        Assert.Equal(rendered, styled);
    }

    /// <remarks>
    /// A BORDER PLUS TEXT, NEVER A TINT ALONE. Under Windows High Contrast a background tint flattens to
    /// Canvas and takes the cue with it, while a border is re-coloured to CanvasText and survives, so the
    /// base rule's border is what keeps the badge a badge at all in that mode. The token is
    /// <c>--line-strong</c> rather than <c>--line</c>: 3.89:1 against <c>--bg</c> instead of the 1.37:1
    /// app.css records as FAILING SC 1.4.11 Non-text Contrast (Level AA), so a badge that somehow lost its
    /// variant class still could not end up with the decoration-only edge.
    /// </remarks>
    [Fact]
    public void TheBadgeIsABorderedBoxRatherThanATintSoForcedColorsCannotFlattenItAway()
    {
        // The rules whose selector is exactly `.badge`, not the first of them: there are two by design, the
        // shared one and the forced-colors override, and only the shared one can satisfy this -- the other
        // draws its border in CanvasText, which the next test reads. So this says "some `.badge` rule draws
        // the border in --line-strong" without depending on which order the two appear in.
        var badge = RulesMentioning(Css(), "badge").Where(r => r.Selector == ".badge").ToList();

        Assert.NotEmpty(badge);
        Assert.Contains(badge, r =>
            Regex.IsMatch(r.Body, @"border\s*:\s*1px\s+solid\s+var\(\s*--line-strong\s*\)"));
    }

    /// <remarks>
    /// THE TWO VARIANTS DIFFER BY COLOUR AND BY NOTHING ELSE, and that is what makes the forced-colors
    /// rule below correct rather than a loss. Forced colors replaces the palette, so both badges become
    /// CanvasText on Canvas and the colour distinction is gone; what is left telling the bands apart is
    /// the TEXT -- "Not matched" against "Needs review" -- which satisfies SC 1.4.1 Use of Color, Level A,
    /// because 1.4.1 is about visual means and words are not one.
    /// <para>
    /// This assertion is the half that could rot. If a later change gave one variant a different border
    /// width, an icon, a background image or a <c>::before</c>, the claim above would quietly stop being
    /// "colour alone" -- and under forced colors that difference would survive while the colour did not,
    /// which is a cue the test for the text would never notice was missing.
    /// </para>
    /// <para>
    /// WHAT IT COVERS, PRECISELY, because an earlier version of this remark overstated it. It reads EVERY
    /// rule in the file whose selector mentions the variant class and requires every declaration in each of
    /// them to be <c>border-color</c> or <c>color</c>, and it rejects a second rule with the same selector.
    /// So a thicker border, an icon through <c>content</c>, a background image, a pseudo-element, a
    /// pseudo-class state and a duplicate rule that overrides the first are all caught. Until the review
    /// found it this read only the FIRST rule whose selector was exactly the class, and two of the four cues
    /// named above -- the <c>::before</c> and the second <c>background-image</c> rule -- landed with every
    /// test in this file and in <c>StyleContractTests</c> green. It is still a single-file scan: see
    /// <see cref="RulesMentioning"/> for the one thing it cannot see.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("badge-unmatched")]
    [InlineData("badge-uncertain")]
    public void AVariantSetsColourAndNothingElse(string variant)
    {
        var rules = RulesMentioning(Css(), variant);

        Assert.True(rules.Count > 0,
            "PosterTile.razor.css has no rule mentioning `." + variant + "`. A badge class the component "
            + "renders with no rule behind it still draws -- the shared `.badge` rule paints it -- in the "
            + "wrong colour, with nothing on screen to say so.");

        // ONE RULE PER SELECTOR. A second rule with the same selector wins over the first, so a reader who
        // stopped at the first would not see it at all, and that is exactly how a background tint got past
        // this test before. If a forced-colors override was intended it belongs on `.badge`, which already
        // flattens both variants to CanvasText on Canvas.
        var repeated = rules.GroupBy(r => r.Selector, StringComparer.Ordinal).Where(g => g.Count() > 1).ToList();

        Assert.True(repeated.Count == 0,
            "PosterTile.razor.css declares "
            + string.Join(" and ", repeated.Select(g => "`" + g.Key + "` " + g.Count() + " times"))
            + ". The later rule wins, and the SC 1.4.1 Use of Color argument in this file rests on the two "
            + "variants differing by colour alone.");

        Assert.All(rules, rule =>
        {
            var declarations = rule.Body
                .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            Assert.True(declarations.Length > 0, "`" + rule.Selector + "` declares nothing at all");
            Assert.All(declarations, d => Assert.Matches(@"^(border-color|color)\s*:", d));
        });
    }

    /// <remarks>
    /// THE BADGE'S WORDS ARE THE CUE THE SC 1.4.1 ARGUMENT RESTS ON, so their size is load-bearing and is
    /// pinned here rather than left to a comment. Under forced colors both variants flatten to CanvasText
    /// on Canvas, and the only thing left telling "Not matched" from "Needs review" is the text; at
    /// <c>.75rem</c> that cue was the smallest type in the app, below even <c>.tile-year</c>'s
    /// <c>.85rem</c> and below the <c>0.8125rem</c> floor <c>app.css</c> records in its
    /// <c>.attribution p</c> comment.
    /// <para>
    /// NO CRITERION IS CITED FOR THE NUMBER, deliberately. WCAG 2.2 sets no minimum text size, and a
    /// declaration in <c>rem</c> already honours the reader's own browser font size rather than only page
    /// zoom, so <c>.75rem</c> was not a failure of anything. This is the project's own floor. The unit is
    /// half of it: a <c>px</c> value fails this test even at the equivalent 13px, because it would stop
    /// scaling with the reader's setting, which is the property that made the floor acceptable as a floor.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheBadgeTextIsNotBelowThisProjectsOwnFontSizeFloor()
    {
        const double floor = 0.8125;         // app.css, .attribution p: "Do not go below 0.8125rem"
        var badge = RulesMentioning(Css(), "badge").Where(r => r.Selector == ".badge").ToList();

        Assert.NotEmpty(badge);

        var sizes = badge
            .SelectMany(r => Regex.Matches(r.Body, @"font-size\s*:\s*(?<n>[0-9]*\.?[0-9]+)rem"))
            .Select(m => double.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture))
            .ToList();

        Assert.True(sizes.Count > 0,
            "No `.badge` rule in PosterTile.razor.css declares a font-size in rem. The badge's words are "
            + "the cue the SC 1.4.1 Use of Color argument in that file rests on, and a px size would stop "
            + "scaling with the reader's own browser font size.");
        Assert.All(sizes, size => Assert.True(size >= floor,
            "The badge is set at " + size + "rem, below this project's own " + floor + "rem floor (app.css, "
            + ".attribution p). The badge text is the only cue distinguishing the two degraded bands under "
            + "forced colors, so it is the last thing in the app that should be its smallest type."));
    }

    /// <remarks>
    /// The <c>@media</c> block states explicitly what forced-colors mode already does to those three
    /// properties. It is asserted because that behaviour is the whole reason the badge is a bordered box
    /// rather than a tinted one: a declaration a reader can see cannot be stranded by a later
    /// <c>forced-color-adjust: none</c> or by a variant that paints a background image instead of a
    /// colour.
    /// </remarks>
    [Fact]
    public void ForcedColorsRecoloursTheBadgeToTheSystemPaletteRatherThanLeavingItToTheTokens()
    {
        var block = Regex.Match(Css(),
            @"@media\s*\(\s*forced-colors\s*:\s*active\s*\)\s*\{\s*\.badge\s*\{(?<body>[^}]*)\}");

        Assert.True(block.Success,
            "PosterTile.razor.css has no `@media (forced-colors: active)` rule for `.badge`. The badge's "
            + "colours come from --accent and --accent2, which forced colors replaces, so without this "
            + "block the file says nothing about what the badge looks like in the mode it was designed for.");

        var body = block.Groups["body"].Value;

        Assert.Matches(@"border\s*:\s*1px\s+solid\s+CanvasText", body);
        Assert.Matches(@"(?<![\w-])color\s*:\s*CanvasText", body);
        Assert.Matches(@"(?<![\w-])background\s*:\s*Canvas\b", body);
    }

    // ================================================================ nothing hides the degraded records

    /// <remarks>
    /// The property a later default must not break. The plan's own words: a filter that hid the unmatched
    /// records by default would be both an accessibility failure -- content unreachable without operating a
    /// control -- and the exact opposite of what this view is for. Asserted over a mixed-band list rather
    /// than a uniform one, because a confidence filter is the one thing that could drop a whole band while
    /// leaving the count of tiles plausible.
    /// </remarks>
    [Fact]
    public void TheGridShowsEveryTitleBeforeAnyFilterIsApplied()
    {
        var films = Enum.GetValues<Confidence>().Select(For).ToList();
        var cut = Render<PosterGrid>(p => p.Add(x => x.Items, films));

        Assert.Equal(films.Count, cut.FindAll("ul.grid > li").Count);
        Assert.Equal(films.Select(f => f.T),
                     cut.FindAll(".tile-title").Select(e => e.TextContent.Trim()));
    }

    /// <remarks>
    /// RULE T1 AT THE LEVEL THAT SHIPS. Everything above renders a component from this file's own records;
    /// this one renders the real app at /movies through the shared stub catalogue, so it is what fails if
    /// <c>AnnouncerTestExtensions</c>'s fixture ever loses a band again. It held a confident film and an
    /// unmatched one and no uncertain one at all until this task, which meant the "Needs review" branch had
    /// no failable subject anywhere in the app's own markup -- the same vacuity the poster asymmetry in
    /// that file was added to end, one band over.
    /// </remarks>
    [Fact]
    public void EveryBandReachesTheShippedPageAndOnlyTheDegradedTwoAreBadged()
    {
        var root = MarkupRules.Parse(this.RenderAppAt("/movies").Markup);
        var tiles = root.QuerySelectorAll("ul.grid > li .tile").ToList();

        Assert.Equal(3, tiles.Count);
        Assert.Equal(new string?[] { null, "Needs review", "Not matched" },
                     tiles.Select(t => t.QuerySelector(".badge")?.TextContent.Trim()));
    }

    /// <remarks>
    /// RULE T2: a green test claims coverage and a skipped one does not, so this names the missing element
    /// and the task that will build it. The confidence facet is Task 8's, and the plan has already settled
    /// that it defaults to "All titles"; there is no filter in the tree today, so there is nothing here to
    /// assert a default of. Rule T3 is why this is an attribute and not a runtime skip:
    /// <c>Assert.Skip</c> is an xunit v3 API, <c>Xunit.Sdk.SkipException</c> compiles against the shared
    /// assert library, and the v2 engine this project runs reports a throw of it as FAILED.
    /// <para>
    /// <see cref="TheConfidenceFilterDefaultIsStillUnassertableForTheRightReason"/> is the live guard
    /// beside it, so the skip expires by failing rather than by being remembered.
    /// </para>
    /// </remarks>
    [Fact(Skip = "Task 8 builds the confidence facet; there is no filter in the tree to have a default. "
                 + "TheConfidenceFilterDefaultIsStillUnassertableForTheRightReason fails the day there is.")]
    public void TheConfidenceFilterDefaultsToAllTitles()
    {
        var root = MarkupRules.Parse(this.RenderAppAt("/movies").Markup);
        var group = root.QuerySelectorAll("fieldset")
            .Single(f => f.QuerySelector("legend")!.TextContent.Contains("confidence",
                                                                        StringComparison.OrdinalIgnoreCase));

        var chosen = group.QuerySelectorAll("input[type=radio]").Single(r => r.HasAttribute("checked"));

        Assert.Equal("All titles", chosen.ParentElement!.TextContent.Trim());
    }

    /// <remarks>
    /// The live guard for the skip above. It asserts the subject set is still EMPTY, which is the only
    /// form of skip that expires on its own: the day Task 8 renders a form control on the films page this
    /// fails, and its message says what to do. A bare skip would simply be forgotten.
    /// </remarks>
    [Fact]
    public void TheConfidenceFilterDefaultIsStillUnassertableForTheRightReason()
    {
        var root = MarkupRules.Parse(this.RenderAppAt("/movies").Markup);
        var controls = root.QuerySelectorAll("input, select, textarea, fieldset, legend").ToList();

        Assert.True(controls.Count == 0,
            "The films page now renders " + controls.Count + " form control(s), so Task 8's filters have "
            + "landed and TheConfidenceFilterDefaultsToAllTitles can be written: remove its Skip, check it "
            + "against the real markup, and delete this guard. Found: "
            + string.Join(" | ", controls.Select(MarkupRules.Describe)));

        // And the default's own words are nowhere on the page yet, which is the specific thing the skipped
        // test is about rather than filters in general.
        Assert.DoesNotContain("All titles", root.TextContent, StringComparison.Ordinal);
    }
}

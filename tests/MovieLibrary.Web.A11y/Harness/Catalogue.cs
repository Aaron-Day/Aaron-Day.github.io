using System.Text.Json;
using System.Text.Json.Serialization;
using MovieLibrary.Core.Models;

namespace MovieLibrary.Web.A11y.Harness;

/// <summary>
/// The catalogue the browser gate scans: synthetic, deterministic, and written into the served site's
/// <c>data/movies.json</c> before the first page load.
/// </summary>
/// <remarks>
/// <para>
/// <b>NO POSTER PATH ON ANY RECORD, AND THAT IS A BUDGET RULE, NOT AN OVERSIGHT.</b> A Ready scan renders one
/// tile per record on the page. With a poster path on each, 60 of them point at
/// <c>https://image.tmdb.org/t/p/w342...</c>, which is 60 external requests per scanned state, from CI, on a
/// public repository. Those are CDN reads and spend none of the owner's TMDB request budget, but a slow or
/// flaky CDN would make an accessibility gate red for a reason that has nothing to do with accessibility.
/// Task 6's own browser pass made the same call. <b>Do not "improve" this fixture by adding real poster
/// paths.</b> If a future reader wants a real <c>&lt;img&gt;</c> in front of axe, see
/// <see cref="OnePosterAmong"/>, which gives it one - and <c>BrowserGate</c> answers the request from
/// inside the test process, on every context it opens, so the count stays at zero whatever catalogue the
/// page finds. That belt is there because this brace is not enough on its own: on the
/// <c>A11Y_BASE_URL</c> path <see cref="WriteInto"/> never runs at all, and the catalogue under test is
/// then whatever the published site is serving.
/// </para>
/// <para>
/// <b>What that costs, said plainly so a green gate does not imply it.</b> Without a poster path
/// <c>PosterTile</c> renders its placeholder branch, <c>&lt;span class="poster-placeholder"
/// aria-hidden="true"&gt;</c>, so the <c>&lt;img&gt;</c> branch is not exercised <i>in a browser</i> by any
/// scan but <see cref="OnePosterAmong"/>'s. That branch's accessibility - <c>alt=""</c> present, explicit
/// <c>width</c> and <c>height</c> - is pinned at the DOM level by
/// <c>MovieLibrary.Web.Tests.MarkupContractTests</c>, whose own subject floor
/// (<c>TheImageAndControlContractsCanSeeTheFilmsPagesOwnMarkup</c>) proves it sees two real posters. The
/// coverage exists; it is not this project's.
/// </para>
/// <para>
/// <b>1,456 records</b>, which is the measured size of the real library and the number the page's own comments
/// are written against: at a page size of 60 it makes 25 pages with 16 on the last, so "Ready, page 1",
/// "Ready, a middle page" and "Ready, the last page" are three genuinely different DOM states rather than
/// three renders of the same one.
/// </para>
/// <para>
/// <b>THE GENRES ARE A REAL SPREAD, AND <c>meta.json</c> NAMES THEM.</b> Every record used to carry <c>g: [28,
/// 18]</c> and <c>ag: "Drama"</c>, which made the genre facet unfalsifiable in a browser: ticking Drama showed
/// 1,456 of 1,456 and passed. The records now cycle through eight genre sets (see <see cref="GenreSets"/>), and
/// <see cref="WriteInto"/> publishes the matching <c>data/meta.json</c> beside <c>movies.json</c>, because
/// without it the page's meta fetch fails, the genre map is empty and the fieldset is not rendered at all.
/// <b>The number of genres is modest.</b> <c>ObscuredFocusTests</c> and <c>FocusVisibleTests</c> walk the tab order
/// in at most <see cref="TabWalk.Presses"/> presses and fail if the document is not left by then; the panel adds one
/// stop per genre and per decade (eleven, and Year unknown), so with the shell, the pager and the error bar that is
/// twenty-two stops before any genre, and a full page of tiles adds <see cref="TabWalk.TilesOnAFullPage"/> = 60
/// links on top of all of it. Nine genres is ninety-one, inside the bound with room to spare, where the real panel
/// has about forty. The bound is sized for the real library and not for this catalogue (see
/// <see cref="TabWalk"/>), and <c>CatalogueTests</c> pins both that and this catalogue's fit.
/// </para>
/// <para>
/// <b>What is still flat, and said so.</b> Every record is matched with <c>mc: 0.95</c>, so the confidence radios
/// show an empty list for "Needs review" and "Not matched" in a browser. Giving some records the degraded
/// shape would put the confidence badges into every existing scan, which is a change to what ten axe scans see
/// and not something to do without running them.
/// </para>
/// <para>
/// Serialised through <c>MovieRecord</c> itself, so a change to the RECORD's shape - a renamed or retyped
/// member - breaks this harness too instead of leaving it scanning a document the app can no longer read.
/// </para>
/// <para>
/// <b>The serialiser options are a hand copy, and that is a real gap rather than a covered one.</b>
/// camelCase with nulls omitted is what <c>MovieLibrary.Enrich.Emit.CatalogWriter</c> writes, but its own
/// <c>JsonSerializerOptions</c> is private to a project this one does not reference and should not: a
/// browser gate has no business pulling in the TMDB and Google Sheet client to borrow a naming policy. So
/// switching <c>CatalogWriter</c> to snake_case would leave this harness writing camelCase, the page
/// reading no records, and the gate noticing only through the Ready states failing to appear. The record's
/// shape is covered here; the naming policy is not.
/// </para>
/// </remarks>
internal static class Catalogue
{
    /// <summary>The measured size of the live library. See the remarks.</summary>
    public const int Records = 1456;

    /// <summary><c>PosterGrid</c>'s default page size. Not configurable from the page, so it is a constant here.</summary>
    public const int PageSize = 60;

    public static int Pages => (Records + PageSize - 1) / PageSize;

    public static int OnTheLastPage => Records - (Pages - 1) * PageSize;

    /// <summary>
    /// The key of the record a film page scan lands on. Read from the catalogue rather than typed, so a change
    /// to the key format cannot leave <c>Site.Addresses</c> pointed at an address the page answers with "No
    /// film in the library has this address." -- a scan that would still be green, on a page showing nothing.
    /// It is record 0, which <see cref="Build"/> furnishes for exactly this purpose.
    /// </summary>
    public static string FirstKey => Build(1)[0].K;

    private static readonly JsonSerializerOptions Published = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Writes <c>data/movies.json</c> and <c>data/meta.json</c> under a served site root, creating the folder.
    /// </summary>
    public static void WriteInto(string siteRoot)
    {
        var folder = Path.Combine(siteRoot, "data");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "movies.json"), Json(Build(Records)));
        File.WriteAllText(Path.Combine(folder, "meta.json"), MetaJson());
        File.WriteAllText(Path.Combine(folder, "shows.json"), ShowsJson());

        var tv = Path.Combine(folder, "tv");
        Directory.CreateDirectory(tv);
        foreach (var n in PublishedShows)
            File.WriteAllText(Path.Combine(tv, $"series-{n + 1}.json"), ShowJson(n));
    }

    /// <summary>
    /// The series whose full records are published, by index into the series list.
    /// </summary>
    /// <remarks>
    /// <b>Not all 122, and that is deliberate.</b> A tile links to every series, but only the addresses
    /// <see cref="Site.Addresses"/> yields are navigated to, and 122 files of episodes would be several
    /// megabytes written before every browser run for three of them to be opened. These three are the
    /// three shapes the series page has to render: the furnished one the arrival scan lands on, one with
    /// no episode list at all, and one whose match is below the accept threshold -- the case where the
    /// page drops the denominators and the Missing marks entirely.
    /// <para>
    /// A series with no file behind it reaches the page's "no series in the library has this address"
    /// state, which is a real state worth scanning and is what any OTHER slug now produces.
    /// </para>
    /// </remarks>
    public static readonly int[] PublishedShows = [0, 1, 5];

    /// <summary>One series' full record, episodes and all, as <c>tv/{slug}.json</c> carries it.</summary>
    public static string ShowJson(int n) => JsonSerializer.Serialize(BuildShow(n), Published);

    private static ShowRecord BuildShow(int n)
    {
        var summary = BuildShows(n + 1)[n];
        var (seasons, total, owned, unplaced) = ShapeOf(n);

        return new ShowRecord(
            I: summary.I,
            T: summary.T,
            Slug: summary.Slug,
            P: null,                     // no path, so the page cannot request an image
            FirstYear: summary.FirstYear,
            Ag: summary.Ag,
            Seasons: BuildSeasons(seasons, total, owned, unplaced),
            OwnedEpisodes: owned,
            TotalEpisodes: total,
            Mc: summary.Mc,
            Srt: null,

            // The owner's own columns, so the "Additional information" list is rendered and scanned. Only
            // on the furnished one: the other two exist to be thin.
            Ex: n == 0
                ? new Dictionary<string, string> { ["Shelf"] = "Blu-ray, box 3", ["Note"] = "Ripped 2019" }
                : null);
    }

    /// <summary>
    /// Splits a series' episodes evenly across its seasons, and its owned ones from the front.
    /// </summary>
    /// <remarks>
    /// From the FRONT, because that is what a part-owned series looks like: the owner has the early
    /// seasons and is missing the late ones. Spreading the gaps evenly would hide the case where a whole
    /// season is absent, which is the one a reader is looking for.
    /// </remarks>
    private static SeasonRecord[] BuildSeasons(int seasons, int total, int owned, int unplaced)
    {
        if (seasons == 0) return [];

        // Season 0 FIRST, because that is the order the build writes it in and the page's job is to move
        // it to the end. A fixture that handed the page the order it wanted would never test that.
        var result = new SeasonRecord[seasons + (unplaced > 0 ? 1 : 0)];
        var offset = 0;

        if (unplaced > 0)
        {
            var bucket = new EpisodeRecord[unplaced];
            for (var e = 0; e < unplaced; e++)
                // NUMBERED FROM 1, as TvPipeline numbers them: it orders the unplaced rows by name and
                // gives them i + 1. All-zero numbers would be a fixture that disagreed with the shipped
                // data about the one thing the row's @key depends on.
                bucket[e] = new EpisodeRecord(e + 1, EpisodeTitleFor(9, e), true, null, 22 + e, null, null);

            // Every episode in it is OWNED: it exists because the owner has them, so there is nothing in
            // it to mark Missing and owned equals total by construction.
            result[0] = new SeasonRecord(0, unplaced, unplaced, bucket);
            offset = 1;
        }

        total -= unplaced;
        owned -= unplaced;

        var each = total / seasons;
        var left = owned;

        for (var s = 0; s < seasons; s++)
        {
            var inSeason = s == seasons - 1 ? total - each * (seasons - 1) : each;
            var ownedHere = Math.Clamp(left, 0, inSeason);
            left -= ownedHere;

            var episodes = new EpisodeRecord[inSeason];
            for (var e = 0; e < inSeason; e++)
                episodes[e] = new EpisodeRecord(
                    E: e + 1,
                    T: EpisodeTitleFor(s, e),
                    O: e < ownedHere,
                    Air: $"{1998 + s:0000}-0{1 + e % 9}-1{e % 10}",
                    Rt: 22 + e % 20,
                    S: null,
                    Ov: null);

            result[s + offset] = new SeasonRecord(s + 1, ownedHere, inSeason, episodes);
        }

        return result;
    }

    /// <summary>An episode title, varied in length so a row has something to wrap, and never a real one.</summary>
    private static string EpisodeTitleFor(int season, int episode) => ((season + episode) % 4) switch
    {
        0 => $"The Episode With A Deliberately Long Name, Part {episode + 1}",
        1 => $"Episode {episode + 1}",
        2 => $"A Fixture Episode {episode + 1}",
        _ => $"Fixture {season + 1}.{episode + 1}",
    };

    /// <summary>The measured number of series in the live library, so the series list pages as it really does.</summary>
    public const int Series = 122;

    /// <summary>
    /// The series index the <c>/tv</c> scan reads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>WITHOUT THIS FILE THE SCAN WOULD HAVE BEEN GREEN AND MEANINGLESS.</b> An unstubbed
    /// <c>shows.json</c> gets the host's 404 body, which is HTML, so the page renders its FAILED state: a
    /// heading, one sentence and a Try again button. axe would have found nothing wrong with it, the gate
    /// would have gone green, and no tile, no badge and no pager on that route would ever have been
    /// scanned. The same hole <c>meta.json</c> was added to close, one page over.
    /// </para>
    /// <para>
    /// <see cref="Series"/> of them, because 122 is what the library holds and three pages is what the
    /// pager has to cope with. The shapes are spread deliberately, and the spread is what stops a facet of
    /// the tile from being scanned over records that cannot tell each other apart:
    /// </para>
    /// <list type="bullet">
    /// <item>every confidence band, so both badge variants and the unbadged majority are rendered;</item>
    /// <item>one series with NO episode list at all -- <c>SeasonCount</c> and <c>TotalEpisodes</c> both 0
    /// -- so the tile that shows a title, a year and nothing else is in the scan. No series in the owner's
    /// library is in that state today, and the branch exists for the one that arrives in it;</item>
    /// <item>a complete series, where owned equals total, and partial ones either side of it;</item>
    /// <item>a series with no first year, so the absent-year branch is rendered.</item>
    /// </list>
    /// <para>
    /// NO POSTER PATHS, for the reason the films fixture gives: a path makes the tile request an image.
    /// The <c>&lt;img&gt;</c> branch is scanned on <c>/movies</c>, which has a furnished record for it;
    /// here every tile takes the placeholder branch, which is the one the films scan barely sees.
    /// </para>
    /// </remarks>
    public static string ShowsJson() => JsonSerializer.Serialize(BuildShows(Series), Published);

    /// <summary>The slug the series page's scan lands on. Index 0, which is the furnished one.</summary>
    /// <remarks>
    /// <b>The subject floor for every series page scan in this project</b>, and the reason index 0 is a
    /// special case below rather than whatever the loop would have produced. Left to the formula it was
    /// both UNMATCHED and empty of episodes -- <c>n % 11 == 0</c> and the no-episode-list case at once --
    /// so the arrival scan would have landed on a heading, a byline and one sentence, and would have read
    /// as covering a page it never rendered. Rule T1: the thing that quietly empties is what has to be
    /// asserted, and <c>CatalogueTests</c> asserts it.
    /// </remarks>
    public static string FirstSlug => BuildShows(1)[0].Slug;

    private static ShowSummary[] BuildShows(int count)
    {
        var shows = new ShowSummary[count];

        for (var n = 0; n < count; n++)
        {
            var (seasons, total, owned, _) = ShapeOf(n);

            shows[n] = new ShowSummary(
                I: n == 0 ? 1 : n % 11 == 0 ? 0 : n + 1,   // unmatched where the id is 0, which the band reads
                T: SeriesTitleFor(n),
                Slug: $"series-{n + 1}",
                P: null,                          // see the remarks: no path, so the tile cannot request an image
                FirstYear: n == 0 ? 1998 : n % 19 == 0 ? null : 1951 + n % 73,
                Ag: GenreSets[n % GenreSets.Length].Apple,
                SeasonCount: seasons,
                OwnedEpisodes: owned,
                TotalEpisodes: total,
                Mc: n == 0 ? 0.95 : n % 5 == 0 ? 0.55 : 0.95);   // 0.55 is below the threshold: Uncertain
        }

        return shows;
    }

    /// <summary>
    /// How many seasons and episodes a series has, and how many of them the owner holds.
    /// </summary>
    /// <remarks>
    /// Index 0 is the furnished series the scan lands on, so it is confident, has several seasons and is
    /// PART-OWNED -- a complete one would render no "Missing" row at all and the mark would go unscanned.
    /// Index 1 is the matched series with no episode list, which is 7 of the real 122. Index 2 is complete.
    /// Everything after that is spread by the formula.
    /// </remarks>
    private static (int Seasons, int Total, int Owned, int Unplaced) ShapeOf(int n)
    {
        // The furnished one carries an UNPLACED bucket as well -- season 0, which is not a season but the
        // build's home for owned episodes it could not place, and which the page labels "Season unknown".
        // The owner's copy of 24 has 29 of them. Without one here the label would ship unscanned, and
        // SeasonCount excluding the bucket while the episode counts include it would have no subject at
        // all: every assertion about the difference would be comparing 4 with 4.
        if (n == 0) return (4, 48, 41, 5);
        if (n == 1) return (0, 0, 0, 0);   // nothing at all: the page's fourth state
        if (n == 2) return (1, 12, 12, 0);   // complete, so no Missing row anywhere in it

        var seasons = 1 + n % 9;
        var total = seasons * (6 + n % 18);

        return (seasons, total, n % 7 == 0 ? total : Math.Max(0, total - 1 - n % 5), 0);
    }

    /// <summary>
    /// A series title, long enough and varied enough to be worth laying out, and never a real one.
    /// </summary>
    private static string SeriesTitleFor(int n) => (n % 6) switch
    {
        0 => $"A Series With A Deliberately Long Name, Part {n + 1}",
        1 => $"Fixture {n + 1}",
        2 => $"The Fixture Chronicles {n + 1}",
        3 => $"Fixture: The Next Fixture {n + 1}",
        4 => $"Fixture {n + 1}: Return Of The Fixture",
        _ => $"Series {n + 1}",
    };

    /// <summary>
    /// The meta document that names the genres the records carry. Without one the page's meta fetch gets the host's
    /// 404 body, which is HTML, so <c>CatalogService.GetMetaAsync</c> returns <c>CatalogMeta.Empty</c> and the
    /// genre facet renders NOTHING: the gate would be scanning a panel with one of its four fieldsets missing and
    /// reading as though it had scanned them all.
    /// </summary>
    public static string MetaJson() => JsonSerializer.Serialize(
        new CatalogMeta(
            BuiltUtc: "2026-01-01T00:00:00Z", MovieCount: Records, ShowCount: Series,
            TotalEpisodeCount: BuildShows(Series).Sum(s => s.TotalEpisodes),
            Genres: GenreNames.ToDictionary(g => g.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), g => g.Value),
            Attribution: CatalogMeta.TmdbAttribution, ReviewQueue: []),
        Published);

    /// <summary>
    /// A body for one scan in which exactly ONE record carries a poster path, so axe has a real
    /// <c>&lt;img&gt;</c> element to evaluate. The request it makes is fulfilled from inside the test
    /// process by the route <c>BrowserGate</c> registers on every context, so this still reaches no CDN and
    /// makes no external request at all.
    /// </summary>
    public static string OnePosterAmong(int count)
    {
        var records = Build(count);
        records[0] = records[0] with { P = "/one-synthetic-poster.jpg" };
        return Json(records);
    }

    public static string Json(IReadOnlyList<MovieRecord> records) => JsonSerializer.Serialize(records, Published);

    /// <summary>
    /// Deterministic, and varied where variety is what a browser can see: title length drives the tile's
    /// unclamped wrap, a missing year drives the tile's second branch, and a long title at 320px is what the
    /// two-column grid rule has to survive.
    /// </summary>
    private static MovieRecord[] Build(int count)
    {
        var records = new MovieRecord[count];

        for (var n = 0; n < count; n++)
            records[n] = new MovieRecord(
                I: n + 1,
                K: $"{n + 1}-{90 + n % 70}",
                T: TitleFor(n),
                Y: n % 23 == 0 ? null : 1928 + n % 97,
                Rt: 90 + n % 70,
                P: null,                      // see the remarks: no path, so the tile cannot request an image
                B: null,
                G: GenreSets[n % GenreSets.Length].Ids,
                Ag: GenreSets[n % GenreSets.Length].Apple,
                Vote: 5.0 + n % 50 / 10.0,
                Dir: ["A Director"],
                Ov: null,
                Tag: null,
                Cast: [],
                Col: null,
                Own: null,
                Mc: 0.95);

        // ONE RECORD IS FURNISHED, AND IT IS THE ONE THE FILM PAGE'S SCAN LANDS ON (see FirstKey). Left as
        // the loop builds them, every record omits every optional part of the film page: no backdrop, no
        // tagline, no overview, no cast, no collection, no purchase line and no extra fields. A scan of
        // /movies/<key> would then see a heading, a byline and one definition list, and would read as
        // covering a page most of which it never rendered -- the applicability hole rule T1 is about.
        // These are the members NO TILE READS, so furnishing them changes nothing in any other scan: a tile
        // shows the title, the year, the runtime, the rating and the badge. P stays null here, because a
        // poster path on a record on page 1 WOULD change every Ready scan, and that is OnePosterAmong's job
        // and the budget rule in the remarks above. The backdrop and the portrait are requests this
        // process answers itself -- BrowserGate routes image.tmdb.org on every context it opens.
        records[0] = records[0] with
        {
            B = "/one-synthetic-backdrop.jpg",
            Tag = "A fixture tagline, which is the one line of prose above the overview.",
            Ov = "A fixture overview. It is several sentences long because the real ones are, and because a "
                 + "paragraph that wraps over four or five lines at 320 pixels is what the reflow and the "
                 + "text-override scans need to see on this page. Nothing here is matched against.",
            Cast =
            [
                // Three members, which is three branches: a portrait, the placeholder that stands in for a
                // missing one, and a billed name with no character to put after it.
                new CastMember("First Billed", "The Detective", "/one-synthetic-portrait.jpg"),
                new CastMember("Second Billed", "The Rookie", null),
                new CastMember("Third Billed", "", null),
            ],
            Col = "The Fixture Collection",
            Own = "The Fixture 3-Film Collection",
            Ex = new Dictionary<string, string> { ["Shelf"] = "B3", ["Case"] = "keep case" },
        };

        return records;
    }

    /// <summary>
    /// The genres the records carry, by TMDB id, with the names <see cref="MetaJson"/> publishes for them. Only ids a
    /// record actually uses are named, as the real pipeline does.
    /// </summary>
    private static readonly SortedDictionary<int, string> GenreNames = new()
    {
        [18] = "Drama", [35] = "Comedy", [28] = "Action", [12] = "Adventure", [53] = "Thriller",
        [10749] = "Romance", [99] = "Documentary", [878] = "Science Fiction", [27] = "Horror",
    };

    /// <summary>
    /// Eight genre sets cycled over the records: single and double, common and rare, so that ticking any one genre
    /// shows a different, non-empty, proper subset of the page. A catalogue where every record is <c>[28, 18]</c>
    /// filters 1,456 films down to 1,456, and a facet test written against it passes over a filter that never
    /// filtered. The first id agrees with the Apple text, as the fixture generator's does.
    /// </summary>
    private static readonly (int[] Ids, string Apple)[] GenreSets =
    [
        ([18], "Drama"),
        ([35], "Comedy"),
        ([28, 12], "Action & Adventure"),
        ([18, 53], "Drama"),
        ([35, 10749], "Comedy"),
        ([99], "Documentary"),
        ([878, 28], "Sci-Fi"),
        ([27, 53], "Horror"),
    ];

    private static readonly string[] Shapes =
    [
        "Fixture {0}",
        "A Rather Longer Fixture Title Number {0} That Has To Wrap",
        "Fixture {0}: The Subtitle",
        "Fixture {0} — Part Two",
        "The Very Long Synthetic Fixture Title {0} Which Exists To Make A Tile Three Lines Tall At 320 Pixels",
    ];

    private static string TitleFor(int n) =>
        string.Format(System.Globalization.CultureInfo.InvariantCulture, Shapes[n % Shapes.Length], n + 1);
}

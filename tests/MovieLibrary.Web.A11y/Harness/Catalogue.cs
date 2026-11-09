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

    private static readonly JsonSerializerOptions Published = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Writes <c>data/movies.json</c> under a served site root, creating the folder.</summary>
    public static void WriteInto(string siteRoot)
    {
        var folder = Path.Combine(siteRoot, "data");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "movies.json"), Json(Build(Records)));
    }

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
                G: [28, 18],
                Ag: "Drama",
                Vote: 5.0 + n % 50 / 10.0,
                Dir: ["A Director"],
                Ov: null,
                Tag: null,
                Cast: [],
                Col: null,
                Own: null,
                Mc: 0.95);

        return records;
    }

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

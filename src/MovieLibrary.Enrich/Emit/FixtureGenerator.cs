using System.Collections.ObjectModel;
using System.Globalization;
using MovieLibrary.Core.Models;
using MovieLibrary.Core.Parsing;
using MovieLibrary.Core.Text;

namespace MovieLibrary.Enrich.Emit;

/// <summary>
/// Builds a full-size catalog from the Sheet alone, with zero TMDB calls, so UI
/// work is never gated on enrichment progress. Synthetic values are derived from
/// a hash of the title, making every run byte-identical.
/// </summary>
/// <remarks>
/// <para>Real from the Sheet: title, runtime, directors, the Apple genre (<c>Ag</c>) and purchase bundle. Everything
/// else is synthesised, including the cast row, the genre facet ids and the collection, because a UI built
/// against empty values could not exercise them before a real run (about 5,600 requests).</para>
/// <para><b>Owned versus total.</b> As in a real run, the Sheet's episode rows are the owned episodes. A real run takes
/// the total from TMDB's season counts; a fixture has no TMDB, so it invents the episodes TMDB would list that the
/// owner lacks (<c>O</c> false, titled "Fixture episode N: ..."): at the numbers the owner skipped, and past the last
/// one. The shape is seeded per series so a library has them all: about one in ten fully owned (the reveal toggle
/// shows nothing extra), most missing a few, some about half, and some missing two thirds or more. Without this
/// every series would be fully owned and the owned-only view with its reveal toggle could not be built or reviewed.</para>
/// <para><b>Collections, taglines, air dates.</b> A collection holds two to five films that are neighbours in ordinal
/// title order, named after the first of them, so the "other films in this collection" view has films to show. About
/// four films in five get a synthetic tagline and the rest none, so both layouts are exercised. Every episode, owned
/// or not, has an ISO air date that ascends through its season.</para>
/// <para><b>No image path is ever produced</b> (<c>P</c>, <c>B</c>, cast <c>P</c>, episode stills): with no path the
/// UI cannot request an image, so fixture data cannot cause a single HTTP request.</para>
/// <para><b>Match confidence.</b> A film is <c>0.95</c>. The best an algorithmic match can score is 0.95, so
/// <c>Mc == 1.0</c> means only "a human confirmed this on the Overrides tab", and fixtures must never claim it.
/// A series is <c>1.0</c>: <c>TvPipeline</c> gives an exact name match 1.0, and a fixture series carries the name
/// from the Sheet itself, so it matches exactly by construction.</para>
/// </remarks>
public static class FixtureGenerator
{
    private static readonly SortedDictionary<int, string> GenreTable = new()
    {
        [28] = "Action", [12] = "Adventure", [16] = "Animation", [35] = "Comedy", [80] = "Crime",
        [99] = "Documentary", [18] = "Drama", [10751] = "Family", [14] = "Fantasy", [36] = "History",
        [27] = "Horror", [10402] = "Music", [9648] = "Mystery", [10749] = "Romance",
        [878] = "Science Fiction", [10770] = "TV Movie", [53] = "Thriller", [10752] = "War",
        [37] = "Western",
    };

    /// <summary>
    /// The real TMDB movie-genre ids and names, in id order. Every id a fixture film carries in <c>G</c> is a key
    /// here, so the fixture-mode <c>CatalogMeta.Genres</c> can be built from it and the facet never shows a bare
    /// number. Read-only: it is shared by every caller.
    /// </summary>
    public static IReadOnlyDictionary<int, string> Genres { get; } = new ReadOnlyDictionary<int, string>(GenreTable);

    private static readonly int[] GenreIds = [.. GenreTable.Keys];

    // How the Apple genre text finds a TMDB genre: by name, ignoring case. "Sci-Fi" is the Apple spelling of the
    // TMDB name "Science Fiction". "Kids" and "Holiday" have no TMDB namesake and fall through to the seed.
    private static readonly Dictionary<string, int> GenreByName = BuildNameIndex();

    private static Dictionary<string, int> BuildNameIndex()
    {
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Sci-Fi"] = 878 };
        foreach (var (id, name) in GenreTable) index[name] = id;
        return index;
    }

    private static readonly string[] Sentences =
    [
        "A quiet life is upended when an old debt comes due.",
        "Two strangers discover they are chasing the same answer.",
        "An ordinary evening turns into the longest night of their lives.",
        "Everything changes after a single, unremarkable decision.",
        "The past refuses to stay buried.",
    ];

    // Invented on purpose: none of these is a real actor. The lengths are deliberately uneven, from five characters
    // to over sixty, with accents and an apostrophe ('), because stressing the cast row layout is the point.
    // Each pool has a PRIME size (23) so PickFour with any stride 1..22 always lands on four distinct entries.
    private static readonly string[] CastNames =
    [
        "Bo Lin", "Ada Kwe", "Ilo Venn", "Tam Orlo", "J. R. Vo", "Imogen Thrupp", "Lucan Dunmarrow",
        "Corwin Pellesmere", "Søren Ibbotsworth", "Zoë Anselm-Quirke", "Thaddeus Orrinwick",
        "Ptolemy Vandersloot", "Marisol Quindlebert", "Anwen Tolliver-Brae", "Yevgenia Strathmoor",
        "Odalys Fenwright-Haze", "Nkechi Oyelaran-Bright", "Zephyrine Marrowby-Dunmore",
        "Raghnall MacQuillan-Dourwood", "Fitzwilliam Crabtree-Holloway", "Esperanza Villalobos-Thornquist",
        "Hyacinthe Philomena Abernook-Kowalzig-Fitzmarrow-Dunstanbury",
        "Maximilian Alasdair Quillfeather-Dunstable von Wetherspoon-Marsh",
    ];

    private static readonly string[] CharacterNames =
    [
        "Kit", "Mum", "Himself", "Old Pete", "Young Mateo", "The Stranger", "The Baroness", "Sergeant Tuck",
        "Uncle Barnaby", "Dr. Imelda Voss", "Nurse Bellwether", "Grandmother Ysolde", "Second Henchman, Left",
        "Voice of the Computer", "Taxi Driver (uncredited)", "Captain Rowena Ashgrove",
        "The Innkeeper's Daughter", "Mrs. Philippa Tidewell-Cross", "Professor Ignatius Blackthorne-Wyke",
        "Lieutenant Commander Odette Varnham-Gray", "Detective Sergeant Alaric Penhallow-Strand",
        "Man in Elevator #2",
        "The Honourable Lady Ottoline Marchbanks-Thistlewood, Dowager Countess of Upper Nowhere",
    ];

    // Distinct salts give each synthetic field its own stream from one seed, so the collection, genres and
    // cast of a film are not all functions of the same low bits.
    private const int SaltCollection = 1, SaltGenre = 2, SaltSecondGenreChance = 3, SaltSecondGenre = 4,
                      SaltCastStart = 5, SaltCastStride = 6, SaltRoleStart = 7, SaltRoleStride = 8,
                      SaltTaglineChance = 9, SaltTaglineText = 10, SaltCollectionSize = 11,
                      SaltSeriesShape = 12, SaltSeasonTail = 13, SaltAirStart = 14, SaltEpisodeTitle = 15;

    // Obviously synthetic, from a few words to a sentence that must wrap, because the tagline is a laid-out field.
    private static readonly string[] Taglines =
    [
        "Fixture: short.",
        "Fixture tagline, not a real one.",
        "Fixture: a made-up tagline of a middling length.",
        "Fixture: this tagline is invented to fill the space where a real one would go.",
        "Fixture: a deliberately long tagline that exists only so the layout has to decide where it wraps, whether " +
        "it truncates, and what happens to everything beside it when it does.",
    ];

    // The title of an episode the owner does not have: "Fixture episode 7: ..." with a stem of uneven length.
    private static readonly string[] UnownedTitleStems =
    [
        "Unowned",
        "Not In The Library Yet",
        "The One Missing From The Shelf",
        "A Gap In The Run",
        "A Deliberately Long Title For An Episode The Owner Does Not Have, To See Where It Wraps",
    ];

    // A season is padded up to its highest owned number, but never past this: one mistyped "Episode 2000000000"
    // on the Sheet must not become two billion fixture episodes.
    private const int MaxFilledEpisodeNumber = 200;

    private static int Seed(string s)
    {
        var h = 17;
        foreach (var c in s) h = unchecked(h * 31 + c);

        // Mask, never Math.Abs: Math.Abs(int.MinValue) throws OverflowException, and some title hashes to it.
        return h & int.MaxValue;
    }

    /// <summary>A deterministic value in [0, bound) from a seed and a salt (a murmur3-style finaliser).</summary>
    private static int Pick(int seed, int salt, int bound)
    {
        unchecked
        {
            var x = (uint)seed ^ ((uint)salt * 0x9E3779B9u);
            x = (x ^ (x >> 16)) * 0x85EBCA6Bu;
            x = (x ^ (x >> 13)) * 0xC2B2AE35u;
            x ^= x >> 16;
            return (int)(x % (uint)bound);
        }
    }

    /// <summary>Four distinct entries from a prime-sized pool: start anywhere, stride by any 1..size-1.</summary>
    private static string[] PickFour(string[] pool, int seed, int startSalt, int strideSalt)
    {
        var start = Pick(seed, startSalt, pool.Length);
        var stride = 1 + Pick(seed, strideSalt, pool.Length - 1);
        return [.. Enumerable.Range(0, 4).Select(k => pool[(start + k * stride) % pool.Length])];
    }

    /// <summary>An id must be unique within its list (favourites are keyed by it), so step past any taken one.</summary>
    private static int ClaimId(HashSet<int> taken, int first)
    {
        var id = first;
        while (!taken.Add(id)) id++;
        return id;
    }

    /// <summary>
    /// One or two TMDB genre ids. Each part of the Apple text that names a TMDB genre ("Action &amp; Adventure" is
    /// Action and Adventure) comes first, so the facet agrees with <c>Ag</c>. A genre that names none is drawn from
    /// the seed. A single genre gets a seeded second one about half the time, so films sit in two facet buckets.
    /// </summary>
    private static int[] GenresFor(string appleGenre, int seed)
    {
        var ids = appleGenre
            .Split('&', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(GenreByName.ContainsKey)
            .Select(part => GenreByName[part])
            .Distinct()
            .Take(2)
            .ToList();

        if (ids.Count == 0) ids.Add(GenreIds[Pick(seed, SaltGenre, GenreIds.Length)]);

        if (ids.Count == 1 && Pick(seed, SaltSecondGenreChance, 2) == 0)
        {
            var second = GenreIds[Pick(seed, SaltSecondGenre, GenreIds.Length)];
            if (second != ids[0]) ids.Add(second);
        }

        return [.. ids];
    }

    /// <summary>
    /// Puts roughly a quarter of the films (the same seeded pick as ever) into collections of two to five. They
    /// are taken in ordinal title order and cut into runs, each run named "{first title} Collection" the way TMDB
    /// names one, so a collection is a handful of neighbouring titles and the UI has other films to group. A lone
    /// film left over at the very end gets no collection rather than a collection of one.
    /// </summary>
    private static string?[] Collections(IReadOnlyList<SheetRow> rows)
    {
        var names = new string?[rows.Count];
        var members = Enumerable.Range(0, rows.Count)
            .Where(i => Pick(Seed(rows[i].Title), SaltCollection, 4) == 0)
            .OrderBy(i => rows[i].Title, StringComparer.Ordinal)
            .ToList();

        for (var at = 0; members.Count - at >= 2;)
        {
            var left = members.Count - at;
            var first = rows[members[at]].Title;
            var size = Math.Min(left, 2 + Pick(Seed(first), SaltCollectionSize, 4));

            for (var k = at; k < at + size; k++) names[members[k]] = $"{first} Collection";
            at += size;
        }

        return names;
    }

    /// <summary>
    /// How many episodes TMDB would list past the owner's last one, drawn per season. The shape is drawn once per
    /// series from its name, so a library has all four: fully owned (0, the caller never asks), a few missing
    /// (1 to 4), about half missing, and a large gap (two to three times as many unowned as owned).
    /// </summary>
    private static int MissingTail(int shape, int owned, int seasonSeed)
    {
        if (shape <= 6) return 1 + Pick(seasonSeed, SaltSeasonTail, 4);

        if (shape <= 8)
        {
            var low = Math.Max(1, (owned + 1) / 2);
            return low + Pick(seasonSeed, SaltSeasonTail, owned - low + 1);
        }

        return 2 * owned + Pick(seasonSeed, SaltSeasonTail, owned + 2);
    }

    /// <summary>A weekly air date from the series' start; a season starts a year after the one before.</summary>
    private static string AirDate(DateTime start, int season, int episode)
    {
        var days = (long)(Math.Max(season, 1) - 1) * 365 + (long)(Math.Max(episode, 1) - 1) * 7;
        return start.AddDays(Math.Min(days, 36_500)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The seasons of one series. The Sheet's rows are the owned episodes and keep their numbers. TMDB would also
    /// list every number the owner does not have, so a padded season is the contiguous list 1..N, N being the
    /// highest owned number plus a seeded tail: a number the owner skipped is an unowned episode among the owned
    /// ones, not a hole. A fully owned series is left exactly as the Sheet has it.
    /// </summary>
    private static SeasonRecord[] BuildSeasons(string name, int firstYear, int runtime, IEnumerable<ParsedEpisodeTitle> rows)
    {
        var seed = Seed(name);
        var shape = Pick(seed, SaltSeriesShape, 10);
        var start = new DateTime(firstYear, 1, 1).AddDays(Pick(seed, SaltAirStart, 300));

        return [.. rows
            .GroupBy(p => p.Season ?? 1)
            .OrderBy(s => s.Key)
            .Select(s =>
            {
                var owned = s.Select((p, i) => (Number: p.Episode ?? i + 1, p.Name)).ToList();
                var taken = owned.Select(o => o.Number).ToHashSet();
                var seasonSeed = Seed($"{name}/{s.Key}");

                var eps = owned.Select(o => new EpisodeRecord(
                    o.Number, o.Name, true, AirDate(start, s.Key, o.Number), runtime, null,
                    Sentences[Seed(o.Name) % Sentences.Length])).ToList();

                if (shape > 0)
                {
                    var top = Math.Min(taken.Max(), MaxFilledEpisodeNumber) + MissingTail(shape, owned.Count, seasonSeed);
                    eps.AddRange(Enumerable.Range(1, top).Where(n => !taken.Contains(n)).Select(n =>
                    {
                        var title = $"Fixture episode {n}: " +
                                    UnownedTitleStems[Pick(Seed($"{name}/{s.Key}/{n}"), SaltEpisodeTitle, UnownedTitleStems.Length)];
                        return new EpisodeRecord(n, title, false, AirDate(start, s.Key, n), runtime, null,
                                                 Sentences[Seed(title) % Sentences.Length]);
                    }));
                }

                var ordered = eps.OrderBy(e => e.E).ToArray();
                return new SeasonRecord(s.Key, owned.Count, ordered.Length, ordered);
            })];
    }

    public static (IReadOnlyList<MovieRecord> Movies, IReadOnlyList<ShowRecord> Shows) Build(
        IReadOnlyList<SheetRow> movieRows, IReadOnlyList<SheetRow> showRows)
    {
        var movies = new List<MovieRecord>(movieRows.Count);
        var movieIds = new HashSet<int>();
        var collections = Collections(movieRows);

        for (var index = 0; index < movieRows.Count; index++)
        {
            var row = movieRows[index];
            var seed = Seed(row.Title);
            var names = PickFour(CastNames, seed, SaltCastStart, SaltCastStride);
            var roles = PickFour(CharacterNames, seed, SaltRoleStart, SaltRoleStride);

            movies.Add(new MovieRecord(
                I: ClaimId(movieIds, 900_000 + (seed % 90_000)),
                T: row.Title,
                Y: 1970 + (seed % 55),
                Rt: RuntimeParser.FilmMinutes(row.Runtime),
                P: null,                       // gradient placeholder in the UI; no path, so no request
                B: null,
                G: GenresFor(row.Genre, seed),
                Ag: row.Genre,
                Vote: Math.Round(4.0 + (seed % 60) / 10.0, 1),
                Dir: row.Directors.Split('&', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                Ov: Sentences[seed % Sentences.Length],
                Tag: Pick(seed, SaltTaglineChance, 5) == 0 ? null : Taglines[Pick(seed, SaltTaglineText, Taglines.Length)],
                Cast: [.. names.Select((name, i) => new CastMember(name, roles[i], null))],
                Col: collections[index],
                Own: string.IsNullOrWhiteSpace(row.PurchaseInfo) ? null : row.PurchaseInfo,
                Mc: 0.95));
        }

        var showIds = new HashSet<int>();
        var shows = showRows
            .Where(r => !string.IsNullOrWhiteSpace(r.Series))
            .GroupBy(r => r.Series.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var firstYear = 1990 + (Seed(g.Key) % 35);
                var seasons = BuildSeasons(g.Key, firstYear, RuntimeParser.EpisodeMinutes(g.First().Runtime),
                                           g.Select(r => EpisodeTitleParser.Parse(r.Title)));

                return new ShowRecord(
                    I: ClaimId(showIds, 800_000 + (Seed(g.Key) % 90_000)),
                    T: g.Key,
                    Slug: Slug.From(g.Key),
                    P: null,
                    FirstYear: firstYear,
                    Ag: g.First().Genre,
                    Seasons: seasons,
                    OwnedEpisodes: seasons.Sum(s => s.Owned),
                    TotalEpisodes: seasons.Sum(s => s.Total),
                    Mc: 1.0);
            }).ToList();

        return (movies, shows);
    }
}

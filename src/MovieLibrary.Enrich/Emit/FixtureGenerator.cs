using System.Collections.ObjectModel;
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
/// else is synthesised, including the cast row, the genre facet ids and the collection badge, because a UI built
/// against empty values could not exercise them before a real run (about 5,600 requests).</para>
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
                      SaltCastStart = 5, SaltCastStride = 6, SaltRoleStart = 7, SaltRoleStride = 8;

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

    public static (IReadOnlyList<MovieRecord> Movies, IReadOnlyList<ShowRecord> Shows) Build(
        IReadOnlyList<SheetRow> movieRows, IReadOnlyList<SheetRow> showRows)
    {
        var movies = new List<MovieRecord>(movieRows.Count);
        var movieIds = new HashSet<int>();

        foreach (var row in movieRows)
        {
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
                Tag: null,
                Cast: [.. names.Select((name, i) => new CastMember(name, roles[i], null))],
                // TMDB names a collection after its first film, "{Title} Collection"; about a quarter of films have one.
                Col: Pick(seed, SaltCollection, 4) == 0 ? $"{row.Title} Collection" : null,
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
                var seasons = g.Select(r => EpisodeTitleParser.Parse(r.Title))
                    .GroupBy(p => p.Season ?? 1)
                    .OrderBy(s => s.Key)
                    .Select(s =>
                    {
                        var eps = s.Select((p, i) => new EpisodeRecord(
                            p.Episode ?? i + 1, p.Name, true, null,
                            RuntimeParser.EpisodeMinutes(g.First().Runtime), null,
                            Sentences[Seed(p.Name) % Sentences.Length])).ToArray();
                        return new SeasonRecord(s.Key, eps.Length, eps.Length, eps);
                    }).ToArray();

                return new ShowRecord(
                    I: ClaimId(showIds, 800_000 + (Seed(g.Key) % 90_000)),
                    T: g.Key,
                    Slug: Slug.From(g.Key),
                    P: null,
                    FirstYear: 1990 + (Seed(g.Key) % 35),
                    Ag: g.First().Genre,
                    Seasons: seasons,
                    OwnedEpisodes: seasons.Sum(s => s.Owned),
                    TotalEpisodes: seasons.Sum(s => s.Total),
                    Mc: 1.0);
            }).ToList();

        return (movies, shows);
    }
}

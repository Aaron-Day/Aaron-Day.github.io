using MovieLibrary.Core.Identity;
using MovieLibrary.Core.Parsing;
using MovieLibrary.Core.Text;

using Film = (string Title, int Minutes, string Directors);

namespace MovieLibrary.Core.Tests;

/// <summary>
/// <see cref="IdentityKey.ForFilm"/> against the owner's real library: the 39 groups of films whose folded
/// titles collide - 81 films - each with the runtime and the directors his export actually has. This is the
/// regression detector for the key that now binds every record-store entry.
/// </summary>
/// <remarks>
/// <para><b>What a failure here means.</b> Two rows that hash to one identity key are the quietest defect this
/// tool has: one store entry covers both, <c>CatalogueReconciler</c> collapses them into one published record,
/// one film leaves the catalogue, and nothing fails anywhere. Synthetic tests cannot find it, because the
/// collisions are properties of this library.</para>
/// <para><b>Where the rows come from.</b> <c>enrich-cache/index.json</c>, which is committed. A real run
/// records each film's <c>normalizedTitle</c>, <c>runtimeMinutes</c> and <c>normalizedDirectors</c> beside its
/// key, and those three are exactly what <c>ForFilm</c> hashes. Nothing was fetched and no TMDB request was
/// spent to build this: the owner's own cache already holds it. The rows are therefore in NORMALIZED form,
/// which <see cref="TheFixtureIsEveryCollidingFoldedTitleInTheRealLibrary"/> pins by re-running the
/// normalizers over them; folding raw titles is <c>TitleNormalizerTests</c>' job, not this file's.</para>
/// <para><b>Its one blind spot.</b> The index is itself keyed by the identity key, so it physically cannot
/// hold a pair that already collides - and the owner's library has one. See
/// <see cref="BladeRunnersTwoCutsAlreadyShareOneKeyWhichIsWhyTheGroupIsAbsentHere"/>, which asserts that
/// collapse directly from the two raw rows rather than pretending the dataset covers it.</para>
/// <para><b>History.</b> Recovered from <c>FilmOverrideKeyTests</c>, which held the same collision set as
/// (title, runtime) pairs for the retired Overrides tab's key and went out with the tab. That key did not
/// hash the directors and did fold in a trailing year; <c>ForFilm</c> is the other way round - it hashes the
/// directors and reads only <c>NormalizedTitle.Value</c>, from which <c>Normalize</c> has already peeled the
/// year. So the axes are re-stated here for the key that exists, not transcribed.</para>
/// </remarks>
public class IdentityKeyRealLibraryTests
{
    /// <summary>The key as a store entry and a sheet row both reach it, through the public normalizers.</summary>
    private static string Key(Film f)
        => IdentityKey.ForFilm(TitleNormalizer.Normalize(f.Title), f.Minutes,
                               TitleNormalizer.NormalizeDirectors(f.Directors));

    /// <summary>The same key with one field neutralised: what the shortcut would hash, not a second format.</summary>
    private static string KeyWithoutDirectors(Film f)
        => IdentityKey.ForFilm(TitleNormalizer.Normalize(f.Title), f.Minutes, "");

    /// <inheritdoc cref="KeyWithoutDirectors"/>
    private static string KeyWithoutRuntime(Film f)
        => IdentityKey.ForFilm(TitleNormalizer.Normalize(f.Title), 0,
                               TitleNormalizer.NormalizeDirectors(f.Directors));

    private static List<IGrouping<string, Film>> Groups()
        => [.. RealCollidingFilms.GroupBy(f => f.Title, StringComparer.Ordinal)];

    /// <summary>The folded titles whose films do not all get a key of their own under <paramref name="key"/>.</summary>
    private static List<string> Unresolved(Func<Film, string> key)
        => [.. Groups()
               .Where(g => g.Select(key).Distinct(StringComparer.Ordinal).Count() != g.Count())
               .Select(g => g.Key)
               .Order(StringComparer.Ordinal)];

    [Fact]
    public void TheFixtureIsEveryCollidingFoldedTitleInTheRealLibrary()
    {
        var groups = Groups();

        Assert.Equal(81, RealCollidingFilms.Length);
        Assert.Equal(39, groups.Count);
        Assert.NotEmpty(groups);
        Assert.All(groups, g => Assert.True(g.Count() > 1, $"'{g.Key}' is not a colliding group"));

        // The rows are the cache index's own normalized form, so both normalizers are the identity on them.
        // If that ever stops being true the rows are no longer what ForFilm would be handed, and every
        // assertion below is measuring something else.
        Assert.All(RealCollidingFilms, f => Assert.Equal(f.Title, TitleNormalizer.Normalize(f.Title).Value));
        Assert.All(RealCollidingFilms,
                   f => Assert.Equal(f.Directors, TitleNormalizer.NormalizeDirectors(f.Directors)));
    }

    [Fact]
    public void EveryCollidingGroupInTheRealLibraryResolvesIndividually()
    {
        var groups = Groups();

        Assert.NotEmpty(groups);
        Assert.All(groups, g => Assert.Equal(g.Count(), g.Select(Key).Distinct(StringComparer.Ordinal).Count()));
        Assert.Empty(Unresolved(Key));
    }

    [Fact]
    public void TheDirectorsAreWhatPartTheTwoOverboards()
    {
        // 1:52:17 and 1:52:18 both truncate to 112, and the film key carries no year at all: Normalize peels a
        // trailing year into YearHint and ForFilm reads only Value. So Garry Marshall and Rob Greenberg are the
        // ONLY thing between the 1987 Overboard and the 2018 one - and they are the only real group in the
        // whole library that rests on the directors.
        Film marshall = ("overboard", 112, "garry marshall");
        Film greenberg = ("overboard", 112, "greenberg rob");

        Assert.NotEqual(Key(marshall), Key(greenberg));
        Assert.Equal(KeyWithoutDirectors(marshall), KeyWithoutDirectors(greenberg));
        Assert.Equal(["overboard"], Unresolved(KeyWithoutDirectors));
    }

    [Fact]
    public void WithoutTheRuntimeTwelveRealGroupsOfCutsBecomeOneFilmEach()
    {
        // Each of these is one film the owner owns twice: a theatrical cut and an extended, unrated or special
        // one, same folded title and same director, parted only by the minutes. A key that dropped the runtime
        // would publish one of each pair and lose the other.
        Assert.Equal(
            ["captain america the first avenger", "friday the 13th", "hitman", "terminator 2 judgment day",
             "the 40 year old virgin", "the exorcist",
             "the lord of the rings the fellowship of the ring",
             "the lord of the rings the return of the king",
             "the lord of the rings the two towers",
             "the martian", "tropic thunder", "x men days of future past"],
            Unresolved(KeyWithoutRuntime));
    }

    /// <summary>
    /// The key is exact, but <c>StoreBinder</c> hashes each row at every
    /// <see cref="RuntimeDrift.Offsets"/> so an entry written before Apple re-encoded a film still binds. On
    /// the real library that window lands on two pairs of cuts of one film, and Hitman's two are exactly two
    /// minutes apart - so an entry keyed at 93 is one minute from each, which is the equidistant tie
    /// <c>StoreBinder</c> refuses to guess at. Named here so the pair is a known fact about this library
    /// rather than a surprise in a note.
    /// </summary>
    [Fact]
    public void TwoRealPairsOfCutsSitInsideTheBindersDriftWindow()
    {
        var pairs = Groups()
            .SelectMany(g => g.SelectMany(a => g.Select(b => (A: a, B: b))))
            .Where(p => p.A.Minutes < p.B.Minutes
                        && p.A.Directors == p.B.Directors
                        && p.B.Minutes - p.A.Minutes <= RuntimeDrift.ToleranceMinutes)
            .ToList();

        Assert.NotEmpty(pairs);
        Assert.Equal(["captain america the first avenger", "hitman"],
                     pairs.Select(p => p.A.Title).Order(StringComparer.Ordinal));
        Assert.Equal(["hitman"],
                     pairs.Where(p => p.B.Minutes - p.A.Minutes == RuntimeDrift.ToleranceMinutes)
                          .Select(p => p.A.Title));
    }

    /// <summary>
    /// Golden values lifted out of the committed <c>enrich-cache/index.json</c>. They are what makes the rows
    /// above the owner's real export rather than plausible-looking data - grep any one of them in that file -
    /// and they pin the frozen hash input (<c>film|folded title|minutes|normalized directors</c>, SHA-1, 40
    /// lower-case hex) against it. Changing <c>ForFilm</c> or <c>Hash</c> invalidates every cached entry and
    /// re-queries about 1,456 films against the owner's paid TMDB budget; see <c>IdentityKey</c>'s remarks.
    /// </summary>
    [Fact]
    public void TheKeysAreTheOnesTheOwnersCommittedCacheIndexAlreadyHolds()
    {
        Assert.Equal("4980b692927fb1491466f9dc566019248c1fc158", Key(("overboard", 112, "garry marshall")));
        Assert.Equal("7de0ea3b45e13d8fe29035bb116f662bc8354767", Key(("overboard", 112, "greenberg rob")));
        Assert.Equal("86f034a53dad7792d864d3b283930001794d546d",
                     Key(("captain america the first avenger", 124, "joe johnston")));
        Assert.Equal("aeba8dfd86b2950addd1d2a6601149fecf0b1d84",
                     Key(("captain america the first avenger", 125, "joe johnston")));
        Assert.Equal("4f23b9cd62aed8f252d27037006b359f4ae3b605", Key(("blade runner", 117, "ridley scott")));
    }

    /// <summary>
    /// <b>The one real collision the key cannot resolve, and the one this file's dataset cannot contain.</b>
    /// The owner's export has two Blade Runner rows - the theatrical cut at 1:57:10 and the Final Cut at
    /// 1:57:29. They fold to one title, truncate to the same 117 minutes and share one director, so they hash
    /// to ONE identity key, and the cache index holds a single <c>blade runner</c> entry where the sheet has
    /// two rows: the data that would prove the collision is the data the collision destroyed. Asserted here
    /// from the raw rows instead. <c>FilmOverrideKeyTests</c> named the same single exception and accepted it
    /// (they are one film, so one TMDB id is right for both); what is NOT settled by that is the published
    /// record, because <c>RecordKey</c> folds in the same minutes - which is <c>CatalogueReconciler</c>'s
    /// business and not this file's.
    /// </summary>
    [Fact]
    public void BladeRunnersTwoCutsAlreadyShareOneKeyWhichIsWhyTheGroupIsAbsentHere()
    {
        var theatrical = IdentityKey.ForFilm(TitleNormalizer.Normalize("Blade Runner"),
                                             RuntimeParser.FilmMinutes("1:57:10"),
                                             TitleNormalizer.NormalizeDirectors("Ridley Scott"));
        var finalCut = IdentityKey.ForFilm(TitleNormalizer.Normalize("Blade Runner (The Final Cut)"),
                                           RuntimeParser.FilmMinutes("1:57:29"),
                                           TitleNormalizer.NormalizeDirectors("Ridley Scott"));

        Assert.Equal(117, RuntimeParser.FilmMinutes("1:57:10"));
        Assert.Equal(117, RuntimeParser.FilmMinutes("1:57:29"));
        Assert.Equal(theatrical, finalCut);
        Assert.DoesNotContain("blade runner", Groups().Select(g => g.Key));
    }

    private static readonly Film[] RealCollidingFilms =
    [
        ("a nightmare on elm street", 91, "craven wes"),
        ("a nightmare on elm street", 95, "bayer samuel"),
        ("aladdin", 91, "clements john musker ron"),
        ("aladdin", 129, "guy ritchie"),
        ("beauty and the beast", 86, "gary kirk trousdale wise"),
        ("beauty and the beast", 130, "bill condon"),
        ("candyman", 91, "dacosta nia"),
        ("candyman", 99, "bernard rose"),
        ("captain america the first avenger", 124, "joe johnston"),
        ("captain america the first avenger", 125, "joe johnston"),
        ("carrie", 98, "brian de palma"),
        ("carrie", 99, "kimberly peirce"),
        ("casino royale", 131, "guest hughes huston john joseph ken mcgrath parrish robert val"),
        ("casino royale", 144, "campbell martin"),
        ("charlie s angels", 98, "mcg"),
        ("charlie s angels", 118, "banks elizabeth"),
        ("dune", 136, "david lynch"),
        ("dune", 155, "denis villeneuve"),
        ("fantastic four", 100, "josh trank"),
        ("fantastic four", 105, "story tim"),
        ("friday the 13th", 95, "cunningham s sean"),
        ("friday the 13th", 97, "marcus nispel"),
        ("friday the 13th", 105, "marcus nispel"),
        ("ghostbusters", 105, "ivan reitman"),
        ("ghostbusters", 133, "feig paul"),
        ("halloween", 90, "carpenter john"),
        ("halloween", 106, "david gordon green"),
        ("halloween", 110, "rob zombie"),
        ("hitman", 92, "gens xavier"),
        ("hitman", 94, "gens xavier"),
        ("miracle on 34th street", 96, "george seaton"),
        ("miracle on 34th street", 114, "les mayfield"),
        ("mortal kombat", 101, "anderson paul s w"),
        ("mortal kombat", 110, "mcquoid simon"),
        ("mulan", 89, "bancroft barry cook tony"),
        ("mulan", 115, "caro niki"),
        ("overboard", 112, "garry marshall"),
        ("overboard", 112, "greenberg rob"),
        ("peter pan", 77, "clyde geronimi hamilton jackson luske wilfred"),
        ("peter pan", 113, "hogan j p"),
        ("red dawn", 93, "bradley dan"),
        ("red dawn", 114, "john milius"),
        ("road house", 114, "herrington rowdy"),
        ("road house", 121, "doug liman"),
        ("robin hood", 83, "reitherman wolfgang"),
        ("robin hood", 116, "bathurst otto"),
        ("robin hood", 155, "ridley scott"),
        ("robocop", 103, "paul verhoeven"),
        ("robocop", 117, "jose padilha"),
        ("scream", 111, "craven wes"),
        ("scream", 114, "bettinelli gillett matt olpin tyler"),
        ("teenage mutant ninja turtles", 93, "barron steve"),
        ("teenage mutant ninja turtles", 101, "jonathan liebesman"),
        ("terminator 2 judgment day", 137, "cameron james"),
        ("terminator 2 judgment day", 153, "cameron james"),
        ("the 40 year old virgin", 116, "apatow judd"),
        ("the 40 year old virgin", 132, "apatow judd"),
        ("the exorcist", 121, "friedkin william"),
        ("the exorcist", 132, "friedkin william"),
        ("the italian job", 99, "collinson peter"),
        ("the italian job", 110, "f gary gray"),
        ("the jungle book", 106, "favreau jon"),
        ("the jungle book", 111, "marck nick"),
        ("the lion king", 89, "allers minkoff rob roger"),
        ("the lion king", 119, "favreau jon"),
        ("the little mermaid", 83, "clements john musker ron"),
        ("the little mermaid", 135, "marshall rob"),
        ("the lord of the rings the fellowship of the ring", 178, "jackson peter"),
        ("the lord of the rings the fellowship of the ring", 228, "jackson peter"),
        ("the lord of the rings the return of the king", 201, "jackson peter"),
        ("the lord of the rings the return of the king", 263, "jackson peter"),
        ("the lord of the rings the two towers", 179, "jackson peter"),
        ("the lord of the rings the two towers", 235, "jackson peter"),
        ("the martian", 142, "ridley scott"),
        ("the martian", 151, "ridley scott"),
        ("total recall", 113, "paul verhoeven"),
        ("total recall", 130, "len wiseman"),
        ("tropic thunder", 106, "ben stiller"),
        ("tropic thunder", 121, "ben stiller"),
        ("x men days of future past", 131, "bryan singer"),
        ("x men days of future past", 149, "bryan singer"),
    ];
}

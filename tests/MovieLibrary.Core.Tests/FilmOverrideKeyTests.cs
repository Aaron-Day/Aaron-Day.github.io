using MovieLibrary.Core.Parsing;
using MovieLibrary.Core.Text;
using MovieLibrary.Enrich.Pipeline;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// The key a film override is looked up under: folded title, the trailing year if there is one, and the runtime
/// in whole minutes. Neither year nor runtime is enough alone, and no tolerance is allowed, because each shortcut
/// brings back a collision that the real library actually has. <see cref="RealCollidingFilms"/> is every film in
/// the owner's export (1,457 films) whose folded title is shared with another: 37 titles, 76 films.
/// </summary>
public class FilmOverrideKeyTests
{
    private static string Key(string title, string runtime)
        => MoviePipeline.OverrideKey(title, RuntimeParser.FilmMinutes(runtime));

    [Fact]
    public void TheKeyIsTheFoldedTitleThenTheYearThenTheRuntimeMinutes()
    {
        Assert.Equal("halloween|91", Key("Halloween", "1:31:00"));
        Assert.Equal("halloween 2018|106", Key("Halloween (2018)", "1:46:07"));
        Assert.Equal("charlie s angels|98", Key("Charlie's Angels", "1:38:42"));
    }

    [Fact]
    public void TheYearHalfIsTheTvPipelinesKeyNotASecondNormalization()
    {
        Assert.Equal(TvPipeline.OverrideKey("Overboard (1987)") + "|112", Key("Overboard (1987)", "1:52:17"));
        Assert.Equal(TvPipeline.OverrideKey("Aladdin") + "|129", Key("Aladdin", "2:09:10"));
    }

    [Fact]
    public void RuntimeIsWholeMinutesTruncatedExactlyAsTheSheetRowIsRead()
    {
        // The pipeline reads a row's runtime with FilmMinutes, so an override must agree with it to the second.
        Assert.Equal("aladdin|129", Key("Aladdin", "2:09:00"));
        Assert.Equal("aladdin|129", Key("Aladdin", "2:09:59"));
        Assert.Equal("aladdin|130", Key("Aladdin", "2:10:00"));
    }

    [Fact]
    public void YearAloneIsNotEnough_TheTwoOverboardsRunTheSameMinutes()
    {
        // 1:52:17 and 1:52:18 both truncate to 112, so only the year tells them apart.
        Assert.Equal(112, RuntimeParser.FilmMinutes("1:52:17"));
        Assert.Equal(112, RuntimeParser.FilmMinutes("1:52:18"));
        Assert.NotEqual(Key("Overboard (1987)", "1:52:17"), Key("Overboard (2018)", "1:52:18"));
    }

    [Fact]
    public void RuntimeAloneIsNotEnough_TheTwoAladdinsCarryNoYear()
    {
        Assert.NotEqual(Key("Aladdin", "2:09:10"), Key("Aladdin", "1:31:37"));
        Assert.NotEqual(Key("Dune", "2:16:27"), Key("Dune", "2:35:37"));
        Assert.NotEqual(Key("Charlie's Angels", "1:58:38"), Key("Charlie's Angels", "1:38:42"));
    }

    [Fact]
    public void ThereIsNoToleranceWindow_CaptainAmerica125And124StayApart()
    {
        // A plus-or-minus-2 window (MatchScorer's) would merge these two editions back into one key.
        Assert.Equal(125, RuntimeParser.FilmMinutes("2:05:15"));
        Assert.Equal(124, RuntimeParser.FilmMinutes("2:04:09"));
        Assert.NotEqual(Key("Captain America: The First Avenger", "2:05:15"),
                        Key("Captain America: The First Avenger", "2:04:09"));
    }

    [Fact]
    public void EveryCollidingGroupInTheRealLibraryResolvesIndividuallyExceptBladeRunner()
    {
        var groups = RealCollidingFilms
            .GroupBy(f => TitleNormalizer.Normalize(f.Title).Value)
            .Where(g => g.Count() > 1)
            .ToList();

        Assert.Equal(37, groups.Count);                    // the fixture is the whole of the collision set
        Assert.Equal(76, groups.Sum(g => g.Count()));

        var unresolved = groups
            .Where(g => g.Select(f => Key(f.Title, f.Runtime)).Distinct().Count() != g.Count())
            .Select(g => g.Key)
            .ToList();

        // Blade Runner and its Final Cut are 1:57:10 and 1:57:29 (both 117 minutes) and neither has a year.
        // Nothing in the key can part them, but they are one film, so an override means the same TMDB id for both.
        // If this list ever changes, someone altered the key or the library: look at why.
        Assert.Equal(["blade runner"], unresolved);
    }

    [Fact]
    public void EachShortcutKeyLeavesItsOwnGroupsUnresolved_ProvingTheFixtureBitesEveryWay()
    {
        // What dropping the year, dropping the runtime, or widening the runtime into a window would break, on the
        // real library. Each is a key that looks fine and quietly retargets a film that was never meant.
        static List<string> Unresolved(Func<string, string, string> key)
            => [.. RealCollidingFilms.GroupBy(f => TitleNormalizer.Normalize(f.Title).Value)
                .Where(g => g.Count() > 1 && g.Select(f => key(f.Title, f.Runtime)).Distinct().Count() != g.Count())
                .Select(g => g.Key).Order(StringComparer.Ordinal)];

        var noYear = Unresolved((t, r) => TitleNormalizer.Normalize(t).Value + "|" + RuntimeParser.FilmMinutes(r));
        var noRuntime = Unresolved((t, r) => TvPipeline.OverrideKey(t));
        var window = Unresolved((t, r) => TvPipeline.OverrideKey(t) + "|" + RuntimeParser.FilmMinutes(r) / 3);

        Assert.Equal(["blade runner", "overboard"], noYear);                    // Overboard: same minutes, only the year parts them
        Assert.Equal(14, noRuntime.Count);                                      // Aladdin, Dune, Halloween, the Rings, ...
        Assert.Contains("aladdin", noRuntime);
        Assert.Contains("captain america the first avenger", noRuntime);
        Assert.Equal(["blade runner", "captain america the first avenger"], window);   // 124 and 125 share a window
    }

    private static readonly (string Title, string Runtime)[] RealCollidingFilms =
    [
        ("A Nightmare On Elm Street", "1:31:10"),
        ("A Nightmare On Elm Street (2010)", "1:35:29"),
        ("Aladdin", "2:09:10"),
        ("Aladdin", "1:31:37"),
        ("Beauty and the Beast", "1:26:00"),
        ("Beauty and the Beast (2017)", "2:10:42"),
        ("Blade Runner", "1:57:10"),
        ("Blade Runner (The Final Cut)", "1:57:29"),
        ("Candyman (1992)", "1:39:20"),
        ("Candyman (2021)", "1:31:01"),
        ("Captain America: The First Avenger", "2:05:15"),
        ("Captain America: The First Avenger", "2:04:09"),
        ("Carrie (1976)", "1:38:15"),
        ("Carrie (2013)", "1:39:43"),
        ("Casino Royale", "2:24:49"),
        ("Casino Royale (1967)", "2:11:14"),
        ("Charlie's Angels", "1:58:38"),
        ("Charlie's Angels", "1:38:42"),
        ("Dune", "2:16:27"),
        ("Dune", "2:35:37"),
        ("Fantastic Four", "1:40:33"),
        ("Fantastic Four (2005)", "1:45:54"),
        ("Ghostbusters", "1:45:14"),
        ("Ghostbusters (2016)", "2:13:42"),
        ("Halloween", "1:30:47"),
        ("Halloween", "1:50:18"),
        ("Halloween (2018)", "1:46:07"),
        ("Hitman (2007)", "1:32:49"),
        ("Hitman (Unrated)", "1:34:28"),
        ("Miracle On 34th Street (1947)", "1:36:34"),
        ("Miracle On 34th Street (1994)", "1:54:07"),
        ("Mortal Kombat", "1:41:23"),
        ("Mortal Kombat (2021)", "1:50:18"),
        ("Mulan", "1:29:00"),
        ("Mulan (2020)", "1:55:29"),
        ("Overboard (1987)", "1:52:17"),
        ("Overboard (2018)", "1:52:18"),
        ("Peter Pan", "1:53:14"),
        ("Peter Pan (1953)", "1:17:09"),
        ("Red Dawn (1984)", "1:54:01"),
        ("Red Dawn (2012)", "1:33:23"),
        ("Road House (1989)", "1:54:14"),
        ("Road House (2024)", "2:01:55"),
        ("Robin Hood", "1:23:05"),
        ("Robin Hood (2018)", "1:56:07"),
        ("Robin Hood (Unrated Director's Cut) (2010)", "2:35:48"),
        ("Robocop", "1:43:20"),
        ("RoboCop (2014)", "1:57:31"),
        ("Scream", "1:51:03"),
        ("Scream (2022)", "1:54:19"),
        ("Teenage Mutant Ninja Turtles", "1:33:34"),
        ("Teenage Mutant Ninja Turtles (2014)", "1:41:44"),
        ("Terminator 2: Judgment Day", "2:17:06"),
        ("Terminator 2: Judgment Day (Special Edition)", "2:33:46"),
        ("The 40-Year-Old Virgin", "1:56:03"),
        ("The 40-Year-Old Virgin (Unrated)", "2:12:27"),
        ("The Italian Job (1969)", "1:39:57"),
        ("The Italian Job (2003)", "1:50:33"),
        ("The Jungle Book (1994)", "1:51:09"),
        ("The Jungle Book (2016)", "1:46:37"),
        ("The Lion King", "1:29:19"),
        ("The Lion King (2019)", "1:59:22"),
        ("The Little Mermaid (1989)", "1:23:13"),
        ("The Little Mermaid (2023)", "2:15:48"),
        ("The Lord of the Rings: The Fellowship of the Ring", "2:58:24"),
        ("The Lord of the Rings: The Fellowship of the Ring (Extended Edition)", "3:48:09"),
        ("The Lord of the Rings: The Return of the King", "3:21:00"),
        ("The Lord of the Rings: The Return of the King (Extended Edition)", "4:23:10"),
        ("The Lord of the Rings: The Two Towers", "2:59:23"),
        ("The Lord of the Rings: The Two Towers (Extended Edition)", "3:55:22"),
        ("The Martian", "2:22:03"),
        ("The Martian (Extended Version)", "2:31:46"),
        ("Tropic Thunder", "1:46:55"),
        ("Tropic Thunder (Director's Cut)", "2:01:07"),
        ("X-Men: Days of Future Past", "2:11:36"),
        ("X-Men: Days of Future Past (The Rogue Cut)", "2:29:16"),
    ];
}

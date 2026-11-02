using System.Net;
using MovieLibrary.Core.Models;
using MovieLibrary.Core.Sheets;
using MovieLibrary.Enrich.Pipeline;
using MovieLibrary.Enrich.Sheets;

namespace MovieLibrary.Core.Tests;

/// <summary>Shared by the three Overrides test classes.</summary>
internal static class OverrideCsv
{
    public const string Header = GoogleSheetClientTests.OverridesHeader;

    public static Task<SheetOverrides> Fetch(string body)
        => GoogleSheetClientTests.Build(body).FetchOverridesAsync("sheet", CancellationToken.None);

    public static string Line(string title, string runtime, string id, string note = "")
        => $"\"{title}\",\"{runtime}\",\"{id}\",\"{note}\"\n";

    public static List<string> Pairs(IReadOnlyDictionary<string, int> map)
        => [.. map.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}")];
}

/// <summary>
/// The Overrides tab: which map a row goes to and under what key, and how a missing tab is told from a present one.
/// Two things here are easy to get wrong and both look fine in a demo. The film pipeline and the TV pipeline look
/// an override up under DIFFERENT keys (a series by name and year, a film by name, year and runtime minutes), and
/// Google answers an unknown tab with the FIRST sheet and HTTP 200, never an error. So the client returns two maps,
/// and a missing tab is recognised by what comes back. Rows that cannot be applied are in
/// <see cref="GoogleSheetClientOverrideRowTests"/>, and rows that match nothing in <see cref="GoogleSheetClientUnmatchedTests"/>.
/// </summary>
public class GoogleSheetClientOverridesTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private const string Header = OverrideCsv.Header;
    private static Task<SheetOverrides> Fetch(string body) => OverrideCsv.Fetch(body);
    private static string Line(string title, string runtime, string id, string note = "")
        => OverrideCsv.Line(title, runtime, id, note);
    private static List<string> Pairs(IReadOnlyDictionary<string, int> map) => OverrideCsv.Pairs(map);

    // ---- two maps, two keys ---------------------------------------------------------------------------

    [Fact]
    public async Task AFilmOverrideIsKeyedByTitleYearAndRuntimeMinutes()
    {
        var o = await Fetch(Header + Line("Halloween (1978)", "1:31:00", "948"));

        Assert.Equal(["halloween 1978|91=948"], Pairs(o.Films));
        Assert.Equal(MoviePipeline.OverrideKey("Halloween (1978)", 91), Assert.Single(o.Films).Key);
        Assert.Empty(o.Series);
        Assert.Empty(o.Notes);
    }

    [Fact]
    public async Task AFilmWithNoYearIsKeyedByTitleAndMinutes()
    {
        var o = await Fetch(Header + Line("Aladdin", "2:09:10", "11"));

        Assert.Equal(["aladdin|129=11"], Pairs(o.Films));
    }

    [Theory]
    [InlineData("1:31:00", 91)]
    [InlineData("1:31:59", 91)]    // truncated, as FilmMinutes reads a sheet row
    [InlineData("1:32:00", 92)]
    [InlineData("0:01:00", 1)]
    [InlineData("0:00:59", 0)]
    [InlineData("10:00:00", 600)]
    public async Task TheRuntimeInTheKeyIsWholeMinutesTruncated(string runtime, int minutes)
    {
        var o = await Fetch(Header + Line("Alias", runtime, "4490"));

        Assert.Equal([$"alias|{minutes}=4490"], Pairs(o.Films));
        Assert.Empty(o.Series);
    }

    [Fact]
    public async Task ASeriesOverrideKeepsItsTrailingYearThroughTheTvPipelinesOwnKey()
    {
        var o = await Fetch(Header + Line("The Biggest Loser (2020)", "", "20"));

        Assert.Equal(["the biggest loser 2020=20"], Pairs(o.Series));
        Assert.Equal(TvPipeline.OverrideKey("The Biggest Loser (2020)"), Assert.Single(o.Series).Key);
        Assert.Empty(o.Films);
        Assert.Empty(o.Notes);
    }

    [Fact]
    public async Task ASeriesWithNoYearIsKeyedByItsFoldedName()
    {
        var o = await Fetch(Header + Line("The Office", "", "2316"));

        Assert.Equal(["the office=2316"], Pairs(o.Series));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("00")]
    [InlineData("0:00")]
    [InlineData("0:00:00")]
    public async Task ABlankOrZeroRuntimeMeansASeries(string runtime)
    {
        var o = await Fetch(Header + Line("Alias", runtime, "4490"));

        Assert.Equal(["alias=4490"], Pairs(o.Series));
        Assert.Empty(o.Films);
        Assert.Empty(o.Notes);
    }

    // ---- same name, different thing: nothing may collide ------------------------------------------------

    [Fact]
    public async Task ThreeHalloweensAreThreeOverrides()
    {
        // Two carry no year and are told apart by runtime, the third by its year (and runtime).
        var o = await Fetch(Header
            + Line("Halloween", "1:30:47", "948")
            + Line("Halloween", "1:50:18", "949")
            + Line("Halloween (2018)", "1:46:07", "424"));

        Assert.Equal(["halloween 2018|106=424", "halloween|110=949", "halloween|90=948"], Pairs(o.Films));
        Assert.Empty(o.Notes);   // three films, not three disagreeing rows
    }

    [Fact]
    public async Task TheTwoOverboardsRunTheSameMinutesAndAreToldApartByYear()
    {
        var o = await Fetch(Header
            + Line("Overboard (1987)", "1:52:17", "21")
            + Line("Overboard (2018)", "1:52:18", "22"));

        Assert.Equal(["overboard 1987|112=21", "overboard 2018|112=22"], Pairs(o.Films));
        Assert.Empty(o.Notes);
    }

    [Fact]
    public async Task TwoEditionsOneMinuteApartAreTwoOverrides()
    {
        var o = await Fetch(Header
            + Line("Captain America: The First Avenger", "2:05:15", "1771")
            + Line("Captain America: The First Avenger", "2:04:09", "1771"));

        Assert.Equal(["captain america the first avenger|124=1771", "captain america the first avenger|125=1771"],
                     Pairs(o.Films));
        Assert.Empty(o.Notes);   // one film in two editions: the same id twice is right, and not a conflict
    }

    [Fact]
    public async Task AFilmAndASeriesSharingOneFoldedTitleDoNotCollide()
    {
        var o = await Fetch(Header
            + Line("Halloween", "1:31:00", "948")
            + Line("Halloween", "", "111"));

        Assert.Equal(["halloween|91=948"], Pairs(o.Films));
        Assert.Equal(["halloween=111"], Pairs(o.Series));
        Assert.Empty(o.Notes);   // different namespaces, so not even a conflict
    }

    [Fact]
    public async Task ASeriesAndItsYearSuffixedTwinStayTwoOverrides()
    {
        var o = await Fetch(Header
            + Line("The Biggest Loser", "", "10")
            + Line("The Biggest Loser (2020)", "", "20"));

        Assert.Equal(["the biggest loser=10", "the biggest loser 2020=20"], Pairs(o.Series));
        Assert.Empty(o.Notes);
    }

    // ---- the maps are what the real pipelines look up -------------------------------------------------

    private static SheetRow Episode(string title, string series)
        => new(title, "44:00:00", "", series + ": The Complete Series", "Drama", series);

    private static SheetRow Film(string title, string runtime)
        => new(title, runtime, "John Carpenter", "", "Horror", "");

    [Fact]
    public async Task TheSeriesMapResolvesAYearSuffixedSeriesInTheRealTvPipeline()
    {
        var o = await Fetch(Header + Line("The Biggest Loser (2020)", "", "20"));
        var tmdb = new FakeTv()
            .Add(new FakeShow(10, "The Biggest Loser", [new FakeSeason(1, ["A"])]))
            .Add(new FakeShow(20, "The Biggest Loser", [new FakeSeason(1, ["B"])]) { SearchAs = "found by id only" });
        var h = TestPipeline.CreateTv(budget: 0, tmdb.Respond);

        var result = await h.Pipeline.RunAsync(
            [Episode("A", "The Biggest Loser"), Episode("B", "The Biggest Loser (2020)")], o.Series, Ct);

        Assert.Equal([10, 20], result.Shows.Select(s => s.I));
        // the plain twin is searched; the 2020 one is taken by id, never searched
        var searches = h.Handler.Requests.Count(r => r.RequestUri!.AbsolutePath == "/3/search/tv");
        Assert.Equal(1, searches);
    }

    [Fact]
    public async Task TheFilmMapResolvesFilmsOfOneTitleToTheirOwnIdsInTheRealMoviePipeline()
    {
        var o = await Fetch(Header
            + Line("Halloween", "1:30:47", "948")
            + Line("Halloween", "1:50:18", "949")
            + Line("Halloween (2018)", "1:46:07", "424"));
        var pipeline = TestPipeline.ThatFailsIfSearched(948, 949, 424);

        var result = await pipeline.RunAsync(
            [Film("Halloween", "1:30:47"), Film("Halloween", "1:50:18"), Film("Halloween (2018)", "1:46:07")],
            o.Films, Ct);

        // a search would have thrown; each row has the id of ITS override
        Assert.Equal(new Dictionary<int, int> { [90] = 948, [110] = 949, [106] = 424 },
                     result.Records.ToDictionary(r => r.Rt, r => r.I));
    }

    [Fact]
    public async Task TheFilmMapSeparatesTwoFilmsThatRunTheSameMinutesByYear()
    {
        var o = await Fetch(Header
            + Line("Overboard (1987)", "1:52:17", "21")
            + Line("Overboard (2018)", "1:52:18", "22"));
        var pipeline = TestPipeline.ThatFailsIfSearched(21, 22);

        var result = await pipeline.RunAsync(
            [Film("Overboard (1987)", "1:52:17"), Film("Overboard (2018)", "1:52:18")], o.Films, Ct);

        Assert.Equal(new Dictionary<string, int> { ["Overboard (1987)"] = 21, ["Overboard (2018)"] = 22 },
                     result.Records.ToDictionary(r => r.T, r => r.I));
    }

    // ---- the Overrides tab may not exist yet: three cases, told apart by what comes back --------------

    private static void AssertTabNotFoundNote(SheetOverrides o)
    {
        Assert.Empty(o.Films);
        Assert.Empty(o.Series);
        var note = Assert.Single(o.Notes);
        Assert.Contains("'Overrides' tab was not found", note);
        Assert.Contains("no overrides were applied", note);
        // The remedy is what the owner acts on, so it is pinned whole: the tab's name and every column it needs.
        Assert.EndsWith("Add a tab named 'Overrides' with the columns Title, Runtime, TmdbId, Note.", note);
    }

    [Fact]
    public async Task CaseOne_TheOverridesHeaderIsParsed_AndEmptyIsNotAMissingTab()
    {
        var o = await Fetch(Header);   // the tab exists and the owner has not overridden anything yet

        Assert.Empty(o.Films);
        Assert.Empty(o.Series);
        Assert.Empty(o.Notes);
    }

    [Fact]
    public async Task CaseOne_TheRealHeaderOnlyTabIsHealthy_NotMissingAndNotAnError()
    {
        // Verified against the live sheet: HTTP 200, text/csv, 33 bytes, no BOM, no trailing newline, the header
        // as row 1 and nothing else. The tab exists, so there is nothing to report: a note here would be noise.
        const string live = "\"Title\",\"Runtime\",\"TmdbId\",\"Note\"";
        Assert.Equal(33, live.Length);

        var o = await Fetch(live);

        Assert.Empty(o.Films);
        Assert.Empty(o.Series);
        Assert.Empty(o.Notes);
    }

    [Fact]
    public async Task CaseTwo_AnEmptyBodyMeansTheTabIsMissing()
        => AssertTabNotFoundNote(await Fetch(""));

    [Theory]
    [InlineData("\"\",\"\",\"\",\"\"\n")]
    [InlineData("\n\n")]
    [InlineData("\" \",\"  \",\" \",\" \"\n")]            // cells holding only spaces are blank too
    [InlineData("\"\",\"\"\n\"\",\"\"\n")]
    public async Task CaseTwo_ABodyOfNothingButBlankCellsMeansTheTabIsMissing(string body)
        => AssertTabNotFoundNote(await Fetch(body));

    [Fact]
    public async Task CaseTwo_GoogleFallingBackToTheMoviesSheetMeansTheTabIsMissing()
    {
        // This is the real shape: HTTP 200, text/csv, the Movies header with its trailing unnamed column.
        // The third Movies column is Directors. A body that is wrongly parsed as overrides would turn this
        // into an override, so the rows carry a number there on purpose.
        var movies = GoogleSheetClientTests.MoviesHeader
            + "\"Zodiac\",\"2:37:00\",\"123\",\"Buy\",\"Thriller\",\"\"\n";

        AssertTabNotFoundNote(await Fetch(movies));
    }

    [Fact]
    public async Task CaseTwo_GoogleFallingBackToTheShowsSheetMeansTheTabIsMissing()
    {
        var shows = GoogleSheetClientTests.ShowsHeader
            + "\"Pilot\",\"22:30\",\"456\",\"Buy\",\"Comedy\",\"\"\n";

        AssertTabNotFoundNote(await Fetch(shows));
    }

    [Fact]
    public async Task CaseTwo_ABomDoesNotHideTheFallbackToAnotherTab()
    {
        AssertTabNotFoundNote(await GoogleSheetClientTests.BuildWithBom(GoogleSheetClientTests.MoviesHeader)
            .FetchOverridesAsync("sheet", Ct));
    }

    [Fact]
    public async Task CaseOne_ABomOnTheOverridesHeaderStillParses()
    {
        var o = await GoogleSheetClientTests.BuildWithBom(Header + Line("Halloween", "1:31:00", "948"))
            .FetchOverridesAsync("sheet", Ct);

        Assert.Equal(["halloween|91=948"], Pairs(o.Films));
        Assert.Empty(o.Notes);
    }

    [Theory]
    [InlineData("\"Title\",\"Runtime\",\"Id\",\"Note\"\n", "column 3", "expected 'TmdbId', got 'Id'")]   // a misspelt TmdbId
    [InlineData("\"Title\",\"Runtime\"\n", "returned 2 columns", "at least 4")]                         // too few columns
    [InlineData("\"Name\",\"Year\",\"Rating\",\"Comment\"\n", "column 1", "expected 'Title', got 'Name'")]  // another sheet
    [InlineData("\"\",\"Runtime\",\"TmdbId\",\"Note\"\n", "column 1", "expected 'Title', got ''")]    // first cell missing
    [InlineData("\"\",\"\",\"\",\"\"\n\"Title\",\"Runtime\",\"TmdbId\",\"Note\"\n", "column 1", "expected 'Title', got ''")] // header on row 2
    [InlineData("\"Note\",\"TmdbId\",\"Runtime\",\"Title\"\n", "column 1", "expected 'Title', got 'Note'")] // reordered
    public async Task CaseThree_AHeaderThatMatchesNothingKnownThrowsAndSaysWhatWasWrong(
        string body, string where, string what)
    {
        var ex = await Assert.ThrowsAsync<SheetSchemaException>(() => Fetch(body));

        Assert.Contains("'Overrides'", ex.Message);
        Assert.Contains(where, ex.Message);
        Assert.Contains(what, ex.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task AFailedRequestIsAnErrorNotAMissingTab(HttpStatusCode status)
    {
        // Google never says "no such tab" with an error status, so an error here is a real failure
        // (an outage, a sheet that is no longer shared) and must not be taken for zero overrides.
        await Assert.ThrowsAsync<HttpRequestException>(
            () => GoogleSheetClientTests.BuildWithStatus(status).FetchOverridesAsync("sheet", Ct));
    }

    [Fact]
    public async Task AsksForTheOverridesTab()
    {
        var (client, handler) = GoogleSheetClientTests.BuildWithHandler(_ => GoogleSheetClientTests.Csv(Header));

        await client.FetchOverridesAsync("abc-123_X", Ct);

        Assert.Equal("/spreadsheets/d/abc-123_X/gviz/tq?tqx=out:csv&sheet=Overrides",
                     Assert.Single(handler.Requests).RequestUri!.PathAndQuery);
    }
}

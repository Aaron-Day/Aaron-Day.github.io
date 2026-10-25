using System.Net;
using MovieLibrary.Core.Models;
using MovieLibrary.Core.Sheets;
using MovieLibrary.Enrich.Pipeline;
using MovieLibrary.Enrich.Sheets;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// The Overrides tab. Two things here are easy to get wrong and both look fine in a demo:
/// the film pipeline and the TV pipeline look an override up under DIFFERENT keys (the TV one keeps a
/// trailing year, the film one drops it), and Google answers an unknown tab with the FIRST sheet and HTTP 200,
/// never an error. So the client returns two maps, and a missing tab is recognised by what comes back.
/// </summary>
public class GoogleSheetClientOverridesTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private const string Header = GoogleSheetClientTests.OverridesHeader;

    private static Task<SheetOverrides> Fetch(string body)
        => GoogleSheetClientTests.Build(body).FetchOverridesAsync("sheet", Ct);

    private static string Line(string title, string runtime, string id, string note = "")
        => $"\"{title}\",\"{runtime}\",\"{id}\",\"{note}\"\n";

    private static List<string> Pairs(IReadOnlyDictionary<string, int> map)
        => [.. map.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}")];

    // ---- two maps, two keys ---------------------------------------------------------------------------

    [Fact]
    public async Task AFilmOverrideIsKeyedByTheNormalizedTitleWithoutItsYear()
    {
        var o = await Fetch(Header + Line("Halloween (1978)", "1:31:00", "948"));

        Assert.Equal(["halloween=948"], Pairs(o.Films));
        Assert.Empty(o.Series);
        Assert.Empty(o.Notes);
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
    }

    [Theory]
    [InlineData("1:31:00")]
    [InlineData("0:00:01")]   // the smallest non-zero runtime is still a film
    [InlineData("0:01:00")]
    [InlineData("9")]         // one character is not blank
    [InlineData("-0")]        // not a zero either: only digits count
    [InlineData("91")]
    [InlineData("n/a")]       // anything else, however odd, is a film: only blank or zero is a series
    [InlineData("1:xx:00")]
    public async Task AnyOtherRuntimeMeansAFilm(string runtime)
    {
        var o = await Fetch(Header + Line("Alias", runtime, "4490"));

        Assert.Equal(["alias=4490"], Pairs(o.Films));
        Assert.Empty(o.Series);
    }

    [Fact]
    public async Task AFilmAndASeriesSharingOneFoldedTitleDoNotCollide()
    {
        var o = await Fetch(Header
            + Line("Halloween", "1:31:00", "948")
            + Line("Halloween", "", "111"));

        Assert.Equal(["halloween=948"], Pairs(o.Films));
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
    public async Task TheFilmMapResolvesAYearSuffixedFilmInTheRealMoviePipeline()
    {
        var o = await Fetch(Header + Line("Halloween (1978)", "1:31:00", "948"));
        var pipeline = TestPipeline.ThatFailsIfSearched(tmdbId: 948);

        var result = await pipeline.RunAsync(
            [new SheetRow("Halloween (1978)", "1:31:00", "John Carpenter", "", "Horror", "")], o.Films, Ct);

        Assert.Equal(948, Assert.Single(result.Records).I);
    }

    private static SheetRow Episode(string title, string series)
        => new(title, "44:00:00", "", series + ": The Complete Series", "Drama", series);

    // ---- the Overrides tab may not exist yet: three cases, told apart by what comes back --------------

    private static void AssertTabNotFoundNote(SheetOverrides o)
    {
        Assert.Empty(o.Films);
        Assert.Empty(o.Series);
        var note = Assert.Single(o.Notes);
        Assert.Contains("'Overrides' tab was not found", note);
        Assert.Contains("no overrides were applied", note);
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

        Assert.Equal(["halloween=948"], Pairs(o.Films));
        Assert.Empty(o.Notes);
    }

    [Theory]
    [InlineData("\"Title\",\"Runtime\",\"Id\",\"Note\"\n")]                // a misspelt TmdbId
    [InlineData("\"Title\",\"Runtime\"\n")]                                  // too few columns
    [InlineData("\"Name\",\"Year\",\"Rating\",\"Comment\"\n")]               // some other sheet entirely
    [InlineData("\"\",\"Runtime\",\"TmdbId\",\"Note\"\n")]   // the first header cell is missing
    [InlineData("<!DOCTYPE html><html><head><title>Sign in</title></head></html>\n")]
    [InlineData("\"\",\"\",\"\",\"\"\n\"Title\",\"Runtime\",\"TmdbId\",\"Note\"\n")] // header on row 2, not row 1
    public async Task CaseThree_AHeaderThatMatchesNothingKnownThrows(string body)
    {
        var ex = await Assert.ThrowsAsync<SheetSchemaException>(() => Fetch(body));

        Assert.Contains("Overrides", ex.Message);
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

    // ---- a row that cannot be applied must say so, not vanish -----------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("948.5")]
    [InlineData("12 34")]
    [InlineData("99999999999")]
    [InlineData("+948")]
    [InlineData("1,234")]
    public async Task ARowWhoseTmdbIdIsNotAPositiveWholeNumberIsSkippedWithANote(string id)
    {
        var o = await Fetch(Header
            + Line("Alias", "", id, "my note")
            + Line("Halloween", "1:31:00", "948"));

        Assert.Equal(["halloween=948"], Pairs(o.Films));   // the good row is still applied
        Assert.Empty(o.Series);
        var note = Assert.Single(o.Notes);
        Assert.Contains("row 2", note);
        Assert.Contains("'Alias'", note);
        Assert.Contains($"TmdbId '{id}'", note);   // the owner sees what was read, not just that it was wrong
    }

    [Fact]
    public async Task TheTmdbIdIsTrimmed()
    {
        var o = await Fetch(Header + Line("Halloween", "1:31:00", "  948 "));

        Assert.Equal(["halloween=948"], Pairs(o.Films));
        Assert.Empty(o.Notes);
    }

    [Theory]
    [InlineData("", "1:31:00")]
    [InlineData("   ", "")]
    [InlineData("(2020)", "")]          // nothing left once the year is peeled off
    [InlineData("(Extended)", "1:31:00")]
    public async Task ARowWithAnIdButNoUsableTitleIsSkippedWithANote(string title, string runtime)
    {
        var o = await Fetch(Header + Line(title, runtime, "55") + Line("Alias", "", "4490"));

        Assert.Empty(o.Films);
        Assert.Equal(["alias=4490"], Pairs(o.Series));
        var note = Assert.Single(o.Notes);
        Assert.Contains("row 2", note);
        Assert.Contains("title", note);
    }

    [Theory]
    [InlineData("Alias", "", "", "")]
    [InlineData("", "1:31:00", "", "")]
    [InlineData("", "", "55", "")]
    [InlineData("", "", "", "just a note")]
    public async Task ARowWithAnythingInItsFourNamedCellsIsNotPadding(
        string title, string runtime, string id, string note)
    {
        var o = await Fetch(Header + Line(title, runtime, id, note));

        Assert.Empty(o.Films);
        Assert.Empty(o.Series);
        Assert.Contains("row 2", Assert.Single(o.Notes));
    }

    [Fact]
    public async Task PaddingRowsAreSkippedSilently()
    {
        var o = await Fetch(Header
            + Line("", "", "")
            + "\"\",\"\",\"\",\"\",\"stray\"\n"   // only the unnamed column is filled
            + Line("Halloween", "1:31:00", "948"));

        Assert.Equal(["halloween=948"], Pairs(o.Films));
        Assert.Empty(o.Notes);
    }

    [Fact]
    public async Task NotesNameTheSheetRowSoTheOwnerCanFindIt()
    {
        var o = await Fetch(Header
            + Line("Halloween", "1:31:00", "948")
            + Line("Alias", "", "oops")
            + Line("Heat", "2:50:00", "949")
            + Line("Lost", "", "nope"));

        Assert.Equal(2, o.Notes.Count);
        Assert.Contains("row 3", o.Notes[0]);   // the header is row 1
        Assert.Contains("row 5", o.Notes[1]);
    }

    [Fact]
    public async Task TwoRowsForOneKeyWithDifferentIdsConflictSoTheLaterWinsLoudly()
    {
        // The pipelines key a film by folded title alone, so these two cannot point at different ids.
        // That limit is old; what must not happen is one row silently losing to the other.
        var o = await Fetch(Header
            + Line("Halloween", "1:31:00", "948")
            + Line("Halloween (2018)", "1:46:00", "424"));

        Assert.Equal(["halloween=424"], Pairs(o.Films));
        var note = Assert.Single(o.Notes);
        Assert.Contains("row 3", note);
        Assert.Contains("948", note);
        Assert.Contains("424", note);
        Assert.Contains("film", note);
    }

    [Fact]
    public async Task ASeriesConflictIsReportedToo()
    {
        var o = await Fetch(Header + Line("Alias", "", "1") + Line("Alias", "", "2"));

        Assert.Equal(["alias=2"], Pairs(o.Series));
        Assert.Contains("series", Assert.Single(o.Notes));
    }

    [Fact]
    public async Task ARepeatedRowWithTheSameIdIsNotAConflict()
    {
        var o = await Fetch(Header + Line("Alias", "", "7") + Line("Alias", "", "7")
            + Line("Heat", "2:50:00", "9") + Line("Heat", "2:50:00", "9"));

        Assert.Equal(["alias=7"], Pairs(o.Series));
        Assert.Equal(["heat=9"], Pairs(o.Films));
        Assert.Empty(o.Notes);
    }
}

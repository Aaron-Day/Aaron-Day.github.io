using System.Net;
using MovieLibrary.Core.Sheets;
using MovieLibrary.Enrich.Sheets;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// The Movies and Shows half of <see cref="GoogleSheetClient"/>, and the HTTP behaviour all three tabs share.
/// The Overrides tab has its own file: <see cref="GoogleSheetClientOverridesTests"/>.
/// </summary>
public class GoogleSheetClientTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    internal const string MoviesHeader = "\"Title\",\"Runtime\",\"Directors\",\"PurchaseInfo\",\"Genres\",\"\"\n";
    internal const string ShowsHeader = "\"Title\",\"Runtime\",\"Series\",\"PurchaseInfo\",\"Genre\",\"\"\n";
    internal const string OverridesHeader = "\"Title\",\"Runtime\",\"TmdbId\",\"Note\"\n";

    private const string MoviesCsv =
        MoviesHeader +
        "\"The A-Team\",\"1:59:16\",\"Joe Carnahan\",\"Buy HD\",\"Action & Adventure\",\"1\"\n";

    private const string ShowsCsv =
        ShowsHeader +
        "\"Season 1, Episode 2: Diversity Day\",\"22:30\",\"The Office\",\"Season Pass\",\"Comedy\",\"\"\n";

    internal static HttpResponseMessage Csv(string body)
        => new(HttpStatusCode.OK)
           { Content = new StringContent(body, System.Text.Encoding.UTF8, "text/csv") };

    internal static (GoogleSheetClient Client, StubHandler Handler) BuildWithHandler(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        return (new GoogleSheetClient(new HttpClient(handler)
                { BaseAddress = new Uri("https://docs.google.com/") }), handler);
    }

    internal static GoogleSheetClient Build(string body) => BuildWithHandler(_ => Csv(body)).Client;

    /// <summary>
    /// A body whose first cell starts with a BOM that survives decoding. HttpContent drops ONE leading UTF-8 BOM,
    /// so a plain U+FEFF prefix would never reach the parser and the test would prove nothing; two do.
    /// </summary>
    internal static GoogleSheetClient BuildWithBom(string body)
        => BuildWithHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
           {
               Content = new ByteArrayContent([0xEF, 0xBB, 0xBF, .. System.Text.Encoding.UTF8.GetPreamble(),
                                               .. System.Text.Encoding.UTF8.GetBytes(body)])
                         { Headers = { ContentType = new("text/csv") { CharSet = "utf-8" } } }
           }).Client;

    internal static GoogleSheetClient BuildWithStatus(HttpStatusCode status)
        => BuildWithHandler(_ => new HttpResponseMessage(status)).Client;

    [Fact]
    public async Task ParsesMovieRowsAndSkipsTheHeader()
    {
        var rows = await Build(MoviesCsv).FetchMoviesAsync("sheet", Ct);

        var row = Assert.Single(rows);
        Assert.Equal("The A-Team", row.Title);
        Assert.Equal("1:59:16", row.Runtime);
        Assert.Equal("Joe Carnahan", row.Directors);
        Assert.Equal("Buy HD", row.PurchaseInfo);
        Assert.Equal("Action & Adventure", row.Genre);
        Assert.Equal(string.Empty, row.Series);
    }

    [Fact]
    public async Task ParsesShowRowsIntoTheSeriesColumn()
    {
        var rows = await Build(ShowsCsv).FetchShowsAsync("sheet", Ct);

        var row = Assert.Single(rows);
        Assert.Equal("Season 1, Episode 2: Diversity Day", row.Title); // a quoted comma survives
        Assert.Equal("22:30", row.Runtime);
        Assert.Equal("The Office", row.Series);
        Assert.Equal("Season Pass", row.PurchaseInfo);
        Assert.Equal("Comedy", row.Genre);
        Assert.Equal(string.Empty, row.Directors);
    }

    [Fact]
    public async Task TrimsCellsAndSkipsRowsWithNoTitle()
    {
        var csv = MoviesHeader +
            "\"  Spaced Out  \",\" 1:30:00 \",\" Someone \",\"\",\" Drama \",\"\"\n" +
            "\"\",\"1:00:00\",\"Nobody\",\"\",\"Drama\",\"\"\n" +
            "\"   \",\"1:00:00\",\"Nobody\",\"\",\"Drama\",\"\"\n";

        var row = Assert.Single(await Build(csv).FetchMoviesAsync("sheet", Ct));

        Assert.Equal(("Spaced Out", "1:30:00", "Someone", "Drama"),
                     (row.Title, row.Runtime, row.Directors, row.Genre));
    }

    [Fact]
    public async Task ARowShorterThanTheHeaderIsReadWithEmptyTrailingCells()
    {
        var row = Assert.Single(await Build(MoviesHeader + "\"Short Film\"\n").FetchMoviesAsync("sheet", Ct));

        Assert.Equal("Short Film", row.Title);
        Assert.Equal(string.Empty, row.Runtime);
        Assert.Equal(string.Empty, row.Genre);
    }

    [Fact]
    public async Task ABomOnTheFirstHeaderCellDoesNotAbortTheImport()
    {
        var rows = await BuildWithBom(MoviesCsv).FetchMoviesAsync("sheet", Ct);

        Assert.Equal("The A-Team", Assert.Single(rows).Title);
    }

    [Fact]
    public async Task AskForTheRightTabOverTheAnonymousCsvEndpoint()
    {
        var (client, handler) = BuildWithHandler(req => Csv(
            req.RequestUri!.Query.EndsWith("sheet=Movies") ? MoviesCsv : ShowsCsv));

        await client.FetchMoviesAsync("abc-123_X", Ct);
        await client.FetchShowsAsync("abc-123_X", Ct);

        Assert.All(handler.Requests, r => Assert.Equal(HttpMethod.Get, r.Method));
        Assert.Equal(
            ["/spreadsheets/d/abc-123_X/gviz/tq?tqx=out:csv&sheet=Movies",
             "/spreadsheets/d/abc-123_X/gviz/tq?tqx=out:csv&sheet=Shows"],
            handler.Requests.Select(r => r.RequestUri!.PathAndQuery));
    }

    [Fact]
    public async Task RejectsTheWrongTabGoogleSilentlySubstituted()
    {
        // Google answers an unknown tab name with the FIRST sheet and HTTP 200. Asking for Shows but
        // receiving the Movies sheet must fail loudly, not quietly enrich.
        var shows = await Assert.ThrowsAsync<SheetSchemaException>(
            () => Build(MoviesCsv).FetchShowsAsync("sheet", Ct));
        Assert.Contains("Shows", shows.Message);

        var movies = await Assert.ThrowsAsync<SheetSchemaException>(
            () => Build(ShowsCsv).FetchMoviesAsync("sheet", Ct));
        Assert.Contains("Movies", movies.Message);
    }

    [Fact]
    public async Task RejectsAnOverridesSheetAsMoviesOrShows()
    {
        var csv = OverridesHeader + "\"Halloween\",\"1:31:00\",\"948\",\"\"\n";

        await Assert.ThrowsAsync<SheetSchemaException>(() => Build(csv).FetchMoviesAsync("sheet", Ct));
        await Assert.ThrowsAsync<SheetSchemaException>(() => Build(csv).FetchShowsAsync("sheet", Ct));
    }

    [Fact]
    public async Task RejectsARenamedColumn()
    {
        var renamed = MoviesCsv.Replace("\"Directors\"", "\"Director\"");

        await Assert.ThrowsAsync<SheetSchemaException>(() => Build(renamed).FetchMoviesAsync("sheet", Ct));
    }

    [Fact]
    public async Task AnEmptyBodyIsRejectedForMoviesAndShows()
    {
        // Unlike Overrides, these two tabs are required: nothing to enrich means something is wrong.
        await Assert.ThrowsAsync<SheetSchemaException>(() => Build("").FetchMoviesAsync("sheet", Ct));
        await Assert.ThrowsAsync<SheetSchemaException>(() => Build("").FetchShowsAsync("sheet", Ct));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task AFailedRequestIsNotSwallowed(HttpStatusCode status)
    {
        var client = BuildWithStatus(status);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.FetchMoviesAsync("sheet", Ct));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.FetchShowsAsync("sheet", Ct));
    }

    /// <summary>Like a real transport, honours the token it is handed. <see cref="StubHandler"/> ignores it.</summary>
    private sealed class CancellationAwareHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Csv(MoviesCsv));
        }
    }

    [Fact]
    public async Task ACancelledTokenStopsEveryFetch()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var client = new GoogleSheetClient(new HttpClient(new CancellationAwareHandler())
                                           { BaseAddress = new Uri("https://docs.google.com/") });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.FetchMoviesAsync("sheet", cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.FetchShowsAsync("sheet", cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.FetchOverridesAsync("sheet", cts.Token));

        // and the same client is fine with a live token, so the failure above was the token
        Assert.Single(await client.FetchMoviesAsync("sheet", Ct));
    }

    // ---- a web page where the CSV should be ------------------------------------------------------------

    private const string HtmlPage =
        "<!DOCTYPE html><html><head><title>Sign in - Google Accounts</title></head><body>Sign in</body></html>";

    private static HttpResponseMessage Response(string body, string mediaType)
        => new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, mediaType) };

    private static Task Fetch(GoogleSheetClient client, string tab) => tab switch
    {
        "Movies" => client.FetchMoviesAsync("sheet", Ct),
        "Shows" => client.FetchShowsAsync("sheet", Ct),
        _ => client.FetchOverridesAsync("sheet", Ct),
    };

    [Theory]
    [InlineData("Movies", "text/html", HtmlPage)]
    [InlineData("Shows", "text/html", HtmlPage)]
    [InlineData("Overrides", "text/html", HtmlPage)]
    [InlineData("Movies", "text/csv", HtmlPage)]                       // the type lies, the body does not
    [InlineData("Shows", "text/csv", "  \r\n" + HtmlPage)]            // leading whitespace
    [InlineData("Movies", "text/html", "")]                            // the type alone is enough
    [InlineData("Overrides", "TEXT/HTML", "")]                          // and its case does not matter
    public async Task AWebPageInsteadOfCsvSaysTheSheetMayNoLongerBeShared(string tab, string mediaType, string body)
    {
        var client = BuildWithHandler(_ => Response(body, mediaType)).Client;

        var ex = await Assert.ThrowsAsync<SheetAccessException>(() => Fetch(client, tab));

        Assert.Contains("shared", ex.Message);
        Assert.Contains($"'{tab}'", ex.Message);
        Assert.DoesNotContain("<", ex.Message);               // the page is not echoed back
        Assert.DoesNotContain("Sign in", ex.Message);
        Assert.DoesNotContain("still named", ex.Message);       // and the misleading tab-name advice is gone
    }

    [Theory]
    [InlineData("Movies")]
    [InlineData("Shows")]
    [InlineData("Overrides")]
    public async Task ABomInFrontOfAWebPageDoesNotHideIt(string tab)
    {
        // BuildWithBom leaves a BOM that survives decoding (a single one is dropped before the client sees it).
        var client = BuildWithBom(HtmlPage);

        await Assert.ThrowsAsync<SheetAccessException>(() => Fetch(client, tab));
    }

    [Theory]
    [InlineData("Movies")]
    [InlineData("Shows")]
    [InlineData("Overrides")]
    public async Task ACsvBodyThatMerelyMentionsMarkupIsNotAWebPage(string tab)
    {
        // Only a body that STARTS with markup is a page; a header cell is data.
        var header = tab switch
        {
            "Movies" => MoviesHeader, "Shows" => ShowsHeader, _ => OverridesHeader,
        };
        var client = BuildWithHandler(_ => Response(header + "\"<b>Bold</b>\",\"1:30:00\",\"7\",\"x\",\"y\"\n", "text/csv")).Client;

        var ex = await Record.ExceptionAsync(() => Fetch(client, tab));

        Assert.IsNotType<SheetAccessException>(ex);
    }

    [Fact]
    public async Task TheSheetIsFetchedAnonymously()
    {
        var (client, handler) = BuildWithHandler(req => Csv(
            req.RequestUri!.Query.EndsWith("sheet=Movies") ? MoviesCsv
            : req.RequestUri!.Query.EndsWith("sheet=Shows") ? ShowsCsv : OverridesHeader));

        await client.FetchMoviesAsync("sheet", Ct);
        await client.FetchShowsAsync("sheet", Ct);
        await client.FetchOverridesAsync("sheet", Ct);

        Assert.Equal(3, handler.Requests.Count);
        foreach (var request in handler.Requests)
        {
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("Authorization"));
            Assert.False(request.Headers.Contains("X-Goog-Api-Key"));
            Assert.False(request.Headers.Contains("Cookie"));
            var url = request.RequestUri!.PathAndQuery.ToLowerInvariant();
            Assert.DoesNotContain("key=", url);
            Assert.DoesNotContain("token", url);
            Assert.Equal(string.Empty, request.RequestUri!.UserInfo);
        }
    }

    [Fact]
    public async Task AResponseWithNoContentTypeIsStillReadAsCsv()
    {
        var client = BuildWithHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(MoviesCsv)) }).Client;

        Assert.Equal("The A-Team", Assert.Single(await client.FetchMoviesAsync("sheet", Ct)).Title);
    }
}

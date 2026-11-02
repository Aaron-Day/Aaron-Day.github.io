namespace MovieLibrary.Core.Tests;

/// <summary>
/// What is checked before a request is spent. A missing token outside fixture mode is known before the Sheet is even
/// fetched, and a Movies row whose runtime cannot be read would otherwise abort the whole film pass with one anonymous
/// message (Task 12's "poison row"), so every such row is named at once, when fixing the Sheet is still free.
/// </summary>
public class EnrichRunPreflightTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AMissingTokenFailsBeforeAnyRequestAndPointsAtTheActionsSecret(string token)
    {
        using var h = new RunHarness { MoviesCsv = RunLibrary.Movies(RunLibrary.Fargo) };
        h.Options = h.Options with { Token = token };

        var result = await h.RunAsync();

        Assert.Equal(1, result.ExitCode);
        Assert.False(result.Deployable);
        var err = h.Err.ToString();
        Assert.Contains("TMDB_TOKEN", err);
        Assert.Contains("repository", err);        // an Actions *repository* secret; Codespaces secrets are another store
        Assert.Empty(h.SheetHandler.Requests);
        Assert.Empty(h.TmdbHandler.Requests);
        Assert.False(h.Wrote("meta.json"));
        Assert.Contains("TMDB_TOKEN", h.Summary);
        Assert.Contains("deployable=false", h.GitHubOutput);
    }

    private static RunHarness WithBrokenRuntimes()
    {
        var h = new RunHarness();
        h.MoviesCsv = RunLibrary.Movies(
            RunLibrary.Fargo,
            ("Broken Arrow", "1:48", "John Woo", "Action"),            // two-part: a film runtime is always h:mm:ss
            ("Casablanca", "", "Michael Curtiz", "Drama"),             // empty
            ("Dune", "two hours", "Denis Villeneuve", "Sci-Fi"));
        h.Tmdb = RunLibrary.FargoOnly();
        return h;
    }

    [Fact]
    public async Task EveryMoviesRowWithAnUnreadableRuntimeIsNamedBeforeAnythingIsSpent()
    {
        using var h = WithBrokenRuntimes();

        var result = await h.RunAsync();

        Assert.Equal(1, result.ExitCode);
        var err = h.Err.ToString();
        Assert.Contains("FATAL", err);
        Assert.Contains("'Broken Arrow' (runtime '1:48')", err);
        Assert.Contains("'Casablanca' (runtime '')", err);
        Assert.Contains("'Dune' (runtime 'two hours')", err);
        Assert.DoesNotContain("Fargo", err);                        // the good row is not accused
        Assert.Contains("Movies tab", err);
        Assert.Contains("h:mm:ss", err);
        Assert.Empty(h.TmdbHandler.Requests);
        Assert.False(h.Wrote("movies.json"));
        Assert.Contains("deployable=false", h.GitHubOutput);
        Assert.Contains("Broken Arrow", h.Summary);
    }

    [Fact]
    public async Task AnUnreadableRuntimeStopsAFixtureRunToo_WithTheSameMessageNotABareFormatException()
    {
        using var h = WithBrokenRuntimes();
        h.Options = h.Options with { Fixtures = true, Token = "" };

        var result = await h.RunAsync();

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("'Broken Arrow' (runtime '1:48')", h.Err.ToString());
        Assert.DoesNotContain("FormatException", h.Err.ToString());
    }

    [Fact]
    public async Task ARuntimeOnTheShowsTabIsNeverChecked_ItIsNotUsed()
    {
        using var h = new RunHarness { MoviesCsv = RunLibrary.Movies(RunLibrary.Fargo) };
        h.ShowsCsv = GoogleSheetClientTests.ShowsHeader
            + "\"Season 1, Episode 1: Pilot\",\"not a runtime\",\"The Office\",\"Pass\",\"Comedy\",\"\"\n";
        var films = RunLibrary.FargoOnly();
        h.Tmdb = req => req.RequestUri!.AbsolutePath.StartsWith("/3/search/tv", StringComparison.Ordinal)
            ? TestPipeline.Json(TestPipeline.EmptyResults)
            : films(req);

        var result = await h.RunAsync();

        Assert.Equal(0, result.ExitCode);
    }
}

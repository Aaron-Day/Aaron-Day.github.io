using MovieLibrary.Core.Identity;
using MovieLibrary.Core.Text;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// The record store's notes are the owner's own corrections failing, so they must reach him. They travel as
/// <c>ReviewQueue.OverrideNotes</c>, which the job summary prints FIRST and caps separately.
/// </summary>
public class EnrichRunStoreNotesTests
{
    private static string Store(string identityKey, string title) =>
        "{ \"version\": 1, \"series\": [], \"films\": [ { \"identityKey\": \"" + identityKey + "\", " +
        "\"sheet\": { \"title\": \"" + title + "\", \"runtime\": \"1:38:00\", \"directors\": \"Joel Coen\" }, " +
        "\"records\": [ { \"tmdbId\": 275 } ] } ] }";

    private static RunHarness Library()
    {
        var h = new RunHarness();
        h.MoviesCsv = RunLibrary.Movies(RunLibrary.Fargo);
        h.Tmdb = RunLibrary.FargoOnly(RunLibrary.FargoDetails, RunLibrary.FargoCredits);
        return h;
    }

    [Fact]
    public async Task AnEntryThatMatchesNoRowIsReportedAndTheRunStillPublishes()
    {
        using var h = Library();
        var gone = IdentityKey.ForFilm(TitleNormalizer.Normalize("A Film I Sold"), 98,
                                       TitleNormalizer.NormalizeDirectors("Joel Coen"));
        h.WriteStore(Store(gone, "A Film I Sold"));

        var result = await h.RunAsync();

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Deployable);                 // a bad entry is not a bad catalogue
        Assert.Contains("A Film I Sold", h.Summary, StringComparison.Ordinal);
        Assert.Contains("matches no film", h.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AMalformedStoreStopsTheRunBeforeAnythingIsWritten()
    {
        using var h = Library();
        h.WriteStore("{ \"version\": 99, \"films\": [], \"series\": [] }");

        var result = await h.RunAsync();

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(result.Deployable);
        Assert.False(h.Wrote("movies.json"));
        Assert.Contains("version 99", h.Err.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NoStoreFileAtAllIsTheNormalCaseAndSaysNothing()
    {
        using var h = Library();

        var result = await h.RunAsync();

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("record store entry", h.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AFixtureRunPrintsTheNotesBecauseItsQueueIsEmptyAndTheyWouldVanish()
    {
        using var h = Library();
        h.Options = h.Options with { Fixtures = true, Token = "" };
        var gone = IdentityKey.ForFilm(TitleNormalizer.Normalize("A Film I Sold"), 98,
                                       TitleNormalizer.NormalizeDirectors("Joel Coen"));
        h.WriteStore(Store(gone, "A Film I Sold"));

        var result = await h.RunAsync();

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Record store entry 'A Film I Sold' matches no film", h.Out.ToString(), StringComparison.Ordinal);
        Assert.Contains("record store: 0 film entries, 0 series entries, 1 note(s)", h.Out.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFixtureRunStopsOnAMalformedStoreBecauseItIsTheCheapestPlaceToFindOut()
    {
        using var h = Library();
        h.Options = h.Options with { Fixtures = true, Token = "" };
        h.WriteStore("{ \"version\": 99, \"films\": [], \"series\": [] }");

        var result = await h.RunAsync();

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("version 99", h.Err.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}

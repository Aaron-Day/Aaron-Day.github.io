using MovieLibrary.Core.Identity;
using MovieLibrary.Core.Models;
using MovieLibrary.Core.Store;
using MovieLibrary.Core.Text;
using MovieLibrary.Enrich.Store;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// <c>--print-keys</c>: the only thing in Phase 1 that prints the 40-character identity key an entry in
/// <c>record-store.json</c> names its row by.
/// </summary>
/// <remarks>
/// <para><b>Every test here proves it spends nothing.</b> Each harness leaves <see cref="RunHarness.Tmdb"/> at
/// its default, which fails the test on any TMDB request whatsoever, and each clears the token, so a run that
/// fell through to the real path would die on the missing-token guard instead.</para>
/// <para><b>And that it publishes nothing.</b> A diagnostic that wrote a catalogue would be a diagnostic that
/// could deploy one.</para>
/// </remarks>
public class EnrichRunPrintKeysTests
{
    private static RunHarness Library()
    {
        var h = new RunHarness();
        h.MoviesCsv = RunLibrary.Movies(RunLibrary.Fargo);
        h.ShowsCsv = RunLibrary.Shows(("Season 1, Episode 1: Pilot", "The Office", "Comedy"));
        h.Options = h.Options with { PrintKeys = true, Token = "" };
        return h;
    }

    private static string FilmKey(string title, int minutes, string directors)
        => IdentityKey.ForFilm(TitleNormalizer.Normalize(title), minutes,
                               TitleNormalizer.NormalizeDirectors(directors));

    [Fact]
    public async Task EveryRowsKeyIsPrintedBesideSomethingAHumanCanRecognise()
    {
        using var h = Library();

        var result = await h.RunAsync();

        var expected = FilmKey("Fargo", 98, "Joel Coen & Ethan Coen");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains(expected, h.Out.ToString(), StringComparison.Ordinal);
        Assert.Contains("Fargo", h.Out.ToString(), StringComparison.Ordinal);
        Assert.Contains(IdentityKey.ForSeriesName("The Office"), h.Out.ToString(), StringComparison.Ordinal);
        Assert.Contains("The Office", h.Out.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ItPrintsAndStopsRatherThanPublishingAnything()
    {
        using var h = Library();

        var result = await h.RunAsync();

        Assert.False(result.Deployable);
        Assert.False(h.Wrote("movies.json"));
        Assert.False(h.Wrote("meta.json"));
        Assert.Empty(h.Summary);
    }

    [Fact]
    public async Task TheActionsOutputsStillSayWhatHappenedSoADeployStepCannotMisreadSilence()
    {
        // WriteOutputsAsync's contract is that every outcome writes the file: a step that finds it WITHOUT
        // `deployable` knows the tool did not finish. A diagnostic run is an outcome like any other.
        using var h = Library();

        var result = await h.RunAsync();

        // Exit code first: a FAILED run writes deployable=false too, so without this the three lines below
        // would be satisfied by the tool dying before it printed anything.
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("deployable=false", h.GitHubOutput, StringComparison.Ordinal);
        Assert.Contains("stopped_on_budget=false", h.GitHubOutput, StringComparison.Ordinal);
        Assert.Contains("cache_saved=true", h.GitHubOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ItNeedsNoTokenAtAllTheWayAFixtureRunDoesNot()
    {
        using var h = Library();                       // Token is already "" above

        var result = await h.RunAsync();

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("TMDB_TOKEN", h.Err.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AStoreTooBrokenToReadIsNoReasonNotToPrintTheKeysThatWouldFixIt()
    {
        // The branch sits BEFORE the store is read on purpose. An unreadable record-store.json is exactly
        // when its owner needs a key to write into it, and a run that refused to print one would send him
        // back to the file he cannot address.
        using var h = Library();
        h.WriteStore("{ not json at all");

        var result = await h.RunAsync();

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(FilmKey("Fargo", 98, "Joel Coen & Ethan Coen"), h.Out.ToString(), StringComparison.Ordinal);
    }

    // ---- a key two rows carry is the one key no entry can use on one of them -------------------------

    /// <summary>
    /// Two cuts of one film: one folded title, one truncated runtime, one director, and so ONE identity key.
    /// Synthetic on purpose - the fact is a property of the key, not of anybody's data - but the owner's own
    /// library has such a pair, and <c>IdentityKeyRealLibraryTests</c> asserts it from the committed cache.
    /// </summary>
    private static RunHarness TwoCutsOfOneFilm()
    {
        var h = new RunHarness();
        h.MoviesCsv = RunLibrary.Movies(
            ("Solaris", "1:45:00", "Andrei Tarkovsky", "Sci-Fi"),
            ("Solaris (Director's Cut)", "1:45:30", "Andrei Tarkovsky", "Sci-Fi"));
        h.Options = h.Options with { PrintKeys = true, Token = "" };
        return h;
    }

    private static string[] Lines(string text) => text.ReplaceLineEndings().Split(Environment.NewLine);

    [Fact]
    public async Task TwoRowsThatCarryOneKeyAreNamedAsAPairNoEntryCanSeparate()
    {
        using var h = TwoCutsOfOneFilm();

        var result = await h.RunAsync();

        var printed = h.Out.ToString();
        var shared = FilmKey("Solaris", 105, "Andrei Tarkovsky");
        Assert.Equal(0, result.ExitCode);
        // The premise, stated here rather than assumed: both rows really do hash to the one key.
        Assert.Equal(shared, FilmKey("Solaris (Director's Cut)", 105, "Andrei Tarkovsky"));
        Assert.Contains("WARNING", printed, StringComparison.Ordinal);
        Assert.Contains($"{shared}  is carried by 2 rows:", printed, StringComparison.Ordinal);
        Assert.Contains("'Solaris' (1:45:00, Andrei Tarkovsky)", printed, StringComparison.Ordinal);
        Assert.Contains("'Solaris (Director's Cut)' (1:45:30, Andrei Tarkovsky)", printed, StringComparison.Ordinal);
        Assert.Contains("never correct one of them", printed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BothOfThoseRowsAreMarkedInTheListingToo()
    {
        // The warning is at the end of a listing that is 1,456 lines long on the real library, so the rows
        // themselves carry the mark: a grep for the title finds it without reading to the bottom.
        using var h = TwoCutsOfOneFilm();

        await h.RunAsync();

        var films = Lines(h.Out.ToString())
            .Where(l => l.StartsWith("film   ", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(2, films.Count);
        Assert.All(films, l => Assert.EndsWith("[shared key]", l, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ARowWhoseKeyIsItsOwnIsMarkedWithNothingAndWarnsAboutNothing()
    {
        using var h = Library();

        var result = await h.RunAsync();

        // The listing has to be there before its silence means anything: a run that printed nothing at all
        // would satisfy both DoesNotContains below.
        var printed = h.Out.ToString();
        Assert.Equal(0, result.ExitCode);
        Assert.Contains(FilmKey("Fargo", 98, "Joel Coen & Ethan Coen"), printed, StringComparison.Ordinal);
        Assert.Contains(IdentityKey.ForSeriesName("The Office"), printed, StringComparison.Ordinal);
        Assert.DoesNotContain("[shared key]", printed, StringComparison.Ordinal);
        Assert.DoesNotContain("WARNING", printed, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every key this listing prints has to be one an entry can actually be written against. A row whose
    /// runtime truncates to 0 minutes (<c>0:00:30</c>) was the exception: the key printed for it was real --
    /// it is the key <c>MoviePipeline</c> looks the row's entry up under -- but <c>StoreBinder.IndexRows</c>
    /// skipped every offset whose runtime was at or below zero, offset 0 among them, so nothing could ever be
    /// filed under it and a pasted entry came back as "matches no film on the Movies tab".
    /// </summary>
    [Fact]
    public async Task TheKeyPrintedForARowWhoseRuntimeTruncatesToZeroIsOneAnEntryCanBind()
    {
        using var h = new RunHarness();
        h.MoviesCsv = RunLibrary.Movies(("Blink", "0:00:30", "Hettie Macdonald", "Sci-Fi"));
        h.Options = h.Options with { PrintKeys = true, Token = "" };

        var result = await h.RunAsync();

        var printed = Lines(h.Out.ToString()).Single(l => l.StartsWith("film   ", StringComparison.Ordinal));
        var key = printed.Split("  ", StringSplitOptions.RemoveEmptyEntries)[1].Trim();
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(FilmKey("Blink", 0, "Hettie Macdonald"), key);

        // The listing's own promise, taken at its word: paste the key, and the entry binds.
        var store = RecordStoreReader.Read(
            "{ \"version\": 1, \"series\": [], \"films\": [ { \"identityKey\": \"" + key + "\", " +
            "\"sheet\": { \"title\": \"Blink\" }, \"records\": [ { \"tmdbId\": 1 } ] } ] }",
            "record-store.json");
        var bound = StoreBinder.Bind(store, [new SheetRow("Blink", "0:00:30", "Hettie Macdonald", "", "Sci-Fi", "")], []);

        Assert.Equal(1, Assert.Contains(key, bound.Films).Records.Single().TmdbId);
        Assert.Empty(bound.Notes);
    }

    /// <summary>
    /// <c>TvPipeline.RunAsync</c> reports "{n} row(s) have no series name and were skipped" in the review
    /// queue. The listing filtered the same rows out and said nothing, so a block of malformed Shows rows was
    /// invisible in the one diagnostic built to show the owner his sheet: a short listing reads as a short
    /// Shows tab.
    /// </summary>
    [Fact]
    public async Task ShowsRowsWithNoSeriesNameAreCountedRatherThanJustDropped()
    {
        using var h = new RunHarness();
        h.ShowsCsv = RunLibrary.Shows(
            ("Season 1, Episode 1: Pilot", "The Office", "Comedy"),
            ("Season 1, Episode 2: Diversity Day", "", "Comedy"),
            ("Season 1, Episode 3: Health Care", "   ", "Comedy"));
        h.Options = h.Options with { PrintKeys = true, Token = "" };

        var result = await h.RunAsync();

        var printed = h.Out.ToString();
        Assert.Equal(0, result.ExitCode);
        // The good row is listed, so the count below is about the other two and not about a failed read.
        Assert.Contains(IdentityKey.ForSeriesName("The Office"), printed, StringComparison.Ordinal);
        Assert.Single(Lines(printed), l => l.StartsWith("series ", StringComparison.Ordinal));
        Assert.Contains("2 row(s) have no series name and were skipped", printed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AShowsTabWithNoBlankRowsSaysNothingAboutSkippedOnes()
    {
        using var h = Library();

        await h.RunAsync();

        Assert.Contains(IdentityKey.ForSeriesName("The Office"), h.Out.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("were skipped", h.Out.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TwoSeriesNamesThatFoldToOneKeyAreNamedTheSameWay()
    {
        using var h = new RunHarness();
        h.ShowsCsv = RunLibrary.Shows(
            ("Season 1, Episode 1: Pilot", "The Office", "Comedy"),
            ("Season 1, Episode 1: Pilot", "The Office!", "Comedy"));
        h.Options = h.Options with { PrintKeys = true, Token = "" };

        var result = await h.RunAsync();

        var printed = h.Out.ToString();
        var shared = IdentityKey.ForSeriesName("The Office");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(shared, IdentityKey.ForSeriesName("The Office!"));
        Assert.Contains($"{shared}  is carried by 2 series names:", printed, StringComparison.Ordinal);
        Assert.Contains("'The Office'", printed, StringComparison.Ordinal);
        Assert.Contains("'The Office!'", printed, StringComparison.Ordinal);
    }
}

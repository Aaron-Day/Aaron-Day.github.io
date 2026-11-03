namespace MovieLibrary.Core.Tests;

/// <summary>
/// The review queue, end to end, as the owner receives it. Three things composed into a hole the whole branch had to be
/// read to see: the queue was one flat list ordered films-before-series and capped at fifty; <c>meta.reviewQueue</c> is
/// published empty by the owner's decision, so the summary is the only channel; and the same truncated string went to
/// stdout, so the rest existed nowhere at all. On a first full run the film section alone is dozens of lines, which
/// means the series lines - one wrong series mis-attributes every owned episode row under it - would have printed
/// nowhere on the very run that creates them.
/// </summary>
/// <remarks>
/// These tests also pin the <b>wiring</b>, which <c>JobSummaryTests</c> cannot: the three groups handed to
/// <c>ReviewQueue</c> are all <c>IReadOnlyList&lt;string&gt;</c>, so exchanging two of them compiles and simply files
/// the lines under the wrong label. Here a real run's own lines are read back out of their sections by content.
/// </remarks>
public class EnrichRunReviewQueueTests
{
    /// <summary>
    /// One of each kind of line: a drifted film override (the owner's own correction, which therefore matches nothing),
    /// the unmatched film it was meant to fix, and a series TMDB answers under a different name.
    /// </summary>
    private static RunHarness OneOfEach()
    {
        var h = new RunHarness();
        h.MoviesCsv = RunLibrary.Movies(("Heat", "2:51:00", "Michael Mann", "Crime"));   // the sheet says 171 minutes
        h.OverridesCsv = RunLibrary.Overrides(("Heat", "2:50:00", "949"));               // the override still says 170
        h.ShowsCsv = RunLibrary.Shows(("Season 1, Episode 1: Pilot", "Alias", "Drama"));

        var tv = new FakeTv { AnyQuery = new FakeShow(9, "Aliens", [new FakeSeason(1, ["Pilot"])]) };
        h.Tmdb = req => req.RequestUri!.AbsolutePath == "/3/search/movie"
            ? TestPipeline.Json(TestPipeline.EmptyResults)
            : tv.Respond(req);
        return h;
    }

    /// <summary>The bullet lines under one labelled section of the "Needs review" list, from either emitted form.</summary>
    private static string[] Section(string text, string label)
    {
        var lines = text.ReplaceLineEndings().Split(Environment.NewLine);
        var start = Array.FindIndex(lines, l => l.StartsWith($"**{label} (", StringComparison.Ordinal));
        Assert.True(start >= 0, $"there is no '{label}' section in the review queue");

        return [.. lines.Skip(start + 1)
            .TakeWhile(l => !l.StartsWith("**", StringComparison.Ordinal) && !l.StartsWith("##", StringComparison.Ordinal))
            .Where(l => l.StartsWith("- ", StringComparison.Ordinal))
            .Select(l => l[2..])];
    }

    [Fact]
    public async Task EachKindOfLineIsFiledUnderItsOwnLabel()
    {
        using var h = OneOfEach();

        var result = await h.RunAsync();

        Assert.Equal(0, result.ExitCode);
        var note = Assert.Single(Section(h.Summary, "Overrides"));
        Assert.StartsWith("Overrides row 2", note);
        Assert.Contains("matches no film on the Movies tab", note);

        var film = Assert.Single(Section(h.Summary, "Films"));
        Assert.StartsWith("Heat (2:51:00, Michael Mann)", film);

        var series = Assert.Single(Section(h.Summary, "Series"));
        Assert.StartsWith("Alias →", series);
        Assert.Contains("Aliens", series);          // TMDB answered under another name, which is the whole point
    }

    [Fact]
    public async Task TheSectionsAreInTheOwnersOrder_HisOwnFailedCorrectionsFirst()
    {
        using var h = OneOfEach();

        await h.RunAsync();

        var overrides = h.Summary.IndexOf("**Overrides (1)**", StringComparison.Ordinal);
        var films = h.Summary.IndexOf("**Films (1)**", StringComparison.Ordinal);
        var series = h.Summary.IndexOf("**Series (1)**", StringComparison.Ordinal);

        Assert.True(overrides >= 0 && films > overrides && series > films,
            "overrides, then films, then series: a failing correction is a mistake to fix, not a doubtful match");
        Assert.Contains("### Needs review (3)", h.Summary);
    }

    /// <summary>
    /// The job log is the only complete copy of the queue there is, so the run must print it there - all of it, not
    /// the page's capped version. The cap itself is <c>JobSummaryTests</c>' business; this is about which form reaches
    /// which destination on a real run.
    /// </summary>
    [Fact]
    public async Task TheWholeQueueGoesToStdoutWhereTheActionsLogKeepsIt()
    {
        using var h = OneOfEach();

        await h.RunAsync();

        var log = h.Out.ToString();
        Assert.Equal(Section(h.Summary, "Overrides"), Section(log, "Overrides"));
        Assert.Equal(Section(h.Summary, "Films"), Section(log, "Films"));
        Assert.Equal(Section(h.Summary, "Series"), Section(log, "Series"));
        Assert.DoesNotContain("...and", log);

        // The log is the complete copy, so it carries neither the cap nor the page's pointer to itself. This is also
        // what fails if the page's text is ever handed to the console leg again.
        Assert.DoesNotContain("at most", log);
        Assert.Contains("at most", h.Summary);
    }

    [Fact]
    public async Task TheQueueIsStillNotPublishedInMetaJson()
    {
        using var h = OneOfEach();

        await h.RunAsync();

        // meta.json is served publicly and the queue names the owner's own problem rows. The owner chose this.
        Assert.Empty(h.PublishedReviewQueue());
    }

    [Fact]
    public async Task AHealthyRunHasNoReviewQueueAtAll()
    {
        using var h = new RunHarness { MoviesCsv = RunLibrary.Movies(RunLibrary.Fargo) };
        h.Tmdb = RunLibrary.FargoOnly();

        await h.RunAsync();

        Assert.DoesNotContain("Needs review", h.Summary);
        Assert.DoesNotContain("Needs review", h.Out.ToString());
    }
}

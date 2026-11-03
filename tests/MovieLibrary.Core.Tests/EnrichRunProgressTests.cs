using MovieLibrary.Enrich.Reporting;
using MovieLibrary.Enrich.Run;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// A seeding run is roughly 9,500 requests over 40 to 70 minutes, and it used to print nothing from the sheet counts
/// until the catalogue was written. A healthy run and one stalled in a 60-second <c>Retry-After</c> loop looked
/// identical from outside, and cancelling mid-enrich is precisely how a spent budget is wasted. Progress goes to stdout
/// only: the job summary is a page about the result, and these lines are about the wait.
/// </summary>
public class EnrichRunProgressTests
{
    private static (List<string> Lines, PassReporter Reporter) Reporter(string noun, int every, Func<int> requests)
    {
        var lines = new List<string>();
        return (lines, new PassReporter(noun, every, requests, lines.Add));
    }

    /// <summary>
    /// Every <c>every</c> titles, and once at the end of the pass whatever the total divides into. Without that last
    /// line a 1,457-film pass would go quiet again at 1,400 for its final stretch.
    /// </summary>
    [Fact]
    public void ALineIsPrintedEveryNTitlesAndOnceMoreWhenThePassEnds()
    {
        var requests = 0;
        var (lines, reporter) = Reporter("films", 100, () => requests);

        for (var done = 1; done <= 250; done++)
        {
            requests = done * 5;      // read when the line is printed, not when the reporter was built
            reporter.Report(new PassProgress { Done = done, Total = 250 });
        }

        Assert.Equal(
        [
            "  100 of 250 films, 500 TMDB requests so far",
            "  200 of 250 films, 1,000 TMDB requests so far",
            "  250 of 250 films, 1,250 TMDB requests so far",
        ], lines);
    }

    [Fact]
    public void APassShorterThanOneIntervalStillSaysItFinished()
    {
        var (lines, reporter) = Reporter("series", 20, () => 7);

        reporter.Report(new PassProgress { Done = 1, Total = 1 });

        Assert.Equal(["  1 of 1 series, 7 TMDB requests so far"], lines);
    }

    [Fact]
    public void NothingIsPrintedBeforeTheFirstTitleIsDone()
    {
        var (lines, reporter) = Reporter("films", 100, () => 0);

        reporter.Report(new PassProgress { Done = 0, Total = 1457 });

        Assert.Empty(lines);
    }

    /// <summary>
    /// The cadence the run actually uses, against the real library: a readable trickle, not a log nobody can read and
    /// not a single line at the end. Fifteen lines for 1,457 films and seven for 122 series.
    /// </summary>
    [Fact]
    public void TheRunsOwnCadenceProducesATrickleOverTheRealLibrary()
    {
        Assert.Equal(15, Count(1457, EnrichRun.FilmsPerProgressLine, "films"));
        Assert.Equal(7, Count(122, EnrichRun.SeriesPerProgressLine, "series"));

        static int Count(int total, int every, string noun)
        {
            var (lines, reporter) = Reporter(noun, every, () => 0);
            for (var done = 1; done <= total; done++) reporter.Report(new PassProgress { Done = done, Total = total });
            return lines.Count;
        }
    }

    // ---- wired into a real run ------------------------------------------------------------------------

    private static RunHarness FargoAndOneSeries()
    {
        var h = new RunHarness { MoviesCsv = RunLibrary.Movies(RunLibrary.Fargo) };
        h.ShowsCsv = RunLibrary.Shows(("Season 1, Episode 1: Pilot", "The Office", "Comedy"));
        var films = RunLibrary.FargoOnly();
        var tv = new FakeTv { AnyQuery = new FakeShow(2316, "The Office", [new FakeSeason(1, ["Pilot"])]) };
        h.Tmdb = req => req.RequestUri!.AbsolutePath.StartsWith("/3/search/tv", StringComparison.Ordinal)
                        || req.RequestUri.AbsolutePath.StartsWith("/3/tv/", StringComparison.Ordinal)
            ? tv.Respond(req)
            : films(req);
        return h;
    }

    [Fact]
    public async Task BothPassesReportTheirProgressOnARealRun()
    {
        using var h = FargoAndOneSeries();

        var result = await h.RunAsync();

        Assert.Equal(0, result.ExitCode);
        var log = h.Out.ToString();
        Assert.Contains("1 of 1 films, ", log);
        Assert.Contains("1 of 1 series, ", log);
        Assert.Contains("TMDB requests so far", log);
    }

    [Fact]
    public async Task ProgressGoesToStdoutAndChangesNothingOnTheSummaryPage()
    {
        using var h = FargoAndOneSeries();

        await h.RunAsync();

        Assert.DoesNotContain("TMDB requests so far", h.Summary);
        Assert.Contains("TMDB requests: **", h.Summary);           // the summary's own count is untouched
        Assert.Equal(string.Empty, h.Err.ToString());              // and none of it is a warning
    }

    /// <summary>A fixture run never reaches the pipelines, so it has no pass to report and stays as quiet as it was.</summary>
    [Fact]
    public async Task AFixtureRunPrintsNoProgressBecauseItHasNoPassToWaitFor()
    {
        using var h = FargoAndOneSeries();
        h.Options = h.Options with { Fixtures = true, Token = "" };

        await h.RunAsync();

        Assert.DoesNotContain("TMDB requests so far", h.Out.ToString());
    }
}

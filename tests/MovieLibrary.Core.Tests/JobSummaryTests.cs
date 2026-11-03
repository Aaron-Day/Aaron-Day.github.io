using MovieLibrary.Enrich.Reporting;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// The job summary is the one page of this tool the owner reads, so a healthy run must be quiet and anything that went
/// wrong without stopping the run must be on it. Two counters exist because Task 14 deliberately let two things pass
/// silently (a TMDB shape change that empties every cast, and a cast member that cannot be read), and these lines are the
/// protection. The budget banner is what Task 18's deploy gate has to be able to see.
/// </summary>
/// <remarks>
/// The review queue is the part with a history. It was one flat list ordered films-before-series, capped at fifty, and
/// the same truncated string went to stdout - so on a first full run, where the film section alone is dozens of lines,
/// the series lines were printed nowhere at all: not on the page, not in the log, and not in <c>meta.json</c>, which
/// publishes an empty queue by the owner's decision. A wrong series mis-attributes every owned episode row under it.
/// The tests from <see cref="TheLogGetsEveryLineAndSaysNothingAboutTruncation"/> down are about that.
/// </remarks>
public class JobSummaryTests
{
    private static JobReport Healthy() => new(
        Films: 1457, Series: 122, TotalEpisodes: 13850, OwnedEpisodes: 9120,
        RequestsUsed: 38, MaxRequests: 40, StoppedOnBudget: false, Fixtures: false,
        Review: ReviewQueue.Empty, MatchedFilms: 1400, FilmsWithEmptyCast: 0, CastMembersSkipped: 0);

    /// <summary>The Actions step summary page: capped per section.</summary>
    private static string Page(JobReport r) => JobSummary.Build(r).StepSummary;

    /// <summary>What goes to stdout, and so into the job log: everything.</summary>
    private static string Log(JobReport r) => JobSummary.Build(r).Console;

    private static ReviewQueue Queue(
        IReadOnlyList<string>? overrides = null, IReadOnlyList<string>? films = null, IReadOnlyList<string>? series = null)
        => new() { OverrideNotes = overrides ?? [], Films = films ?? [], Series = series ?? [] };

    private static string[] Lines(int count, string stem)
        => [.. Enumerable.Range(1, count).Select(i => $"{stem} {i:000}")];

    [Fact]
    public void AHealthyRunSaysWhatWasBuiltAndNothingElse()
    {
        var text = Page(Healthy());

        Assert.Contains("## Enrichment run", text);
        Assert.Contains("Films: **1,457**", text);
        Assert.Contains("Series: **122** (13,850 episodes listed, 9,120 owned)", text);
        Assert.Contains("TMDB requests: **38** of 40", text);
        Assert.DoesNotContain("INCOMPLETE", text);
        Assert.DoesNotContain("Do not deploy", text);
        Assert.DoesNotContain("no cast", text);
        Assert.DoesNotContain("unreadable", text);
        Assert.DoesNotContain("Needs review", text);
        Assert.DoesNotContain("Fixture", text);
    }

    [Fact]
    public void ABudgetStoppedRunIsMarkedIncompleteAndNotForDeploying()
    {
        var text = Page(Healthy() with { StoppedOnBudget = true, RequestsUsed = 40 });

        Assert.Contains("INCOMPLETE", text);
        Assert.Contains("Do not deploy", text);
        Assert.Contains("max-requests", text);
        Assert.StartsWith("## Enrichment run", text);
        Assert.True(text.IndexOf("INCOMPLETE", StringComparison.Ordinal) < text.IndexOf("Films:", StringComparison.Ordinal),
            "the warning comes before the numbers, where it cannot be skimmed past");
    }

    [Fact]
    public void ARunWithNoBudgetCeilingSaysUnlimited()
    {
        var text = Page(Healthy() with { MaxRequests = 0, RequestsUsed = 5600 });

        Assert.Contains("TMDB requests: **5,600** (unlimited)", text);
    }

    // ---- the two silent-failure counters -------------------------------------------------------------

    [Fact]
    public void AnEmptyCastOnEveryMatchedFilmIsOneUnmissableLine()
    {
        var text = Page(Healthy() with { FilmsWithEmptyCast = 1400 });

        Assert.Contains("Films with no cast: **1,400** of 1,400 matched", text);
        Assert.DoesNotContain("unreadable", text);
    }

    [Fact]
    public void SkippedCastMembersAreCountedWhenThereAreAny()
    {
        var text = Page(Healthy() with { CastMembersSkipped = 3 });

        Assert.Contains("Cast members skipped as unreadable: **3**", text);
        Assert.DoesNotContain("no cast", text);
    }

    [Fact]
    public void BothCountersPrintTogetherUnderOneHeading()
    {
        var text = Page(Healthy() with { FilmsWithEmptyCast = 2, CastMembersSkipped = 5 });

        Assert.Contains("Films with no cast: **2** of 1,400 matched", text);
        Assert.Contains("Cast members skipped as unreadable: **5**", text);
        Assert.Equal(1, text.Split("### Check these").Length - 1);
    }

    [Fact]
    public void EverythingAboveTheReviewQueueIsTheSameOnThePageAndInTheLog()
    {
        var r = Healthy() with
        {
            StoppedOnBudget = true, FilmsWithEmptyCast = 2, CastMembersSkipped = 5,
            Review = Queue(films: Lines(400, "film")),     // long enough that the two forms differ below the heading
        };
        var built = JobSummary.Build(r);

        var page = built.StepSummary[..built.StepSummary.IndexOf("### Needs review", StringComparison.Ordinal)];
        var log = built.Console[..built.Console.IndexOf("### Needs review", StringComparison.Ordinal)];

        Assert.Equal(page, log);
    }

    // ---- the review queue ----------------------------------------------------------------------------

    [Fact]
    public void TheReviewQueueIsListedWithItsCountAndTheWayToFixOne()
    {
        var text = Page(Healthy() with
        {
            Review = Queue(overrides: ["Overrides row 2 matched nothing"], films: ["Heat (2:50:00) -> unmatched"])
        });

        Assert.Contains("### Needs review (2)", text);
        Assert.Contains("- Overrides row 2 matched nothing", text);
        Assert.Contains("- Heat (2:50:00) -> unmatched", text);
        Assert.Contains("`Overrides` tab", text);
    }

    [Fact]
    public void EachGroupIsPrintedUnderItsOwnLabelWithItsOwnCount()
    {
        var text = Page(Healthy() with
        {
            Review = Queue(overrides: ["an override note"], films: Lines(3, "film"), series: Lines(2, "series"))
        });

        Assert.Contains("### Needs review (6)", text);
        Assert.Contains("**Overrides (1)**", text);
        Assert.Contains("**Films (3)**", text);
        Assert.Contains("**Series (2)**", text);
        Assert.True(text.IndexOf("**Overrides (1)**", StringComparison.Ordinal) < text.IndexOf("**Films (3)**", StringComparison.Ordinal)
                    && text.IndexOf("**Films (3)**", StringComparison.Ordinal) < text.IndexOf("**Series (2)**", StringComparison.Ordinal),
            "the owner's failing corrections first, then films, then series");
    }

    [Fact]
    public void AGroupWithNothingInItIsNotPrintedAtAll()
    {
        var text = Page(Healthy() with { Review = Queue(series: ["a series line"]) });

        Assert.Contains("**Series (1)**", text);
        Assert.DoesNotContain("**Overrides", text);
        Assert.DoesNotContain("**Films", text);
    }

    [Fact]
    public void ThePageShowsTheFirstLinesOfASectionAndCountsTheRest()
    {
        var text = Page(Healthy() with { Review = Queue(films: Lines(400, "film")) });

        Assert.Contains("### Needs review (400)", text);
        Assert.Contains("**Films (400)**", text);
        Assert.Contains("- film 150", text);
        Assert.DoesNotContain("film 151", text);
        Assert.Contains("- ...and 250 more, in this job's log", text);
    }

    [Fact]
    public void ASectionExactlyAtTheCapHasNoAndMoreLine()
    {
        var text = Page(Healthy() with { Review = Queue(films: Lines(150, "film")) });

        Assert.Contains("- film 150", text);
        Assert.DoesNotContain("...and", text);
    }

    /// <summary>
    /// The defect this shape exists to prevent. The film section is listed first and on a first full run it is long;
    /// with one cap shared across the whole queue, the series section - where one wrong value mis-attributes every
    /// owned episode row under it - printed nothing at all, on the very run that creates it.
    /// </summary>
    [Fact]
    public void ATruncatedFilmSectionCannotHideTheSeriesSection()
    {
        var series = "Alias -> matched TMDB (id 9), which is not the same name";
        var text = Page(Healthy() with { Review = Queue(films: Lines(400, "film"), series: [series]) });

        Assert.Contains("- ...and 250 more, in this job's log", text);
        Assert.Contains("**Series (1)**", text);
        Assert.Contains($"- {series}", text);
    }

    [Fact]
    public void TheCapIsPerSectionSoTwoLongSectionsBothPrintTheirShare()
    {
        var text = Page(Healthy() with { Review = Queue(films: Lines(400, "film"), series: Lines(400, "series")) });

        Assert.Contains("- film 150", text);
        Assert.DoesNotContain("film 151", text);
        Assert.Contains("- series 150", text);
        Assert.DoesNotContain("series 151", text);
        Assert.Equal(2, text.Split("more, in this job").Length - 1);
    }

    /// <summary>
    /// The log is the only complete record there is: meta.reviewQueue is published empty on purpose, and nothing writes
    /// the queue to a file. The page used to go to stdout as well, so every line it dropped existed nowhere at all.
    /// </summary>
    [Fact]
    public void TheLogGetsEveryLineAndSaysNothingAboutTruncation()
    {
        var r = Healthy() with { Review = Queue(films: Lines(400, "film"), series: Lines(400, "series")) };

        var log = Log(r);

        Assert.Contains("- film 400", log);
        Assert.Contains("- series 400", log);
        Assert.Contains("**Films (400)**", log);
        Assert.Contains("**Series (400)**", log);
        Assert.DoesNotContain("...and", log);
        Assert.DoesNotContain("at most", log);
        Assert.DoesNotContain("in this job", log);
    }

    [Fact]
    public void ThePageSaysItIsCappedAndWhereTheWholeListIs()
    {
        var text = Page(Healthy() with { Review = Queue(films: ["one line"]) });

        Assert.Contains("at most 150 lines", text);
        Assert.Contains("in this job", text);
    }

    // ---- fixtures and failures -----------------------------------------------------------------------

    [Fact]
    public void AFixtureRunSaysItIsSyntheticAndMadeNoRequests()
    {
        var text = Page(Healthy() with { Fixtures = true, RequestsUsed = 0, MaxRequests = 40 });

        Assert.Contains("Fixture run", text);
        Assert.Contains("synthetic", text);
        Assert.Contains("TMDB requests: **0** (fixture run, none made)", text);
        Assert.DoesNotContain("INCOMPLETE", text);
    }

    [Fact]
    public void AFailureNamesWhatHappenedAndWhatToDo()
    {
        var text = JobSummary.BuildFailure("The Sheet is not shared.", "Share it again.");

        Assert.StartsWith("## Enrichment run FAILED", text.StepSummary);
        Assert.Contains("The Sheet is not shared.", text.StepSummary);
        Assert.Contains("Share it again.", text.StepSummary);
        Assert.Equal(text.StepSummary, text.Console);
    }

    // ---- redaction and emitting -----------------------------------------------------------------------

    /// <summary>
    /// Redaction has to reach both forms. The run applies it at the print site for everything it prints, and the log
    /// form is the one carrying every line of text that came from outside this tool.
    /// </summary>
    [Fact]
    public void RedactingCoversThePageAndTheLogAlike()
    {
        var built = JobSummary.Build(Healthy() with { Review = Queue(films: Lines(400, "film"), series: ["secret 999"]) });

        var clean = built.Redacted(t => t.Replace("secret", "[redacted]", StringComparison.Ordinal));

        Assert.DoesNotContain("secret", clean.Console);
        Assert.DoesNotContain("secret", clean.StepSummary);
        Assert.Contains("[redacted] 999", clean.Console);
        Assert.Contains("[redacted] 999", clean.StepSummary);
    }

    [Fact]
    public async Task EmitPrintsTheWholeReportAndAppendsTheCappedPageToTheStepSummary()
    {
        var path = Path.Combine(Path.GetTempPath(), "ml-summary-" + Guid.NewGuid() + ".md");
        try
        {
            var console = new StringWriter();
            var built = JobSummary.Build(Healthy() with { Review = Queue(films: Lines(400, "film")) });

            await JobSummary.EmitAsync(built, console, path, CancellationToken.None);

            Assert.Contains("- film 400", console.ToString());
            Assert.DoesNotContain("film 400", File.ReadAllText(path));
            Assert.Contains("...and 250 more", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task TwoEmissionsAppendToTheSameStepSummaryFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "ml-summary-" + Guid.NewGuid() + ".md");
        try
        {
            var console = new StringWriter();
            var first = new JobSummary.SummaryText { Console = "first-log\n", StepSummary = "first\n" };
            var second = new JobSummary.SummaryText { Console = "second-log\n", StepSummary = "second\n" };

            await JobSummary.EmitAsync(first, console, path, CancellationToken.None);
            await JobSummary.EmitAsync(second, console, path, CancellationToken.None);

            Assert.Contains("first-log", console.ToString());
            Assert.Equal("first\nsecond\n", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmitWithNoStepSummaryFileOnlyPrints(string? path)
    {
        var console = new StringWriter();

        await JobSummary.EmitAsync(
            new JobSummary.SummaryText { Console = "only console\n", StepSummary = "page only\n" }, console, path,
            CancellationToken.None);

        Assert.Contains("only console", console.ToString());
        Assert.DoesNotContain("page only", console.ToString());
    }
}

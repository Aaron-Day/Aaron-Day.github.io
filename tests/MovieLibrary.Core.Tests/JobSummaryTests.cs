using MovieLibrary.Enrich.Reporting;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// The job summary is the one page of this tool the owner reads, so a healthy run must be quiet and anything that went
/// wrong without stopping the run must be on it. Two counters exist because Task 14 deliberately let two things pass
/// silently (a TMDB shape change that empties every cast, and a cast member that cannot be read), and these lines are the
/// protection. The budget banner is what Task 18's deploy gate has to be able to see.
/// </summary>
public class JobSummaryTests
{
    private static JobReport Healthy() => new(
        Films: 1457, Series: 122, TotalEpisodes: 13850, OwnedEpisodes: 9120,
        RequestsUsed: 38, MaxRequests: 40, StoppedOnBudget: false, Fixtures: false,
        ReviewQueue: [], MatchedFilms: 1400, FilmsWithEmptyCast: 0, CastMembersSkipped: 0);

    [Fact]
    public void AHealthyRunSaysWhatWasBuiltAndNothingElse()
    {
        var text = JobSummary.Build(Healthy());

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
        var text = JobSummary.Build(Healthy() with { StoppedOnBudget = true, RequestsUsed = 40 });

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
        var text = JobSummary.Build(Healthy() with { MaxRequests = 0, RequestsUsed = 5600 });

        Assert.Contains("TMDB requests: **5,600** (unlimited)", text);
    }

    // ---- the two silent-failure counters -------------------------------------------------------------

    [Fact]
    public void AnEmptyCastOnEveryMatchedFilmIsOneUnmissableLine()
    {
        var text = JobSummary.Build(Healthy() with { FilmsWithEmptyCast = 1400 });

        Assert.Contains("Films with no cast: **1,400** of 1,400 matched", text);
        Assert.DoesNotContain("unreadable", text);
    }

    [Fact]
    public void SkippedCastMembersAreCountedWhenThereAreAny()
    {
        var text = JobSummary.Build(Healthy() with { CastMembersSkipped = 3 });

        Assert.Contains("Cast members skipped as unreadable: **3**", text);
        Assert.DoesNotContain("no cast", text);
    }

    [Fact]
    public void BothCountersPrintTogetherUnderOneHeading()
    {
        var text = JobSummary.Build(Healthy() with { FilmsWithEmptyCast = 2, CastMembersSkipped = 5 });

        Assert.Contains("Films with no cast: **2** of 1,400 matched", text);
        Assert.Contains("Cast members skipped as unreadable: **5**", text);
        Assert.Equal(1, text.Split("### Check these").Length - 1);
    }

    // ---- the review queue ----------------------------------------------------------------------------

    [Fact]
    public void TheReviewQueueIsListedWithItsCountAndTheWayToFixOne()
    {
        var text = JobSummary.Build(Healthy() with { ReviewQueue = ["Overrides row 2 matched nothing", "Heat (2:50:00) -> unmatched"] });

        Assert.Contains("### Needs review (2)", text);
        Assert.Contains("- Overrides row 2 matched nothing", text);
        Assert.Contains("- Heat (2:50:00) -> unmatched", text);
        Assert.Contains("`Overrides` tab", text);
    }

    [Fact]
    public void ALongReviewQueueShowsTheFirstFiftyAndCountsTheRest()
    {
        var queue = Enumerable.Range(1, 60).Select(i => $"line {i:00}").ToList();

        var text = JobSummary.Build(Healthy() with { ReviewQueue = queue });

        Assert.Contains("### Needs review (60)", text);
        Assert.Contains("- line 50", text);
        Assert.DoesNotContain("line 51", text);
        Assert.Contains("and 10 more", text);
    }

    [Fact]
    public void AnExactlyFiftyLongQueueHasNoAndMoreLine()
    {
        var text = JobSummary.Build(Healthy() with { ReviewQueue = [.. Enumerable.Range(1, 50).Select(i => $"line {i:00}")] });

        Assert.Contains("- line 50", text);
        Assert.DoesNotContain("more", text);
    }

    // ---- fixtures and failures -----------------------------------------------------------------------

    [Fact]
    public void AFixtureRunSaysItIsSyntheticAndMadeNoRequests()
    {
        var text = JobSummary.Build(Healthy() with { Fixtures = true, RequestsUsed = 0, MaxRequests = 40 });

        Assert.Contains("Fixture run", text);
        Assert.Contains("synthetic", text);
        Assert.Contains("TMDB requests: **0** (fixture run, none made)", text);
        Assert.DoesNotContain("INCOMPLETE", text);
    }

    [Fact]
    public void AFailureNamesWhatHappenedAndWhatToDo()
    {
        var text = JobSummary.BuildFailure("The Sheet is not shared.", "Share it again.");

        Assert.StartsWith("## Enrichment run FAILED", text);
        Assert.Contains("The Sheet is not shared.", text);
        Assert.Contains("Share it again.", text);
    }

    // ---- emitting -------------------------------------------------------------------------------------

    [Fact]
    public async Task EmitPrintsToTheConsoleAndAppendsToTheStepSummary()
    {
        var path = Path.Combine(Path.GetTempPath(), "ml-summary-" + Guid.NewGuid() + ".md");
        try
        {
            var console = new StringWriter();

            await JobSummary.EmitAsync("first\n", console, path, CancellationToken.None);
            await JobSummary.EmitAsync("second\n", console, path, CancellationToken.None);

            Assert.Contains("first", console.ToString());
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

        await JobSummary.EmitAsync("only console\n", console, path, CancellationToken.None);

        Assert.Contains("only console", console.ToString());
    }
}

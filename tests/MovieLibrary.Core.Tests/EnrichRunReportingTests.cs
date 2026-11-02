namespace MovieLibrary.Core.Tests;

/// <summary>
/// The job summary and the outputs file are advice to other tools and to the owner. A run that has already written a good
/// catalogue is not failed because one of them could not be written, and a run outside Actions has neither.
/// </summary>
public class EnrichRunReportingTests
{
    private static RunHarness Fargo()
    {
        var h = new RunHarness { MoviesCsv = RunLibrary.Movies(RunLibrary.Fargo) };
        h.Tmdb = RunLibrary.FargoOnly();
        return h;
    }

    [Fact]
    public async Task AnUnwritableSummaryFileIsAWarningAndTheCatalogueStillCounts()
    {
        using var h = Fargo();
        var services = h.Services() with { StepSummaryPath = Path.Combine(h.Root, "no-such-folder", "summary.md") };

        var result = await new MovieLibrary.Enrich.Run.EnrichRun(h.Options, services).RunAsync(CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Deployable);
        Assert.Contains("WARNING", h.Err.ToString());
        Assert.True(h.Wrote("meta.json"));
        Assert.Contains("deployable=true", h.GitHubOutput);      // the other file is still written
    }

    [Fact]
    public async Task OutsideActionsThereAreNoFilesToWriteAndTheSummaryGoesToTheConsole()
    {
        using var h = Fargo();
        var services = h.Services() with { StepSummaryPath = null, OutputFilePath = null };

        var result = await new MovieLibrary.Enrich.Run.EnrichRun(h.Options, services).RunAsync(CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Enrichment run", h.Out.ToString());
        Assert.False(File.Exists(h.SummaryPath));
        Assert.False(File.Exists(h.GitHubOutputPath));
        Assert.Equal(string.Empty, h.Err.ToString());
    }

    [Fact]
    public async Task TwoRunsInOneJobAppendToTheSameSummary()
    {
        using var h = Fargo();

        await h.RunAsync();
        await h.RunAsync();

        Assert.Equal(2, h.Summary.Split("## Enrichment run").Length - 1);
        Assert.Equal(2, h.GitHubOutput.Split("deployable=true").Length - 1);
    }

    [Fact]
    public async Task ASuccessfulRunWritesNothingToTheErrorStream()
    {
        using var h = Fargo();

        await h.RunAsync();

        Assert.Equal(string.Empty, h.Err.ToString());
    }
}

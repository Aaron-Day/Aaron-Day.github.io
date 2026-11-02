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

    /// <summary>A console that fails the moment the summary is printed. By then the catalogue is already on disk.</summary>
    private sealed class FailsOnTheSummary : StringWriter
    {
        public override void WriteLine(string? value)
        {
            if (value is not null && value.StartsWith("## Enrichment run", StringComparison.Ordinal))
                throw new InvalidOperationException("the console went away");
            base.WriteLine(value);
        }
    }

    /// <summary>
    /// Writing the summary is caught whatever it throws, not only for the two file exceptions. Anything that escapes
    /// reaches the catch-all, which exits 1 saying "Nothing was written." - after a catalogue that was published in
    /// full, and with deployable=false already on the outputs file.
    /// </summary>
    [Fact]
    public async Task AnUnexpectedFailureWritingTheSummaryDoesNotChangeTheVerdictOnAPublishedCatalogue()
    {
        using var h = Fargo();
        var services = h.Services() with { Out = new FailsOnTheSummary() };

        var result = await new MovieLibrary.Enrich.Run.EnrichRun(h.Options, services).RunAsync(CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Deployable);
        Assert.True(h.Wrote("meta.json"));
        Assert.Contains("deployable=true", h.GitHubOutput);
        Assert.Contains("WARNING: could not write the job summary", h.Err.ToString());
        Assert.DoesNotContain("Nothing was written", h.Err.ToString());
    }
}

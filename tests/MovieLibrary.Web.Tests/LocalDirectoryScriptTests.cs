using System.Diagnostics;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// Runs the real <c>localDirectory.js</c> under node against a fake directory handle. The source-level tests in
/// LocalDirectoryTests catch a deletion; this is the one that catches a mutation, because it executes the guard
/// and the backup rather than looking for their text.
/// </summary>
public sealed class LocalDirectoryScriptTests
{
    [NodeFact]
    public async Task TheWriteLogicHoldsWhenTheRealScriptIsRun()
    {
        var spec = WebRoot.RepoPath("tests", "MovieLibrary.Web.Tests", "Scripts", "localDirectory.spec.mjs");
        var script = WebRoot.SiblingPath("MovieLibrary.Web.Editor", "wwwroot", "localDirectory.js");

        Assert.True(File.Exists(spec), $"the spec is missing: {spec}");
        Assert.True(File.Exists(script), $"the script is missing: {script}");

        var info = new ProcessStartInfo(Node.Path!)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        info.ArgumentList.Add(spec);
        info.ArgumentList.Add(script);

        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("the node spec did not finish within 60 seconds");
        }

        Assert.True(process.ExitCode == 0,
            $"node exited {process.ExitCode}.{Environment.NewLine}stdout:{Environment.NewLine}{await output}"
            + $"{Environment.NewLine}stderr:{Environment.NewLine}{await errors}");
    }
}

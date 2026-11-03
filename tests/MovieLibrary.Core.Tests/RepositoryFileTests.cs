namespace MovieLibrary.Core.Tests;

/// <summary>
/// Two hand-maintained files that nothing else in the build reads: the Actions workflow, which only GitHub ever
/// executes, and <c>.env.example</c>, which only a person ever reads. Both describe how this tool is run, and both can
/// go stale without a single test noticing - which is how the workflow came to float on mutable tags and the example
/// file came to offer a setting that now exits 1.
/// </summary>
public class RepositoryFileTests
{
    /// <summary>The repository root, found by walking up to the solution file rather than guessing a depth.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MovieLibrary.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);   // the test is worthless if it silently cannot find the repo
        return dir.FullName;
    }

    private static string[] UsesLines()
    {
        var yaml = File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "main.yml"));
        var lines = yaml.ReplaceLineEndings().Split(Environment.NewLine)
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("- uses:", StringComparison.Ordinal) || l.StartsWith("uses:", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(lines);   // read from the file, so a renamed workflow fails here rather than passing vacuously
        return lines;
    }

    /// <summary>
    /// Every third-party action is pinned to a full commit SHA. This repository is public and the deploy job holds
    /// <c>contents: write</c> with the TMDB secret in scope, so a <c>vN</c> tag - which the action's owner, or anyone
    /// who takes over the account, can repoint at any moment - is unreviewed code with write access to master and to
    /// gh-pages on the next push. A SHA cannot be repointed.
    /// </summary>
    [Fact]
    public void EveryActionTheWorkflowUsesIsPinnedToACommitSha()
    {
        Assert.All(UsesLines(), line =>
        {
            var reference = line.Split("uses:", 2)[1].Split('#')[0].Trim();
            var at = reference.Split('@');

            Assert.Equal(2, at.Length);
            Assert.Matches("^[0-9a-f]{40}$", at[1]);
        });
    }

    /// <summary>
    /// A bare SHA says nothing about what it is, so nobody can tell whether it is a year out of date. The trailing
    /// comment carries the version the SHA was the tip of, and the two are bumped together.
    /// </summary>
    [Fact]
    public void EveryPinSaysWhichVersionTheShaWas()
        => Assert.All(UsesLines(), line => Assert.Matches(@"uses: \S+@[0-9a-f]{40} # v\d+\.\d+\.\d+$", line));

    /// <summary>
    /// <c>.env.example</c> is where a local run learns about the request budget, and the only place that explains it.
    /// It offered <c>FORCE_REFRESH</c> with no mention that the tool now refuses it unless the budget is unlimited, so
    /// copying the file and uncommenting that one line exits 1 before anything is fetched; and it never named
    /// <c>MAX_REQUESTS_PER_RUN</c> at all, although every local run silently defaults to 40 of 9,500 requests.
    /// </summary>
    [Fact]
    public void TheExampleEnvFileNamesTheBudgetAndWhatForceRefreshNeedsWithIt()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), ".env.example"));

        Assert.Contains("MAX_REQUESTS_PER_RUN", text);
        Assert.Contains("FORCE_REFRESH=false", text);

        // The refusal is about the PAIR, so the force-refresh advice has to carry the budget with it.
        Assert.Contains("REQUIRES MAX_REQUESTS_PER_RUN=0", text);
    }
}

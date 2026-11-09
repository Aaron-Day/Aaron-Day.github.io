namespace MovieLibrary.Core.Tests;

/// <summary>
/// Hand-maintained files that nothing else in the build reads: the Actions workflows, which only GitHub ever
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

    /// <summary>
    /// Every workflow in <c>.github/workflows</c>, found by glob and not by name.
    /// </summary>
    /// <remarks>
    /// This used to read <c>main.yml</c> alone, and it was the only workflow there was. Task 4b added
    /// <c>a11y.yml</c>, which would have been pinned by nothing: the rule would have read as covering the
    /// repository's workflows while covering one of them - the same shape of defect as a contract whose subject
    /// set has quietly emptied, reached by the other direction. A glob plus the floor below means a new
    /// workflow is covered the moment it lands.
    /// </remarks>
    private static string[] WorkflowFiles()
    {
        var folder = Path.Combine(RepoRoot(), ".github", "workflows");
        var files = Directory.GetFiles(folder, "*.yml").Concat(Directory.GetFiles(folder, "*.yaml"))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();

        Assert.True(files.Length >= 2,
            $"Found {files.Length} workflow file(s) in {folder}. This repository has at least two - the deploy "
            + "in main.yml and the accessibility gate in a11y.yml - so fewer means the glob stopped finding "
            + "them and every assertion below would pass over nothing.");

        return files;
    }

    private static string[] UsesLines()
    {
        var lines = WorkflowFiles()
            .SelectMany(file => File.ReadAllText(file).ReplaceLineEndings().Split(Environment.NewLine)
                .Select(l => l.Trim())
                .Where(l => l.StartsWith("- uses:", StringComparison.Ordinal)
                            || l.StartsWith("uses:", StringComparison.Ordinal)))
            .ToArray();

        Assert.NotEmpty(lines);   // read from the files, so a renamed workflow fails here rather than passing vacuously
        return lines;
    }

    /// <summary>
    /// The accessibility gate names no secret. It is a browser gate on a public repository, and the whole
    /// reason it is a separate workflow is that the deploy job holds the TMDB secret and this one must not.
    /// A secret reference appearing here would undo the separation silently.
    /// </summary>
    /// <remarks>
    /// <b>Full-line comments are stripped first, and only full-line ones.</b> The gate's own comments explain
    /// at length which secret it must not hold, so a plain substring scan over the whole file fails on the
    /// documentation rather than on a reference - the same trap SourceContractTests works around by not
    /// spelling the name it scans for. A line whose first non-blank character is <c>#</c> cannot carry a
    /// usable secret reference, so dropping those loses nothing; an INLINE comment is deliberately not
    /// stripped, because in <c>run: echo "${{ secrets.X }}" # note</c> the reference sits before the
    /// <c>#</c> and truncating there would hide it. The floor below is what proves the strip left a file.
    /// </remarks>
    [Fact]
    public void TheAccessibilityGateReferencesNoSecret()
    {
        var path = Path.Combine(RepoRoot(), ".github", "workflows", "a11y.yml");
        var yaml = string.Join(Environment.NewLine,
            File.ReadAllText(path).ReplaceLineEndings().Split(Environment.NewLine)
                .Where(l => !l.TrimStart().StartsWith('#')));

        Assert.Contains("dotnet test tests/MovieLibrary.Web.A11y", yaml, StringComparison.Ordinal);

        Assert.DoesNotContain("TMDB_TOKEN", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("secrets.", yaml, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every third-party action in every workflow is pinned to a full commit SHA. This repository is public and the deploy job holds
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

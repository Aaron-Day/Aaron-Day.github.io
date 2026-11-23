using System.Globalization;
using MovieLibrary.Core.Store;
using MovieLibrary.Enrich.Config;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// Hand-maintained files that nothing else in the build reads: the Actions workflows, which only GitHub ever
/// executes, <c>.env.example</c>, which only a person ever reads, and <c>record-store.example.json</c>, which only
/// a person ever edits. All of them describe how this tool is run, and all of them can
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

    /// <summary>
    /// <c>record-store.example.json</c> is where <see cref="RecordStoreReader"/>'s parse failure sends a
    /// hand-editor, and the only written-down copy of the store's format that is actually in the repository.
    /// </summary>
    /// <remarks>
    /// <b>Why a tracked example and not the spec.</b> The message used to name
    /// <c>docs/superpowers/specs/2026-11-16-record-store-design.md</c>, and <c>docs/</c> is gitignored
    /// (<c>.gitignore:465</c>), so on any clone it cited a document that is not there - worse than citing
    /// nothing. This file is committed, which is also what makes this test meaningful on a clone: the earlier
    /// test that read the spec had to be dropped because it would pass here and fail everywhere else.
    /// <para>
    /// <b>Parsed through the real reader, not eyeballed.</b> The example is hand-authored and nothing in the
    /// build loads it, so nothing else can notice it going stale into a shape the reader refuses - which is
    /// exactly what happened to the spec's own section 5.2 example, which showed <c>tmdbId</c> and
    /// <c>candidates</c> in one record. Anybody copying it got a rejection.
    /// </para>
    /// <para>
    /// The exact counts below are the non-vacuity guard this project's <c>Assert.NotEmpty</c>-before-
    /// <c>Assert.All</c> rule asks for, and they are stronger than it: a file that parsed into an empty store
    /// fails on the count rather than passing over nothing.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheExampleRecordStoreIsInTheRepositoryAndTheReaderAcceptsIt()
    {
        var path = Path.Combine(RepoRoot(), RecordStoreReader.ExampleFileName);

        Assert.True(File.Exists(path),
            path + " is missing. RecordStoreReader's parse failure names it as where the format is written "
            + "down, so a clone without it sends its reader to a file that is not there - which is the exact "
            + "defect this file was added to fix.");

        var text = File.ReadAllText(path);

        // Throws RecordStoreException on anything the reader would refuse, which is the whole point.
        var store = RecordStoreReader.Read(text, RecordStoreReader.ExampleFileName);

        // ASSERTED AGAINST THE FILE'S OWN TEXT, and not against store.Version. The reader UPGRADES an older
        // version in place, so the parsed value is CurrentVersion whatever the file says -- an assertion on it
        // is one no edit to this file can fail, and this example's job is to show the CURRENT shape to someone
        // writing an entry by hand. It said "version": 1 for the whole of version 2's existence until this
        // assertion was written.
        Assert.Contains(
            "\"version\": " + RecordStore.CurrentVersion.ToString(CultureInfo.InvariantCulture), text,
            StringComparison.Ordinal);
        Assert.Equal(RecordStore.CurrentVersion, store.Version);
        Assert.Equal(3, store.Films.Count);

        // Named rather than merely counted, because a count can be met by three copies of one shape and the
        // example exists to show the shapes. One entry asserting an id with its titles and extras; one
        // offering candidates and no id; one expanding a single sheet row into three films.
        var asserted = Assert.Single(store.Films[0].Records);
        Assert.Equal(9000001, asserted.TmdbId);
        Assert.Equal("The Phantom Orchard", asserted.Titles!.Display);
        Assert.Equal("Phantom Orchard, The", asserted.Titles!.Sort);
        Assert.Equal("2019-03-14", Assert.Contains("Purchase Date", asserted.Extra!));

        // The example documents publishedKey on the ordinary case and on the bundle, which is where it matters
        // most. Named here so that dropping it from the file fails rather than passing quietly.
        Assert.Equal("9000001-112", asserted.PublishedKey);

        var ambiguous = Assert.Single(store.Films[1].Records);
        Assert.Null(ambiguous.TmdbId);
        Assert.Equal([9000002, 9000003], ambiguous.Candidates!);

        var expanded = store.Films[2].Records;
        Assert.Equal(3, expanded.Count);
        Assert.All(expanded, r => Assert.NotNull(r.Runtime));
        Assert.Equal(3, expanded.Select(r => (r.TmdbId, r.Runtime)).Distinct().Count());
        Assert.Equal("9000004-95", expanded[0].PublishedKey);

        var series = Assert.Single(store.Series).Record;
        Assert.Equal(9000007, series.TmdbId);
        Assert.Equal("Lanterns of Brackwater (2019)", series.Titles!.Display);

        // The sheet echo is the one block that makes the file readable by a human, so an example without it
        // would be missing the part that is not about the reader at all.
        Assert.All(store.Films, f => Assert.False(string.IsNullOrWhiteSpace(f.Sheet.Title)));
    }

    /// <summary>
    /// Every argument the workflows hand the enrich tool is one the tool still recognises. An unrecognised
    /// argument used to be ignored and is now fatal, which closed a way to spend the TMDB budget by typo and
    /// opened the one failure mode that costs more than that bug: a deploy that exits on an argument it used
    /// to tolerate. Nothing in the build reads these files, so nothing else can catch it.
    /// </summary>
    /// <remarks>
    /// <b>Only what follows a bare <c>--</c> reaches the tool.</b>
    /// <c>dotnet run --project X -c Release</c> passes no arguments at all, because <c>-c</c> is a
    /// <c>dotnet run</c> option. The floor is therefore on the INVOCATIONS and not on the arguments: today
    /// every invocation passes none, and "none" is exactly what the deploy needs
    /// <c>EnrichOptions.Load</c> to go on accepting. A run line that stopped being found fails here rather
    /// than passing over an empty set.
    /// </remarks>
    [Fact]
    public void EveryArgumentTheWorkflowsHandTheEnrichToolIsOneItStillRecognises()
    {
        var invocations = WorkflowFiles()
            .SelectMany(file => File.ReadAllText(file).ReplaceLineEndings().Split(Environment.NewLine))
            .Select(line => line.Trim())
            .Where(line => !line.StartsWith('#')
                           && line.Contains("dotnet run", StringComparison.Ordinal)
                           && line.Contains("src/MovieLibrary.Enrich", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(invocations);

        Assert.All(invocations, line =>
        {
            var halves = line.Split(" -- ", 2);
            string[] args = halves.Length == 2 ? halves[1].Split(' ', StringSplitOptions.RemoveEmptyEntries) : [];

            // Throws ArgumentException on an argument Load does not recognise, which is the whole point.
            Assert.Equal(args.Contains("--fixtures"),
                         EnrichOptions.Load(args, new Dictionary<string, string>()).Fixtures);
        });
    }
}

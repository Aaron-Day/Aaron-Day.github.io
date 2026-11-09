using System.Text;
using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace MovieLibrary.Web.A11y.Harness;

/// <summary>
/// One axe run over one DOM state, and the rules about what its four result arrays mean here.
/// </summary>
internal sealed class AxeScan
{
    /// <summary>
    /// The tags the run is restricted to. Without RunOnly, axe runs its DEFAULTS, which is neither this list
    /// nor anything a reader could predict from a brief that talks about tags.
    /// <para>
    /// wcag22a carries no axe rule today. It costs nothing and picks up the ones that follow: WCAG 2.2
    /// introduced two Level A criteria. Every aaa tag is left out, which is correct for a Level AA target -
    /// including, specifically, the one that would otherwise assert 44x44 as though a criterion required it.
    /// </para>
    /// </summary>
    private static readonly string[] Tags =
        ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22a", "wcag22aa", "best-practice"];

    /// <summary>
    /// The WCAG tags above, as the partition test. A result carrying any of these is asserted.
    /// </summary>
    private static readonly HashSet<string> WcagTags =
        new(Tags.Where(t => t.StartsWith("wcag", StringComparison.Ordinal)), StringComparer.Ordinal);

    /// <summary>
    /// The best-practice rules this project asserts as hard failures, promoted BY HOUSE RULE and not by any
    /// success criterion.
    /// </summary>
    /// <remarks>
    /// Running all of best-practice as print-only would silently demote every rule here, and each of them
    /// describes something the shell is deliberately built around: one main landmark, uniquely labelled
    /// landmarks, every piece of content inside one, exactly one h1 in document order, no heading level
    /// skipped, a working skip link, no positive tabindex, no empty heading. Two of them - the single h1 and
    /// the absence of a positive tabindex - are already HARD failures in bUnit, in
    /// MovieLibrary.Web.Tests.MarkupContractTests, so demoting them here would have left the two halves of
    /// this project's accessibility gate disagreeing about the same rules.
    /// <para>
    /// The rest of best-practice stays print-only. It is advice, it changes between axe releases, and a gate
    /// that fails on advice gets switched off.
    /// </para>
    /// </remarks>
    private static readonly HashSet<string> PromotedBestPractice = new(StringComparer.Ordinal)
    {
        "region",
        "landmark-one-main",
        "landmark-unique",
        "page-has-heading-one",
        "heading-order",
        "skip-link",
        "tabindex",
        "empty-heading",
    };

    /// <summary>
    /// The three rules whose "I could not decide" is indistinguishable from a failure on this app, so an
    /// incomplete from any of them fails the gate.
    /// </summary>
    /// <remarks>
    /// axe returns FOUR arrays and violations alone reads as a clean pass even where axe is saying it could
    /// not determine the answer. On a dark-themed app that is not hypothetical: color-contrast and
    /// link-in-text-block both go incomplete whenever the background behind the text cannot be resolved -
    /// a gradient, an image, a translucent layer - and target-size goes incomplete whenever an overlap
    /// cannot be decided. Each of those is exactly the case this app has to get right, so an undecided
    /// result is not a pass.
    /// </remarks>
    private static readonly HashSet<string> IncompleteIsAFailure = new(StringComparer.Ordinal)
    {
        "color-contrast",
        "link-in-text-block",
        "target-size",
    };

    private readonly string state;
    private readonly AxeResult result;
    private readonly ITestOutputHelper output;

    private AxeScan(string state, AxeResult result, ITestOutputHelper output)
    {
        this.state = state;
        this.result = result;
        this.output = output;
    }

    /// <summary>Runs axe over the page as it stands and reports everything it found.</summary>
    /// <param name="state">What state the page is in, named so a failure says which one.</param>
    public static async Task<AxeScan> OfAsync(IPage page, string state, ITestOutputHelper output)
    {
        var options = new AxeRunOptions
        {
            RunOnly = new RunOnlyOptions { Type = "tag", Values = [.. Tags] },
        };

        var scan = new AxeScan(state, await page.RunAxe(options), output);
        scan.Report();
        return scan;
    }

    /// <summary>
    /// The whole assertion: nothing violated in the asserted rule set, and nothing undecided among the three
    /// rules whose "do not know" cannot be told from a failure here.
    /// </summary>
    /// <remarks>
    /// There is deliberately NO baseline file. Two page types of near-static markup belong at zero, and a
    /// baseline committed before any finding exists is a place to park real defects where CI stops mentioning
    /// them. A violation that genuinely cannot be fixed is a decision for the owner, not for this file.
    /// </remarks>
    public void MustBeClean()
    {
        ProvedItRan();

        var asserted = result.Violations.Where(IsAsserted).ToList();
        var undecided = Undecided();

        if (asserted.Count == 0 && undecided.Count == 0) return;

        var message = new StringBuilder();
        message.Append("axe found ").Append(asserted.Count).Append(" violation(s) and ")
               .Append(undecided.Count).Append(" undecided result(s) that this project treats as failures, in ")
               .Append("the ").Append(state).Append(" state.").AppendLine().AppendLine();

        foreach (var item in asserted) Describe(message, "VIOLATION", item);
        foreach (var item in undecided) Describe(message, "INCOMPLETE", item);

        if (undecided.Count > 0)
            message.AppendLine(
                "An INCOMPLETE above is axe saying it could not decide. color-contrast, link-in-text-block "
                + "and target-size are the three rules whose undecided result is indistinguishable from a "
                + "failure on this app, so they are failures here. Fix the thing axe could not resolve - "
                + "usually a background it cannot compute - rather than removing the rule from the list.");

        Assert.Fail(message.ToString());
    }

    /// <summary>
    /// The three rules whose undecided result is a failure here, as the result arrays reported them.
    /// </summary>
    private List<AxeResultItem> Undecided() =>
        result.Incomplete.Where(i => IncompleteIsAFailure.Contains(i.Id)).ToList();

    /// <summary>
    /// The undecided half of the assertion on its own, for the exemption path, which has already dealt with
    /// the violations. An exemption may never reach into this: see IncompleteIsAFailure for why these three
    /// cannot be read as passes.
    /// </summary>
    private void MustBeDecided()
    {
        var undecided = Undecided();
        if (undecided.Count == 0) return;

        var message = new StringBuilder();
        message.Append("axe could not decide ").Append(undecided.Count)
               .Append(" result(s) in the ").Append(state)
               .AppendLine(" state that this project treats as failures.").AppendLine();

        foreach (var item in undecided) Describe(message, "INCOMPLETE", item);

        Assert.Fail(message.ToString());
    }

    /// <summary>
    /// The same assertion, less a named set of rule ids that this state is not the app and so is not held to.
    /// </summary>
    /// <param name="why">Why the exemption is sound. Printed, and repeated in the failure message.</param>
    /// <param name="ruleIds">
    /// The exempt rules. Every one of them MUST still fire, or this fails: the exemption is self-expiring, so
    /// the day the markup changes the test says to delete it rather than quietly covering less than it reads
    /// as covering. That is rule T2 in the shape a whole-page scan can honour.
    /// </param>
    /// <remarks>
    /// This is NOT a baseline mechanism and must not be used as one. There is no file, nothing accumulates,
    /// the exemption is per state, it names the rules in the test that uses it, and it fails when it stops
    /// being needed. It exists for one case: a document that never became an application cannot be held to
    /// best-practice advice about an application's document structure. A WCAG-tagged rule may never be passed
    /// in here, and is refused below.
    /// </remarks>
    public void MustBeCleanExcept(string why, params string[] ruleIds)
    {
        ProvedItRan();

        var exempt = new HashSet<string>(ruleIds, StringComparer.Ordinal);
        output.WriteLine($"exempt in the {state} state: {string.Join(", ", ruleIds)} - {why}");

        var wcagExempt = result.Violations
            .Concat(result.Passes).Concat(result.Incomplete).Concat(result.Inapplicable)
            .Where(i => exempt.Contains(i.Id) && i.Tags.Any(WcagTags.Contains))
            .Select(i => i.Id)
            .Distinct()
            .ToList();

        Assert.True(wcagExempt.Count == 0,
            "A rule carrying a WCAG tag cannot be exempted from a scan: "
            + string.Join(", ", wcagExempt)
            + ". Exemptions here are only for best-practice advice that does not apply to a document which "
            + "never became an application.");

        var unexpected = result.Violations.Where(IsAsserted).Where(i => !exempt.Contains(i.Id)).ToList();
        var stale = exempt.Where(id => result.Violations.All(v => v.Id != id)).OrderBy(id => id, StringComparer.Ordinal).ToList();

        if (unexpected.Count == 0 && stale.Count == 0)
        {
            MustBeDecided();
            return;
        }

        var message = new StringBuilder();

        if (unexpected.Count > 0)
        {
            message.Append("axe found ").Append(unexpected.Count)
                   .Append(" asserted violation(s) in the ").Append(state)
                   .AppendLine(" state that the exemption does not cover.").AppendLine();

            foreach (var item in unexpected) Describe(message, "VIOLATION", item);
        }

        if (stale.Count > 0)
            message.Append("The exemption for ").Append(string.Join(", ", stale))
                   .Append(" is stale: ").Append(state)
                   .AppendLine(" no longer violates it, so delete it from the call. The exemption is written "
                               + "to expire on its own for exactly this reason - it was: " + why);

        Assert.Fail(message.ToString());
    }

    /// <summary>
    /// Rule T1, applied to a whole-page scan: a scan whose subject set was empty reports coverage it does not
    /// have. An axe run that evaluated nothing returns four empty arrays and reads exactly like a clean pass.
    /// </summary>
    private void ProvedItRan()
    {
        var evaluated = result.Passes.Length + result.Violations.Length + result.Incomplete.Length;

        Assert.True(evaluated > 0,
            $"axe evaluated no rule at all in the {state} state, which is not a pass: it means the run saw "
            + "nothing to look at. Either the page had not rendered when the scan ran - every wait in this "
            + "project is on a selector for that reason - or the tag list no longer selects any rule.");

        Assert.True(result.Passes.Length > 0,
            $"axe reported no PASSING rule in the {state} state. A document with real content passes dozens, "
            + "so this means the scan ran against a page that was effectively empty.");
    }

    /// <summary>
    /// Whether a result belongs to the asserted set. ONE axe run, partitioned on each result's own tags,
    /// rather than two runs: two runs over two configurations of the same page can disagree, and the second
    /// one doubles the slowest part of the gate.
    /// </summary>
    private static bool IsAsserted(AxeResultItem item) =>
        item.Tags.Any(WcagTags.Contains) || PromotedBestPractice.Contains(item.Id);

    /// <summary>
    /// Everything axe said, to test output, whether or not the assertion passes: the asserted results, the
    /// print-only best-practice results, every other incomplete, and the inapplicable count.
    /// </summary>
    private void Report()
    {
        output.WriteLine($"=== axe: {state} ===");
        output.WriteLine($"tags: {string.Join(" ", Tags)}");
        output.WriteLine(
            $"violations {result.Violations.Length}, incomplete {result.Incomplete.Length}, "
            + $"passes {result.Passes.Length}, inapplicable {result.Inapplicable.Length}");

        Lines("asserted violations", result.Violations.Where(IsAsserted));
        Lines("print-only best-practice violations (NOT asserted; advice, not conformance)",
              result.Violations.Where(i => !IsAsserted(i)));
        Lines("incomplete treated as failures", result.Incomplete.Where(i => IncompleteIsAFailure.Contains(i.Id)));
        Lines("other incomplete (printed only)", result.Incomplete.Where(i => !IncompleteIsAFailure.Contains(i.Id)));

        output.WriteLine(
            "inapplicable (printed only): "
            + (result.Inapplicable.Length == 0
                ? "none"
                : string.Join(", ", result.Inapplicable.Select(i => i.Id).OrderBy(i => i, StringComparer.Ordinal))));
        output.WriteLine(string.Empty);
    }

    private void Lines(string heading, IEnumerable<AxeResultItem> items)
    {
        var list = items.ToList();
        output.WriteLine($"{heading}: {(list.Count == 0 ? "none" : list.Count.ToString())}");

        foreach (var item in list)
        {
            output.WriteLine($"  [{item.Id}] impact={item.Impact} tags={string.Join(",", item.Tags)}");
            output.WriteLine($"    {item.Help}");

            foreach (var node in item.Nodes)
                output.WriteLine($"    at {node.Target?.Selector} :: {Trim(node.Html)}");
        }
    }

    private static void Describe(StringBuilder message, string kind, AxeResultItem item)
    {
        message.Append(kind).Append(" [").Append(item.Id).Append("] ").AppendLine(item.Help);
        message.Append("  tags: ").AppendLine(string.Join(", ", item.Tags));
        message.Append("  help: ").AppendLine(item.HelpUrl);

        foreach (var node in item.Nodes)
        {
            message.Append("  at: ").AppendLine(node.Target?.Selector);
            message.Append("      ").AppendLine(Trim(node.Html));

            foreach (var check in node.Any.Concat(node.All).Concat(node.None))
                if (check.Message is { Length: > 0 })
                    message.Append("      why: ").AppendLine(check.Message);
        }

        message.AppendLine();
    }

    private static string Trim(string? html) =>
        html is null ? string.Empty
        : html.Length <= 220 ? html.ReplaceLineEndings(" ")
        : html.ReplaceLineEndings(" ")[..220] + " ...";
}

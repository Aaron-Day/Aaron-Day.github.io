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
    /// introduced two Level A criteria. Every aaa tag is left out, which is correct for a Level AA target:
    /// axe-core 4.10.2 carries exactly three wcag2aaa rules - color-contrast-enhanced, which is SC 1.4.6
    /// Contrast (Enhanced), identical-links-same-purpose and meta-refresh-no-exceptions - and
    /// color-contrast-enhanced is the one this list is deliberately not asking for. It is NOT a 44x44 rule:
    /// axe implements nothing at all for SC 2.5.5 Target Size (Enhanced), and a sentence here used to claim
    /// a wcag2aaa rule that asserts 44x44, which does not exist. The 44px figure is a house goal and lives
    /// in TouchTargetReportTests, which asserts nothing.
    /// </para>
    /// <para>
    /// <b>Two version numbers, and every rule claim in this file is about the second.</b> The NuGet package
    /// is Deque.AxeCore.Playwright 4.10.1, which brings Deque.AxeCore.Commons 4.10.1. The axe-core LIBRARY
    /// bundled inside Deque.AxeCore.Commons 4.10.1 is 4.10.2: its axe.min.js opens with "axe v4.10.2" and
    /// reports <c>axe.version = "4.10.2"</c>. Package 4.10.1 ships axe-core 4.10.2. The rule inventories,
    /// defaults and tag memberships here were read from that bundle, so they are pinned to 4.10.2; do not
    /// "correct" them back to 4.10.1 to agree with the .csproj.
    /// </para>
    /// <para>
    /// <b>target-size runs here only because this list is non-empty.</b> The rule ships
    /// <c>enabled: false</c> in axe-core 4.10.2. axe's own tag matcher resolves to
    /// <c>include.some(t =&gt; rule.tags.includes(t)) || (include.length === 0 &amp;&amp; rule.enabled !==
    /// false)</c>: it consults <c>rule.enabled</c> SOLELY when the include list is empty, so with a tag list
    /// present a tag match alone is sufficient. The brief's claim that the tag list enables target-size is
    /// correct, and this is the implementation detail it rests on rather than the rule's own default. It is
    /// also the strongest argument for <see cref="AlwaysApplicable"/> below: SC 2.5.8 is covered in this
    /// project by one rule, switched on by that one mechanism, over a subject set axe alone decides.
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

    /// <summary>
    /// Rule T1 reached from the applicability side: the rules that must have had a SUBJECT in every state
    /// this project scans.
    /// </summary>
    /// <remarks>
    /// <see cref="ProvedItRan"/> is a WHOLE-SCAN floor - some rule evaluated, some rule passed - and it
    /// cannot see one named rule quietly going inapplicable. The case that matters most is target-size: it
    /// is this project's only coverage of SC 2.5.8 Target Size (Minimum), Level AA, it matches through
    /// axe's own widget-not-inline-matches, which stops matching if the controls change shape, and a scan
    /// in which it matched nothing still reports clean while the project still reads as gating 2.5.8. The
    /// same argument holds for the eight best-practice rules promoted above, and for link-in-text-block,
    /// which is already inapplicable on "/" and "/movies".
    /// <para>
    /// Each of these has a subject in every state this project scans, measured one state at a time rather
    /// than assumed. Rules only some states have - skip-link and listitem, which the boot-failure shell has
    /// neither of, and link-in-text-block, which only the not-found page has - are named by the caller, per
    /// state, so that each scan says what it actually covered.
    /// </para>
    /// <para>
    /// axe's <c>list</c> rule is deliberately in no list here. It was measured inapplicable in all fifteen
    /// states, and correctly so: both of this app's lists declare <c>role="list"</c> - PosterGrid says why,
    /// and NavMenu does the same - while the rule's own matcher is <c>no-role-matches</c>. The subjects are
    /// the <c>li</c> elements instead, through <c>listitem</c>, which the caller names per state.
    /// </para>
    /// </remarks>
    private static readonly string[] AlwaysApplicable =
    [
        "target-size",
        "color-contrast",
        "region",
        "landmark-one-main",
        "landmark-unique",
        "page-has-heading-one",
        "heading-order",
        "empty-heading",
        "tabindex",
    ];

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
    /// <para>
    /// <b>And no exemption mechanism either, where there used to be one.</b> MustBeCleanExcept named its
    /// rules at the call site, refused outright any rule carrying a WCAG tag, and failed when an exemption
    /// stopped being needed - and it had exactly one caller, the boot-failure shell with no main landmark
    /// and no h1. index.html now gives that shell both, the state scans at zero, and the exemption expired
    /// on its own. The mechanism went with it rather than waiting for the next inconvenient state to reuse
    /// it: this project's brief said to stop and ask rather than invent one, and the answer to the asking
    /// was markup.
    /// </para>
    /// </remarks>
    /// <param name="alsoApplicable">
    /// Rule ids this state has subjects for over and above <see cref="AlwaysApplicable"/>. Every one of
    /// them must have had a subject, so naming a rule here is a claim about this state's own markup.
    /// </param>
    public void MustBeClean(params string[] alsoApplicable)
    {
        ProvedItRan();
        EveryNamedRuleHadASubject(alsoApplicable);

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
    /// Every rule in <see cref="AlwaysApplicable"/>, and every one this state was told it has, appeared in
    /// Passes, Violations or Incomplete - which is to say axe found something to run it against.
    /// </summary>
    /// <remarks>
    /// A rule that came back INAPPLICABLE contributed nothing, and the scan still reports clean. See
    /// <see cref="AlwaysApplicable"/> for why target-size makes this more than a tidiness check.
    /// </remarks>
    private void EveryNamedRuleHadASubject(IEnumerable<string> alsoApplicable)
    {
        var applicable = Applicable();

        var expected = AlwaysApplicable.Concat(alsoApplicable)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        var absent = expected.Where(id => !applicable.Contains(id)).ToList();

        Assert.True(absent.Count == 0,
            $"In the {state} state axe had no subject for {string.Join(", ", absent)}, so the rule(s) came "
            + "back INAPPLICABLE and the scan reports clean while covering nothing they would have covered. "
            + "ProvedItRan cannot see this: it is satisfied by any rule running at all. target-size in "
            + "particular is this project's ONLY coverage of SC 2.5.8 Target Size (Minimum), Level AA, and "
            + "it matches through axe's own widget-not-inline-matches, which stops matching when the "
            + "controls change shape - at which point the scan stays green and the project still reads as "
            + "gating 2.5.8. Either the markup lost the elements the rule looks at, in which case say so "
            + "here rather than letting a green scan imply the coverage, or the state driver scanned a DOM "
            + "that is not the one this test names.");
    }

    /// <summary>Every rule axe found a subject for, which is every array but Inapplicable.</summary>
    private HashSet<string> Applicable() =>
        new(result.Passes.Concat(result.Violations).Concat(result.Incomplete).Select(i => i.Id),
            StringComparer.Ordinal);

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
            "applicable (axe found a subject): "
            + string.Join(", ", Applicable().OrderBy(i => i, StringComparer.Ordinal)));

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

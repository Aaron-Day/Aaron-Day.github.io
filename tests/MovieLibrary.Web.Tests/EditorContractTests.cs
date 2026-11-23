using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using MovieLibrary.Core.Models;
using MovieLibrary.Core.Store;
using MovieLibrary.Web.Editor;
using MovieLibrary.Web.Editor.Components;
using MovieLibrary.Web.Editor.Pages;
using MovieLibrary.Web.Editor.Store;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// The editor's markup under the same <see cref="MarkupRules"/> the public pages answer to: "exempt from
/// nothing". MarkupContractTests cannot reach it, by design -- its scopes are the addresses the SITE serves,
/// AppUnderTest reflects over the web assembly alone, and splicing "/editor" in would break the identity
/// "addresses + 2 = scopes" and the two skip guards, which are claims about the web app's markup and stay
/// true. So the editor has its own scopes here, rendered directly with RecordEditor as the root, exactly as
/// the other editor test classes do (it needs no router and no route; the test project references the
/// editor unconditionally, so these contracts run in Release as well, which spec 8.5 "exempt from
/// nothing" requires), and the same house pattern: every rule asserts its subject set has a measured floor
/// first, because a loop over nothing passes.
/// <para>
/// Heading ORDER is not a contract here, on purpose: the editor has exactly one heading in its whole markup,
/// so there is no adjacent pair for a rule to compare, and a heading invented to make one possible would be
/// markup added for a test.
/// </para>
/// </summary>
public sealed class EditorContractTests
{
    private sealed record Scope(string Where, IElement Root);

    private sealed class Source : ICatalogueSource
    {
        public CatalogueState State { get; init; } = CatalogueState.Ready;
        public bool Fixtures { get; init; }
        public TaskCompletionSource? Hold { get; init; }

        // A film with a poster, so the worklist's thumbnail is in the markup: without one the image rules
        // would have no subject here at all.
        private static readonly MovieRecord Film =
            EditorCatalogueTests.Film(550, "Fight Club", 139, 0.5, "David Fincher") with { P = "/fight-club.jpg" };

        public async Task<(CatalogueState State, IReadOnlyList<MovieRecord> Movies)> MoviesAsync()
        {
            if (Hold is { } h) await h.Task;
            return (State, [Film]);
        }

        public Task<(CatalogueState State, CatalogMeta? Meta)> MetaAsync() =>
            Task.FromResult((State, (CatalogMeta?)(CatalogMeta.Empty with { Fixtures = Fixtures })));
    }

    private enum Open { No, Row, RowWithAnExtraField }

    /// <summary>The folder the granted scopes are given, and so the name the granted sentence must carry.</summary>
    private const string GrantedFolder = "MovieLibrary";

    private const string ExtraFieldScope = "the editor ready, folder granted, a row open with an extra field";

    private static Scope Render(
        string where, string environment = "Development", Source? source = null, string? folder = GrantedFolder,
        Open open = Open.No, string waitFor = "ul.worklist")
    {
        var context = new BunitContext();

        try
        {
            EditorPage.ScriptFolder(context, folder: folder, storeText: RecordStoreReader.Write(RecordStore.Empty));

            // A manual clock: nothing here may let the worklist's debounce timers fire mid-render.
            EditorPage.Register(
                context, EditorPage.Environment(environment), source ?? new Source(),
                new EditorShellTests.SilentRegion(), new ManualTimeProvider());

            var cut = EditorPage.Render(context);
            if (waitFor.Length > 0) cut.WaitForState(() => cut.FindAll(waitFor).Count > 0);

            if (open != Open.No)
            {
                cut.Find("button.worklist-row").Click();
                if (open == Open.RowWithAnExtraField) cut.Find("button.record-add").Click();
            }

            return new Scope(where, MarkupRules.Parse(cut.Markup));
        }
        finally
        {
            context.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static Func<IElement, bool> Says(string sentence, string selector = ".catalog-state p") =>
        root => root.QuerySelector(selector)?.TextContent.Trim() == sentence;

    private static Func<IElement, bool> Has(string selector) => root => root.QuerySelector(selector) is not null;

    /// <summary>
    /// The editor's states, each with the thing that proves it was REACHED: a scope that rendered the wrong
    /// state would pass every rule below while covering none of what it is named for.
    /// <para>
    /// "No grant" and "a grant" are rendered with a row open: a closed worklist reads the same either way,
    /// because the folder is only ever shown inside the form, so closed they would be one scope twice. The
    /// last scope adds one extra field, the only state in which the remove button exists.
    /// </para>
    /// </summary>
    private static readonly Lazy<IReadOnlyList<(Scope Scope, Func<IElement, bool> Reached)>> Scopes = new(() =>
    [
        (Render("the editor when not local", environment: "Production", waitFor: ""),
            Says(RecordEditor.NotLocalSentence)),
        (Render("the editor while loading", source: new Source { Hold = new TaskCompletionSource() }, waitFor: ""),
            Says(RecordEditor.LoadingSentence)),
        (Render("the editor with a failed fetch", source: new Source { State = CatalogueState.Failed },
                waitFor: "button.try-again"),
            Says(RecordEditor.CatalogueFailedSentence)),
        (Render("the editor over a synthetic catalogue", source: new Source { Fixtures = true }),
            Says(RecordEditor.SyntheticSentence)),
        // The two grant scopes differ in exactly one observable way, the form's grant sentence, so the
        // proof IS that sentence: a row being open is true of both and would let either stand in for the other.
        (Render("the editor ready, no folder granted, one row open", folder: null, open: Open.Row),
            Says(RecordForm.NoFolderSentence, ".record-grant")),
        (Render("the editor ready, folder granted, one row open", open: Open.Row),
            Says("Saving to: " + GrantedFolder, ".record-grant")),
        (Render(ExtraFieldScope, open: Open.RowWithAnExtraField),
            Has("button.record-remove")),
    ]);

    private static IReadOnlyList<Scope> EveryScope() => Scopes.Value.Select(s => s.Scope).ToList();

    private static void TheScopesAreTheSevenStatesTheyAreNamedFor()
    {
        var scopes = Scopes.Value;

        // Scopes is a hand-written literal, so this count can only change at a visible edit of that list;
        // it will not notice a state rendering wrongly. The loop below is the real guard, and it is what
        // stops two entries standing in for one another.
        Assert.True(scopes.Count == 7,
            "The editor contracts have " + scopes.Count + " scopes, so the list was edited. Seven states "
            + "are covered: not local, loading, failed, synthetic, ready with no grant, ready with a grant, "
            + "and a row open with an extra field. A different number means one was added or dropped, and "
            + "every message here that says seven needs the same edit.");

        foreach (var (scope, reached) in scopes)
            Assert.True(reached(scope.Root), scope.Where + " did not reach the state it is named for.");
    }

    private static void Subjects(Func<IElement, int> count, string what, int floor, string whatADropMeans)
    {
        var total = EveryScope().Sum(s => count(s.Root));

        Assert.True(total >= floor,
            "This rule found " + total + " " + what + " across " + EveryScope().Count + " editor scopes. "
            + floor + " were measured, and a rule over fewer is checking less than it reads as checking. "
            + whatADropMeans);
    }

    private static void NoViolations(Func<IElement, IReadOnlyList<string>> rule, string why)
    {
        var found = EveryScope()
            .SelectMany(s => rule(s.Root).Select(v => "  in " + s.Where + ": " + v))
            .ToList();

        Assert.True(found.Count == 0, why + Environment.NewLine + string.Join(Environment.NewLine, found));
    }

    [Fact]
    public void TheScopesReachTheStatesTheyAreNamedFor() => TheScopesAreTheSevenStatesTheyAreNamedFor();

    [Fact]
    public void EveryStateOfTheEditorRendersExactlyOneH1()
    {
        TheScopesAreTheSevenStatesTheyAreNamedFor();

        Subjects(s => MarkupRules.TopLevelHeadings(s).Count, "<h1>", 7,
            "The heading is outside every branch of RecordEditor and so is one per scope, seven; fewer means "
            + "a branch of the page stopped rendering it.");

        NoViolations(MarkupRules.ExactlyOneTopLevelHeading,
            "The editor must render exactly one <h1> in every state: FocusOnNavigate's selector is \"#app h1\".");
    }

    [Fact]
    public void EveryEditorImgHasAnAltAttributeEvenIfEmpty()
    {
        TheScopesAreTheSevenStatesTheyAreNamedFor();

        Subjects(s => MarkupRules.Images(s).Count, "<img>", 4,
            "One thumbnail in each of the four scopes that list the film (synthetic, and the three with a row "
            + "open). Fewer means the Source here lost its poster path or the worklist row stopped emitting "
            + "the image, and both image contracts are then checking nothing.");

        NoViolations(MarkupRules.EveryImageHasAnAltAttribute,
            "An <img> with no alt attribute makes a screen reader announce the file name.");
    }

    [Fact]
    public void EveryEditorImgHasExplicitWidthAndHeight()
    {
        TheScopesAreTheSevenStatesTheyAreNamedFor();

        Subjects(s => MarkupRules.Images(s).Count, "<img>", 4,
            "One thumbnail in each of the four scopes that list the film (synthetic, and the three with a row "
            + "open). Fewer means the Source here lost its poster path or the worklist row stopped emitting "
            + "the image, and both image contracts are then checking nothing.");

        NoViolations(MarkupRules.EveryImageHasExplicitWidthAndHeight,
            "Without width and height the user agent cannot reserve the box before the image arrives.");
    }

    [Fact]
    public void NoFocusableWidgetInTheEditorIsNestedInsideAnother()
    {
        TheScopesAreTheSevenStatesTheyAreNamedFor();

        Subjects(s => MarkupRules.LeafWidgets(s).Count, "element with a leaf widget role", 95,
            "The nested-widget rule loops over these. Fewer means worklist rows, search, filter or form "
            + "controls stopped rendering as widgets in the ready scopes.");

        NoViolations(MarkupRules.NoFocusableWidgetIsNestedInsideAnother,
            "A focusable control inside another is not reliably announced (SC 4.1.2).");
    }

    [Fact]
    public void EveryEditorInteractiveElementHasAnAccessibleName()
    {
        TheScopesAreTheSevenStatesTheyAreNamedFor();

        Subjects(s => MarkupRules.InteractiveElements(s).Count, "interactive element", 99,
            "The accessible-name rule loops over these. Fewer means controls stopped rendering in the ready "
            + "scopes, for instance the whole worklist search block (24 of the 99); the contract below "
            + "names the ones that must not go missing.");

        NoViolations(MarkupRules.EveryInteractiveElementHasAnAccessibleName,
            "A control with no accessible name is announced as its role and nothing else (SC 4.1.2).");
    }

    /// <summary>
    /// SC 2.5.3 Label in Name. The editor's one control with an author label over visible text is the
    /// extra-field remove button (text "Remove", aria-label naming the field), and it exists only while a
    /// row carries an extra field. The count is asserted for THAT scope on its own, so the contract cannot
    /// quietly go back to looping over nothing if the scope loses the field.
    /// </summary>
    [Fact]
    public void TheRemoveButtonSatisfiesLabelInNameAndIsASubject()
    {
        TheScopesAreTheSevenStatesTheyAreNamedFor();

        var withField = Scopes.Value.Single(s => s.Scope.Where == ExtraFieldScope).Scope;
        var subjects = MarkupRules.ElementsWhoseLabelReplacesVisibleText(withField.Root);

        Assert.True(subjects.Count >= 1,
            "SC 2.5.3 applies to " + subjects.Count + " control(s) in " + withField.Where + ". There is at "
            + "least one, the extra-field remove button; none means the scope no longer opens a row with an "
            + "extra field, and the rule below would pass over an empty set.");

        NoViolations(MarkupRules.AnAriaLabelContainsTheVisibleTextItReplaces,
            "Someone operating the page by voice says the words they can see. If the accessible name does "
            + "not contain the visible text, the spoken command does not match the control.");
    }

    /// <summary>
    /// MEASURED, from the tree: across the seven scopes the contracts see 7 headings, 4 images, 99 interactive
    /// elements, 95 leaf widgets, 20 tabindex carriers and 82 failable id references, and each rule above
    /// holds its own floor at that number. A floor survives one control being swapped for another and a
    /// whole block vanishing only if the block is large enough to move it, so the controls the worklist and
    /// form are built around are NAMED here as well. The case this exists for: the search block closes before
    /// the list, so deleting it left every wait-for-the-list satisfied and every row clickable while taking
    /// about 24 controls and two label references per scope out of four scopes.
    /// </summary>
    [Fact]
    public void TheContractsCanSeeTheEditorsOwnControlsByName()
    {
        TheScopesAreTheSevenStatesTheyAreNamedFor();

        var listed = Scopes.Value.Where(s => s.Scope.Root.QuerySelector("ul.worklist") is not null)
            .Select(s => s.Scope).ToList();

        Assert.True(listed.Count == 4,
            "Four scopes list the film, and " + listed.Count + " do. Fewer means a ready scope stopped "
            + "reaching the worklist, and the named controls below would be looked for in fewer places.");

        foreach (var scope in listed)
        {
            var controls = MarkupRules.InteractiveElements(scope.Root);
            var references = MarkupRules.FailableIdReferences(scope.Root);

            Assert.True(controls.Any(e => e.Id == "worklist-query"), scope.Where + " lost the search input.");
            Assert.True(controls.Any(e => e.Id == "worklist-sort"), scope.Where + " lost the sort select.");
            Assert.True(controls.Count(e => e.GetAttribute("name") == "worklist-band") == 4,
                scope.Where + " should offer four band radios.");
            Assert.True(controls.Any(e => e.ClassList.Contains("worklist-row")), scope.Where + " lost its row.");

            foreach (var id in new[] { "worklist-query", "worklist-sort" })
                Assert.True(references.Any(r => r.Attribute == "for" && r.Id == id),
                    scope.Where + " has no label pointing at #" + id + ".");
        }

        foreach (var scope in listed.Where(s => s.Root.QuerySelector("button.worklist-row[aria-expanded=true]") is not null))
            Assert.True(
                MarkupRules.InteractiveElements(scope.Root).Any(e => e.ClassList.Contains("record-save")),
                scope.Where + " has a row open and no save button.");
    }

    [Fact]
    public void NoEditorElementHasAPositiveTabIndex()
    {
        TheScopesAreTheSevenStatesTheyAreNamedFor();

        Subjects(s => MarkupRules.ElementsWithATabIndex(s).Count, "element with a tabindex", 20,
            "The heading and the state container carry tabindex minus one in every state, and the worklist "
            + "and form add the rest. Fewer means a focus target lost its tabindex, and the positive-tabindex "
            + "rule is then reading less of the page.");

        NoViolations(MarkupRules.NoElementHasAPositiveTabIndex,
            "A positive tabindex jumps an element ahead of document order for the whole page.");
    }

    [Fact]
    public void EveryEditorIdReferenceResolvesToAnIdInTheSameRender()
    {
        TheScopesAreTheSevenStatesTheyAreNamedFor();

        Subjects(s => MarkupRules.FailableIdReferences(s).Count, "id reference the rule can fail on", 82,
            "The labels, descriptions and aria-controls in the worklist and the open forms are the "
            + "references. Fewer means a label or description was removed, so whatever it provided is no "
            + "longer checked to resolve; losing the search block alone takes two per ready scope.");

        NoViolations(MarkupRules.EveryIdReferenceResolves,
            "An attribute pointing at an id that is not in the render is silently ignored, so whatever it "
            + "was providing is simply absent.");
    }
}

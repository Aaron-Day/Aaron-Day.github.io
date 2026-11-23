#if DEBUG
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using MovieLibrary.Core.Models;
using MovieLibrary.Core.Store;
using MovieLibrary.Web.Editor;
using MovieLibrary.Web.Editor.Pages;
using MovieLibrary.Web.Editor.Store;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// The editor's markup under the same <see cref="MarkupRules"/> the public pages answer to: "exempt from
/// nothing". MarkupContractTests cannot reach it, by design -- its scopes are the addresses the SITE serves,
/// AppUnderTest reflects over the web assembly alone, and splicing "/editor" in would break the identity
/// "addresses + 2 = scopes" and the two skip guards, which are claims about the web app's markup and stay
/// true. So the editor has its own scopes here, rendered directly (the editor is Debug-only, and a
/// router-driven render would fail in a Release run), and the same house pattern: every rule asserts its
/// subject set is non-empty first, because a loop over nothing passes.
/// <para>
/// Heading ORDER is not a contract here, on purpose: the editor has exactly one heading in its whole markup,
/// so there is no adjacent pair for a rule to compare, and a heading invented to make one possible would be
/// markup added for a test.
/// </para>
/// </summary>
public sealed class EditorContractTests
{
    private sealed record Scope(string Where, IElement Root);

    private sealed class Host(string environment) : IWebAssemblyHostEnvironment
    {
        public string Environment { get; } = environment;
        public string BaseAddress => "http://localhost/";
    }

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

    private static Scope Render(
        string where, string environment = "Development", Source? source = null, string? folder = "MovieLibrary",
        Open open = Open.No, string waitFor = "ul.worklist")
    {
        var context = new BunitContext();

        try
        {
            context.JSInterop.Mode = JSRuntimeMode.Loose;
            var module = context.JSInterop.SetupModule(LocalDirectory.ModulePath);
            module.Setup<bool>("supported").SetResult(true);
            module.Setup<string?>("status").SetResult(folder);
            module.Setup<string?>("readText", RecordStoreReader.DefaultFileName)
                .SetResult(RecordStoreReader.Write(RecordStore.Empty));

            context.Services
                .AddSingleton<IWebAssemblyHostEnvironment>(new Host(environment))
                .AddSingleton<ICatalogueSource>(source ?? new Source())
                .AddSingleton<IStatusRegion>(new EditorShellTests.SilentRegion())
                .AddSingleton<TimeProvider>(new ManualTimeProvider())
                .AddScoped<LocalDirectory>()
                .AddScoped<EditorCatalogue>();

            var cut = context.Render<RecordEditor>();
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

    private static Func<IElement, bool> Says(string sentence) =>
        root => root.QuerySelector(".catalog-state p")?.TextContent.Trim() == sentence;

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
        (Render("the editor ready, no folder granted, one row open", folder: null, open: Open.Row),
            Has("button.worklist-row[aria-expanded=true]")),
        (Render("the editor ready, folder granted, one row open", open: Open.Row),
            Has("button.worklist-row[aria-expanded=true]")),
        (Render("the editor ready, folder granted, a row open with an extra field", open: Open.RowWithAnExtraField),
            Has("button.record-remove")),
    ]);

    private static IReadOnlyList<Scope> EveryScope() => Scopes.Value.Select(s => s.Scope).ToList();

    private static void TheScopesAreTheSevenStatesTheyAreNamedFor()
    {
        var scopes = Scopes.Value;

        Assert.True(scopes.Count == 7,
            "The editor contracts have " + scopes.Count + " scopes. Seven states are covered: not local, "
            + "loading, failed, synthetic, ready with no grant, ready with a grant, and a row open with an "
            + "extra field. Fewer means a state was dropped from the list.");

        foreach (var (scope, reached) in scopes)
            Assert.True(reached(scope.Root), scope.Where + " did not reach the state it is named for.");
    }

    private static void Subjects(Func<IElement, int> count, string what, string whenItIsGone)
    {
        var total = EveryScope().Sum(s => count(s.Root));

        Assert.True(total > 0,
            "This rule found no " + what + " in any of " + EveryScope().Count + " editor scopes, so it "
            + "looped over nothing and passed without checking anything. " + whenItIsGone);
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

        Subjects(s => MarkupRules.TopLevelHeadings(s).Count, "<h1>",
            "The heading is outside every branch of RecordEditor, so it should be in all seven.");

        NoViolations(MarkupRules.ExactlyOneTopLevelHeading,
            "The editor must render exactly one <h1> in every state: FocusOnNavigate's selector is \"#app h1\".");
    }

    [Fact]
    public void EveryEditorImgHasAnAltAttributeEvenIfEmpty()
    {
        TheScopesAreTheSevenStatesTheyAreNamedFor();

        Subjects(s => MarkupRules.Images(s).Count, "<img>",
            "The worklist thumbnail needs a film with a poster path; the Source here has lost its poster.");

        NoViolations(MarkupRules.EveryImageHasAnAltAttribute,
            "An <img> with no alt attribute makes a screen reader announce the file name.");
    }

    [Fact]
    public void EveryEditorImgHasExplicitWidthAndHeight()
    {
        TheScopesAreTheSevenStatesTheyAreNamedFor();

        Subjects(s => MarkupRules.Images(s).Count, "<img>",
            "The worklist thumbnail needs a film with a poster path; the Source here has lost its poster.");

        NoViolations(MarkupRules.EveryImageHasExplicitWidthAndHeight,
            "Without width and height the user agent cannot reserve the box before the image arrives.");
    }

    [Fact]
    public void NoFocusableWidgetInTheEditorIsNestedInsideAnother()
    {
        TheScopesAreTheSevenStatesTheyAreNamedFor();

        Subjects(s => MarkupRules.LeafWidgets(s).Count, "element with a leaf widget role",
            "The worklist rows and form controls are widgets; none means the open-row scopes broke.");

        NoViolations(MarkupRules.NoFocusableWidgetIsNestedInsideAnother,
            "A focusable control inside another is not reliably announced (SC 4.1.2).");
    }

    [Fact]
    public void EveryEditorInteractiveElementHasAnAccessibleName()
    {
        TheScopesAreTheSevenStatesTheyAreNamedFor();

        Subjects(s => MarkupRules.InteractiveElements(s).Count, "interactive element",
            "The editor's controls are not rendering in these scopes.");

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

        var withField = Scopes.Value[6].Scope;
        var subjects = MarkupRules.ElementsWhoseLabelReplacesVisibleText(withField.Root);

        Assert.True(subjects.Count >= 1,
            "SC 2.5.3 applies to " + subjects.Count + " control(s) in " + withField.Where + ". There is at "
            + "least one, the extra-field remove button; none means the scope no longer opens a row with an "
            + "extra field, and the rule below would pass over an empty set.");

        NoViolations(MarkupRules.AnAriaLabelContainsTheVisibleTextItReplaces,
            "Someone operating the page by voice says the words they can see. If the accessible name does "
            + "not contain the visible text, the spoken command does not match the control.");
    }

    [Fact]
    public void NoEditorElementHasAPositiveTabIndex()
    {
        TheScopesAreTheSevenStatesTheyAreNamedFor();

        Subjects(s => MarkupRules.ElementsWithATabIndex(s).Count, "element with a tabindex",
            "The heading and the state container carry tabindex minus one in every state.");

        NoViolations(MarkupRules.NoElementHasAPositiveTabIndex,
            "A positive tabindex jumps an element ahead of document order for the whole page.");
    }

    [Fact]
    public void EveryEditorIdReferenceResolvesToAnIdInTheSameRender()
    {
        TheScopesAreTheSevenStatesTheyAreNamedFor();

        Subjects(s => MarkupRules.FailableIdReferences(s).Count, "id reference the rule can fail on",
            "The form's labels and descriptions are the references; none means no row opened in any scope.");

        NoViolations(MarkupRules.EveryIdReferenceResolves,
            "An attribute pointing at an id that is not in the render is silently ignored, so whatever it "
            + "was providing is simply absent.");
    }
}
#endif

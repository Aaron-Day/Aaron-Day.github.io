using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MovieLibrary.Core.Store;
using MovieLibrary.Web.Editor;
using MovieLibrary.Web.Editor.Components;
using MovieLibrary.Web.Editor.Store;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// The form inside the open row, rendered on its own. Every assertion is a fact a keyboard or a screen reader
/// meets: a label that really resolves, a state sentence that is really referenced, an error that is announced
/// both ways, and no control at all where nothing can be edited.
/// </summary>
public sealed class RecordFormTests
{
    /// <summary>A matched key, so every id in the form is proved to survive beginning with a digit.</summary>
    private const string K = "550-139";

    private static readonly MovieLibrary.Core.Models.MovieRecord Movie =
        EditorCatalogueTests.Film(550, "Fight Club", 139, 0.5, "David Fincher");

    private static string Id(string rest) => $"row-{K}-{rest}";

    private sealed class Harness : IAsyncDisposable
    {
        public BunitContext Context { get; } = new();

        public int Saves { get; private set; }

        public int Grants { get; private set; }

        public Harness()
        {
            Context.JSInterop.Mode = JSRuntimeMode.Loose;
            Context.Services.AddSingleton<IStatusRegion>(new EditorShellTests.SilentRegion());
        }

        public IRenderedComponent<RecordForm> Render(
            RecordDraft draft, bool editable = true, bool api = true, string? folder = "MovieLibrary",
            StoreReading store = StoreReading.Read, bool saving = false, string outcome = "",
            FilmEntry? entry = null) =>
            Context.Render<RecordForm>(p => p
                .Add(f => f.Row, new WorklistRow(Movie, new string('a', 40), MovieLibrary.Core.Matching.Confidence.Uncertain, "x", entry))
                .Add(f => f.Draft, draft)
                .Add(f => f.Editable, editable)
                .Add(f => f.ApiSupported, api)
                .Add(f => f.FolderName, folder)
                .Add(f => f.Store, store)
                .Add(f => f.Saving, saving)
                .Add(f => f.Outcome, outcome)
                .Add(f => f.Save, () => Saves++)
                .Add(f => f.ChooseFolder, () => Grants++));

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private static RecordDraft Draft(RecordOverride? saved = null) => RecordDraft.For(Movie, saved);

    /// <summary>Every input that holds a value, as against the override checkboxes and the chooser's radios.</summary>
    private static IReadOnlyList<IElement> ValueInputs(IRenderedComponent<RecordForm> cut) =>
        cut.FindAll("input").Where(i => i.GetAttribute("type") == "text").ToList();

    [Fact]
    public async Task EveryInputHasARealLabelThatResolves()
    {
        await using var h = new Harness();
        var draft = Draft(new RecordOverride(Candidates: [807, 808], Runtime: 139));
        draft.AddExtra();
        var cut = h.Render(draft);

        var inputs = cut.FindAll("input");
        Assert.NotEmpty(inputs);

        // The relation DIRECTLY, and never through MarkupRules.AccessibleName: that helper accepts a
        // placeholder as a last resort, exactly as the HTML accessible name computation does, so a
        // placeholder-only input passes it and the test would not catch the thing it exists to catch.
        foreach (var input in inputs)
        {
            var byFor = cut.FindAll("label[for]").Any(l => l.GetAttribute("for") == input.Id);
            var wrapping = input.Closest("label") is not null;

            Assert.True(byFor || wrapping, MarkupRules.Describe(input) + " has no label pointing at it.");
            Assert.False(input.HasAttribute("placeholder"), MarkupRules.Describe(input) + " has a placeholder.");
        }
    }

    [Fact]
    public async Task TheFieldsAreGroupedAndEveryGroupHasALegend()
    {
        await using var h = new Harness();
        var draft = Draft(new RecordOverride(Candidates: [807], Runtime: 139));
        draft.AddExtra();
        var cut = h.Render(draft);

        var legends = cut.FindAll("fieldset > legend").Select(l => l.TextContent.Trim()).ToList();

        foreach (var named in new[] { "Match", "Titles", "Facts", "Source", "Extra fields" })
            Assert.Contains(named, legends);

        // NEVER an exact count: A29 adds a fieldset per extras row and the chooser adds another.
        Assert.All(cut.FindAll("fieldset"), set =>
        {
            var legend = set.Children.FirstOrDefault(c => c.LocalName == "legend");
            Assert.NotNull(legend);
            Assert.False(string.IsNullOrWhiteSpace(legend.TextContent));
        });

        // A legend is not a heading, which is what keeps the heading-order skip guard alive until Task 6.
        Assert.Empty(cut.FindAll("h1, h2, h3, h4, h5, h6"));
    }

    [Fact]
    public async Task EveryFieldCarriesItsStateSentenceThroughDescribedBy()
    {
        await using var h = new Harness();
        var draft = Draft();
        var cut = h.Render(draft);

        var inputs = ValueInputs(cut);
        Assert.Equal(9, inputs.Count);

        foreach (var input in inputs)
        {
            var ids = (input.GetAttribute("aria-describedby") ?? "")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            Assert.NotEmpty(ids);
            Assert.Equal(input.Id + "-state", ids[0]);

            // A40: every id in the list resolves in the SAME render, and the element it names has text.
            foreach (var id in ids)
            {
                var target = Assert.Single(cut.FindAll($"[id='{id}']"));
                Assert.False(string.IsNullOrWhiteSpace(target.TextContent));
            }
        }

        // The described text is this field's state and not some other field's.
        Assert.Equal(draft.TmdbId.Describe(), cut.Find($"[id='{Id("tmdb-id-state")}']").TextContent.Trim());
        Assert.Equal(draft.Runtime.Describe(), cut.Find($"[id='{Id("runtime-state")}']").TextContent.Trim());
        Assert.Contains("139 minutes", cut.Find($"[id='{Id("runtime-state")}']").TextContent, StringComparison.Ordinal);

        // The error id is in the list ONLY while that error exists: an always-rendered empty paragraph is a
        // resolve target with no text, which is a described-by that says nothing.
        Assert.Empty(cut.FindAll($"[id='{Id("tmdb-id-error")}']"));
        Assert.DoesNotContain("-error", cut.Find($"[id='{Id("tmdb-id")}']").GetAttribute("aria-describedby")!,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheIntegerFieldsAreTextWithNumericInputModeAndNotNumberSpinners()
    {
        await using var h = new Harness();
        var cut = h.Render(Draft());

        foreach (var slug in new[] { "tmdb-id", "year", "runtime" })
        {
            var input = cut.Find($"[id='{Id(slug)}']");
            Assert.Equal("text", input.GetAttribute("type"));
            Assert.Equal("numeric", input.GetAttribute("inputmode"));
        }

        // m4: the candidates field takes no numeric keypad, because the keypad has no comma.
        Assert.False(cut.Find($"[id='{Id("candidates")}']").HasAttribute("inputmode"));

        // m9: every one of these turns on native constraint validation, whose bubble is a second error channel
        // with no aria-describedby link -- it breaks A40's association and cannot be measured for contrast.
        Assert.Empty(cut.FindAll("input[type=number]"));
        foreach (var banned in new[] { "pattern", "min", "max", "step", "maxlength", "required" })
            Assert.Empty(cut.FindAll($"input[{banned}]"));
    }

    [Fact]
    public async Task AnInvalidFieldSaysSoBothWaysAndNeitherAlone()
    {
        await using var h = new Harness();
        var draft = Draft();
        var cut = h.Render(draft);

        cut.Find($"[id='{Id("year-override")}']").Change(true);
        cut.Find($"[id='{Id("year")}']").Input("1500");
        cut.Find($"[id='{Id("year")}']").Blur();

        var input = cut.Find($"[id='{Id("year")}']");

        // From a string, never a bool: a bool attribute is present-and-empty when true and omitted when
        // false, and ARIA reads both as undefined.
        Assert.Equal("true", input.GetAttribute("aria-invalid"));

        var message = cut.Find($"[id='{Id("year-error")}']").TextContent.Trim();
        Assert.Equal("Enter a release year between 1870 and 2200.", message);
        Assert.Contains(Id("year-error"), input.GetAttribute("aria-describedby")!, StringComparison.Ordinal);

        // Neither alone: aria-invalid says something is wrong without saying what, and a message nothing
        // references is a message a reader never reaches.
        Assert.NotEqual("Invalid", message);
    }

    [Fact]
    public async Task NoFieldIsDisabledAndNoInputIsReadonly()
    {
        await using var h = new Harness();
        var draft = Draft(new RecordOverride(Candidates: [807], Runtime: 139));
        draft.AddExtra();
        var cut = h.Render(draft, folder: null);

        Assert.Empty(cut.FindAll("[disabled]"));
        Assert.Empty(cut.FindAll("input[readonly]"));

        // A55 and M14's unavailable Save is aria-disabled and stays focusable, which is the whole point.
        Assert.Equal("true", cut.Find("button.record-save").GetAttribute("aria-disabled"));
    }

    [Fact]
    public async Task TheFormOffersNoControlAtAllWhenNothingIsEditable()
    {
        await using var h = new Harness();
        var draft = Draft();
        var cut = h.Render(draft, editable: false);

        // M6: readonly has no effect on a checkbox, so a read-only form cannot be built, and a disabled one
        // is forbidden. There is nothing to make inert, so there is nothing rendered.
        Assert.Empty(cut.FindAll("input"));
        Assert.Empty(cut.FindAll("button.record-save"));
        Assert.Empty(cut.FindAll("button.record-folder"));
        Assert.Empty(cut.FindAll("[disabled]"));
        Assert.Empty(TabStops.Stops(cut.Find("div.record-form")));

        // The A27 sentences survive as prose, so he can still read what the site shows, and one sentence says
        // why nothing can be changed.
        Assert.Contains(RecordForm.NotEditableSentence, cut.Markup, StringComparison.Ordinal);
        foreach (var field in draft.Fields)
            Assert.Contains(field.Describe(), cut.Markup, StringComparison.Ordinal);

        // C2: the outcome container is the same element in every state, including this one.
        Assert.Single(cut.FindAll("p.record-state[tabindex='-1']"));
    }

    [Fact]
    public async Task TheFormRendersNoFormElementAndEveryButtonIsTypeButton()
    {
        await using var h = new Harness();
        var draft = Draft(new RecordOverride(Candidates: [807], Runtime: 139));
        draft.AddExtra();
        var cut = h.Render(draft);

        // A bare <form> turns Enter in any text field into a native GET submit, which reloads the whole
        // WebAssembly app and loses every unsaved edit -- worse than the redirect A3 forbids.
        Assert.Empty(cut.FindAll("form"));
        Assert.NotEmpty(cut.FindAll("button"));
        Assert.All(cut.FindAll("button"), b => Assert.Equal("button", b.GetAttribute("type")));

        var source = Source();
        foreach (var banned in new[] { "<form", "EditForm", "DataAnnotationsValidator", "ValidationSummary", "ValidationMessage" })
            Assert.DoesNotContain(banned, source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EachExtraRowIsItsOwnGroupAndItsRemoveButtonNamesItsField()
    {
        await using var h = new Harness();
        var draft = Draft();
        draft.AddExtra();
        draft.AddExtra();
        draft.AddExtra();
        draft.Extras[2].Name = "collection";
        var cut = h.Render(draft);

        // Both halves of A29, not either/or: the legend names the group and is NOT part of an input's
        // accessible name, so without unique label text twelve inputs are all called "Field name".
        var rows = cut.FindAll("fieldset.record-extra");
        Assert.Equal(3, rows.Count);
        Assert.Equal("Extra field 3", rows[2].QuerySelector("legend")!.TextContent.Trim());
        Assert.Equal("Field 3 name", cut.Find($"label[for='{Id("extra-3-name")}']").TextContent.Trim());
        Assert.Equal("Field 3 value", cut.Find($"label[for='{Id("extra-3-value")}']").TextContent.Trim());

        // SC 2.5.3: the name CONTAINS the visible text verbatim, and the position comes first so the name is
        // stable while he types the key and unique when two rows hold the same text.
        var remove = rows[2].QuerySelector("button.record-remove")!;
        Assert.Equal("Remove", remove.TextContent.Trim());
        Assert.Equal("Remove field 3, \"collection\"", remove.GetAttribute("aria-label"));
        Assert.Equal("Remove field 2", rows[1].QuerySelector("button.record-remove")!.GetAttribute("aria-label"));
        Assert.Empty(MarkupRules.AnAriaLabelContainsTheVisibleTextItReplaces(cut.Find("div.record-form")));

        // No two controls in the form answer to the same words.
        var names = MarkupRules.InteractiveElements(cut.Find("div.record-form"))
            .Select(MarkupRules.AccessibleName).ToList();
        Assert.All(names, n => Assert.False(string.IsNullOrWhiteSpace(n)));
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>
    /// M12, through the one signal bUnit gives: Blazor applies a reference capture only on the render that
    /// CREATES the element, so a reference attribute that is empty on a later render is an element that was
    /// kept, and the id in a focus call identifies which element instance focus really went to.
    /// </summary>
    [Fact]
    public async Task AnExtraRowKeepsFocusAndCaretWhileItsNameIsTyped()
    {
        await using var h = new Harness();
        var draft = Draft();
        draft.AddExtra();
        draft.AddExtra();
        var cut = h.Render(draft);

        string Reference(int position) =>
            cut.Find($"[id='{Id($"extra-{position}-name")}']").GetAttribute("blazor:elementreference")!;

        var first = Reference(1);
        var second = Reference(2);
        Assert.False(string.IsNullOrEmpty(first) || string.IsNullOrEmpty(second));

        // Keyed on the TYPED KEY, every keystroke would be a new key and a new input: the capture would be
        // re-applied and the attribute would carry a fresh id instead of being empty.
        cut.Find($"[id='{Id("extra-2-name")}']").Input("col");
        Assert.Equal(string.Empty, Reference(2));
        cut.Find($"[id='{Id("extra-2-name")}']").Input("coll");
        Assert.Equal(string.Empty, Reference(2));

        // Keyed on the LOOP INDEX, removing an earlier row shifts every key below it, so the row he was in
        // would be patched into the element ABOVE and his own element destroyed. Focus after the remove goes
        // to the surviving row's name input, and which element that is answers the question.
        cut.FindAll("button.record-remove")[0].Click();
        Assert.Single(cut.FindAll("fieldset.record-extra"));
        var moved = ((ElementReference)Focuses(h)[^1].Arguments[0]!).Id;
        Assert.Equal(second, moved);
        Assert.NotEqual(first, moved);
    }

    [Fact]
    public async Task AddingARowMovesFocusIntoIt()
    {
        await using var h = new Harness();
        var cut = h.Render(Draft());
        var before = Focuses(h);

        cut.Find("button.record-add").Click();

        // A30: the action is invisible without it -- the row appears below the control he pressed and nothing
        // tells him so. Its own label is then read as focus arrives, which is the statement.
        var moved = Assert.Single(Focuses(h).Skip(before.Count));
        Assert.Equal(
            cut.Find($"[id='{Id("extra-1-name")}']").GetAttribute("blazor:elementreference"),
            ((ElementReference)moved.Arguments[0]!).Id);
    }

    [Fact]
    public async Task RemovingARowMovesFocusDeliberately()
    {
        await using var h = new Harness();
        var draft = Draft();
        draft.AddExtra();
        draft.AddExtra();
        var cut = h.Render(draft);

        // Captured on the render that created them: a reference capture is applied once per element instance.
        var second = cut.Find($"[id='{Id("extra-2-name")}']").GetAttribute("blazor:elementreference")!;
        var add = cut.Find("button.record-add").GetAttribute("blazor:elementreference")!;

        // The pressed control is destroyed by the action it performs, so focus goes to the next row's name.
        var before = Focuses(h).Count;
        cut.FindAll("button.record-remove")[0].Click();
        Assert.Equal(before + 1, Focuses(h).Count);
        Assert.Equal(second, ((ElementReference)Focuses(h)[^1].Arguments[0]!).Id);

        // The last row leaves nothing below it, so focus goes to the Add button rather than to the body.
        cut.Find("button.record-remove").Click();
        Assert.Empty(cut.FindAll("fieldset.record-extra"));
        Assert.Equal(add, ((ElementReference)Focuses(h)[^1].Arguments[0]!).Id);
    }

    [Fact]
    public async Task CandidatesAreOfferedAsARadioGroupWithAnUndecidedOptionAndNothingIsRetyped()
    {
        await using var h = new Harness();
        var draft = Draft(new RecordOverride(Candidates: [807, 808], Runtime: 139));
        var cut = h.Render(draft);

        var group = cut.Find("fieldset.record-chooser");
        Assert.Equal("Candidate TMDB ids", group.QuerySelector("legend")!.TextContent.Trim());

        var radios = group.QuerySelectorAll("input[type=radio]").ToList();
        Assert.Equal(3, radios.Count);
        Assert.All(radios, r => Assert.Equal($"row-{K}-candidate", r.GetAttribute("name")));
        Assert.Equal(
            ["Use 807", "Use 808", "Keep all of these as candidates — undecided"],
            radios.Select(r => r.Closest("label")!.TextContent.Trim()).ToList());

        // A31: the ids he already saw are pre-filled and offered as values. Nothing is retyped.
        Assert.Equal("807, 808", cut.Find($"[id='{Id("candidates")}']").GetAttribute("value"));
        Assert.True(radios[2].HasAttribute("checked"));
        Assert.False(radios[0].HasAttribute("checked"));

        // Not buttons: A32 forbids a focus move on choosing, so with buttons nothing would announce the
        // change and A48 leaves no region to fix it with. The checked state IS the announcement.
        var focuses = Focuses(h).Count;
        radios[0].Change(true);
        Assert.Equal(focuses, Focuses(h).Count);
        Assert.Equal("807", cut.Find($"[id='{Id("tmdb-id")}']").GetAttribute("value"));
        Assert.Equal(string.Empty, cut.Find($"[id='{Id("candidates")}']").GetAttribute("value"));
        Assert.Equal(0, h.Saves);
    }

    [Fact]
    public async Task NothingInTheFormSubmitsOnInputOrOnFocus()
    {
        await using var h = new Harness();
        var draft = Draft(new RecordOverride(Candidates: [807, 808], Runtime: 139));
        draft.AddExtra();
        var cut = h.Render(draft);

        cut.Find($"[id='{Id("display-title-override")}']").Change(true);
        cut.Find($"[id='{Id("display-title")}']").Input("Se7en");
        cut.Find($"[id='{Id("display-title")}']").Blur();
        cut.FindAll("fieldset.record-chooser input[type=radio]")[0].Change(true);
        cut.Find($"[id='{Id("extra-1-name")}']").Input("collection");
        cut.Find($"[id='{Id("extra-1-name")}']").Blur();

        // SC 3.2.1 and SC 3.2.2: nothing saves, nothing reorders, nothing moves focus. Only Save saves.
        Assert.Equal(0, h.Saves);
        cut.Find("button.record-save").Click();
        Assert.Equal(1, h.Saves);
    }

    [Fact]
    public async Task NoFieldIsMarkedInvalidBeforeItHasBeenLeftOrSaveHasBeenPressed()
    {
        await using var h = new Harness();
        var draft = Draft();
        var cut = h.Render(draft);

        // A freshly ticked override is an error he has not made yet: nothing is entered, and saying so before
        // he has had the chance to type is noise he cannot stop (m7).
        cut.Find($"[id='{Id("year-override")}']").Change(true);
        Assert.Equal("Overridden, but no value is entered yet.", draft.ProblemFor(draft.Year));
        Assert.False(cut.Find($"[id='{Id("year")}']").HasAttribute("aria-invalid"));
        Assert.Empty(cut.FindAll($"[id='{Id("year-error")}']"));

        // Nor per keystroke: aria-invalid flipping on every character re-announces the field as he types.
        cut.Find($"[id='{Id("year")}']").Input("1");
        Assert.False(cut.Find($"[id='{Id("year")}']").HasAttribute("aria-invalid"));

        cut.Find($"[id='{Id("year")}']").Blur();
        Assert.Equal("true", cut.Find($"[id='{Id("year")}']").GetAttribute("aria-invalid"));

        // The other half: Save marks every field that is wrong, including ones he never visited.
        var fresh = Draft();
        fresh.TmdbId.Overridden = true;
        var second = h.Render(fresh);
        Assert.False(second.Find($"[id='{Id("tmdb-id")}']").HasAttribute("aria-invalid"));
        second.Find("button.record-save").Click();
        Assert.Equal("true", second.Find($"[id='{Id("tmdb-id")}']").GetAttribute("aria-invalid"));
    }

    [Fact]
    public async Task TheFormOffersNoTmdbDerivedField()
    {
        await using var h = new Harness();
        var draft = Draft(new RecordOverride(Candidates: [807], Runtime: 139));
        draft.AddExtra();
        var cut = h.Render(draft);

        // The exact set, so a field added later without a decision fails this test rather than shipping.
        string[] expected =
        [
            Id("tmdb-id-override"), Id("tmdb-id"), Id("candidates-override"), Id("candidates"),
            Id("candidate-807"), Id("candidate-undecided"),
            Id("display-title-override"), Id("display-title"), Id("sort-title-override"), Id("sort-title"),
            Id("year-override"), Id("year"), Id("runtime-override"), Id("runtime"),
            Id("directors-override"), Id("directors"),
            Id("apple-genre-override"), Id("apple-genre"),
            Id("purchase-info-override"), Id("purchase-info"),
            Id("extra-1-name"), Id("extra-1-value"),
        ];

        Assert.Equal(expected, cut.FindAll("input").Select(i => i.Id).ToArray());

        // D8: if one of those is wrong then the MATCH is wrong, and the fix is the TMDB id. Two ways to
        // express one correction is two ways for them to disagree. The Apple genre is the SHEET's and is
        // the witness that this list is not matching on the word "genre" alone.
        foreach (var derived in new[] { "poster", "backdrop", "genres", "genre", "vote", "overview", "tagline",
                                        "cast", "collection", "confidence", "score", "band" })
            Assert.Empty(cut.FindAll($"[id='{Id(derived)}']"));

        Assert.Single(cut.FindAll($"[id='{Id("apple-genre")}']"));
    }

    [Fact]
    public async Task TheFormInstallsNoKeyHandler()
    {
        await using var h = new Harness();
        var draft = Draft(new RecordOverride(Candidates: [807], Runtime: 139));
        draft.AddExtra();
        var cut = h.Render(draft);

        // m6: A21 to A24 chose a disclosure precisely so there is no SC 2.1.2 obligation, and an Escape that
        // discarded unsaved edits would be a new silent loss. FacetPanel is the precedent: no role, no
        // tabindex of its own, no key handler, and a test that says so.
        Assert.Empty(cut.FindAll("[onkeydown], [onkeyup], [onkeypress]"));

        var source = Source();
        foreach (var banned in new[] { "@onkeydown", "@onkeyup", "@onkeypress", "Escape" })
            Assert.DoesNotContain(banned, source, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ the stylesheet, read from the file
    //
    // m1. Until Task 6 widens WebRoot.Files the new stylesheet is bound by no project contract at all, and the
    // scoped sheet inherits nothing from Worklist.razor.css, so the rules that file carries for Bootstrap's
    // reboot have to be here as well -- and asserted here, or the first reader with a larger font meets a
    // legend floated full-width at a size that ignores it. WorklistTests' own floors already read every
    // *.razor.css in this project, so the target-size, viewport, motion and hover floors are covered there.

    /// <summary>
    /// The component's CODE, with its prose removed. The prose has to be allowed to name what it forbids --
    /// Worklist.razor spells out the live-region markers for exactly this reason -- so a scan for a banned
    /// construct that read the comments too would fail on the sentence explaining why the construct is absent.
    /// </summary>
    private static string Source() => WithoutProse(File.ReadAllText(WebRoot.EditorFiles("RecordForm.razor").Single()));

    private static string Stylesheet() =>
        WithoutProse(File.ReadAllText(WebRoot.EditorFiles("RecordForm.razor.css").Single()));

    private static string WithoutProse(string text) =>
        System.Text.RegularExpressions.Regex.Replace(
            System.Text.RegularExpressions.Regex.Replace(
                text, @"(@\*.*?\*@)|(/\*.*?\*/)", "", System.Text.RegularExpressions.RegexOptions.Singleline),
            @"^\s*//.*$", "", System.Text.RegularExpressions.RegexOptions.Multiline);

    [Fact]
    public void TheScopedStylesheetCarriesTheRulesItCannotInherit()
    {
        var css = Stylesheet();

        Assert.Matches(@"legend\s*\{[^}]*float:\s*none", css);
        Assert.Matches(@"legend\s*\{[^}]*line-height:\s*1\.5", css);
        Assert.Matches(@"fieldset\s*\{[^}]*min-width:\s*0", css);
        Assert.Matches(@"input\[type=""radio""\]", css);
        Assert.Matches(@"font-size:\s*max\(16px, 1rem\)", css);
        Assert.Matches(@"border:\s*2px solid var\(--line-strong\)", css);
        Assert.Matches(@"min-height:\s*var\(--touch\)", css);

        // m8: one column from the start, and nothing may size a track from its content.
        Assert.Matches(@"min-width:\s*0", css);

        // A27 and A33: an unset field is never shown by fading it. opacity turns axe's colour-contrast
        // INCOMPLETE, which is configured here as a hard failure, and .text-muted ships !important.
        Assert.DoesNotContain("opacity", css, StringComparison.Ordinal);
        Assert.DoesNotContain("text-muted", css, StringComparison.Ordinal);
        Assert.DoesNotContain("gradient", css, StringComparison.Ordinal);

        // A44 and the one permitted suppression: a programmatic focus move is the case where the ring is the
        // only thing telling a sighted keyboard user where focus went.
        Assert.DoesNotContain("outline", css, StringComparison.Ordinal);
    }

    private static IReadOnlyList<Bunit.JSRuntimeInvocation> Focuses(Harness h) =>
        h.Context.JSInterop.Invocations
            .Where(i => i.Identifier.EndsWith("focus", StringComparison.Ordinal))
            .ToList();
}

using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MovieLibrary.Core.Matching;
using MovieLibrary.Core.Models;
using MovieLibrary.Core.Store;
using MovieLibrary.Web.Editor;
using MovieLibrary.Web.Editor.Components;
using MovieLibrary.Web.Editor.Pages;
using MovieLibrary.Web.Editor.Store;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// The save, end to end: the page rendered over a stub catalogue and a scripted module, so every write that
/// crosses the boundary is observable and every one that should not happen can be proved not to have.
/// </summary>
public sealed class EditorSaveTests
{
    private sealed class Env : IWebAssemblyHostEnvironment
    {
        public string Environment => "Development";
        public string BaseAddress => "http://localhost/";
    }

    private sealed class Source(IReadOnlyList<MovieRecord> movies, bool fixtures) : ICatalogueSource
    {
        public Task<(CatalogueState State, IReadOnlyList<MovieRecord> Movies)> MoviesAsync() =>
            Task.FromResult((CatalogueState.Ready, movies));

        public Task<(CatalogueState State, CatalogMeta? Meta)> MetaAsync() =>
            Task.FromResult((CatalogueState.Ready, (CatalogMeta?)(CatalogMeta.Empty with { Fixtures = fixtures })));
    }

    private sealed class Region : IStatusRegion
    {
        public List<string> Said { get; } = [];

        public List<string> Alerted { get; } = [];

        public void Status(string text) => Said.Add(text);

        public void Alert(string text) => Alerted.Add(text);
    }

    internal static MovieRecord Fight => EditorCatalogueTests.Film(550, "Fight Club", 139, 0.5, "David Fincher");

    private const string FightKey = "550-139";

    private static string StoreOf(params FilmEntry[] films) =>
        RecordStoreReader.Write(RecordStore.Empty with { Films = films });

    /// <summary>One page, its module and the region it may not write to, with everything scripted up front.</summary>
    private sealed class Page : IAsyncDisposable
    {
        public BunitContext Context { get; } = new();

        public Region Region { get; } = new();

        public BunitJSModuleInterop Module { get; }

        public JSRuntimeInvocationHandler<bool> Writes { get; }

        public Page(
            string? store = null, bool supported = true, string? folder = "MovieLibrary",
            IReadOnlyList<MovieRecord>? movies = null, bool fixtures = false, IJSRuntime? runtime = null)
        {
            Context.JSInterop.Mode = JSRuntimeMode.Loose;
            Module = Context.JSInterop.SetupModule(LocalDirectory.ModulePath);
            Module.Setup<bool>("supported").SetResult(supported);
            Module.Setup<string?>("status").SetResult(folder);
            Module.Setup<string?>("readText", RecordStoreReader.DefaultFileName).SetResult(store);
            Module.SetupVoid("download", _ => true).SetVoidResult();
            Writes = Module.Setup<bool>("writeText", _ => true);

            Context.Services
                .AddSingleton<IWebAssemblyHostEnvironment>(new Env())
                .AddSingleton<ICatalogueSource>(new Source(movies ?? [Fight], fixtures))
                .AddSingleton<IStatusRegion>(Region)
                .AddSingleton(TimeProvider.System)
                .AddScoped(_ => new LocalDirectory(runtime ?? Context.JSInterop.JSRuntime))
                .AddScoped<EditorCatalogue>();
        }

        public IRenderedComponent<RecordEditor> Open(string key = FightKey)
        {
            var cut = Context.Render<RecordEditor>();
            cut.WaitForState(() => cut.FindAll("ul.worklist").Count == 1);
            Toggle(cut, key).Click();
            return cut;
        }

        public static IElement Toggle(IRenderedComponent<RecordEditor> cut, string key) =>
            cut.FindAll("button.worklist-row").Single(b => b.GetAttribute("aria-controls") == $"row-{key}-fields");

        /// <summary>Every text that crossed the boundary as a write, in order.</summary>
        public List<string> Written => Module.Invocations
            .Where(i => i.Identifier == "writeText")
            .Select(i => (string)i.Arguments[1]!)
            .ToList();

        public List<string> Names => Module.Invocations
            .Where(i => i.Identifier == "writeText")
            .Select(i => (string)i.Arguments[0]!)
            .ToList();

        public int Downloads => Module.Invocations.Count(i => i.Identifier == "download");

        public List<JSRuntimeInvocation> Focuses => Context.JSInterop.Invocations
            .Where(i => i.Identifier.EndsWith("focus", StringComparison.Ordinal))
            .ToList();

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private static void Tick(IRenderedComponent<RecordEditor> cut, string slug, bool on = true, string key = FightKey) =>
        cut.Find($"[id='row-{key}-{slug}-override']").Change(on);

    private static void Fill(IRenderedComponent<RecordEditor> cut, string slug, string text, string key = FightKey) =>
        cut.Find($"[id='row-{key}-{slug}']").Input(text);

    private static void Press(IRenderedComponent<RecordEditor> cut) => cut.Find("button.record-save").Click();

    private static RecordStore Parse(string text) => RecordStoreReader.Read(text, RecordStoreReader.DefaultFileName);

    private static string Outcome(IRenderedComponent<RecordEditor> cut) => cut.Find("p.record-state").TextContent;

    [Fact]
    public async Task SavingANewOverrideWritesTheStoreOnceAndTheReaderAcceptsIt()
    {
        await using var page = new Page();
        page.Writes.SetResult(true);
        var cut = page.Open();

        Tick(cut, "tmdb-id");
        Fill(cut, "tmdb-id", "807");
        Press(cut);

        Assert.Equal([RecordStoreReader.DefaultFileName], page.Names);
        var film = Assert.Single(Parse(Assert.Single(page.Written)).Films);
        Assert.Equal(807, Assert.Single(film.Records).TmdbId);
    }

    /// <summary>
    /// The echo is what a human reads to find the entry in the file, and the runtime is what the next build
    /// re-derives the record's key from: a record with neither is a correction nobody can trace and the
    /// pipeline cannot place.
    /// </summary>
    [Fact]
    public async Task TheWrittenEntryCarriesTheRowsIdentityKeyAndAnExplicitRuntime()
    {
        await using var page = new Page();
        page.Writes.SetResult(true);
        var cut = page.Open();

        Tick(cut, "tmdb-id");
        Fill(cut, "tmdb-id", "807");
        Press(cut);

        var film = Assert.Single(Parse(Assert.Single(page.Written)).Films);
        Assert.Equal(RecordLink.IdentityKeyOf(Fight), film.IdentityKey);
        Assert.Equal(139, Assert.Single(film.Records).Runtime);
        Assert.Equal("Fight Club", film.Sheet.Title);
        Assert.Equal("David Fincher", film.Sheet.Directors);
        Assert.Equal("2:19:00", film.Sheet.Runtime);
    }

    [Fact]
    public async Task SavingTheSameRecordTwiceLeavesOneEntry()
    {
        await using var page = new Page();
        page.Writes.SetResult(true);
        var cut = page.Open();

        Tick(cut, "tmdb-id");
        Fill(cut, "tmdb-id", "807");
        Press(cut);
        Fill(cut, "tmdb-id", "550");
        Press(cut);

        Assert.Equal(2, page.Written.Count);
        var film = Assert.Single(Parse(page.Written[^1]).Films);
        Assert.Equal(550, Assert.Single(film.Records).TmdbId);
    }

    private static MovieRecord Seven => EditorCatalogueTests.Film(807, "Se7en", 127, 0.5, "David Fincher");

    private const string SevenKey = "807-127";

    /// <summary>One Apple row that publishes two films, which is what a position exists for.</summary>
    private static FilmEntry Expanded() => new()
    {
        IdentityKey = new string('a', 40),
        Sheet = new SheetEcho("Fight Club / Se7en", "2:19:00", "David Fincher"),
        Note = "Bundle: the sheet row holds both films.",
        Records = [new RecordOverride(TmdbId: 550, Runtime: 139), new RecordOverride(TmdbId: 807, Runtime: 127)],
    };

    [Fact]
    public async Task SavingOneRecordOfAnExpandedEntryLeavesItsSiblingsIntact()
    {
        await using var page = new Page(StoreOf(Expanded()), movies: [Fight, Seven]);
        page.Writes.SetResult(true);
        var cut = page.Open(SevenKey);

        Tick(cut, "year", key: SevenKey);
        Fill(cut, "year", "1995", key: SevenKey);
        Press(cut);

        var film = Assert.Single(Parse(Assert.Single(page.Written)).Films);
        Assert.Equal(2, film.Records.Count);
        Assert.Equal(new RecordOverride(TmdbId: 550, Runtime: 139), film.Records[0]);
        Assert.Equal(807, film.Records[1].TmdbId);
        Assert.Equal(1995, film.Records[1].Year);
    }

    [Fact]
    public async Task ANoteTheOwnerWroteByHandSurvivesASave()
    {
        await using var page = new Page(StoreOf(Expanded()), movies: [Fight, Seven]);
        page.Writes.SetResult(true);
        var cut = page.Open(SevenKey);

        Tick(cut, "year", key: SevenKey);
        Fill(cut, "year", "1995", key: SevenKey);
        Press(cut);

        var film = Assert.Single(Parse(Assert.Single(page.Written)).Films);
        Assert.Equal(Expanded().Note, film.Note);
        Assert.Equal(Expanded().Sheet, film.Sheet);
    }

    /// <summary>
    /// An entry that overrides nothing is a line in a committed file that says nothing, and the reader refuses
    /// one anyway. The runtime is excluded from the judgment because the save always writes it (M3), so an
    /// entry that is only a runtime is still an entry that corrects nothing.
    /// </summary>
    [Fact]
    public async Task ClearingEveryOverrideRemovesTheEntryRatherThanWritingAnEmptyOne()
    {
        var entry = new FilmEntry
        {
            IdentityKey = new string('b', 40),

            // The record must PUBLISH this row's key or it binds to no row at all: a matched record's key is
            // its id and its runtime, so 807 here would be an entry for a film the catalogue does not hold.
            Records = [new RecordOverride(TmdbId: 550, Runtime: 139)],
        };
        await using var page = new Page(StoreOf(entry));
        page.Writes.SetResult(true);
        var cut = page.Open();

        // Both halves: the box is unticked AND the value cleared. A value left in a field whose override is
        // clear is a field error by design, not a silent drop, so the save would otherwise refuse.
        Tick(cut, "tmdb-id", on: false);
        Fill(cut, "tmdb-id", "");
        Press(cut);

        Assert.Empty(Parse(Assert.Single(page.Written)).Films);
    }

    /// <summary>
    /// A43: the first invalid field takes focus and there is no error summary at the top. Its own error text is
    /// in its described-by list, so landing on it says the name, the value and the problem in one arrival.
    /// </summary>
    [Fact]
    public async Task ASaveWithAFieldErrorWritesNothingAndFocusesTheFirstInvalidField()
    {
        await using var page = new Page();
        page.Writes.SetResult(true);
        var cut = page.Open();

        // Captured on the render that CREATED the input: a reference capture is applied once, on that render.
        var year = cut.Find($"[id='row-{FightKey}-year']").GetAttribute("blazor:elementreference");
        var before = page.Focuses.Count;

        Tick(cut, "year");
        Fill(cut, "year", "1500");
        Press(cut);

        Assert.Empty(page.Written);
        var moved = Assert.Single(page.Focuses.Skip(before));
        Assert.Equal(year, ((ElementReference)moved.Arguments[0]!).Id);
        Assert.Empty(cut.FindAll(".record-error-summary"));
    }

    [Fact]
    public async Task WithoutTheApiTheSaveDownloadsInsteadAndTheControlSaidSoBeforehand()
    {
        await using var page = new Page(supported: false, folder: null);
        var cut = page.Open();

        // BEFORE the click: a promise made after the action has already been broken.
        var save = cut.Find("button.record-save");
        Assert.Equal(RecordForm.DownloadName, save.TextContent.Trim());
        Assert.Contains($"row-{FightKey}-grant", save.GetAttribute("aria-describedby"), StringComparison.Ordinal);
        Assert.Equal(RecordForm.DownloadMechanismSentence, cut.Find($"[id='row-{FightKey}-grant']").TextContent);
        Assert.Equal(RecordForm.DownloadNoBackupSentence, cut.Find($"[id='row-{FightKey}-backup']").TextContent);

        Tick(cut, "tmdb-id");
        Fill(cut, "tmdb-id", "807");
        cut.Find("button.record-save").Click();

        Assert.Equal(1, page.Downloads);
        Assert.Empty(page.Written);
        Assert.Contains("downloads", Outcome(cut), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".bak", Outcome(cut), StringComparison.Ordinal);
    }

    /// <summary>
    /// C3. The download route is the only save a browser without the folder API has, and a failed import left
    /// it a button that did nothing, reported nothing and announced nothing.
    /// </summary>
    [Fact]
    public async Task WithNoScriptAtAllTheDownloadIsReportedAndNotSilent()
    {
        await using var page = new Page(runtime: new LocalDirectoryTests.FailingImportRuntime());
        var cut = page.Open();

        Tick(cut, "tmdb-id");
        Fill(cut, "tmdb-id", "807");
        cut.Find("button.record-save").Click();

        Assert.Contains("was not downloaded", Outcome(cut), StringComparison.Ordinal);
        Assert.Contains("could not be loaded", Outcome(cut), StringComparison.Ordinal);
        Assert.Empty(page.Region.Said);
    }

    /// <summary>
    /// D5 and A57. The refusal is a LOAD state, so it belongs to the page's own container above the list, and
    /// the open row offers nothing to press. The callback is then invoked directly, past the form's own guard,
    /// because a save that is only unreachable through the markup is one refactor away from reachable.
    /// </summary>
    [Fact]
    public async Task AFixtureCatalogueIsRefusedBeforeAnythingIsWritten()
    {
        await using var page = new Page(fixtures: true);
        page.Writes.SetResult(true);
        var cut = page.Open();

        Assert.Contains(RecordEditor.SyntheticSentence, cut.Find("div.catalog-state").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll(".record-form input"));
        Assert.Empty(cut.FindAll("button.record-save"));

        var form = cut.FindComponent<RecordForm>();
        await cut.InvokeAsync(() => form.Instance.Save.InvokeAsync());

        Assert.Empty(page.Written);
    }

    /// <summary>
    /// A45 and A49. The focus move IS the report, so the sentence is spoken once as the contents of what focus
    /// landed on; giving the polite region the same words speaks it twice, a settle delay apart.
    /// </summary>
    [Fact]
    public async Task AFailedWriteIsReportedOnceAndNotTwice()
    {
        await using var page = new Page();
        page.Writes.SetResult(false);
        var cut = page.Open();

        var state = cut.Find("p.record-state").GetAttribute("blazor:elementreference");
        var before = page.Focuses.Count;

        Tick(cut, "tmdb-id");
        Fill(cut, "tmdb-id", "807");
        Press(cut);

        Assert.Contains("was not written and is unchanged", Outcome(cut), StringComparison.Ordinal);
        Assert.Equal(state, ((ElementReference)Assert.Single(page.Focuses.Skip(before)).Arguments[0]!).Id);
        Assert.Empty(page.Region.Said);
        Assert.Empty(page.Region.Alerted);
    }

    /// <summary>
    /// A45's assertive fallback, which is unreachable and unproven without this: a refused focus move would
    /// otherwise leave a screen reader user hearing nothing at all about a save that did not happen.
    /// </summary>
    [Fact]
    public async Task AFailureWhoseFocusMoveDidNotHappenIsRaisedAssertivelyInstead()
    {
        await using var page = new Page();
        page.Writes.SetResult(false);
        page.Context.JSInterop
            .SetupVoid("Blazor._internal.domWrapper.focus", _ => true)
            .SetException(new JSException("the element has gone"));
        var cut = page.Open();

        Tick(cut, "tmdb-id");
        Fill(cut, "tmdb-id", "807");
        Press(cut);

        Assert.Contains("was not written and is unchanged", Assert.Single(page.Region.Alerted), StringComparison.Ordinal);
        Assert.Empty(page.Region.Said);
    }

    /// <summary>
    /// D3 and m5. The reader's own sentence names the entry, the record and the rule; one of mine would be a
    /// summary of a check I did not write. The framing clause in front of it is required, because
    /// ToValidatedJson passes "the record store about to be saved" as the thing being read and the message
    /// would otherwise be spoken starting mid-sentence.
    /// </summary>
    [Fact]
    public async Task AStoreTheReaderRejectsIsReportedInTheReadersOwnWordsAndNothingIsWritten()
    {
        var refused = Expanded() with
        {
            Records = [new RecordOverride(TmdbId: 550, Runtime: 139), new RecordOverride(TmdbId: 550, Runtime: 139)],
        };
        var expected = Assert.Throws<RecordStoreException>(
            () => StoreDraft.From(RecordStore.Empty with { Films = [refused] }).ToValidatedJson());

        await using var page = new Page(StoreOf(Expanded()), movies: [Fight, Seven]);
        page.Writes.SetResult(true);
        var cut = page.Open(SevenKey);

        // The second record is edited into a copy of the first: same id, same runtime, so both publish one key
        // and the next build would drop one of the two films silently.
        Fill(cut, "tmdb-id", "550", key: SevenKey);
        Tick(cut, "runtime", key: SevenKey);
        Fill(cut, "runtime", "139", key: SevenKey);
        cut.Find("button.record-save").Click();

        Assert.Empty(page.Written);
        Assert.Contains(expected.Message, cut.Find("p.record-state").TextContent, StringComparison.Ordinal);
        Assert.NotEqual(expected.Message, cut.Find("p.record-state").TextContent);
    }

    /// <summary>
    /// M14. The script copies the CURRENT file to .bak on every write, so a second press would make the second
    /// save's backup the first save's output and A58's one-step promise would silently become a state he never
    /// had. The control keeps its place in the DOM: disabling the focused element blurs it.
    /// </summary>
    [Fact]
    public async Task ASecondPressWhileTheFirstSaveIsInFlightWritesNothing()
    {
        await using var page = new Page();
        var cut = page.Open();

        Tick(cut, "tmdb-id");
        Fill(cut, "tmdb-id", "807");
        var before = page.Focuses.Count;

        var first = cut.Find("button.record-save").ClickAsync(new MouseEventArgs());

        Assert.Equal("true", cut.Find("button.record-save").GetAttribute("aria-disabled"));
        Assert.Null(cut.Find("button.record-save").GetAttribute("disabled"));

        var second = cut.Find("button.record-save").ClickAsync(new MouseEventArgs());

        // Deliberately NOT awaited: a handler that ignored the click has already returned, while one that
        // started a second save would be sitting on the unresolved write -- so the broken version fails here
        // in milliseconds instead of hanging the suite on a task nothing will complete.
        Assert.True(second.IsCompleted,
            "The second press started a save of its own. The script copies the current file to .bak on every "
            + "write, so the second save's backup would be the first save's output.");
        Assert.Single(page.Written);
        Assert.Equal(before, page.Focuses.Count);
        Assert.Equal("", Outcome(cut));

        page.Writes.SetResult(true);
        await first;
        await second;

        Assert.Single(page.Written);
    }

    /// <summary>
    /// M8. The script writes the backup only when the existing file was READ, so on a fresh clone the first
    /// save creates the file and there is no previous version at all. A58's promise is state-dependent, and a
    /// promise of a .bak that was never written is worse than no promise: he would stop keeping his own copy.
    /// </summary>
    [Fact]
    public async Task TheBackupPromiseIsOnlyMadeWhenAStoreWasRead()
    {
        await using (var read = new Page(StoreOf()))
        {
            var cut = read.Open();
            Assert.Equal(RecordForm.BackupPromise, cut.Find($"[id='row-{FightKey}-backup']").TextContent);
        }

        await using (var fresh = new Page(store: null))
        {
            var cut = fresh.Open();
            var said = cut.Find($"[id='row-{FightKey}-backup']").TextContent;
            Assert.Equal(RecordForm.NoBackupYetSentence, said);
            Assert.DoesNotContain(".bak", said, StringComparison.Ordinal);
        }

        await using (var downloads = new Page(StoreOf(), supported: false, folder: null))
        {
            var cut = downloads.Open();
            var said = cut.Find($"[id='row-{FightKey}-backup']").TextContent;
            Assert.Equal(RecordForm.DownloadNoBackupSentence, said);
            Assert.DoesNotContain(".bak", said, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// C5. The row keeps its place in the filtered list: a row that vanished on save would move 284 others
    /// under a hand already on the next one, and the panel focus had nowhere to go. What changes is the badge
    /// and the counts, both of which are the editor's own knowledge until the next build.
    /// </summary>
    [Fact]
    public async Task ASavedRecordRebadgesItsRowInPlaceAndTheCountDrops()
    {
        await using var page = new Page(movies: [Fight, Seven]);
        page.Writes.SetResult(true);
        var cut = page.Open();

        Assert.StartsWith("2 records need attention", cut.Find("p.worklist-counts").TextContent, StringComparison.Ordinal);

        Tick(cut, "tmdb-id");
        Fill(cut, "tmdb-id", "807");
        Press(cut);

        var row = Page.Toggle(cut, FightKey);
        Assert.Equal(Worklist.CorrectedBadge, row.QuerySelector(".worklist-badge")!.TextContent);
        Assert.StartsWith("1 record", cut.Find("p.worklist-counts").TextContent, StringComparison.Ordinal);
    }

    /// <summary>
    /// The other half of C5: candidates recorded without a choice are the explicitly undecided state, so the
    /// row still needs a decision and still says which band it is in.
    /// </summary>
    [Fact]
    public async Task ASaveThatRecordsCandidatesOnlyKeepsTheOriginalBadge()
    {
        await using var page = new Page(movies: [Fight, Seven]);
        page.Writes.SetResult(true);
        var cut = page.Open();

        Tick(cut, "candidates");
        Fill(cut, "candidates", "807, 808");
        Press(cut);

        Assert.Equal(
            ConfidenceBand.BadgeFor(Confidence.Uncertain),
            Page.Toggle(cut, FightKey).QuerySelector(".worklist-badge")!.TextContent);
        Assert.StartsWith("2 records need attention", cut.Find("p.worklist-counts").TextContent, StringComparison.Ordinal);
    }

    /// <summary>
    /// M11. The worklist closes the open row on every debounced query change, so a draft held inside the form
    /// would be destroyed 150 ms after one character reached the search box. It also discharges A31's
    /// "pre-fill, never re-ask" for the unsaved case.
    /// </summary>
    [Fact]
    public async Task EditsSurviveTheRowBeingClosedAndReopened()
    {
        await using var page = new Page();
        var cut = page.Open();

        Tick(cut, "tmdb-id");
        Fill(cut, "tmdb-id", "807");
        cut.Find("button.worklist-close").Click();
        Page.Toggle(cut, FightKey).Click();

        Assert.Equal("807", cut.Find($"[id='row-{FightKey}-tmdb-id']").GetAttribute("value"));
        Assert.NotNull(cut.Find($"[id='row-{FightKey}-tmdb-id-override']").GetAttribute("checked"));
    }

    /// <summary>
    /// A58. The script copies the current file to .bak on every write, so a save that writes the same bytes
    /// spends the one step back he has and gives nothing for it. Pressing Save on a record he has not touched
    /// is the ordinary way to reach that.
    /// </summary>
    [Fact]
    public async Task PressingSaveWithNoCorrectionsAtAllWritesNothingRatherThanRotatingTheBackup()
    {
        await using var page = new Page(StoreOf());
        page.Writes.SetResult(true);
        var cut = page.Open();

        Press(cut);

        Assert.Empty(page.Written);
        Assert.Equal(RecordEditor.NothingToSaveSentence, Outcome(cut));
    }

    /// <summary>
    /// A52 to A54 and M9. The prompt is a button activation, the granted state is visible text that is also
    /// the button's own description, and focus returns to the button whichever way the prompt ended -- so the
    /// state is spoken on arrival with no live region anywhere.
    /// </summary>
    [Fact]
    public async Task ChoosingAFolderMakesTheSaveAvailableAndSaysWhereItWillWrite()
    {
        await using var page = new Page(store: null, folder: null);
        page.Module.Setup<string?>("grant").SetResult("MovieLibrary");
        var cut = page.Open();

        var button = cut.Find("button.record-folder").GetAttribute("blazor:elementreference");
        Assert.Equal("true", cut.Find("button.record-save").GetAttribute("aria-disabled"));
        Assert.Equal(RecordForm.NoFolderSentence, cut.Find($"[id='row-{FightKey}-grant']").TextContent);

        cut.Find("button.record-folder").Click();

        Assert.Null(cut.Find("button.record-save").GetAttribute("aria-disabled"));
        Assert.Equal("Saving to: MovieLibrary", cut.Find($"[id='row-{FightKey}-grant']").TextContent);
        Assert.Contains(
            $"row-{FightKey}-grant",
            cut.Find("button.record-folder").GetAttribute("aria-describedby")!,
            StringComparison.Ordinal);
        Assert.Equal(button, ((ElementReference)page.Focuses[^1].Arguments[0]!).Id);
    }

    /// <summary>
    /// A54's failure mode, named in the requirement: a dismissed prompt that says nothing leaves the page
    /// looking unchanged, and he cannot tell whether it worked.
    /// </summary>
    [Fact]
    public async Task AFolderPickerTheOwnerDismissedIsSaidInTextAndChangesNothing()
    {
        await using var page = new Page(store: null, folder: null);
        page.Module.Setup<string?>("grant").SetResult((string?)null);
        var cut = page.Open();

        var button = cut.Find("button.record-folder").GetAttribute("blazor:elementreference");

        cut.Find("button.record-folder").Click();

        Assert.Equal(RecordEditor.NoFolderChosenSentence, Outcome(cut));
        Assert.Equal("true", cut.Find("button.record-save").GetAttribute("aria-disabled"));
        Assert.Equal(RecordForm.NoFolderSentence, cut.Find($"[id='row-{FightKey}-grant']").TextContent);
        Assert.NotEmpty(cut.FindAll("button.record-folder"));
        Assert.Equal(button, ((ElementReference)page.Focuses[^1].Arguments[0]!).Id);
    }
}

using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using MovieLibrary.Core.Models;
using MovieLibrary.Core.Store;
using MovieLibrary.Web.Editor;
using MovieLibrary.Web.Editor.Pages;
using MovieLibrary.Web.Editor.Store;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// The page's states: what it says when the catalogue fails, when no store was read, when the reader refuses
/// the file and when a correction belongs to nothing -- all in one container that is never swapped.
/// </summary>
public sealed class RecordEditorTests
{
    /// <summary>A source whose first answer can be held, so the page can be read in the middle of a load.</summary>
    private sealed class HeldSource : ICatalogueSource
    {
        public CatalogueState State { get; set; } = CatalogueState.Ready;
        public IReadOnlyList<MovieRecord> Movies { get; set; } = [];
        public TaskCompletionSource? Hold { get; set; }

        public async Task<(CatalogueState State, IReadOnlyList<MovieRecord> Movies)> MoviesAsync()
        {
            if (Hold is { } h) await h.Task;
            return (State, Movies);
        }

        public Task<(CatalogueState State, CatalogMeta? Meta)> MetaAsync() => Task.FromResult((State, (CatalogMeta?)null));
    }

    private static (BunitContext Context, HeldSource Source) Page(string? storeText, string? folder = "MovieLibrary")
    {
        var context = new BunitContext();
        EditorPage.ScriptFolder(context, folder: folder, storeText: storeText);

        // The real clock, deliberately: these tests let the worklist's timers run.
        var source = new HeldSource();
        EditorPage.Register(
            context, EditorPage.Environment(), source, new EditorShellTests.SilentRegion(), TimeProvider.System);
        return (context, source);
    }

    private static string StoreOf(params FilmEntry[] films) =>
        RecordStoreReader.Write(RecordStore.Empty with { Films = films });

    private static readonly MovieRecord Uncertain = EditorCatalogueTests.Film(807, "Se7en", 127, 0.5, "David Fincher");

    /// <summary>
    /// The container is one node through loading, failed and ready, and the proof is the focus call: the page
    /// moves focus to it when a problem is reported, and Blazor gives every NEW element a new reference id, so the
    /// same id on two calls either side of a retry means the node was patched and not replaced. (Comparing
    /// objects or attributes proves nothing here: bUnit re-parses the markup on every render.)
    /// </summary>
    [Fact]
    public async Task TheStateContainerIsTheSameNodeAcrossLoadingFailedAndReady()
    {
        var (context, source) = Page(storeText: null);
        await using var _ = context;
        source.Hold = new TaskCompletionSource();
        source.State = CatalogueState.Failed;

        var cut = EditorPage.Render(context);
        Assert.Equal(RecordEditor.LoadingSentence, cut.Find(".catalog-state p").TextContent.Trim());

        source.Hold.SetResult();
        cut.WaitForState(() => cut.Find(".catalog-state p").TextContent.Contains("could not be loaded"));
        Assert.Equal(RecordEditor.CatalogueFailedSentence, cut.Find(".catalog-state p").TextContent.Trim());

        source.State = CatalogueState.Ready;
        source.Movies = [Uncertain];
        cut.Find("button.try-again").Click();
        cut.WaitForState(() => cut.FindAll("ul.worklist").Count == 1);
        Assert.Equal(RecordEditor.NoStoreReadWithFolder("MovieLibrary"),
                     cut.Find(".catalog-state p").TextContent.Trim());

        var focuses = context.JSInterop.Invocations
            .Where(i => i.Identifier == "movieLibrary.focusIfUnmoved")
            .Select(i => ((ElementReference)i.Arguments[0]!).Id)
            .ToList();

        Assert.Equal(2, focuses.Count);
        Assert.Equal(focuses[0], focuses[1]);
        Assert.Equal("-1", cut.Find(".catalog-state").GetAttribute("tabindex"));
    }

    [Fact]
    public async Task AFailedFetchOffersTheRetryAndShowsNoWorklistThatCouldReadAsNothingToDo()
    {
        var (context, source) = Page(StoreOf());
        await using var _ = context;
        source.State = CatalogueState.Failed;

        var cut = EditorPage.Render(context);
        cut.WaitForState(() => cut.FindAll("button.try-again").Count == 1);

        Assert.Empty(cut.FindAll("ul.worklist"));
        Assert.Empty(cut.FindAll("search"));
    }

    /// <summary>
    /// The arm that fixes the defect: no folder granted, so the page names the control that CAN grant one and
    /// says to come back. Every session starts here -- a stored handle is not a stored grant, so status()
    /// answers null until permission is re-granted -- and the sentence this replaced told the owner to "try
    /// again once the repository folder is available" beside a button that could never make it so.
    /// </summary>
    [Fact]
    public async Task WithNoFolderGrantedThePageNamesTheControlThatCanGrantOneInsteadOfTheRetry()
    {
        var (context, source) = Page(storeText: null, folder: null);
        await using var _ = context;
        source.Movies = [Uncertain];

        var cut = EditorPage.Render(context);
        cut.WaitForState(() => cut.FindAll("ul.worklist").Count == 1);

        var sentence = cut.Find(".catalog-state p").TextContent.Trim();

        Assert.Equal(RecordEditor.NoStoreReadNoFolderSentence, sentence, StringComparer.Ordinal);

        // IT NAMES THE FORM'S OWN BUTTON, verbatim, and the name is read off that button rather than retyped:
        // the string lives in two files and this is what notices them drifting apart.
        cut.Find("button.worklist-row").Click();
        var granter = cut.Find("button.record-folder");
        Assert.Contains(granter.TextContent.Trim(), sentence, StringComparison.Ordinal);

        // The retry still renders -- the API is there, so a grant can still happen -- but it is no longer the
        // whole instruction.
        Assert.Single(cut.FindAll("button.try-again"));
    }

    /// <summary>
    /// A retry that SUCCEEDS removes the button the owner was standing on, and must therefore move focus
    /// outright. Nothing scheduled the unmoved helper on this path, so without the move focus lands on the body
    /// and the next Tab restarts above the skip link.
    /// </summary>
    /// <remarks>
    /// This branch was unreachable until the store could read on a retry, which is why it shipped unproven:
    /// NeedsAttention is true for every Store other than Read, so every other retry takes the other path.
    /// </remarks>
    [Fact]
    public async Task ARetryThatSucceedsMovesFocusOutrightBecauseTheButtonItWasOnIsGone()
    {
        var entry = new FilmEntry
        {
            IdentityKey = RecordLink.IdentityKeyOf(Uncertain),
            Records = [new RecordOverride(TmdbId: 807, Runtime: Uncertain.Rt)],
        };

        var (context, source) = Page(
            storeText: RecordStoreReader.Write(RecordStore.Empty with { Films = [entry] }));
        await using var _ = context;
        context.JSInterop.SetupVoid("Blazor._internal.domWrapper.focus", _ => true).SetVoidResult();

        // Fail first, so there is a retry to press at all.
        source.State = CatalogueState.Failed;
        var cut = EditorPage.Render(context);
        cut.WaitForState(() => cut.Find(".catalog-state p").TextContent.Contains("could not be loaded"));

        source.State = CatalogueState.Ready;
        source.Movies = [Uncertain];
        cut.Find("button.try-again").Click();
        cut.WaitForState(() => cut.FindAll("ul.worklist").Count == 1);

        // Landed healthy: the store read, so the retry is gone and nothing needs attention.
        Assert.Equal($"The library holds 1 records.", cut.Find(".catalog-state p").TextContent.Trim());
        Assert.Empty(cut.FindAll("button.try-again"));

        // THE LAST focus call, and not merely some call: focusIfUnmoved legitimately ran on the FIRST render,
        // where the catalogue had failed and the retry button was still there to stand on. What this asserts is
        // that the RETRY's move was the outright one -- the control that had focus no longer exists, so there is
        // nothing to ask the unmoved helper about.
        var focusCalls = context.JSInterop.Invocations
            .Select(i => i.Identifier)
            .Where(id => id is "Blazor._internal.domWrapper.focus" or "movieLibrary.focusIfUnmoved")
            .ToList();

        Assert.Equal("Blazor._internal.domWrapper.focus", focusCalls[^1], StringComparer.Ordinal);
        Assert.Equal("movieLibrary.focusIfUnmoved", focusCalls[0], StringComparer.Ordinal);
    }

    [Fact]
    public async Task WhenNoStoreWasReadItSaysSoOffersTheRetryAndMakesNoStrongerClaim()
    {
        var (context, source) = Page(storeText: null);
        await using var _ = context;
        source.Movies = [Uncertain];

        var cut = EditorPage.Render(context);
        cut.WaitForState(() => cut.FindAll("ul.worklist").Count == 1);

        var sentence = cut.Find(".catalog-state p").TextContent;

        // THE FOLDER-GRANTED ARM: this harness answers "MovieLibrary" to status, so the retry can actually
        // succeed and the retry is the whole instruction. The no-folder arm names the form's own grant button
        // instead, because there the retry is a loop.
        Assert.Equal(RecordEditor.NoStoreReadWithFolder("MovieLibrary"), sentence.Trim());
        Assert.Single(cut.FindAll("button.try-again"));

        // The script answers null for "absent" and for "unreadable" alike, so the page may say what was not
        // read and must not say there is no store.
        foreach (var claim in new[] { "no store yet", "does not exist", "not there", "missing", "no record store exists" })
            Assert.DoesNotContain(claim, sentence, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AStoreTheReaderRefusesIsReportedWithItsOwnMessageAndNothingIsListed()
    {
        var (context, source) = Page("{ not json");
        await using var _ = context;
        source.Movies = [Uncertain];
        var expected = Assert.Throws<RecordStoreException>(() => RecordStoreReader.Read("{ not json", RecordStoreReader.DefaultFileName));

        var cut = EditorPage.Render(context);
        cut.WaitForState(() => cut.Find(".catalog-state p").TextContent.Contains(expected.Message));

        Assert.Empty(cut.FindAll("ul.worklist"));
        Assert.Single(cut.FindAll("button.try-again"));
    }

    [Fact]
    public async Task ACorrectionThatBelongsToNothingIsNamedByCountAndKeyAndNotLostInSilence()
    {
        var stray = new FilmEntry { IdentityKey = new string('c', 40), Records = [new RecordOverride(TmdbId: 999, Runtime: 100)] };
        var (context, source) = Page(StoreOf(stray));
        await using var _ = context;
        source.Movies = [Uncertain];

        var cut = EditorPage.Render(context);
        cut.WaitForState(() => cut.FindAll("ul.worklist").Count == 1);

        var sentence = cut.Find(".catalog-state p").TextContent;
        Assert.Contains("1 correction", sentence, StringComparison.Ordinal);
        Assert.Contains(stray.IdentityKey, sentence, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheStateContainerHoldsProseAndAButtonAndNothingThatSpeaksOnItsOwn()
    {
        var (context, source) = Page(storeText: null);
        await using var _ = context;
        source.Movies = [Uncertain];

        var cut = EditorPage.Render(context);
        cut.WaitForState(() => cut.FindAll("ul.worklist").Count == 1);

        var container = cut.Find(".catalog-state");
        Assert.Equal(["p", "button"], container.Children.Select(c => c.LocalName).ToArray());
        Assert.Empty(container.QuerySelectorAll("[role], [aria-live]"));
        Assert.False(container.HasAttribute("role"));
        Assert.False(container.HasAttribute("aria-live"));
    }
}

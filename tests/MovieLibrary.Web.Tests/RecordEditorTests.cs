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
    private sealed class Env : IWebAssemblyHostEnvironment
    {
        public string Environment => "Development";
        public string BaseAddress => "http://localhost/";
    }

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
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var module = context.JSInterop.SetupModule(LocalDirectory.ModulePath);
        module.Setup<bool>("supported").SetResult(true);
        module.Setup<string?>("reconfirm").SetResult(folder);
        module.Setup<string?>("readText", RecordStoreReader.DefaultFileName).SetResult(storeText);

        var source = new HeldSource();
        context.Services
            .AddSingleton<IWebAssemblyHostEnvironment>(new Env())
            .AddSingleton<ICatalogueSource>(source)
            .AddSingleton<IStatusRegion>(new EditorShellTests.SilentRegion())
            .AddSingleton(TimeProvider.System)
            .AddScoped<LocalDirectory>()
            .AddScoped<EditorCatalogue>();
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

        var cut = context.Render<RecordEditor>();
        Assert.Equal(RecordEditor.LoadingSentence, cut.Find(".catalog-state p").TextContent.Trim());

        source.Hold.SetResult();
        cut.WaitForState(() => cut.Find(".catalog-state p").TextContent.Contains("could not be loaded"));
        Assert.Equal(RecordEditor.CatalogueFailedSentence, cut.Find(".catalog-state p").TextContent.Trim());

        source.State = CatalogueState.Ready;
        source.Movies = [Uncertain];
        cut.Find("button.try-again").Click();
        cut.WaitForState(() => cut.FindAll("ul.worklist").Count == 1);
        Assert.Equal(RecordEditor.NoStoreReadSentence, cut.Find(".catalog-state p").TextContent.Trim());

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

        var cut = context.Render<RecordEditor>();
        cut.WaitForState(() => cut.FindAll("button.try-again").Count == 1);

        Assert.Empty(cut.FindAll("ul.worklist"));
        Assert.Empty(cut.FindAll("search"));
    }

    [Fact]
    public async Task WhenNoStoreWasReadItSaysSoOffersTheRetryAndMakesNoStrongerClaim()
    {
        var (context, source) = Page(storeText: null);
        await using var _ = context;
        source.Movies = [Uncertain];

        var cut = context.Render<RecordEditor>();
        cut.WaitForState(() => cut.FindAll("ul.worklist").Count == 1);

        var sentence = cut.Find(".catalog-state p").TextContent;
        Assert.Equal(RecordEditor.NoStoreReadSentence, sentence.Trim());
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

        var cut = context.Render<RecordEditor>();
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

        var cut = context.Render<RecordEditor>();
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

        var cut = context.Render<RecordEditor>();
        cut.WaitForState(() => cut.FindAll("ul.worklist").Count == 1);

        var container = cut.Find(".catalog-state");
        Assert.Equal(["p", "button"], container.Children.Select(c => c.LocalName).ToArray());
        Assert.Empty(container.QuerySelectorAll("[role], [aria-live]"));
        Assert.False(container.HasAttribute("role"));
        Assert.False(container.HasAttribute("aria-live"));
    }
}

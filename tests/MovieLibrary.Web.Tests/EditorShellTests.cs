using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using MovieLibrary.Web.Editor;
using MovieLibrary.Web.Editor.Pages;
using MovieLibrary.Web.Editor.Store;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// The editor's shell, rendered directly rather than through the router. Directly, because MovieLibrary.Web
/// references this project only in Debug, so a router-driven test would fail in a Release test run -- and
/// because the four-address contract in MarkupContractTests is the set of addresses the SITE serves, which
/// this page is deliberately not one of.
/// </summary>
public sealed class EditorShellTests
{
    /// <summary>An empty, ready catalogue: the page has a source to ask, and nothing in it to list.</summary>
    internal sealed class EmptySource : ICatalogueSource
    {
        public Task<(CatalogueState State, IReadOnlyList<MovieLibrary.Core.Models.MovieRecord> Movies)> MoviesAsync() =>
            Task.FromResult((CatalogueState.Ready, (IReadOnlyList<MovieLibrary.Core.Models.MovieRecord>)[]));

        public Task<(CatalogueState State, MovieLibrary.Core.Models.CatalogMeta? Meta)> MetaAsync() =>
            Task.FromResult((CatalogueState.Ready, (MovieLibrary.Core.Models.CatalogMeta?)null));
    }

    internal sealed class SilentRegion : IStatusRegion
    {
        public void Status(string text) { }

        public void Alert(string text) { }
    }

    private static BunitContext Context(string environment)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IWebAssemblyHostEnvironment>(EditorPage.Environment(environment));
        context.Services.AddSingleton(TimeProvider.System);
        context.Services.AddRecordEditor<EmptySource, SilentRegion>();
        return context;
    }

    [Fact]
    public void TheEditorIsRoutedAtTheOneAddressAndNowhereElse()
    {
        var routes = typeof(RecordEditor).GetCustomAttributes(typeof(RouteAttribute), false)
            .Cast<RouteAttribute>().Select(r => r.Template).ToList();

        Assert.Equal(["/editor"], routes);
    }

    [Fact]
    public void TheRouteSetTheSiteServesDoesNotGrow()
    {
        // AppUnderTest.RouteTemplates() reads typeof(Movies).Assembly, so this page is invisible to it by
        // construction. If this fails, a route has appeared in the MovieLibrary.Web assembly itself, and the
        // four-address contract is about to assert something about a different set of addresses than the one
        // it was written for. That is not the same claim as the editor having been compiled into the web
        // assembly: a route added for any other reason fails here too. The claim that no editor assembly
        // reaches a published payload is pinned by EditorContractTests, over the web project's csproj.
        Assert.DoesNotContain("/editor", AppUnderTest.RouteTemplates());
    }

    [Fact]
    public async Task OutsideDevelopmentThePageRendersOneSentenceAndNoEditor()
    {
        await using var context = Context("Production");

        var cut = context.Render<RecordEditor>();

        Assert.Equal(RecordEditor.NotLocalSentence, cut.Find("p").TextContent.Trim());
        Assert.Empty(cut.FindAll("ul"));
    }

    // The other half of the guard. OutsideDevelopmentThePageRendersOneSentenceAndNoEditor proves the page
    // refuses when it is not local; without this, a Local that is always false would pass that test and the
    // page would never open on the owner's own machine at all.
    [Fact]
    public async Task InDevelopmentThePageShowsItsOwnContentAndNotTheRefusal()
    {
        await using var context = Context("Development");

        var cut = context.Render<RecordEditor>();

        Assert.DoesNotContain(RecordEditor.NotLocalSentence, cut.Markup, StringComparison.Ordinal);
        Assert.Equal(RecordEditor.NoStoreReadSentence, cut.Find(".catalog-state p").TextContent.Trim());
    }

    [Fact]
    public async Task TheHeadingIsThereInEveryEnvironmentAndIsFocusable()
    {
        foreach (var environment in new[] { "Development", "Production" })
        {
            await using var context = Context(environment);

            var cut = context.Render<RecordEditor>();
            var headings = cut.FindAll("h1");

            Assert.Single(headings);
            Assert.Equal(RecordEditor.Heading, headings[0].TextContent.Trim());
            Assert.Equal("-1", headings[0].GetAttribute("tabindex"));
        }
    }

    [Fact]
    public void NoFileInTheEditorDeclaresALiveRegionOrNavigates()
    {
        // The same rules SourceContractTests applies to MovieLibrary.Web, applied to the new project, because
        // the editor "is exempt from nothing". SourceContractTests now walks the editor's .razor files too
        // (WebRoot.FilesWithEditor), so this is not the only thing holding them. It stays because it is
        // not subsumed: it also covers the editor's .cs files, and it checks role="status" and the Core
        // band literals, which the contract walk does not.
        var files = WebRoot.EditorFiles("*.razor", "*.cs").ToList();

        Assert.True(files.Count >= 14,
            "This rule found " + files.Count + " editor source files. Fourteen were counted when it was "
            + "written: four .razor and ten .cs. Fewer means the glob stopped finding them, not that the "
            + "editor shrank, and a rule that loops over nothing passes without checking anything.");

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("aria-live", text, StringComparison.Ordinal);
            Assert.DoesNotContain("role=\"alert\"", text, StringComparison.Ordinal);
            Assert.DoesNotContain("role=\"status\"", text, StringComparison.Ordinal);
            Assert.DoesNotContain("NavigateTo(", text, StringComparison.Ordinal);
            Assert.DoesNotContain("<footer", text, StringComparison.Ordinal);

            // A12, as a test and not a shell command that proves it once: the band's words are Core's, in
            // every file of this project, comments included. Case-insensitive on purpose: a lower-case copy
            // slips past a case-sensitive check while the drift it exists to stop is already there -- a count
            // sentence reading one way beside a badge reading another is two names for one band.
            foreach (var literal in new[] { "Not matched", "Needs review" })
            {
                Assert.DoesNotContain(literal, text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}

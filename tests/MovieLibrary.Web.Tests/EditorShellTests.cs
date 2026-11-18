using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using MovieLibrary.Web.Editor.Pages;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// The editor's shell, rendered directly rather than through the router. Directly, because MovieLibrary.Web
/// references this project only in Debug, so a router-driven test would fail in a Release test run -- and
/// because the four-address contract in MarkupContractTests is the set of addresses the SITE serves, which
/// this page is deliberately not one of.
/// </summary>
public sealed class EditorShellTests
{
    private sealed class Env(string environment) : IWebAssemblyHostEnvironment
    {
        public string Environment { get; } = environment;
        public string BaseAddress => "http://localhost/";
    }

    private static BunitContext Context(string environment)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IWebAssemblyHostEnvironment>(new Env(environment));
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
        // The whole point of the separate assembly: AppUnderTest.RouteTemplates() reads typeof(Movies).Assembly,
        // so this page is invisible to it. If this fails, the editor has been compiled into MovieLibrary.Web and
        // the four-address contract is about to assert something false about the published site.
        Assert.DoesNotContain("/editor", AppUnderTest.RouteTemplates());
    }

    [Fact]
    public void OutsideDevelopmentThePageRendersOneSentenceAndNoEditor()
    {
        using var context = Context("Production");

        var cut = context.Render<RecordEditor>();

        Assert.Equal(RecordEditor.NotLocalSentence, cut.Find("p").TextContent.Trim());
        Assert.Empty(cut.FindAll("ul"));
    }

    [Fact]
    public void TheHeadingIsThereInEveryEnvironmentAndIsFocusable()
    {
        foreach (var environment in new[] { "Development", "Production" })
        {
            using var context = Context(environment);

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
        // the editor "is exempt from nothing". Task 6 folds this into the real contract walk; this is the floor
        // that stops the first file breaking it.
        var files = WebRoot.EditorFiles("*.razor", "*.cs").ToList();

        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("aria-live", text, StringComparison.Ordinal);
            Assert.DoesNotContain("role=\"alert\"", text, StringComparison.Ordinal);
            Assert.DoesNotContain("role=\"status\"", text, StringComparison.Ordinal);
            Assert.DoesNotContain("NavigateTo(", text, StringComparison.Ordinal);
            Assert.DoesNotContain("<footer", text, StringComparison.Ordinal);
        }
    }
}

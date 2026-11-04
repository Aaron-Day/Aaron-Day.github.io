using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// index.html is hand-written and outside Blazor, so no component test sees it. These pin what the Task 2 brief
/// decided about the boot block and the error bar, and what it said must not be touched.
/// </summary>
public class HostPageTests
{
    private static readonly IDocument Doc = new HtmlParser().ParseDocument(WebRoot.Read("wwwroot", "index.html"));

    [Fact]
    public void TheDocumentHasARealTitle()
    {
        Assert.Equal("Film and TV library", Doc.Title);
    }

    [Fact]
    public void TheColourSchemeIsDeclaredSoPhonesDoNotReInvertTheDarkPage()
    {
        Assert.Equal("dark", Doc.QuerySelector("meta[name='color-scheme']")?.GetAttribute("content"));
    }

    [Fact]
    public void TheThingsTheBriefSaidToKeepAreKept()
    {
        Assert.Equal("/", Doc.QuerySelector("base")?.GetAttribute("href"));
        Assert.Equal("noindex", Doc.QuerySelector("meta[name='robots']")?.GetAttribute("content"));
        // No maximum-scale, user-scalable=no or viewport-fit=cover: they break zoom and reflow.
        Assert.Equal("width=device-width, initial-scale=1.0", Doc.QuerySelector("meta[name='viewport']")?.GetAttribute("content"));
    }

    [Fact]
    public void NothingIsMarkedBusy()
    {
        // The runtime replaces the children of #app and never touches its attributes, so aria-busy would be
        // permanent, on the ancestor of every live region the app adds.
        Assert.Empty(Doc.QuerySelectorAll("[aria-busy]"));
    }

    [Fact]
    public void TheBootBlockAnnouncesOnceAndItsDecorationIsHidden()
    {
        var app = Doc.GetElementById("app")!;

        var status = Assert.Single(app.QuerySelectorAll("[role='status']"));
        Assert.Equal("Loading library", status.TextContent.Trim());
        Assert.Contains("visually-hidden", status.ClassList);
        Assert.Equal("true", app.QuerySelector("svg.loading-progress")?.GetAttribute("aria-hidden"));
        Assert.Equal("true", app.QuerySelector(".loading-progress-text")?.GetAttribute("aria-hidden"));
        Assert.NotNull(app.QuerySelector("noscript"));
    }

    [Fact]
    public void TheErrorBarIsAnAlertWithARealDismissButtonAndALayoutWrapper()
    {
        var bar = Doc.GetElementById("blazor-error-ui")!;

        Assert.Equal("alert", bar.GetAttribute("role"));
        // The runtime reveals the bar with an inline display:block, so layout lives on the inner wrapper.
        var inner = bar.QuerySelector(".error-ui-inner")!;
        Assert.NotNull(inner.QuerySelector("a.reload"));
        var dismiss = inner.QuerySelector("button.dismiss")!;
        Assert.Equal("button", dismiss.GetAttribute("type"));
        Assert.Equal("Dismiss error", dismiss.GetAttribute("aria-label"));
        Assert.DoesNotContain("\U0001F5D9", bar.TextContent);   // tofu on phones with patchy font coverage
    }

    [Fact]
    public void TheAttributionFooterIsTheOnlyContentinfo()
    {
        var footer = Assert.Single(Doc.QuerySelectorAll("footer"));
        Assert.Contains("attribution", footer.ClassList);
        Assert.Null(Doc.GetElementById("app")!.QuerySelector("footer"));
        Assert.Empty(footer.QuerySelectorAll("h1, h2, h3, h4, h5, h6"));
    }
}

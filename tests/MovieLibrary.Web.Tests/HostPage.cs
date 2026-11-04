using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace MovieLibrary.Web.Tests;

/// <summary>index.html parsed once, and the boot watchdog's source taken from it as it will ship.</summary>
internal static class HostPage
{
    public static readonly IDocument Doc = new HtmlParser().ParseDocument(WebRoot.Read("wwwroot", "index.html"));

    public static IElement WatchdogElement() =>
        Assert.Single(Doc.QuerySelectorAll("script:not([src])"), s => s.TextContent.Contains("unhandledrejection"));

    public static string WatchdogSource() => WatchdogElement().TextContent;
}

using System.Text.RegularExpressions;

namespace MovieLibrary.Web.Tests;

/// <summary>Interfaces later tasks extend, pinned as source text because a compiler will not notice them breaking.</summary>
public class SourceContractTests
{
    [Fact]
    public void MainLayoutHasExactlyOneCodeBlockForTask3ToExtend()
    {
        var razor = WebRoot.Read("Layout", "MainLayout.razor");

        Assert.Single(Regex.Matches(razor, @"@code\s*\{"));
    }

    [Fact]
    public void ProgramRegistersTheAnnouncerBecauseTheTestsRegisterTheirOwn()
    {
        // Every bUnit test that renders MainLayout adds the announcer itself, so a missing line here would pass
        // the whole suite and then fail at boot with a DI error. This is the only check on that line.
        Assert.Matches(@"builder\.Services\.AddScoped<Announcer>\(\);", WebRoot.Read("Program.cs"));
    }

    [Fact]
    public void MainLayoutOwnsTheSubscriptionAndMountsTheRouteAnnouncer()
    {
        var razor = WebRoot.Read("Layout", "MainLayout.razor");

        Assert.Contains("@implements IDisposable", razor, StringComparison.Ordinal);
        Assert.Contains("@inject Announcer Announcer", razor, StringComparison.Ordinal);
        Assert.Contains("<RouteAnnouncer />", razor, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(razor, @"@code\s*\{"));
    }

    [Fact]
    public void TheSkipLinkDoesNotRelyOnItsOwnHref()
    {
        var razor = WebRoot.Read("Layout", "MainLayout.razor");

        Assert.Contains("@onclick=\"SkipToMain\" @onclick:preventDefault", razor, StringComparison.Ordinal);
    }

    [Fact]
    public void FocusOnNavigateIsScopedToTheApp()
    {
        Assert.Contains("Selector=\"#app h1\"", WebRoot.Read("App.razor"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("App.razor")]
    [InlineData("Layout/MainLayout.razor")]
    [InlineData("Layout/NavMenu.razor")]
    [InlineData("Layout/RouteAnnouncer.razor")]
    [InlineData("Pages/Movies.razor")]
    [InlineData("Pages/NotFound.razor")]
    public void NoComponentRedirectsAndNoFooterIsAddedInsideTheApp(string file)
    {
        var text = WebRoot.Read(file.Split('/'));

        Assert.DoesNotContain("RedirectTo", text, StringComparison.Ordinal);
        Assert.DoesNotContain("NavigateTo(", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<footer", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheWebProjectNeverReferencesTheEnrichProject()
    {
        // Enrich reads TMDB_TOKEN. Referencing it from the browser project would put that code in the payload.
        Assert.DoesNotContain("MovieLibrary.Enrich", WebRoot.Read("MovieLibrary.Web.csproj"), StringComparison.Ordinal);
    }
}

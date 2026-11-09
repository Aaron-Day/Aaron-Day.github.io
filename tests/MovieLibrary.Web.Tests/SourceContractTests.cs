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
        // the whole suite and then fail at boot with a DI error. This is the only check on that line, so it has
        // to be anchored: an unanchored pattern matches the line commented out too, which is exactly how a
        // registration gets lost. Proven below against the commented form rather than left to inspection.
        const string registration = @"(?m)^[ \t]*builder\.Services\.AddScoped<Announcer>\(\);";

        Assert.Matches(registration, WebRoot.Read("Program.cs"));
        Assert.DoesNotMatch(registration, "// builder.Services.AddScoped<Announcer>();");
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

    [Fact]
    public void MainLayoutAnchorsTheSettleDelayToEveryRenderAndNotJustTheFirst()
    {
        // Source text, because bUnit cannot tell the wiring from its absence: its renderer commits in the same
        // clock instant as the call that asked for the render, so a re-armed timer and an un-armed one fall due
        // together. AnnouncerTests drives RenderCommitted itself for the behaviour this line buys.
        var razor = WebRoot.Read("Layout", "MainLayout.razor");

        Assert.Matches(@"OnAfterRender\(bool firstRender\)\s*=>\s*Announcer\.RenderCommitted\(\);", razor);
        Assert.DoesNotMatch(@"if\s*\(\s*firstRender\s*\)", razor);
    }

    [Fact]
    public void TheAlertRegionIsTheOnlyAssertiveRegionInAnyComponent()
    {
        // LiveRegionTests only renders MainLayout with a stub body, so a role="alert" added by a page or a child
        // component is invisible to it. The likeliest violation is a later task marking the VISIBLE failed-state
        // element role="alert" -- which it must not, because the hidden alert region already speaks that failure
        // and the user would hear it twice, from two assertive regions, in an order nobody controls.
        //
        // What this cannot catch: an assertive region built from a variable, a computed attribute, a splatted
        // dictionary, or markup that arrives from a .cs component. It turns "a reviewer has to notice" into a red
        // test for the ordinary spelling, which is the point; it is not proof.
        foreach (var (path, text) in WebRoot.Files("*.razor"))
        {
            var assertive = new[] { "role=\"alert\"", "role='alert'", "aria-live=\"assertive\"", "aria-live='assertive'" }
                .Where(marker => text.Contains(marker, StringComparison.Ordinal))
                .ToArray();

            if (path == "Layout/MainLayout.razor") Assert.Equal(new[] { "role=\"alert\"" }, assertive);
            else Assert.Empty(assertive);
        }
    }

    [Fact]
    public void NoCodePathRaisesAnAlertAndThenRethrows()
    {
        // catch { Announcer.Alert(..); throw; } is the shape that makes two assertive regions speak at once: the
        // hidden alert region for the handled failure, and #blazor-error-ui for the exception that got away. A
        // code path does one or the other.
        //
        // What this cannot catch: a rethrow further from the Alert call than one statement, a rethrow in a
        // different method up the stack, or an exception that escapes without the word throw. The canonical
        // spelling is what it pins.
        foreach (var (path, text) in WebRoot.Files("*.cs", "*.razor"))
            Assert.DoesNotMatch(@"Alert\([^;]*\);\s*throw\b", text);
    }

    [Fact]
    public void TheRouteRegionHasExactlyOneCallerAndItIsTheRouteAnnouncer()
    {
        // Announcer.Route is internal, so nothing in the web project stops a second component announcing a route
        // name of its own. The design intends one caller; until now nothing said so. A second caller would give
        // #route-announcer two owners and the latest-wins rule would silently drop one of them.
        //
        // What this cannot catch: a caller that reaches the method through a differently named expression than
        // ".Route(", or through reflection.
        var callers = WebRoot.Files("*.cs", "*.razor")
            .Where(f => Regex.IsMatch(f.Text, @"\.Route\s*\("))
            .Select(f => f.Path)
            .ToArray();

        Assert.Equal(new[] { "Layout/RouteAnnouncer.razor" }, callers);
    }

    [Fact]
    public void ProgramRegistersTheCatalogServiceBecauseTheTestsRegisterTheirOwn()
    {
        // The same hole as the announcer's, for the same reason: every bUnit test registers its own
        // CatalogService through AnnouncerTestExtensions.AddAnnouncer, so a missing line here would pass all
        // 315 tests and then fail at boot with a DI error on the first page render -- the films page, which is
        // "/" . Anchored for the same reason too: an unanchored pattern matches the line commented out, which is
        // exactly how a registration gets lost. Proven below against the commented form.
        // CatalogServiceTests.TheRegistrationShapeProgramUsesResolves is the other half: that this line, with
        // the HttpClient Program.cs already registers, actually resolves.
        const string registration = @"(?m)^[ \t]*builder\.Services\.AddScoped<CatalogService>\(\);";

        Assert.Matches(registration, WebRoot.Read("Program.cs"));
        Assert.DoesNotMatch(registration, "// builder.Services.AddScoped<CatalogService>();");
    }
}

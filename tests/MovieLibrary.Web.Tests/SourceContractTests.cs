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
    public void TheCoreProjectCarriesNoReferencesOfItsOwn()
    {
        // MovieLibrary.Web.csproj's comment beside its one ProjectReference says "Core carries ZERO package
        // references, so this adds only its own IL to the payload and no transitive dependency". That was
        // prose with nothing behind it, and it is the claim that makes the web project's first ever
        // ProjectReference cheap: a PackageReference added to Core comes into the browser payload with it,
        // transitively and without anyone deciding to, and a ProjectReference brings a whole project.
        //
        // What this cannot catch: a dependency arriving through Directory.Build.props, a central package
        // management file, or an SDK that is not Microsoft.NET.Sdk. It pins the ordinary spelling.
        var core = WebRoot.ReadSibling("MovieLibrary.Core", "MovieLibrary.Core.csproj");

        Assert.DoesNotContain("PackageReference", core, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectReference", core, StringComparison.Ordinal);
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

    // ================================================================== live regions in page content

    /// <summary>The one component allowed to declare a live region, because it owns all three of them.</summary>
    private const string LiveRegionOwner = "Layout/MainLayout.razor";

    /// <summary>
    /// Every live-region marker a component's source declares: an alert, status or log ROLE, or an aria-live
    /// attribute whatever its value. Returned as the text found, so a failure can quote it.
    /// </summary>
    /// <remarks>
    /// A pure function over text it is handed, proven below against markup written for the purpose, because
    /// the contract that uses it runs over an app where the correct answer is "none" everywhere but one file:
    /// breaking the assertion inside that loop would still pass if the scan silently stopped matching.
    /// <para>
    /// aria-live="off" is reported too. It is not a live region, but it is not something a page should be
    /// declaring either, and a rule that let it through would let a typo through with it.
    /// </para>
    /// <para>
    /// What it cannot catch: a role or an aria-live built from a variable, a computed attribute, a splatted
    /// dictionary, or markup that arrives from a .cs component. It turns "a reviewer has to notice" into a red
    /// test for the ordinary spelling. It is not proof.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<string> LiveRegionMarkers(string text) =>
        AllLiveRegionMarkers(text).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>
    /// Every live-region marker, duplicates included, so a count of regions can be asserted rather than a set
    /// of spellings.
    /// </summary>
    /// <remarks>
    /// The distinct set cannot count regions, and the layout's carve-out needs a count. A fourth region added
    /// to MainLayout that REUSED an existing marker -- a second <c>role="status"</c>, by far the likeliest
    /// accidental addition, because it is what a second polite announcement looks like -- collapses into the
    /// same three-element set and passes.
    /// </remarks>
    internal static IReadOnlyList<string> AllLiveRegionMarkers(string text) =>
        LiveRegionMarker.Matches(text).Select(m => m.Value).ToList();

    // The leading boundary stops data-aria-live and the like matching. Requiring the closing quote right
    // after the role word stops role="alertdialog" matching, which is a dialog and not a live region.
    private static readonly Regex LiveRegionMarker = new(
        @"(?<![\w-])(?:role\s*=\s*([""'])(?:alert|status|log)\1|aria-live\s*=\s*([""'])[^""']*\2)",
        RegexOptions.IgnoreCase);

    /// <remarks>
    /// SC 4.1.3 Status Messages, Level AA, and rule R2. The rule R2 cited a test that did not exist: the
    /// assertive scan above looks only for an alert role and an assertive aria-live, and LiveRegionTests
    /// renders MainLayout with a STUB body, so a status role on the films page's visible loading paragraph
    /// passed all 295 tests before this. That is the likeliest violation by far, because it looks helpful: it
    /// would be a second utterance for the same words, from a region the announcer does not own, and on a
    /// repeated message the two would race in an order nobody controls.
    /// </remarks>
    [Fact]
    public void NoComponentOutsideTheLayoutDeclaresALiveRegion()
    {
        var files = WebRoot.Files("*.razor").ToList();
        var failable = files.Where(f => f.Path != LiveRegionOwner).ToList();

        // T1: the count is of what the rule can actually fail on, which is every .razor file except the one
        // carve-out. A discovery walk that stopped matching would otherwise loop over nothing and pass.
        //
        // EIGHT, counted rather than guessed: App, Layout/NavMenu, Layout/RouteAnnouncer, Pages/Movies,
        // Pages/NotFound, _Imports and, since Task 6, Components/PosterTile and Components/PosterGrid.
        // _Imports is in the count because the rule can genuinely fail on it -- this is a text scan, and a
        // status role typed into it would be found and reported like any other. The floor was 4 when there
        // were six, which let two components fall out of WebRoot.Files before the rule noticed: the T1
        // failure mode exactly. Tasks 7 to 9 only add components, so this floor only rises.
        //
        // Task 6 is the first task to put real content inside main, which is the case rule R2's gate was
        // written for, and the gate was already here when it arrived: a count sentence and a pager are
        // exactly the markup someone would reach for a polite region with, and they are plain text in both
        // components. Each of the two says so in its own comment, spelled in words, because this scan is a
        // substring match and a comment quoting a marker fails just as the attribute would.
        Assert.True(failable.Count >= 8,
            "This rule found " + failable.Count + " components it could fail on. There are eight "
            + "hand-written .razor files outside the layout's own MainLayout. Fewer means WebRoot.Files "
            + "stopped finding them, not that the app shrank -- and a rule with no subjects passes over "
            + "nothing.");

        // Named as well as counted, for the same reason the motion rule names its stylesheets: a floor can
        // be met by the wrong files. These two are the first content inside main, so they are the subjects
        // this gate was written for, and a path refactor that dropped them out of the walk would otherwise
        // leave the count intact.
        var paths = failable.Select(f => f.Path).ToList();

        Assert.Contains("Components/PosterTile.razor", paths);
        Assert.Contains("Components/PosterGrid.razor", paths);
        Assert.Contains("Pages/Movies.razor", paths);

        foreach (var (path, text) in failable)
            Assert.True(LiveRegionMarkers(text).Count == 0,
                path + " declares " + string.Join(", ", LiveRegionMarkers(text)) + ". The announcer in "
                + LiveRegionOwner + " owns every live region in this app. A second one in page content is a "
                + "second utterance of the same words from a region nothing coordinates, and the two race in "
                + "an order nobody controls. Visible loading, count and failure text is plain text; the "
                + "spoken copy goes through Announcer.Status or Announcer.Alert.");

        // And the carve-out is not a hole. The layout owns exactly three regions and may not grow a fourth
        // without this saying so -- which needs the COUNT as well as the set, because the set is distinct:
        // a second role="status" beside the first is the likeliest fourth region and would not change it.
        var owner = files.Single(f => f.Path == LiveRegionOwner).Text;

        Assert.Equal(
            new[] { "aria-live=\"polite\"", "role=\"alert\"", "role=\"status\"" },
            LiveRegionMarkers(owner).OrderBy(m => m, StringComparer.Ordinal));
        Assert.Equal(3, AllLiveRegionMarkers(owner).Count);
    }

    [Theory]
    [InlineData("<p role=\"status\">Loading films</p>", "role=\"status\"")]
    [InlineData("<p role='status'>Loading films</p>", "role='status'")]
    [InlineData("<div role=\"log\">a line</div>", "role=\"log\"")]
    [InlineData("<div role = \"alert\">x</div>", "role = \"alert\"")]
    [InlineData("<div aria-live=\"polite\">x</div>", "aria-live=\"polite\"")]
    [InlineData("<div aria-live=\"assertive\">x</div>", "aria-live=\"assertive\"")]
    [InlineData("<div aria-live=\"off\">x</div>", "aria-live=\"off\"")]
    public void TheLiveRegionScanSeesTheMarker(string markup, string expected)
        => Assert.Equal(new[] { expected }, LiveRegionMarkers(markup));

    /// <remarks>
    /// T4 on the counting scan, which is the one thing the distinct set cannot do: two regions that share a
    /// spelling are two regions. If this collapsed them the layout's three-region carve-out would accept a
    /// fourth without saying anything.
    /// </remarks>
    [Fact]
    public void TheCountingScanSeesTwoRegionsThatShareASpellingAsTwo()
    {
        const string markup = "<div role=\"status\">one</div><div role=\"status\">two</div>";

        Assert.Equal(2, AllLiveRegionMarkers(markup).Count);
        Assert.Single(LiveRegionMarkers(markup));   // the set collapses them, which is why the count exists
    }

    [Theory]
    [InlineData("<p>Loading films</p>")]
    [InlineData("<button type=\"button\" aria-disabled=\"true\">Try again</button>")]
    [InlineData("<div role=\"alertdialog\">a dialog, not a live region</div>")]
    [InlineData("<div role=\"statusbar\">not a role at all, and not a live region either</div>")]
    [InlineData("<div data-aria-live=\"polite\">someone else's data attribute</div>")]
    [InlineData("<div aria-atomic=\"true\">atomic without live is not a region</div>")]
    public void TheLiveRegionScanDoesNotCryWolf(string markup)
        => Assert.Empty(LiveRegionMarkers(markup));
}

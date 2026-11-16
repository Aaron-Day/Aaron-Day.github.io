using MovieLibrary.Web.A11y.Harness;
using Xunit.Abstractions;

namespace MovieLibrary.Web.A11y;

/// <summary>
/// The one link in Part A that sits inside running prose keeps a non-colour cue in the rendered page.
/// </summary>
/// <remarks>
/// <para>
/// <b>SC 1.4.1 Use of Color, Level A</b> - not AA, and the level matters because this is the only Level A
/// criterion in this file. The criterion itself has no numeric threshold; the 3:1 comes from technique G183,
/// and failure F73 is the exact shape: a link distinguished from the text around it by colour alone
/// needs 3:1 against that text to pass G183, and <c>--accent2</c> (#4da3ff) against
/// <c>--txt</c> (#e8eaf0) is <b>2.18:1</b>. NotFound's "Back to the library" is that shape, so the
/// underline is the only thing making it conform.
/// </para>
/// <para>
/// <b>What this adds to <c>StyleContractTests.AnInProseLinkIsDistinguishedByMoreThanItsColour</c>.</b> That
/// contract greps app.css for <c>a { text-decoration: underline }</c> and bans
/// <c>text-decoration: none</c> across every hand-written stylesheet. Its own remark names what it cannot
/// catch: an inline style, or a Bootstrap utility class in the markup - <c>.text-decoration-none</c> ships
/// in the bundle and would win. Those need the rendered page, and this is it: a computed
/// <c>text-decoration-line</c> read off the element the criterion is about.
/// </para>
/// <para>
/// Both addresses are walked because both render NotFound.razor by different paths - <c>/not-found</c> by
/// its route, <c>/zzz</c> by the router's own NotFoundPage - and a computed style is cheap enough that
/// covering the second costs nothing.
/// </para>
/// </remarks>
[Collection(BrowserCollection.Name)]
public class UseOfColourTests(BrowserGate gate, ITestOutputHelper output)
{
    /// <summary>The in-prose link, selected through the paragraph so that the shape is part of the query.</summary>
    private const string InProse = "main p a[href=\"movies\"]";

    [BrowserTheory]
    [InlineData("/not-found")]
    [InlineData(Site.UnmatchedUrl)]
    public async Task TheInProseLinkKeepsItsUnderlineInTheRenderedPage(string address)
    {
        await using var session = await gate.OpenAsync();
        await session.GotoAsync(address);

        // THE SUBJECT, before anything is said about it, and in two parts. The link has to be there, and it
        // has to be INSIDE A BLOCK OF PROSE - F73 is about a link a reader has to pick out of surrounding
        // text, and a link that is the whole of its paragraph is distinguished by its own position instead.
        // The day the sentence around it goes, this criterion stops applying here and the assertion below
        // would be covering nothing while still passing.
        var paragraph = session.Page.Locator("main p").Filter(
            new Microsoft.Playwright.LocatorFilterOptions { Has = session.Page.Locator("a[href=\"movies\"]") });

        Assert.Equal(1, await paragraph.CountAsync());

        var prose = (await paragraph.InnerTextAsync()).Trim();
        var label = (await session.Page.Locator(InProse).InnerTextAsync()).Trim();

        Assert.True(prose.Length > label.Length + 10,
            $"The link \"{label}\" is {label.Length} characters of a {prose.Length}-character paragraph "
            + $"(\"{prose}\"), so it is not a link inside a block of prose any more. SC 1.4.1 Use of Color "
            + "and technique failure F73 are about a link a reader has to pick out of the text around it; a "
            + "standalone link is distinguished by its own position. This assertion has stopped covering "
            + "anything - decide deliberately whether the underline still has to be pinned here.");

        var link = await session.Page.EvaluateAsync<string[]>(Scripts.InProseLink, InProse);
        var (what, decoration, colour, around) = (link[0], link[1], link[2], link[3]);

        output.WriteLine($"=== {address}: {what} text-decoration-line {decoration}, "
                         + $"{colour} on {around} (2.18:1, measured in app.css) ===");

        Assert.DoesNotContain("no element matches", what);

        Assert.True(decoration != "none",
            $"{what} on {address} computes text-decoration-line: none, so the only thing distinguishing it "
            + $"from the prose around it is colour - and that is {colour} on {around}, which app.css "
            + "measures at 2.18:1, far below the 3:1 technique G183 asks for before colour "
            + "alone may do the job (SC 1.4.1 Use of Color, Level A, sets no number itself). Failure F73. app.css declares the underline itself rather "
            + "than inheriting Bootstrap's so that a dropped stylesheet cannot take it away; an inline "
            + "style or a .text-decoration-none utility in the markup still can, and that is what this "
            + "reads the rendered page for.");

        Assert.Contains("underline", decoration);
    }
}

using MovieLibrary.Web.A11y.Harness;
using Xunit.Abstractions;

namespace MovieLibrary.Web.A11y;

/// <summary>
/// Rotating the device changes nothing about what is on the page.
/// </summary>
/// <remarks>
/// <para>
/// <b>SC 1.3.4 Orientation, Level AA.</b> Its words are that content does not restrict its view and
/// operation to a single display orientation. axe has no rule for it - it cannot have one, because the
/// question is a COMPARISON between two orientations and axe sees one render at a time. bUnit cannot have
/// one either: there is no viewport in a bUnit render, so <c>@media (orientation: portrait)</c> would
/// apply or not apply entirely unobserved.
/// </para>
/// <para>
/// <b>This rotates one page rather than loading two.</b> The criterion is about a reader who turns their
/// device while using the page, so the viewport is changed under a page that is already booted and
/// populated. Two separate loads would miss anything that survives a fresh render and breaks on a resize.
/// </para>
/// <para>
/// <b>The two mechanisms by which an app fails this, and where each is covered.</b> A stylesheet that
/// hides or replaces content behind an orientation media query is caught in text by
/// <c>StyleContractTests.NoStylesheetChangesWhatContentExistsWithTheOrientation</c>; a script that calls
/// <c>screen.orientation.lock</c> is caught by
/// <c>SourceContractTests.NothingLocksTheScreenOrientation</c>. Neither reaches an inline style, a
/// Bootstrap utility class or a layout that simply collapses, and that is what this is for. The two text
/// scans are the cheap early warning; this is the one that measures the rendered result.
/// </para>
/// </remarks>
[Collection(BrowserCollection.Name)]
public class OrientationTests(BrowserGate gate, ITestOutputHelper output)
{
    /// <summary>A small phone, landscape then portrait, with the same pixels in both.</summary>
    /// <remarks>
    /// The same area rotated, so a difference between the two readings is the rotation and not a
    /// different-sized device. Both are below the 48rem breakpoint, so the nav is collapsed in both and
    /// the breakpoint is not a confounder either.
    /// </remarks>
    private const int LongSide = 640;

    private const int ShortSide = 360;

    [BrowserFact]
    public async Task RotatingTheDeviceChangesNeitherTheContentNorTheTileCount()
    {
        await using var session = await gate.OpenAsync(LongSide, ShortSide);
        await session.GotoAsync("/");
        await session.GridAsync();

        var (landscapeText, landscapeTiles) = await ReadAsync(session, "landscape");

        // THE SUBJECT, and the floor rule T1 asks for. Two equal readings of nothing are equal, and an
        // innerText comparison is the easiest assertion in this file to leave comparing "" with "".
        Assert.True(landscapeTiles > 0, "The grid reported no tiles in landscape, so the comparison below "
                                        + "would be between two empty pages.");

        Assert.True(landscapeText.Length > 80,
            $"main#main-content holds {landscapeText.Length} characters of text in landscape, which is too "
            + "little to be the films page. The comparison below would pass over an effectively empty "
            + $"page. Text: \"{landscapeText}\"");

        await session.Page.SetViewportSizeAsync(ShortSide, LongSide);

        var (portraitText, portraitTiles) = await ReadAsync(session, "portrait");

        Assert.True(landscapeTiles == portraitTiles,
            $"The grid holds {landscapeTiles} tile(s) at {LongSide}x{ShortSide} and {portraitTiles} at "
            + $"{ShortSide}x{LongSide}. SC 1.3.4 Orientation (Level AA): rotating the device must not "
            + "change what content exists. A column count that changes with the width is expected and is "
            + "not this - this is tiles appearing or disappearing.");

        Assert.True(landscapeText == portraitText,
            "The text of main#main-content changes when the device is rotated, which is SC 1.3.4 "
            + "Orientation (Level AA): content must not restrict itself to one display orientation. "
            + "Whitespace is already normalised, so this is words gained or lost and not a different "
            + $"wrap. At {LongSide}x{ShortSide}: \"{landscapeText}\". At {ShortSide}x{LongSide}: "
            + $"\"{portraitText}\"");
    }

    /// <summary>
    /// The page's visible text and tile count in the orientation it is expected to be in.
    /// </summary>
    /// <remarks>
    /// The expected orientation is asserted and not merely printed, because this harness CAN report the
    /// wrong one: <c>screen.orientation.type</c> stays <c>landscape-primary</c> however the viewport is
    /// emulated, so without this guard a rotation that never happened would compare two identical readings
    /// and pass. Whitespace is collapsed because the same words wrap differently at a different width, and
    /// a wrap is not a content change.
    /// </remarks>
    private async Task<(string Text, int Tiles)> ReadAsync(PageSession session, string expected)
    {
        var facts = await session.Page.EvaluateAsync<string[]>(Scripts.OrientationFacts);

        output.WriteLine($"=== {facts[1]}: matchMedia says {facts[0]}, screen.orientation says {facts[2]} "
                         + "(which this harness gets wrong, hence the media query) ===");

        Assert.Equal(expected, facts[0]);

        var text = await session.Page.Locator("main#main-content").InnerTextAsync();
        var tiles = await session.Page.EvaluateAsync<int>(Scripts.TileCount);

        output.WriteLine($"  {tiles} tile(s), {text.Length} characters of text");

        return (System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim(), tiles);
    }
}

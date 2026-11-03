using MovieLibrary.Core.Models;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// TMDB's API terms fix the attribution sentence verbatim, and the project keeps it in two places: the catalogue JSON,
/// where <c>CatalogWriter</c> refuses to publish anything whose attribution is not ordinal-equal to
/// <see cref="CatalogMeta.TmdbAttribution"/>, and the site's host page, which is hand-maintained HTML. The JSON copy is
/// tightly guarded and the HTML copy was not guarded at all, so a typo or a well-meaning rewording there was a licence
/// problem nothing in the build would catch. These tests close that asymmetry.
/// </summary>
public class TmdbAttributionTests
{
    /// <summary>The web project's wwwroot, found by walking up to the solution file rather than guessing a depth.</summary>
    private static string WwwRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MovieLibrary.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);   // the test is worthless if it silently cannot find the repo
        return Path.Combine(dir.FullName, "src", "MovieLibrary.Web", "wwwroot");
    }

    [Fact]
    public void TheHostPageCarriesTheAttributionSentenceExactlyAsTheConstantSpellsIt()
    {
        var html = File.ReadAllText(Path.Combine(WwwRoot(), "index.html"));

        // Ordinal, so a curly apostrophe, a non-breaking space or a changed case fails here the way it already fails
        // in CatalogWriter. Once, so the sentence cannot be split across two elements and still look present.
        var occurrences = html.Split(CatalogMeta.TmdbAttribution, StringSplitOptions.None).Length - 1;
        Assert.Equal(1, occurrences);
    }

    /// <summary>
    /// The sentence must be one run of text, not words with markup threaded through them. A <c>&lt;br&gt;</c> or a
    /// <c>&lt;span&gt;</c> inside it still renders the same words, so the test above would still pass, but it breaks
    /// TMDB's wording requirement and splits the sentence into fragments for a screen reader.
    /// </summary>
    [Fact]
    public void TheAttributionSentenceIsOneUnbrokenRunOfText()
    {
        var html = File.ReadAllText(Path.Combine(WwwRoot(), "index.html"));

        // The paragraph's own content, read from the file rather than from the constant: asserting about the
        // constant would be a tautology that no edit to index.html could ever fail.
        var open = html.IndexOf("<p>", html.IndexOf("<footer", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.True(open >= 0, "the footer has no <p> holding the attribution");
        var close = html.IndexOf("</p>", open, StringComparison.Ordinal);
        Assert.True(close > open, "the attribution paragraph is not closed");

        var inner = html[(open + 3)..close].Trim();
        Assert.Equal(CatalogMeta.TmdbAttribution, inner);
    }

    /// <summary>
    /// The terms require the logo to be displayed, so a reference to a file that is not there is as much a licence
    /// problem as a reworded sentence - and it is the kind that only shows up as a broken image on the live site.
    /// </summary>
    [Fact]
    public void TheLogoTheHostPageReferencesExistsAndCanScale()
    {
        var wwwroot = WwwRoot();
        var html = File.ReadAllText(Path.Combine(wwwroot, "index.html"));

        Assert.Contains("src=\"tmdb-logo.svg\"", html);

        var svg = Path.Combine(wwwroot, "tmdb-logo.svg");
        Assert.True(File.Exists(svg), $"{svg} is referenced by index.html but is not in the repository");

        // Without a viewBox an SVG has no intrinsic ratio: it will not scale to the CSS width and the mark distorts.
        Assert.Contains("viewBox", File.ReadAllText(svg), StringComparison.Ordinal);
    }

    /// <summary>
    /// The logo sits beside a sentence that already names TMDB, so its accessible name would be redundant noise -
    /// "TMDB, graphic" followed by the same word three more times. An empty alt prunes it from the accessibility tree.
    /// Omitting alt entirely is the one thing that must not happen: screen readers then announce the file name.
    /// </summary>
    [Fact]
    public void TheLogoHasAnEmptyAltRatherThanNoneAtAll()
    {
        var html = File.ReadAllText(Path.Combine(WwwRoot(), "index.html"));
        var img = html[html.IndexOf("<img src=\"tmdb-logo.svg\"", StringComparison.Ordinal)..];
        img = img[..(img.IndexOf('>') + 1)];

        Assert.Contains("alt=\"\"", img);
    }
}

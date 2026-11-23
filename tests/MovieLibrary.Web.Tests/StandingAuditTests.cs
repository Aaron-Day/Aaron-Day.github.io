using System.Text.RegularExpressions;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// The accessibility audit Task 9 ran by hand, kept as tests so it stays true as Part B adds pages. Every pattern
/// here is also driven against a fragment that breaks it, because a scan whose glob or regex matches nothing
/// passes in silence and that is this project's most repeated defect.
/// </summary>
/// <remarks>
/// <para>
/// What is NOT here, and why. <c>outline: none</c> is not scanned: the one hit is <c>#main-content:focus</c>, the
/// skip link's landing target, a deliberate choice and not a violation. <c>[..</c> is not scanned: it is C#
/// collection-expression spread and range syntax and matches the language instead of the defect.
/// </para>
/// <para>
/// Comments are stripped first. Razor comments (<c>@* *@</c>) and HTML comments quote these very strings when they
/// explain a ruling, and a scan that fails on its own explanation gets deleted.
/// </para>
/// </remarks>
public class StandingAuditTests
{
    private const string InteractiveTag = @"<(?:a|button|input|select|textarea|summary)\b";

    // A tag body is read as quoted strings or non-">" characters, because a Blazor attribute like
    // @onchange="e => Set(e)" has a ">" inside its quotes and a naive [^>]* would stop there and miss a title after it.
    private const string TagBody = @"(?:""[^""]*""|'[^']*'|[^>""'])*?";

    private static readonly (string Rule, Regex Pattern)[] Rules =
    [
        ("a range input: a slider fails SC 2.5.7 Dragging Movements", new(@"\btype\s*=\s*[""']range[""']", RegexOptions.IgnoreCase)),
        ("role=button: use a button", new(@"\brole\s*=\s*[""']button[""']", RegexOptions.IgnoreCase)),
        ("a mouseover handler: hover is not available to touch or keyboard", new(@"@?\bonmouseover\b", RegexOptions.IgnoreCase)),
        ("title= on an interactive element: touch and keyboard users never see it",
            new(InteractiveTag + TagBody + @"\btitle\s*=", RegexOptions.IgnoreCase)),
        ("Substring( : truncation belongs to CSS, which line-clamps", new(@"\bSubstring\s*\(")),
        ("an ellipsis concatenated onto text: truncation belongs to CSS",
            new(@"\+\s*""(?:…|\.\.\.)""|""(?:…|\.\.\.)""\s*\+|\$""[^""]*\}(?:…|\.\.\.)""")),
    ];

    private static readonly Regex Comments = new(@"@\*.*?\*@|<!--.*?-->", RegexOptions.Singleline);

    private static IEnumerable<string> Violations(string text) =>
        Rules.Where(r => r.Pattern.IsMatch(Comments.Replace(text, ""))).Select(r => r.Rule);

    private static IReadOnlyList<(string Path, string Text)> Subjects()
    {
        // The editor is "exempt from nothing", so the walk covers both projects: FilesWithEditor, not Files.
        var files = WebRoot.FilesWithEditor("*.razor", "index.html")
            .Where(f => !f.Path.StartsWith("wwwroot/lib/", StringComparison.Ordinal)
                        && !f.Path.StartsWith(WebRoot.EditorPrefix + "wwwroot/lib/", StringComparison.Ordinal))
            .ToList();

        // The glob has to have found the real files. A count alone survives one being swapped for another.
        var names = files.Select(f => f.Path).ToList();
        Assert.True(files.Count >= 15,
            "The standing accessibility rules found " + files.Count + " files to scan. Fifteen were counted: "
            + "ten .razor files and index.html in the web project, four .razor files in the editor. Fewer "
            + "means WebRoot.FilesWithEditor stopped finding them, and a rule with no subjects passes over "
            + "nothing.");
        Assert.Contains(WebRoot.EditorPrefix + "Pages/RecordEditor.razor", names);
        Assert.Contains(WebRoot.EditorPrefix + "Components/RecordForm.razor", names);
        Assert.Contains(WebRoot.EditorPrefix + "Components/Worklist.razor", names);
        Assert.Contains("Components/FacetPanel.razor", names);
        Assert.Contains("Components/PosterTile.razor", names);
        Assert.Contains("Components/PosterGrid.razor", names);
        Assert.Contains("Pages/Movies.razor", names);
        Assert.Contains("wwwroot/index.html", names);
        return files;
    }

    [Fact]
    public void NoHandWrittenComponentOrThePageBreaksAnyStandingRule()
    {
        var subjects = Subjects();

        Assert.All(subjects, f => Assert.Empty(Violations(f.Text).Select(v => f.Path + ": " + v)));
    }

    /// <remarks>Positive control for the comment stripper: the shipped panel quotes "aria-pressed" and friends in prose.</remarks>
    [Fact]
    public void TheScanReadsPastRulingsQuotedInComments()
    {
        Assert.Empty(Violations("@* never type=\"range\" and never role=\"button\" *@<!-- onmouseover title=\" -->"));
    }

    [Theory]
    [InlineData("<input type=\"range\" min=\"1900\" />", "a range input")]
    [InlineData("<div role=\"button\" @onclick=\"Go\">x</div>", "role=button")]
    [InlineData("<li @onmouseover=\"Hover\">x</li>", "a mouseover handler")]
    [InlineData("<div onmouseover=\"x()\">x</div>", "a mouseover handler")]
    [InlineData("<button type=\"button\" title=\"Next\">Next</button>", "title=")]
    [InlineData("<input type=\"checkbox\" @onchange=\"e => Set(e)\" title=\"Drama\" />", "title=")]
    [InlineData("<a\n   href=\"/x\"\n   title=\"x\">x</a>", "title=")]
    [InlineData("@code { string Short(string t) => t.Substring(0, 20); }", "Substring(")]
    [InlineData("@code { string Short(string t) => t[..20] + \"…\"; }", "an ellipsis")]
    [InlineData("@code { string Short(string t) => t + \"...\"; }", "an ellipsis")]
    [InlineData("<span>@($\"{Title}…\")</span>", "an ellipsis")]
    public void EachRuleFailsOnTheViolationItExistsToCatch(string fragment, string ruleStart)
    {
        Assert.Contains(Violations(fragment), v => v.StartsWith(ruleStart, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("<div class=\"tile\" title=\"Heat\">x</div>")]           // title on a non-interactive element is not this rule
    [InlineData("<input type=\"checkbox\" @onchange=\"e => Set(e)\" /> <p>title=x</p>")]
    [InlineData("internal const string LoadingVisible = \"Loading films…\";")]   // a whole visible string, not a built title
    [InlineData("var rest = names[..3];")]                                  // C# range syntax is not truncation
    public void TheScanDoesNotFlagWhatItWasNotWrittenFor(string fragment)
    {
        Assert.Empty(Violations(fragment));
    }
}

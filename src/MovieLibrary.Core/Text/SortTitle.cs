namespace MovieLibrary.Core.Text;

/// <summary>
/// Where a title sorts, derived from what the site displays.
/// </summary>
/// <remarks>
/// <para><b>In Core, and that is load-bearing.</b> The pipeline derives this at build time and the editor shows
/// the same value as a placeholder before a human overrides it. Two implementations would disagree, and the
/// disagreement would present as a title sorting in one place and previewing in another.</para>
/// <para><b>The rule is one line on purpose.</b> Strip one leading English article; otherwise the sort title IS
/// the display title. It must not grow numerals, roman numerals, subtitle handling or a stop-word list: every
/// title it handles badly has <c>titles.sort</c> in the record store, which is precisely why that override
/// exists. A rule that tried to be clever would be wrong in cases nobody had listed, silently, across 1,456
/// records.</para>
/// <para><b>Not <c>TitleNormalizer</c>.</b> That folds case, accents and punctuation away and peels editions and
/// years, because its output feeds a hash and a search query. A sort title is shown to a human and must keep its
/// case, its punctuation and its year.</para>
/// </remarks>
public static class SortTitle
{
    private static readonly string[] Articles = ["The", "A", "An"];

    public static string From(string displayTitle)
    {
        var text = (displayTitle ?? string.Empty).Trim();

        foreach (var article in Articles)
        {
            if (!text.StartsWith(article, StringComparison.OrdinalIgnoreCase)) continue;

            var rest = text[article.Length..].TrimStart();

            // Two guards, both necessary. A following character that is not whitespace means the article was
            // only the start of a longer word ("Theodore", "Analyze", "Alien"). An empty remainder means the
            // title was nothing BUT the article, and an empty sort key sorts before every real title.
            if (text.Length > article.Length && !char.IsWhiteSpace(text[article.Length])) continue;
            if (rest.Length == 0) continue;

            return rest;
        }

        return text;
    }
}

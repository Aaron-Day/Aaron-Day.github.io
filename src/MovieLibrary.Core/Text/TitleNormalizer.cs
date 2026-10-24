using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MovieLibrary.Core.Text;

public static class TitleNormalizer
{
    private static readonly string[] EditionWords =
        ["unrated", "uncut", "edition", "version", "cut", "remastered", "extended", "ultimate", "theatrical"];

    private static readonly Regex TrailingParen = new(@"\s*\(([^()]*)\)\s*$", RegexOptions.Compiled);

    public static NormalizedTitle Normalize(string raw)
    {
        var text = raw?.Trim() ?? string.Empty;
        int? year = null;

        // Peel trailing parentheticals right-to-left; a title may carry several.
        while (true)
        {
            var m = TrailingParen.Match(text);
            if (!m.Success) break;
            var inner = m.Groups[1].Value.Trim();

            if (inner.Length == 4 && int.TryParse(inner, out var y) && y is >= 1870 and <= 2200)
                year ??= y;
            else if (!EditionWords.Any(w => inner.Contains(w, StringComparison.OrdinalIgnoreCase)))
                break; // a meaningful parenthetical, e.g. a real subtitle — keep it

            text = text[..m.Index];
        }

        return new NormalizedTitle(Fold(text), year);
    }

    public static string NormalizeDirectors(string raw)
        => string.Join(' ', NameTokens(raw).OrderBy(t => t, StringComparer.Ordinal));

    public static IReadOnlySet<string> NameTokens(string raw)
        => Fold(raw).Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(t => t != "and")
                    .ToHashSet(StringComparer.Ordinal);

    private static string Fold(string input)
    {
        var decomposed = (input ?? string.Empty).Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (ch == '&') { sb.Append(" and "); continue; }
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        }

        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }
}

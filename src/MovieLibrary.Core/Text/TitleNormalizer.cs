using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MovieLibrary.Core.Text;

public static class TitleNormalizer
{
    // Words that mark a cut or a re-release rather than a different film. Shared by both peels below,
    // which read them differently on purpose: a parenthetical needs ONE of these to count as an edition,
    // a bare suffix after a colon needs ALL of its words to be one of these or a filler. A parenthetical
    // is already set apart by its brackets, so "(Director's Cut)" is unambiguous; "Star Wars: A New Hope"
    // shows what the looser rule would do to a real subtitle.
    private static readonly HashSet<string> EditionWords = new(StringComparer.OrdinalIgnoreCase)
        { "unrated", "uncut", "uncensored", "edition", "version", "cut", "remastered",
          "extended", "ultimate", "theatrical", "director", "special", "anniversary",
          "alternate", "ending" };

    /// <summary>Words that carry no meaning of their own inside an edition phrase, so they never block one.</summary>
    private static readonly HashSet<string> PhraseFiller = new(StringComparer.OrdinalIgnoreCase)
        { "the", "a", "an", "and", "of", "in", "s" };

    /// <summary>
    /// A trailing <c>(...)</c> or <c>[...]</c>. Apple's export uses both, sometimes together:
    /// <c>Friday the 13th (Uncut Version) [1980]</c>. Reading only the round pair left the whole of that
    /// title in the search query and in the identity key, which is why the film never resolved.
    /// </summary>
    private static readonly Regex TrailingBracket = new(
        @"\s*(?:\((?<inner>[^()]*)\)|\[(?<inner>[^\[\]]*)\])\s*$", RegexOptions.Compiled);

    public static NormalizedTitle Normalize(string raw)
    {
        var text = raw?.Trim() ?? string.Empty;
        int? year = null;

        // Peel right-to-left; a title may carry several markers, in either form and either order
        // ("Friday the 13th (Uncut Version) [1980]", "Scary Movie 5: Unrated Version"). A meaningful
        // parenthetical stops the bracket peel but not the suffix peel, so one does not hide the other.
        while (TryPeelBracket(ref text, ref year) || TryPeelEditionSuffix(ref text)) { }

        return new NormalizedTitle(Fold(text), text, year);
    }

    public static string NormalizeDirectors(string raw)
        => string.Join(' ', NameTokens(raw).OrderBy(t => t, StringComparer.Ordinal));

    public static IReadOnlySet<string> NameTokens(string raw)
        => NameParts(raw).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The folded words of a name, in the order they were written, so a caller can tell a surname from a
    /// given name. <see cref="NameTokens"/> is this with the order thrown away.
    /// </summary>
    public static IReadOnlyList<string> NameParts(string raw)
        => [.. Fold(raw).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => t != "and")];

    private static bool TryPeelBracket(ref string text, ref int? year)
    {
        var m = TrailingBracket.Match(text);
        if (!m.Success) return false;

        var inner = m.Groups["inner"].Value.Trim();
        if (YearIn(inner) is { } y) year ??= y;
        else if (!IsEdition(inner)) return false;   // a real subtitle, e.g. "Pi (Sequel)" - keep it

        text = text[..m.Index];
        return true;
    }

    /// <summary>
    /// A trailing <c>: Unrated Version</c> or <c>: Extended Director's Cut</c>, which Apple writes without
    /// brackets as often as with them. Every word of the suffix must be an edition word or filler, and at
    /// least one an edition word, so <c>Captain America: The First Avenger</c> keeps its subtitle.
    /// </summary>
    private static bool TryPeelEditionSuffix(ref string text)
    {
        var colon = text.LastIndexOf(':');
        if (colon <= 0) return false;

        var tokens = Fold(text[(colon + 1)..]).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0
            || !tokens.Any(EditionWords.Contains)
            || !tokens.All(t => EditionWords.Contains(t) || PhraseFiller.Contains(t)))
            return false;

        text = text[..colon].TrimEnd();
        return true;
    }

    private static int? YearIn(string inner)
        => inner.Length == 4
           && int.TryParse(inner, NumberStyles.None, CultureInfo.InvariantCulture, out var y)
           && y is >= 1870 and <= 2200
            ? y : null;

    private static bool IsEdition(string inner) =>
        Fold(inner).Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(EditionWords.Contains);

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

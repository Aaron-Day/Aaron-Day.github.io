using System.Text.RegularExpressions;

namespace MovieLibrary.Core.Text;

public static class Slug
{
    public static string From(string raw)
    {
        // Fold() already strips accents and reduces punctuation to spaces. Normalize moves a
        // trailing year into YearHint, so re-append it: "X" and "X (2020)" are different series.
        var normalized = TitleNormalizer.Normalize(raw);
        var slug = Regex.Replace(normalized.Value.Replace(' ', '-'), "-{2,}", "-").Trim('-');
        return normalized.YearHint is { } year ? $"{slug}-{year}" : slug;
    }
}

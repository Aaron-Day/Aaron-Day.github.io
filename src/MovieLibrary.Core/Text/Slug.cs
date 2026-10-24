using System.Text.RegularExpressions;

namespace MovieLibrary.Core.Text;

public static class Slug
{
    public static string From(string raw)
    {
        // Fold() already strips accents and reduces punctuation to spaces.
        var folded = TitleNormalizer.Normalize(raw).Value;
        return Regex.Replace(folded.Replace(' ', '-'), "-{2,}", "-").Trim('-');
    }
}

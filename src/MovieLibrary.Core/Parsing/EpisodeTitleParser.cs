using System.Text.RegularExpressions;

namespace MovieLibrary.Core.Parsing;

public sealed record ParsedEpisodeTitle(int? Season, int? Episode, string Name);

public static class EpisodeTitleParser
{
    // CultureInvariant is defensive: under tr-TR the uppercase I in "EPISODE" lowercases
    // to a dotless i and IgnoreCase alone stops matching. It cannot be pinned by a unit
    // test, because this is a static compiled Regex whose culture binds at type
    // initialization — switching CurrentCulture inside a test does not reach it.
    private static readonly Regex Numbered = new(
        @"^Season\s+(\d+)\s*,\s*Episode\s+(\d+)\s*[:,]\s*(.*)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static ParsedEpisodeTitle Parse(string raw)
    {
        var text = (raw ?? string.Empty).Trim();
        var m = Numbered.Match(text);

        return m.Success
            ? new ParsedEpisodeTitle(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value),
                                     m.Groups[3].Value.Trim())
            : new ParsedEpisodeTitle(null, null, text);
    }
}

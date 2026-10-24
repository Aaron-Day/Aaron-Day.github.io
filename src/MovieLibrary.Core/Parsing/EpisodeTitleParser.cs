using System.Text.RegularExpressions;

namespace MovieLibrary.Core.Parsing;

public sealed record ParsedEpisodeTitle(int? Season, int? Episode, string Name);

public static class EpisodeTitleParser
{
    private static readonly Regex Numbered = new(
        @"^Season\s+(\d+),\s*Episode\s+(\d+):\s*(.*)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

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

namespace MovieLibrary.Core.Parsing;

public static class RuntimeParser
{
    /// <summary>Film runtimes are honest h:mm:ss.</summary>
    public static int FilmMinutes(string raw)
    {
        var (a, b, c) = Split(raw);
        return (int)((a * 3600 + b * 60 + c) / 60);
    }

    /// <summary>
    /// Episode runtimes arrive as h:mm:ss too, but Google Sheets coerced the
    /// original mm:ss into a duration, so "45:37" became "45:37:00" — 45 hours.
    /// Any value claiming ten or more hours is really minutes and seconds.
    /// The seconds field is irrelevant to the test: real rows carry "44:50:59".
    /// </summary>
    public static int EpisodeMinutes(string raw)
    {
        var (a, b, c) = Split(raw);
        return a >= 10 ? (int)a : (int)((a * 3600 + b * 60 + c) / 60);
    }

    private static (long, long, long) Split(string raw)
    {
        var parts = (raw ?? string.Empty).Split(':');
        if (parts.Length != 3) throw new FormatException($"Unparseable runtime: '{raw}'");

        if (!long.TryParse(parts[0], out var h) ||
            !long.TryParse(parts[1], out var m) ||
            !long.TryParse(parts[2], out var s))
            throw new FormatException($"Unparseable runtime: '{raw}'");

        return (h, m, s);
    }
}

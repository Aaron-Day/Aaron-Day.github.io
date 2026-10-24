using System.Globalization;

namespace MovieLibrary.Core.Parsing;

public static class RuntimeParser
{
    /// <summary>
    /// Film runtimes are honest h:mm:ss. The Movies tab is uniformly three-part,
    /// so a two-part film runtime signals corrupt data and is rejected.
    /// </summary>
    public static int FilmMinutes(string raw)
    {
        var (a, b, c) = Split(raw, allowMinutesSeconds: false);
        return (int)((a * 3600 + b * 60 + c) / 60);
    }

    /// <summary>
    /// Episode runtimes arrive as h:mm:ss too, but Google Sheets coerced the
    /// original mm:ss into a duration, so "45:37" became "45:37:00" — 45 hours.
    /// Any value claiming ten or more hours is really minutes and seconds.
    /// The seconds field is irrelevant to the test: real rows carry "44:50:59".
    /// A plain two-part "mm:ss" (e.g. "23:04") is also accepted: Sheets left about
    /// 5% of rows as text instead of coercing them.
    /// </summary>
    /// <remarks>
    /// Known limitation: a value of one to nine hours is ambiguous and is
    /// deliberately NOT resolved by a heuristic. "1:06:19" is a genuine 66-minute
    /// episode, but "6:10:00" is a coerced 6m10s extra, and the two cannot be told
    /// apart from the text alone. A seconds-equals-zero rule was evaluated against
    /// the real export and rejected: it would repair about 810 extras but corrupt
    /// about 425 real episodes. This is safe because the sheet runtime is never
    /// used for episode matching (episodes are matched by season/episode number or
    /// by name, and the stored runtime comes from TMDB); the sheet value only
    /// reaches fixture generation. Do not add a rule here without re-auditing.
    /// </remarks>
    public static int EpisodeMinutes(string raw)
    {
        var (a, b, c) = Split(raw, allowMinutesSeconds: true);
        return a >= 10 ? (int)a : (int)((a * 3600 + b * 60 + c) / 60);
    }

    private static (long h, long m, long s) Split(string raw, bool allowMinutesSeconds)
    {
        var parts = (raw ?? string.Empty).Split(':');

        // Two parts is a plain mm:ss the sheet never coerced (e.g. "23:04").
        if (allowMinutesSeconds && parts.Length == 2)
            return (0, Num(parts[0], raw), Num(parts[1], raw));

        if (parts.Length == 3)
            return (Num(parts[0], raw), Num(parts[1], raw), Num(parts[2], raw));

        throw new FormatException($"Unparseable runtime: '{raw}'");
    }

    private static long Num(string part, string? raw)
        => long.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var v)
            ? v
            : throw new FormatException($"Unparseable runtime: '{raw}'");
}

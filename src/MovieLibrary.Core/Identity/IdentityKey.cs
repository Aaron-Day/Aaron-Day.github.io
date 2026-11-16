using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MovieLibrary.Core.Text;

namespace MovieLibrary.Core.Identity;

/// <remarks>
/// SHA-1 is used purely as a content fingerprint for cache lookup, never for security.
/// <para>
/// The hash input format is a frozen contract. Changing the algorithm, the delimiter, the
/// field order, or which fields participate invalidates EVERY cached entry: the next run
/// re-queries all 1,457 films and silently re-spends the TMDB budget, with no error anywhere.
/// The golden-value tests exist to make such a change fail loudly instead.
/// </para>
/// </remarks>
public static class IdentityKey
{
    public static string ForFilm(NormalizedTitle title, int runtimeMinutes, string normalizedDirectors)
        => Hash($"film|{title.Value}|{runtimeMinutes.ToString(CultureInfo.InvariantCulture)}|{normalizedDirectors}");

    public static string ForSeries(NormalizedTitle series)
        => Hash($"series|{series.Value}");

    /// <summary>
    /// The folded name a series is keyed and cached under: its folded title with a trailing year folded back
    /// in, so <c>X</c> and <c>X (2020)</c> are two series and not one.
    /// </summary>
    /// <remarks>
    /// <c>Normalize</c> moves a trailing year out into <c>YearHint</c>, which leaves those two names sharing
    /// one <c>Value</c>. This puts it back. The one definition of the rule: <c>TvPipeline.KeyOf</c> delegates
    /// here, and the record store's binder calls <see cref="ForSeriesName"/>, so a series cannot be keyed one
    /// way by the pipeline and another by the store.
    /// </remarks>
    public static string SeriesKeyValue(NormalizedTitle title)
        => title.YearHint is { } year
            ? $"{title.Value} {year.ToString(CultureInfo.InvariantCulture)}"
            : title.Value;

    /// <summary>The identity key for a series named as the Shows tab names it.</summary>
    public static string ForSeriesName(string seriesName)
    {
        var title = TitleNormalizer.Normalize(seriesName);

        return ForSeries(title with { Value = SeriesKeyValue(title) });
    }

    private static string Hash(string input)
        => Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(input)));
}

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

    private static string Hash(string input)
        => Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(input)));
}

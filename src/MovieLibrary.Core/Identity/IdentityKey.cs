using System.Security.Cryptography;
using System.Text;
using MovieLibrary.Core.Text;

namespace MovieLibrary.Core.Identity;

public static class IdentityKey
{
    public static string ForFilm(NormalizedTitle title, int runtimeMinutes, string normalizedDirectors)
        => Hash($"film|{title.Value}|{runtimeMinutes}|{normalizedDirectors}");

    public static string ForSeries(NormalizedTitle series)
        => Hash($"series|{series.Value}");

    private static string Hash(string input)
        => Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(input)));
}

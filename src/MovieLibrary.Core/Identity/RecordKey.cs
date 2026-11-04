using System.Globalization;

namespace MovieLibrary.Core.Identity;

/// <summary>
/// What identifies one catalogue record to the outside world: its URL segment, its <c>@key</c>, and the
/// key a visitor's saved state is filed under.
/// </summary>
/// <remarks>
/// <para>A matched film is its TMDB id, which is stable for as long as the match holds and survives any edit
/// to the owner's sheet. An unmatched film has no id — the pipeline writes <c>0</c> for all of them — so it
/// takes a short prefix of its content identity key instead. The <c>u-</c> prefix is what makes the two
/// spaces provably disjoint: no decimal id can start with it.</para>
/// <para>The hash form is deliberately NOT stable across sheet edits: the identity key folds in title,
/// runtime and directors, so correcting any of them re-keys the record. That is the right trade for an
/// unmatched film, which exists only to be fixed, and it is why the owner chose this over slugs for
/// everything (a slug would re-key MATCHED films on a title edit, silently breaking saved favourites).</para>
/// </remarks>
public static class RecordKey
{
    /// <summary>Hex characters of the identity key kept. 8 gives 4.3 billion values for 1,457 records.</summary>
    public const int HashLength = 8;

    /// <summary>Marks the hash form. No TMDB id can begin with this, so the two forms never collide.</summary>
    public const string UnmatchedPrefix = "u-";

    public static string ForFilm(int? tmdbId, string identityKey)
    {
        if (tmdbId is { } id and > 0) return id.ToString(CultureInfo.InvariantCulture);

        if (identityKey is null || identityKey.Length < HashLength)
            throw new ArgumentException(
                $"An unmatched record needs at least {HashLength} characters of identity key to hash, " +
                $"got '{identityKey}'. The caller should be passing IdentityKey.ForFilm's output.",
                nameof(identityKey));

        return UnmatchedPrefix + identityKey[..HashLength];
    }
}

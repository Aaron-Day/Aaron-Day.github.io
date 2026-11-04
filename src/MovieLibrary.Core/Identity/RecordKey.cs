using System.Globalization;

namespace MovieLibrary.Core.Identity;

/// <summary>
/// What identifies one catalogue record to the outside world: its URL segment, its <c>@key</c>, and the
/// key a visitor's saved state is filed under.
/// </summary>
/// <remarks>
/// <para>A matched film is <c>{tmdbId}-{runtimeMinutes}</c>, for example <c>120-178</c>. The TMDB id alone is NOT
/// unique: the owner owns more than one cut of some films, and every cut resolves to the same TMDB id (both
/// Fellowship of the Ring cuts are 120, both Exorcist cuts 9552). Keyed on the bare id they would be one record,
/// and the UI's <c>@key</c>, its <c>/movies/{k}</c> route and a visitor's saved favourites would merge them. The
/// runtime is what separates the cuts (120-178 against 120-228). It is the field <c>IdentityKey</c> already uses to
/// tell cuts apart, it keeps the TMDB id readable in the URL, and unlike a row number or a content hash it does
/// not depend on where the row sits in the sheet or on any other column.</para>
/// <para>Two rows that agree on id AND runtime still share a key. That is intended: nothing distinguishes them
/// (the two Blade Runner rows are id 78 at 117 minutes with the same director), so they are one film listed twice.
/// A key must be unique in the published catalogue, because a duplicate sibling <c>@key</c> throws in Blazor, so the
/// pipeline collapses such rows into one record (<c>CatalogueReconciler</c>) rather than leaving the clash for the UI.</para>
/// <para>A matched key is a function of the TMDB id and the sheet's runtime, so it is NOT permanent. It changes when:
/// (1) the sheet's runtime shifts. Apple re-encodes and the runtime moves a minute or two, which is why
/// <c>CacheStore</c> keeps a two-minute alias at all, and <c>RuntimeParser.FilmMinutes</c> truncates seconds, so even a
/// sub-minute drift across a minute boundary counts. The alias keeps the TMDB match, so <c>I</c> holds, but the
/// pipeline builds the record from the new runtime and <c>K</c> moves anyway. (2) The title or the directors are
/// edited. That changes the identity key, misses the cache and searches again, and the key survives only if the search
/// lands on the same id. (3) An Overrides row forces a different id. (4) The match moves for any other reason: a
/// re-match, or the cross-row conflict rule rejecting it, which makes the film unmatched and gives it the hash form. A
/// consumer must therefore not treat <c>K</c> as permanent: an unknown <c>/movies/{k}</c> needs a not-found path, and
/// anything saved per film should store <c>I</c> beside <c>K</c> as a fallback.</para>
/// <para>An unmatched film has no id (the pipeline writes <c>0</c> for all of them), so it takes a short prefix of its
/// content identity key instead. The <c>u-</c> prefix keeps the two spaces provably disjoint: a matched key starts
/// with a digit.</para>
/// <para>The hash form is deliberately NOT stable across sheet edits: the identity key folds in title, runtime and
/// directors, so correcting any of them re-keys the record. That is the right trade for an unmatched film, which
/// exists only to be fixed, and it is why the owner chose this over slugs for everything (a slug would re-key
/// MATCHED films on a title edit, silently breaking saved favourites).</para>
/// </remarks>
public static class RecordKey
{
    /// <summary>Hex characters of the identity key kept. 8 gives 4.3 billion values for 1,457 records.</summary>
    public const int HashLength = 8;

    /// <summary>Marks the hash form. A matched key begins with a digit, so the two forms never collide.</summary>
    public const string UnmatchedPrefix = "u-";

    /// <summary>The key for one film record.</summary>
    /// <param name="tmdbId">The matched TMDB id; null, zero or negative means unmatched.</param>
    /// <param name="runtimeMinutes">The row's runtime in minutes. Separates two cuts of one TMDB film; ignored when unmatched.</param>
    /// <param name="identityKey">Output of <c>IdentityKey.ForFilm</c>; only read for an unmatched film.</param>
    public static string ForFilm(int? tmdbId, int runtimeMinutes, string identityKey)
    {
        if (tmdbId is { } id and > 0)
            return $"{id.ToString(CultureInfo.InvariantCulture)}-{runtimeMinutes.ToString(CultureInfo.InvariantCulture)}";

        if (identityKey is null || identityKey.Length < HashLength)
            throw new ArgumentException(
                $"An unmatched record needs at least {HashLength} characters of identity key to hash, " +
                $"got '{identityKey}'. The caller should be passing IdentityKey.ForFilm's output.",
                nameof(identityKey));

        return UnmatchedPrefix + identityKey[..HashLength];
    }
}

using MovieLibrary.Core.Identity;
using MovieLibrary.Core.Models;
using MovieLibrary.Core.Parsing;
using MovieLibrary.Core.Store;
using MovieLibrary.Core.Text;

namespace MovieLibrary.Web.Editor.Store;

/// <summary>
/// The two directions between a published record and a store entry. A store entry binds on the sheet row's
/// identity key and the published catalogue does not carry one, so both directions are derived here.
/// </summary>
/// <remarks>
/// <para><b>Why a published record is enough to compute its row's key.</b> MoviePipeline publishes
/// <c>T: row.Title</c> and <c>Dir: row.Directors.Split('&amp;', ...)</c> -- the sheet's own title and directors,
/// verbatim -- and <c>Rt</c> is the same int the key hashed. Only TMDB-derived fields come from TMDB. So the
/// hash input is recoverable, and <see cref="TitleNormalizer.NormalizeDirectors"/> discards the separator and
/// the order, which is what makes re-joining the split array safe.</para>
/// <para><b>Where <see cref="IdentityKeyOf"/> stops being true.</b> A store override can replace the title, the
/// runtime (MoviePipeline.cs:392) and the directors (:426) of the record it publishes. On an overridden record
/// all three hash inputs may differ from the sheet's, so the recomputed key is one the pipeline never hashed.
/// What makes the method safe is that an override is applied only when the store already holds an entry for
/// the row's key (MoviePipeline.cs:128-129): a record with NO store entry is never overridden.
/// <see cref="IdentityKeyOf"/> is therefore valid only on the no-entry path. A record that already has an entry
/// must be resolved through its entry instead, computed forwards and matched on <c>K</c>
/// (<see cref="PublishedKeyOf"/>). The pipeline-side claim is pinned by
/// <c>AMatchedFilmPublishesTheSheetsOwnTitleRuntimeAndDirectorsSoItsIdentityKeyIsRecomputable</c> in
/// MovieLibrary.Core.Tests.</para>
/// <para><b>What cannot be checked.</b> A matched record's key is <c>{id}-{minutes}</c> and holds no part of the
/// identity key, so <see cref="Agrees"/> has nothing to compare and returns null rather than a true that would
/// mean nothing. An unmatched record's key is the key's first 8 characters, which is a real check on the 30
/// records that have one.</para>
/// </remarks>
public static class RecordLink
{
    public static string IdentityKeyOf(MovieRecord movie)
    {
        ArgumentNullException.ThrowIfNull(movie);

        return IdentityKey.ForFilm(
            TitleNormalizer.Normalize(movie.T),
            movie.Rt,
            TitleNormalizer.NormalizeDirectors(string.Join(" & ", movie.Dir)));
    }

    /// <returns>True or false when the record's own key carries the hash; null when it cannot say.</returns>
    public static bool? Agrees(MovieRecord movie, string identityKey)
    {
        ArgumentNullException.ThrowIfNull(movie);
        ArgumentException.ThrowIfNullOrWhiteSpace(identityKey);

        if (!movie.K.StartsWith(RecordKey.UnmatchedPrefix, StringComparison.Ordinal)) return null;
        if (identityKey.Length < RecordKey.HashLength) return false;

        var published = movie.K[RecordKey.UnmatchedPrefix.Length..].Split('-')[0];
        return string.Equals(published, identityKey[..RecordKey.HashLength], StringComparison.Ordinal);
    }

    /// <returns>The key the entry's record at <paramref name="position"/> publishes, or null when no runtime is knowable.</returns>
    public static string? PublishedKeyOf(FilmEntry entry, int position)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentOutOfRangeException.ThrowIfLessThan(position, 1);

        if (position > entry.Records.Count) return null;

        var record = entry.Records[position - 1];
        var minutes = record.Runtime
                      ?? (RuntimeParser.TryFilmMinutes(entry.Sheet.Runtime, out var echoed) ? echoed : (int?)null);

        return minutes is { } found ? RecordKey.ForFilm(record.TmdbId, found, entry.IdentityKey, position) : null;
    }
}

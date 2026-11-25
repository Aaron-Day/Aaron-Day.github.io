using System.Text.Json.Serialization;

namespace MovieLibrary.Web.Models;

/// <summary>
/// One title's saved state: whether the owner has starred it, his own score, and which of his lists it is in.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="Rating"/> is nullable and 0 is a REAL score.</b> TMDB's own scale starts at 0 and the owner
/// chose those units so his number and theirs are comparable, which leaves no value free to mean "unrated".
/// A rating of zero and no rating at all are different claims -- "I watched this and it was nothing" against
/// "I have not said" -- and a sort by his score has to be able to put the second one somewhere else entirely.
/// </para>
/// <para>
/// <b><see cref="Lists"/> holds list IDS and never names.</b> Renaming a list must not rewrite every entry
/// that mentions it, and two lists may not share a name only because nothing stopped them.
/// </para>
/// <para>
/// <b><see cref="TmdbId"/> is the fallback, and it is here because <c>RecordKey</c>'s own remarks demand it:
/// "anything saved per film should store <c>I</c> beside <c>K</c>".</b> A published key is a function of the
/// TMDB id and the sheet's runtime, so it MOVES -- Apple re-encodes a file and the runtime shifts a minute,
/// or the title is edited and the match lands on the same id by a different route. Without the id beside it,
/// every favourite on that title would silently become a favourite of nothing, and nothing on screen would
/// say so. 0 means there is no fallback: an unmatched record, whose key is a content hash.
/// </para>
/// </remarks>
public sealed record TitlePreference(
    [property: JsonPropertyName("i")] int TmdbId = 0,
    [property: JsonPropertyName("fav")] bool Favourite = false,
    [property: JsonPropertyName("rate")] int? Rating = null,
    [property: JsonPropertyName("lists")] string[]? Lists = null)
{
    /// <summary>The bounds of <see cref="Rating"/>, inclusive, which are TMDB's own.</summary>
    public const int MinRating = 0;

    public const int MaxRating = 10;

    /// <summary>The list ids this title is in, never null, in the order they were saved.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> ListIds => Lists ?? [];

    /// <summary>
    /// Whether this entry says anything at all. An entry that says nothing is DELETED rather than written,
    /// so unstarring the last title leaves no record of it instead of a file full of empty objects.
    /// </summary>
    [JsonIgnore]
    public bool IsEmpty => !Favourite && Rating is null && ListIds.Count == 0;
}

/// <summary>One of the owner's named lists. The <see cref="Id"/> is what entries point at; the name is his.</summary>
/// <remarks>
/// The id is generated once and never derived from the name, because a name derivation would re-key the list
/// the first time he corrected a typo in it -- the same defect <c>RecordKey</c>'s remarks record for slugs,
/// one layer up.
/// </remarks>
public sealed record TitleList(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name);

/// <summary>
/// Everything the owner has saved in this browser: his lists, and one entry per title that has any state.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two maps and not one.</b> A film is keyed by its published key (<c>RecordKey</c>) and a series by its
/// slug, and the two spaces are not disjoint -- nothing stops a slug reading like a key. One map would make
/// a collision between them silent, and the collision would move a favourite from a film to a series.
/// </para>
/// <para>
/// <b><see cref="Version"/> is written and read.</b> This document lives in a visitor's own browser for
/// years and the app ships from a branch that moves weekly, so a reader that assumed the current shape
/// would throw on a document written by an older build -- and the only recovery a visitor would have is
/// clearing site data, which destroys exactly what this file exists to keep.
/// </para>
/// <para>
/// A plain mutable dictionary, deliberately: this is the document shape, serialised straight to JSON, and
/// the store above it owns every change to it.
/// </para>
/// </remarks>
public sealed class PreferenceDocument
{
    /// <summary>The shape this build writes. Bump it only with a migration beside it.</summary>
    public const int CurrentVersion = 1;

    [JsonPropertyName("v")]
    public int Version { get; set; } = CurrentVersion;

    [JsonPropertyName("lists")]
    public List<TitleList> Lists { get; set; } = [];

    [JsonPropertyName("films")]
    public Dictionary<string, TitlePreference> Films { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyName("series")]
    public Dictionary<string, TitlePreference> Series { get; set; } = new(StringComparer.Ordinal);
}

namespace MovieLibrary.Core.Store;

/// <summary>The three titles one record can have. Raw lives on the sheet; these two override what the site shows.</summary>
/// <param name="Display">What the card and the detail page show, replacing <c>MovieRecord.T</c>.</param>
/// <param name="Sort">Where the title sorts, replacing what <see cref="Text.SortTitle"/> would derive.</param>
public sealed record TitleSet(string? Display = null, string? Sort = null);

/// <summary>
/// The sheet row's fields as they were when the entry was written. NEVER consumed for matching -- the
/// identity key does that. It exists so a human can read the file, and so a note can name the row in words
/// rather than as a hash.
/// </summary>
public sealed record SheetEcho(string Title = "", string Runtime = "", string Directors = "");

/// <summary>
/// One published record's corrections. Every member is optional, and an omitted member means "the pipeline
/// decides" -- which is what keeps the store sparse: about 1,456 films are expected to need no entry at all.
/// </summary>
/// <remarks>
/// <para><b>TMDB-derived fields are deliberately absent</b> (poster, backdrop, genres, vote, overview,
/// tagline, cast, collection). If one of those is wrong then the MATCH is wrong, and the fix is
/// <see cref="TmdbId"/>. Two ways to express one correction is two ways for them to disagree, and a store
/// holding a hand-typed overview is a store that goes stale against TMDB with nothing to notice.</para>
/// <para><see cref="Extra"/> is <c>string</c> to <c>string</c> on purpose. Anything the UI must reason about
/// numerically earns a typed field of its own; a map of strings round-trips through a trimmed WebAssembly
/// serializer with no reflection hazard, and the UI cannot sort or filter on a type it does not know exists.</para>
/// </remarks>
/// <param name="TmdbId">The id the owner asserts. Mutually exclusive with <see cref="Candidates"/>.</param>
/// <param name="Candidates">
/// Ids the owner saw and did NOT choose between. The record stays unmatched and badged, and the editor offers
/// the same shortlist next time. This is how ambiguity is surfaced instead of guessed.
/// </param>
/// <param name="Runtime">Minutes. Required on every record of an entry that holds more than one.</param>
/// <param name="PublishedKey">
/// The key the PREVIOUS build published for this record, as the editor read it out of <c>movies.json</c> when
/// it bound this record and saved. Written by the editor, never by the build, and never computed forwards: it
/// is evidence of what happened, which is exactly what the key the editor derives from the store is not.
/// <para><b>Why a derived key is not enough.</b> A matched record publishes <c>{id}-{minutes}</c>, which holds
/// no part of its sheet row, so a key computed from what the store says NOW moves the instant a correction
/// asserts an id -- and two rows that legitimately share an id at different runtimes (two cuts of one film,
/// which <c>CatalogueReconciler.RejectConflictingIds</c> keeps apart on purpose) can be made to compute each
/// other's key by correcting a runtime. The binding then takes the stranger.</para>
/// <para><b>Null means "not recorded yet", not "no key".</b> A version 1 file predates this member and a
/// hand-written entry may omit it, so the binding keeps its derived-key pass as well. The stored key matches
/// in the window before the next build runs; the derived key matches after it, when store and published output
/// finally agree. Dropping either one re-opens the hole.</para>
/// </param>
public sealed record RecordOverride(
    int? TmdbId = null,
    int[]? Candidates = null,
    int? Runtime = null,
    TitleSet? Titles = null,
    int? Year = null,
    string? Directors = null,
    string? AppleGenre = null,
    string? PurchaseInfo = null,
    string? PublishedKey = null,
    Dictionary<string, string>? Extra = null);

/// <summary>One sheet row on the Movies tab, and the one or more records it should publish.</summary>
public sealed record FilmEntry
{
    /// <summary>The row's <c>IdentityKey.ForFilm</c> output: 40 lower-case hex characters. The binding key.</summary>
    public required string IdentityKey { get; init; }

    public SheetEcho Sheet { get; init; } = new();

    /// <summary>The owner's own words. Echoed into the job summary, never parsed.</summary>
    public string? Note { get; init; }

    /// <summary>One or more. More than one expands the row (Halloweentown is four films in one Apple entry).</summary>
    public required IReadOnlyList<RecordOverride> Records { get; init; }
}

/// <summary>One series on the Shows tab. A series publishes exactly one record, so there is no expansion here.</summary>
public sealed record SeriesEntry
{
    /// <summary>The series' <c>IdentityKey.ForSeries</c> output: 40 lower-case hex characters.</summary>
    public required string IdentityKey { get; init; }

    public SheetEcho Sheet { get; init; } = new();
    public string? Note { get; init; }

    /// <summary>
    /// Only <c>TmdbId</c>, <c>Candidates</c>, <c>Titles</c> and <c>Extra</c> are meaningful. Runtime, year,
    /// directors and purchase info are per-episode or absent for a series, and <c>ShowRecord</c> has no fields
    /// for them, so the reader rejects them rather than accepting a value it would silently drop.
    /// </summary>
    public required RecordOverride Record { get; init; }
}

/// <summary>
/// Everything a human has decided about this library that the sheet cannot hold, because an Apple paste
/// overwrites the sheet.
/// </summary>
/// <remarks>
/// <b>Named properties, not a positional record.</b> <see cref="Films"/> and <see cref="Series"/> are the same
/// type, so a positional constructor would let the two be exchanged with nothing at all to notice -- entries
/// would simply bind against the wrong tab. This project has already shipped that defect once, with the two
/// same-typed override maps, which is why <c>ReviewQueue</c> is written the same way.
/// </remarks>
public sealed record RecordStore
{
    /// <summary>What a save writes. A store read at an older version is upgraded to this on load.</summary>
    public const int CurrentVersion = 2;

    /// <summary>
    /// The oldest version this build still reads. Version 1 has no <see cref="RecordOverride.PublishedKey"/>
    /// anywhere, which is indistinguishable from version 2 with the member omitted -- so the upgrade is nothing
    /// but the version number, and the reader does it rather than asking anyone to migrate a file by hand.
    /// </summary>
    public const int OldestReadableVersion = 1;

    /// <summary>A repository with no store, which is the normal state of a fresh clone.</summary>
    public static RecordStore Empty => new() { Version = CurrentVersion, Films = [], Series = [] };

    /// <summary>Not <c>required</c>: a missing version reads as 0, which the reader rejects with the fix spelled out.</summary>
    public int Version { get; init; }

    /// <summary>Not <c>required</c>: a hand-written store may leave a tab out, and that means no entries.</summary>
    public IReadOnlyList<FilmEntry> Films { get; init; } = [];

    public IReadOnlyList<SeriesEntry> Series { get; init; } = [];
}

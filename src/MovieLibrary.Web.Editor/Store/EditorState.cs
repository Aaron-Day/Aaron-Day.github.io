using MovieLibrary.Core.Matching;
using MovieLibrary.Core.Models;
using MovieLibrary.Core.Store;

namespace MovieLibrary.Web.Editor.Store;

/// <summary>One published record, with everything the worklist needs to show it and Task 5 needs to edit it.</summary>
/// <param name="IdentityKey">
/// The key of the store entry this record belongs to: the entry's own stored key when it has one, the recomputed
/// one only when it has none. See <see cref="EditorCatalogue"/>.
/// </param>
/// <param name="Badge">The band's words from <c>ConfidenceBand.BadgeFor</c>; null for a confident record.</param>
/// <param name="Entry">The record's existing store entry, or null when nobody has corrected it yet.</param>
/// <param name="Position">
/// Which of <see cref="FilmEntry.Records"/> this published record is, 1-based; 1 for a row with no entry.
/// <para><b>A save replaces the record at this position and nothing else.</b> An entry's Records is a LIST --
/// one Apple row can publish four films -- and its Note is the owner's own words, so building an entry from
/// the row and the form alone would write an entry holding one record: three siblings silently deleted, the
/// note nulled, and a file that still validates and publishes one film where four were.</para>
/// </param>
/// <param name="Corrected">
/// Whether this record's store entry asserts a TMDB id, which re-badges the row in place.
/// <para><b>A new vocabulary item, and deliberately not a hand-copied band word.</b> ConfidenceBand.Of reads
/// only the PUBLISHED record's id and score, so a save to the store changes no band and no count until the
/// next build, and 285 saves would look identical to none. ConfidenceBand.BadgeFor has no word for "corrected,
/// waiting for the next build" -- Confident returns null -- and the two band words may not be written in this
/// project's files at all, in any case, so the third word is the editor's own and is named here once.</para>
/// </param>
public sealed record WorklistRow(
    MovieRecord Movie, string IdentityKey, Confidence Band, string? Badge, FilmEntry? Entry,
    int Position = 1, bool Corrected = false);

/// <summary>What reading the store file produced. Three answers, because the third is the dangerous one.</summary>
public enum StoreReading
{
    /// <summary>A file was read and parsed. The draft is what it holds.</summary>
    Read,

    /// <summary>
    /// No text came back. That is the ordinary state of a fresh clone AND the state of a file that exists but
    /// could not be read, and the script cannot tell them apart across the interop boundary. The draft is empty,
    /// which is safe only because the write side refuses to overwrite a file it could not read. The screen must
    /// say that no store was READ and never that none exists.
    /// </summary>
    NotRead,

    /// <summary>The text came back and the reader refused it. <see cref="EditorState.StoreProblem"/> is its own message.</summary>
    Refused,
}

/// <summary>Everything one load of the editor produced. Immutable: the page asks for a new one rather than editing this.</summary>
public sealed record EditorState(
    CatalogueState Catalogue,
    IReadOnlyList<WorklistRow> Rows,
    StoreDraft Draft,
    StoreReading Store,
    string? StoreProblem,
    string? FolderName,
    bool ApiSupported,
    bool Synthetic,
    int Unmatched,
    int Uncertain,
    int Confident,
    IReadOnlyList<FilmEntry> Unplaceable)
{
    /// <summary>
    /// Whether a correction may be made at all. A failed catalogue has no records to correct, a refused store is a
    /// file this tool could not read and must not write over, and a synthetic catalogue's ids do not exist.
    /// </summary>
    public bool Editable => Catalogue == CatalogueState.Ready && Store != StoreReading.Refused && !Synthetic;

    /// <summary>Records that need a decision: every one the pipeline did not match with confidence.</summary>
    public int NeedAttention => Unmatched + Uncertain;
}

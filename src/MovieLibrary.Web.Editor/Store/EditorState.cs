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
public sealed record WorklistRow(MovieRecord Movie, string IdentityKey, Confidence Band, string? Badge, FilmEntry? Entry);

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

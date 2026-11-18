using MovieLibrary.Core.Matching;
using MovieLibrary.Core.Models;
using MovieLibrary.Core.Store;

namespace MovieLibrary.Web.Editor.Store;

/// <summary>
/// Loads the published catalogue and the store beside it and binds one to the other. Holds no state of its own:
/// the page owns the current <see cref="EditorState"/> and asks for a new one.
/// </summary>
/// <remarks>
/// <para><b>How a record's store key is resolved, and the one way to get it wrong.</b> A record that already has
/// a store entry is resolved through that entry, whose own stored identity key is authoritative. Recomputing it
/// from the published fields is wrong there: a store override can replace the published title, runtime and
/// directors (MoviePipeline.cs:417, :392, :426), so the recomputed key is one the pipeline never hashed, and the
/// edit would bind to an entry that does not exist -- a duplicate, or a write over another film, with nothing
/// throwing and a file that still validates. <see cref="RecordLink.IdentityKeyOf"/> is the fallback for a record
/// with NO entry, which is safe because an override is only ever applied when the store already holds an entry
/// (MoviePipeline.cs:128-129). The choice is made in <see cref="Link"/> alone, so a later edit cannot pick the
/// wrong function at a call site.</para>
/// </remarks>
public sealed class EditorCatalogue(ICatalogueSource catalogue, LocalDirectory directory)
{
    public async Task<EditorState> LoadAsync()
    {
        // Independent of one another, so none waits for another.
        var moviesRequest = catalogue.MoviesAsync();
        var metaRequest = catalogue.MetaAsync();
        var storeRequest = ReadStoreAsync();

        var (state, movies) = await moviesRequest;
        var (_, meta) = await metaRequest;
        var store = await storeRequest;

        // A failed fetch is not an empty worklist. Nothing downstream may read "no rows" as "nothing to do".
        if (state != CatalogueState.Ready)
            return store.With(CatalogueState.Failed, [], false, []);

        var synthetic = meta?.Fixtures ?? false;

        // A refused store leaves nothing editable: saving over a file this tool could not read is how a
        // hand-made correction gets destroyed.
        if (store.Kind == StoreReading.Refused)
            return store.With(CatalogueState.Ready, [], synthetic, []);

        var byPublishedKey = EntriesByPublishedKey(store.Draft);
        var rows = movies.Select(m => Link(m, byPublishedKey)).ToList();

        // An entry that claimed no row is reported rather than dropped: a correction he already made, silently
        // invisible, is worse than the drop. "Claimed" is asked of THIS entry, and not of whether its key exists
        // in the catalogue: two entries can publish the same key (the identity key is not part of a matched
        // record's key), the map keeps the first, and asking only whether the key exists would leave the second
        // neither shown nor named. An entry with several records is placed when any one of them is bound.
        var claimed = rows.Select(r => r.Entry).OfType<FilmEntry>().ToHashSet(ReferenceEqualityComparer.Instance);
        var unplaceable = store.Draft.Store.Films.Where(f => !claimed.Contains(f)).ToList();

        return store.With(CatalogueState.Ready, rows, synthetic, unplaceable);
    }

    /// <summary>The ONE place a record's key is decided. Entry present: through the entry. Absent: the fallback.</summary>
    private static WorklistRow Link(MovieRecord movie, IReadOnlyDictionary<string, FilmEntry> entries)
    {
        var band = ConfidenceBand.Of(movie);
        var entry = entries.GetValueOrDefault(movie.K);
        var key = entry is null ? RecordLink.IdentityKeyOf(movie) : entry.IdentityKey;

        return new WorklistRow(movie, key, band, ConfidenceBand.BadgeFor(band), entry);
    }

    private static Dictionary<string, FilmEntry> EntriesByPublishedKey(StoreDraft draft)
    {
        var map = new Dictionary<string, FilmEntry>(StringComparer.Ordinal);

        foreach (var entry in draft.Store.Films)
            for (var position = 1; position <= entry.Records.Count; position++)
                if (RecordLink.PublishedKeyOf(entry, position) is { } key)
                    map.TryAdd(key, entry);

        return map;
    }

    private async Task<Reading> ReadStoreAsync()
    {
        // Query-only on load: a picker or a prompt fires on a user gesture and never on load (A52), and
        // requestPermission without a click rejects in Chromium, which would report no folder on every fresh
        // load even with a grant standing. The prompt is ReconfirmAsync's, behind a button.
        var supported = await directory.SupportedAsync();
        var folder = supported ? await directory.StatusAsync() : null;
        var text = await directory.ReadTextAsync(RecordStoreReader.DefaultFileName);
        var empty = StoreDraft.From(RecordStore.Empty);

        if (text is null) return new Reading(empty, StoreReading.NotRead, null, folder, supported);

        try
        {
            var parsed = RecordStoreReader.Read(text, RecordStoreReader.DefaultFileName);
            return new Reading(StoreDraft.From(parsed), StoreReading.Read, null, folder, supported);
        }
        catch (RecordStoreException ex)
        {
            return new Reading(empty, StoreReading.Refused, ex.Message, folder, supported);
        }
    }

    private sealed record Reading(StoreDraft Draft, StoreReading Kind, string? Problem, string? Folder, bool Supported)
    {
        public EditorState With(
            CatalogueState catalogueState, IReadOnlyList<WorklistRow> rows, bool synthetic,
            IReadOnlyList<FilmEntry> unplaceable) =>
            new(catalogueState, rows, Draft, Kind, Problem, Folder, Supported, synthetic,
                rows.Count(r => r.Band == Confidence.Unmatched),
                rows.Count(r => r.Band == Confidence.Uncertain),
                rows.Count(r => r.Band == Confidence.Confident),
                unplaceable);
    }
}

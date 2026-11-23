using System.Globalization;
using MovieLibrary.Core.Identity;
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

        var bound = Bind(store.Draft, movies);
        var rows = movies.Select(m => Link(m, bound)).ToList();

        // A correction that claimed no row is reported rather than dropped: one he already made, silently
        // invisible, is worse than the drop. "Claimed" is asked of the (entry, position) pair and not of whether
        // its key exists in the catalogue: two entries can publish the same key (the identity key is not part of
        // a matched record's key), the map keeps the first, and asking only whether the key exists would leave
        // the second neither shown nor named. Asked of the entry as a whole it would still miss one record of a
        // multi-record entry whose siblings bound, so the entry is reported when ANY of its records went unbound.
        var claimed = bound.Values.ToHashSet();
        var unplaceable = store.Draft.Store.Films
            .Where(f => f.Records.Count == 0
                        || Enumerable.Range(1, f.Records.Count).Any(p => !claimed.Contains(new RecordSlot(f, p))))
            .ToList();

        return store.With(CatalogueState.Ready, rows, synthetic, unplaceable);
    }

    /// <summary>The ONE place a record's key is decided. Entry present: through the entry. Absent: the fallback.</summary>
    /// <remarks>
    /// The POSITION comes from the same lookup and is no extra work: <see cref="Bind"/> already holds it, and
    /// the save needs it to replace one record of an entry rather than the entry wholesale. A row with no entry
    /// takes 1.
    /// </remarks>
    private static WorklistRow Link(MovieRecord movie, IReadOnlyDictionary<string, RecordSlot> bound)
    {
        var band = ConfidenceBand.Of(movie);
        var found = bound.TryGetValue(movie.K, out var slot);
        var entry = found ? slot.Entry : null;
        var key = entry is null ? RecordLink.IdentityKeyOf(movie) : entry.IdentityKey;
        var position = found ? slot.Position : 1;

        return new WorklistRow(
            movie, key, band, ConfidenceBand.BadgeFor(band), entry, position, IsCorrected(entry, position));
    }

    /// <summary>
    /// Whether the entry's record at <paramref name="position"/> asserts an id, which is what re-badges a row.
    /// An entry that records candidates only keeps the row's original badge: it is the explicitly undecided
    /// state and it genuinely still needs a decision.
    /// </summary>
    public static bool IsCorrected(FilmEntry? entry, int position) =>
        entry is not null
        && position >= 1 && position <= entry.Records.Count
        && entry.Records[position - 1].TmdbId is not null;

    /// <summary>One record of one entry. The entry is compared by reference: two entries are two entries even when equal.</summary>
    private readonly record struct RecordSlot(FilmEntry Entry, int Position)
    {
        public bool Equals(RecordSlot other) => ReferenceEquals(Entry, other.Entry) && Position == other.Position;

        public override int GetHashCode() => HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Entry), Position);
    }

    /// <summary>
    /// Which store record each published record belongs to, keyed on the published key. Two ways in, each with
    /// its own proof, and a record that neither of them can place stays unbound and is NAMED (Unplaceable).
    /// </summary>
    /// <remarks>
    /// <para><b>Why the published key alone is not enough.</b> It is the key the NEXT build will publish, and
    /// movies.json is the PREVIOUS build's output, which does not move. So the moment a correction asserts or
    /// changes a TMDB id -- the whole point of this editor -- the two disagree and the lookup misses: the badge
    /// goes, the row returns to the needs-a-decision count, and reopening the record he saved shows a blank
    /// form (A31, SC 3.3.7). Worse, the save then writes a SECOND entry under a key of the row's own.</para>
    /// <para><b>Pass 2, an unmatched record: a real check.</b> Its key is <c>u-</c>, the first eight characters
    /// of its ROW's identity key, and its position inside its entry. The identity key is the SHEET row's, so no
    /// override can move it -- this is the one fallback <see cref="RecordLink"/> grants outright, and it covers
    /// every record the pipeline matched to nothing. <b>The lookup is the whole check</b>: <c>byHash</c> is
    /// keyed on each entry's own <c>IdentityKey[..RecordKey.HashLength]</c> and asked for exactly the eight
    /// characters the published key carries, so a hit has already compared them. A second
    /// <see cref="RecordLink.Agrees"/> beside it could not return anything but true, and a condition no test
    /// can turn false is a condition no test can protect, so it is not there (m5).</para>
    /// <para><b>Pass 2, a matched record: the hit is the proof.</b> Its key carries no part of its row, so
    /// <see cref="RecordLink.Agrees"/> has nothing to compare. What is left is the recomputed identity key --
    /// guaranteed only for a record with no entry, because an override can replace the title, runtime and
    /// directors it hashed. So it is used as a LOOKUP and never as a key to write under: a hit means the
    /// published fields hash to that entry's own sheet row, which is the same row by definition, and a miss
    /// leaves the record exactly where it was. The entry must have one free slot; two and it says nothing
    /// about which record this is.</para>
    /// <para><b>And there is no third pass, deliberately: a bundle's leftover slot is not tied on a runtime.</b>
    /// One stood here and has been retired. It paired a multi-record entry's free slots with published records
    /// on the runtime the record states, from a pool narrowed to the Apple row's own films. The argument that
    /// retires it is not about the pool. The tie was <c>record.Runtime == movie.Rt</c>, and the pipeline
    /// publishes <c>over?.Runtime ?? sheetRuntime</c>, so a record's own published film carries that record's
    /// PREVIOUS runtime -- which splits every answer the pass could give into exactly two classes.</para>
    /// <list type="number">
    /// <item><b>The runtime is unchanged.</b> The record's own published film is always in the narrowed pool --
    /// it publishes either the record's own stated display title or the row's title like its siblings, with the
    /// row's directors -- and it always fits. So the pass is never choosing between a right answer and nothing:
    /// either that film binds, correctly, or a stranger fits as well, the one-to-one test fails, and the pass
    /// abstains. A stranger can never win uniquely here -- and the own record is a competing pair ONLY while
    /// the runtime holds.</item>
    /// <item><b>The runtime moved.</b> The record's own film is excluded BY THE TIE ITSELF: the record states
    /// the corrected minutes and its film still publishes the previous ones. Every candidate left is therefore,
    /// by construction, not the film this record came from. The pass is not probabilistically wrong in this
    /// class; it is wrong with certainty whenever it answers.</item>
    /// </list>
    /// <para>Narrowing the pool changed how OFTEN a survivor exists, never whether a survivor can be right. And
    /// the class where it is certainly wrong is reached by this editor's own first-class action -- correcting a
    /// runtime. No predicate over published fields rescues it: two films that share a title and a director
    /// (Haneke's Funny Games and his own remake) agree on every row-derived published field there is, and a
    /// simpler variant needs only the same film bought twice, which the pipeline documents as correct. So a
    /// bundle slot neither pass places stays unbound, its entry is NAMED by Unplaceable, and the save side
    /// refuses rather than guesses. Nobody is to reintroduce a pass like it.</para>
    /// <para><b>Abstaining is not the same as being safe to save, and that guard is not here.</b> An unbound
    /// row still carries the key it recomputes for itself and position 1, so the save side has to refuse a
    /// correction whose position nothing proved -- see Place in RecordEditor.razor.</para>
    /// </remarks>
    private static Dictionary<string, RecordSlot> Bind(StoreDraft draft, IReadOnlyList<MovieRecord> movies)
    {
        var bound = new Dictionary<string, RecordSlot>(StringComparer.Ordinal);
        var taken = new HashSet<RecordSlot>();

        void Take(MovieRecord movie, RecordSlot slot)
        {
            bound[movie.K] = slot;
            taken.Add(slot);
        }

        var byPublishedKey = RecordsByPublishedKey(draft);

        foreach (var movie in movies)
            if (byPublishedKey.TryGetValue(movie.K, out var slot) && !taken.Contains(slot))
                Take(movie, slot);

        var byHash = new Dictionary<string, FilmEntry>(StringComparer.Ordinal);
        var byIdentityKey = new Dictionary<string, FilmEntry>(StringComparer.Ordinal);

        foreach (var entry in draft.Store.Films)
        {
            byIdentityKey.TryAdd(entry.IdentityKey, entry);

            // A key the reader accepted is 40 hex characters. One that reached a draft another way may not be.
            if (entry.IdentityKey.Length >= RecordKey.HashLength)
                byHash.TryAdd(entry.IdentityKey[..RecordKey.HashLength], entry);
        }

        foreach (var movie in movies)
            if (!bound.ContainsKey(movie.K) && Rehomed(movie, byHash, byIdentityKey, taken) is { } slot)
                Take(movie, slot);

        return bound;
    }

    /// <summary>The slot a record whose published key has moved belongs to, or null when nothing proves one.</summary>
    private static RecordSlot? Rehomed(
        MovieRecord movie, IReadOnlyDictionary<string, FilmEntry> byHash,
        IReadOnlyDictionary<string, FilmEntry> byIdentityKey, HashSet<RecordSlot> taken)
    {
        if (Unmatched(movie.K) is { } form)
            return byHash.TryGetValue(form.Hash, out var hashed)
                   && form.Position <= hashed.Records.Count
                   && !taken.Contains(new RecordSlot(hashed, form.Position))
                ? new RecordSlot(hashed, form.Position)
                : null;

        if (!byIdentityKey.TryGetValue(RecordLink.IdentityKeyOf(movie), out var entry)) return null;

        var free = Enumerable.Range(1, entry.Records.Count)
            .Select(p => new RecordSlot(entry, p))
            .Where(s => !taken.Contains(s))
            .ToList();

        return free.Count == 1 ? free[0] : null;
    }

    /// <summary>The hash and the position an unmatched key carries, or null for a matched key.</summary>
    private static (string Hash, int Position)? Unmatched(string key)
    {
        if (!key.StartsWith(RecordKey.UnmatchedPrefix, StringComparison.Ordinal)) return null;

        var rest = key[RecordKey.UnmatchedPrefix.Length..];
        if (rest.Length < RecordKey.HashLength) return null;

        var hash = rest[..RecordKey.HashLength];
        var suffix = rest[RecordKey.HashLength..];

        if (suffix.Length == 0) return (hash, 1);

        // RecordKey.ForFilm appends "-n" for the n-th record of one entry, and only for n above 1.
        return suffix[0] == '-'
               && int.TryParse(suffix[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var position)
               && position > 1
            ? (hash, position)
            : null;
    }

    private static Dictionary<string, RecordSlot> RecordsByPublishedKey(StoreDraft draft)
    {
        var map = new Dictionary<string, RecordSlot>(StringComparer.Ordinal);

        foreach (var entry in draft.Store.Films)
            for (var position = 1; position <= entry.Records.Count; position++)
                if (RecordLink.PublishedKeyOf(entry, position) is { } key)
                    map.TryAdd(key, new RecordSlot(entry, position));

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

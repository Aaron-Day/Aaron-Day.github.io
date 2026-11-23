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
    /// Which store record each published record belongs to, keyed on the published key. Three ways in, each
    /// with its own proof, and a record that none of them can place stays unbound and is NAMED (Unplaceable).
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
    /// every record the pipeline matched to nothing.</para>
    /// <para><b>Pass 2, a matched record: the hit is the proof.</b> Its key carries no part of its row, so
    /// <see cref="RecordLink.Agrees"/> has nothing to compare. What is left is the recomputed identity key --
    /// guaranteed only for a record with no entry, because an override can replace the title, runtime and
    /// directors it hashed. So it is used as a LOOKUP and never as a key to write under: a hit means the
    /// published fields hash to that entry's own sheet row, which is the same row by definition, and a miss
    /// leaves the record exactly where it was. The entry must have one free slot; two and it says nothing
    /// about which record this is.</para>
    /// <para><b>Pass 3, a bundle.</b> A multi-record entry's identity key hashed the Apple row -- its title and
    /// its one runtime -- and its records publish per-film runtimes, so nothing recomputes it and pass 2 cannot
    /// help. The tie that survives an id change is the record's own runtime: the pipeline publishes
    /// <c>over?.Runtime ?? sheetRuntime</c>, and the reader makes a runtime compulsory on every record of an
    /// expanded entry. Paired one-to-one and only where it is unambiguous in both directions; anything else
    /// stays unbound, because binding the wrong slot writes a correction over another film.</para>
    /// <para><b>And the pool is one Apple row's own published records, never the catalogue.</b> One integer
    /// cannot carry a catalogue-wide tie, in either direction. Few records in a real library have a runtime no
    /// other record shares, so over the whole catalogue the one-to-one test almost always fails and the pass
    /// does nothing at all; and the runtime is also one of only the two fields that MOVE a matched published
    /// key, so the moment the owner corrects a runtime -- a first-class action of this very editor -- pass 1
    /// misses that record and the tie points at whatever else happens to run that long. That is how a film
    /// from another row took a bundle's slot, wore the Corrected badge, pre-filled the bundle's id and had a
    /// save written into the bundle's record, with Unplaceable saying nothing because the slot was claimed.
    /// So the pool for one entry's free slots is the published records that can have come from the same sheet
    /// row as a slot of that entry pass 1 or pass 2 already placed (<see cref="CouldShareTheRow"/>), and where
    /// no slot of it is placed the entry abstains. Within one Apple row the per-film runtimes are nearly
    /// always distinct, which is the denominator that matters. The sheet ECHO is deliberately not used
    /// instead: it is documented as never consumed for matching, and a placed sibling's published fields need
    /// no such exception.</para>
    /// <para><b>Abstaining is not the same as being safe to save, and that guard is not here.</b> An unbound
    /// row still carries the key it recomputes for itself and position 1, so the save side has to refuse a
    /// correction whose position nothing proved -- see Place in RecordEditor.razor.</para>
    /// </remarks>
    private static Dictionary<string, RecordSlot> Bind(StoreDraft draft, IReadOnlyList<MovieRecord> movies)
    {
        var bound = new Dictionary<string, RecordSlot>(StringComparer.Ordinal);
        var taken = new HashSet<RecordSlot>();

        // The other direction, which pass 3 needs: WHICH published record a slot was placed from. A placed
        // sibling's published title and directors are the only account of the sheet row that does not depend
        // on the echo, and pass 3's pool is built from them.
        var placed = new Dictionary<RecordSlot, MovieRecord>();

        void Take(MovieRecord movie, RecordSlot slot)
        {
            bound[movie.K] = slot;
            taken.Add(slot);
            placed[slot] = movie;
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

        // One list per multi-record entry that still has a free slot, holding ALL of its slots: pass 3 reads
        // the placed ones to find out whose sheet row the free ones belong to.
        var bundles = draft.Store.Films
            .Where(f => f.Records.Count > 1)
            .Select(f => Enumerable.Range(1, f.Records.Count).Select(p => new RecordSlot(f, p)).ToList())
            .Where(slots => slots.Any(s => !taken.Contains(s)))
            .ToList();

        if (bundles.Count == 0) return bound;

        var loose = movies.Where(m => !bound.ContainsKey(m.K)).ToList();

        // Every (slot, published record) pairing the narrowed pool allows at all, built in one pass and only
        // then filtered, so no slot's outcome depends on the order the entries happen to come in.
        var pairs = new List<(RecordSlot Slot, MovieRecord Movie)>();

        foreach (var slots in bundles)
        {
            // WHOSE SHEET ROW THIS IS, said by a slot of this very entry that pass 1 or pass 2 already placed.
            // No placed slot means nothing names the row, so nothing is guessed: the entry's free slots stay
            // unbound and the entry is named by Unplaceable instead.
            var siblings = slots.Where(placed.ContainsKey).Select(s => placed[s]).ToList();
            if (siblings.Count == 0) continue;

            foreach (var slot in slots.Where(s => !taken.Contains(s)))
            {
                var record = Record(slot);

                pairs.AddRange(loose
                    .Where(m => CouldShareTheRow(siblings, record, m) && Fits(record, m))
                    .Select(m => (slot, m)));
            }
        }

        // One slot, one record, in both directions. A published record that two leftover slots both fit says
        // nothing about which of them it is, so it binds to neither and both are named instead.
        foreach (var (slot, movie) in pairs)
            if (pairs.Count(p => p.Slot.Equals(slot)) == 1
                && pairs.Count(p => string.Equals(p.Movie.K, movie.K, StringComparison.Ordinal)) == 1)
                Take(movie, slot);

        return bound;
    }

    private static RecordOverride Record(RecordSlot slot) => slot.Entry.Records[slot.Position - 1];

    /// <summary>The slot a record whose published key has moved belongs to, or null when nothing proves one.</summary>
    private static RecordSlot? Rehomed(
        MovieRecord movie, IReadOnlyDictionary<string, FilmEntry> byHash,
        IReadOnlyDictionary<string, FilmEntry> byIdentityKey, HashSet<RecordSlot> taken)
    {
        if (Unmatched(movie.K) is { } form)
            return byHash.TryGetValue(form.Hash, out var hashed)
                   && RecordLink.Agrees(movie, hashed.IdentityKey) == true
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

    /// <summary>
    /// Whether one store record is the one a published record came from, as far as the two of them can say.
    /// </summary>
    /// <remarks>
    /// The runtime is the tie, because the editor writes it on every record and the pipeline publishes it
    /// (<c>over?.Runtime ?? sheetRuntime</c>). The display title is a veto and not a second tie: an expanded
    /// entry's records carry their own titles -- the Apple row's describes none of its films -- but a record
    /// that states none contradicts nothing.
    /// <para><b>This is the tie WITHIN one Apple row and never across the catalogue.</b> It is asked only of
    /// the pool <see cref="CouldShareTheRow"/> allows, which is why one integer is enough: two films of one
    /// purchase nearly always run different lengths, while two records of the whole library very often do
    /// not -- and the runtime is a field this editor can itself move, which breaks the tie exactly where it
    /// would otherwise be needed.</para>
    /// </remarks>
    private static bool Fits(RecordOverride record, MovieRecord movie) =>
        record.Runtime == movie.Rt
        && (record.Titles?.Display is null
            || string.Equals(record.Titles.Display, movie.T, StringComparison.Ordinal));

    /// <summary>
    /// Whether <paramref name="candidate"/> can have come from the same sheet row as the records already
    /// placed in one entry, which is the only pool pass 3 is allowed to draw from.
    /// </summary>
    /// <remarks>
    /// <para><b>The directors are the row's.</b> MoviePipeline publishes
    /// <c>Dir: row.Directors.Split('&amp;', ...)</c> on every record of the row, so a placed sibling's
    /// directors ARE the row's. A record whose entry overrides Directors publishes different ones and is
    /// excluded here, which loses a binding and can never make a wrong one -- the direction to fail in.</para>
    /// <para><b>The title is the row's only where the record does not replace it.</b> The pipeline publishes
    /// <c>T: row.Title</c>, and its Apply replaces that with the record's own <c>Titles.Display</c> wherever
    /// the record states one -- which record-store.example.json's own three-film bundle does for every record,
    /// because the Apple row's title describes none of the films in it. So a candidate is in the pool when it
    /// publishes a placed sibling's title (neither of them replaced it) or the title THIS record states (this
    /// one did). <see cref="Fits"/> then asks that second equality again as its veto; here it is what lets the
    /// record into the pool at all.</para>
    /// <para><b>Both halves are required.</b> On their own the directors would admit another film by the same
    /// director that happens to run as long, and the title would admit another cut of the same film; together
    /// with <see cref="Fits"/>'s runtime they are three fields where the pass once had one.</para>
    /// </remarks>
    private static bool CouldShareTheRow(
        IReadOnlyList<MovieRecord> placed, RecordOverride record, MovieRecord candidate) =>
        placed.Any(p => p.Dir.SequenceEqual(candidate.Dir, StringComparer.Ordinal))
        && (placed.Any(p => string.Equals(p.T, candidate.T, StringComparison.Ordinal))
            || string.Equals(record.Titles?.Display, candidate.T, StringComparison.Ordinal));

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

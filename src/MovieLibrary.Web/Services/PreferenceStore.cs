using System.Text.Json;
using MovieLibrary.Web.Models;

namespace MovieLibrary.Web.Services;

/// <summary>Which of the two key spaces a title lives in. See <see cref="PreferenceDocument"/>.</summary>
public enum TitleKind
{
    Film,
    Series,
}

/// <summary>Whether an import happened at all, and if not, why -- in the terms the page has to say.</summary>
public enum ImportOutcome
{
    Done,

    /// <summary>Not a document this app wrote, or not readable as one. Nothing was changed.</summary>
    NotOurFile,

    /// <summary>Written by a later version of the site than this one. Nothing was changed.</summary>
    NewerVersion,
}

/// <summary>What an import did, for the sentence the page puts on screen afterwards.</summary>
/// <remarks>
/// Counts and not a bare success, because "imported" on its own tells the owner nothing he can check: the
/// file he picked may have been the empty one from the device he never uses, and the only way he finds out
/// is the page saying how many titles and lists it read. <see cref="DroppedRatings"/> is separate because it
/// is the one number that means something went wrong in a file he may want to look at.
/// </remarks>
public readonly record struct ImportReport(
    ImportOutcome Outcome, int Films = 0, int Series = 0, int Lists = 0, int DroppedRatings = 0);

/// <summary>How a list edit went, in the terms a page has to tell the owner about.</summary>
/// <remarks>
/// A result and not an exception: an empty name and a name he already used are both things a person does,
/// not defects, and each one needs a sentence on screen beside the control he typed into (SC 3.3.1 Error
/// Identification). <c>NoSuchList</c> is the one that should never happen from the UI, and it is here rather
/// than as a throw because a stale list id in a second tab is a real sequence.
/// </remarks>
public enum ListEdit
{
    Done,
    EmptyName,
    DuplicateName,
    NoSuchList,
}

/// <summary>What an import does to what is already here.</summary>
public enum ImportMode
{
    /// <summary>
    /// Keep both sides. A star on either device stays starred, a score in the FILE wins over one here, a
    /// score only here is kept, and list memberships are unioned.
    /// </summary>
    /// <remarks>
    /// <b>A null score in the file does not erase one here, and that asymmetry is deliberate.</b> "I have
    /// not scored this" is what every untouched title in the file says, so letting it win would make
    /// importing a file from a device he barely used wipe the scores on the device he actually uses -- and
    /// the merge is the mode offered precisely to the owner who does not want to lose anything.
    /// <see cref="Replace"/> is there for when he does.
    /// </remarks>
    Merge,

    /// <summary>Discard everything in this browser and keep only the file.</summary>
    Replace,
}

/// <summary>
/// The owner's own state over the catalogue -- starred titles, his scores, his lists -- held in memory and
/// written through to the browser behind it.
/// </summary>
/// <remarks>
/// <para>
/// <b>READS ARE SYNCHRONOUS AND THAT IS THE WHOLE SHAPE OF THIS CLASS.</b> A star has to flip in the frame
/// the owner clicks it: a toggle whose <c>aria-pressed</c> waited on a round trip through interop would
/// announce the OLD state to a screen reader, because the attribute is read when focus and state settle and
/// not when the write lands. So every query here answers from the document already in memory, and the write
/// is what is awaited afterwards -- which is also why a failed write is an exception the caller reports
/// rather than a return value it may ignore.
/// </para>
/// <para>
/// <b>An entry that says nothing is REMOVED.</b> Unstarring a title does not leave <c>{"fav":false}</c>
/// behind: the document is what gets exported to another device and read back by a build that may be years
/// newer, and a file that remembers every title the owner ever touched is both larger and a worse record of
/// what he meant than one holding only what he said.
/// </para>
/// <para>
/// <b>PUBLIC, and only because a component injects it.</b> <c>@inject</c> generates a public property on
/// the generated component class, so an internal service type is CS0053 Inconsistent accessibility -- not a
/// warning this project could waive -- and the Razor SDK offers no way to make the component internal
/// instead. <c>TileFact</c> carries the same note for the same reason, one layer out. The interface and the
/// exception follow: a public class cannot take an internal constructor parameter (CS0051) and cannot throw
/// a type a caller is unable to name.
/// </para>
/// </remarks>
public sealed class PreferenceStore(IPreferenceStorage storage)
{
    /// <summary>
    /// How the document is written, and it is NOT the web defaults.
    /// </summary>
    /// <remarks>
    /// Every property name is spelled by a <c>JsonPropertyName</c> on the model, so a naming policy here
    /// would be a second answer to the same question -- and the one that lost would be invisible until a
    /// document written by one build failed to read in another. Nothing is indented: this is a machine
    /// document in a 5 MB budget, and the export is the same bytes.
    /// </remarks>
    internal static readonly JsonSerializerOptions Format = new() { WriteIndented = false };

    private PreferenceDocument document = new();
    private bool loaded;

    /// <summary>Whether <see cref="LoadAsync"/> has run. False means every query is answering from a blank document.</summary>
    public bool Loaded => loaded;

    /// <summary>The owner's lists, in the order he made them.</summary>
    public IReadOnlyList<TitleList> Lists => document.Lists;

    /// <summary>
    /// Reads what this browser holds. Once: a second call is a no-op, so a page that loads on navigation
    /// cannot throw away an edit made since.
    /// </summary>
    /// <remarks>
    /// A document this build cannot read is REPLACED rather than repaired, and that is a deliberate loss:
    /// the alternative is guessing at a shape some other build wrote, and a wrong guess puts a favourite on
    /// the wrong film. A version NEWER than this build's is the case that will really happen -- the owner
    /// opens an older tab -- and it is the one case where keeping the file untouched matters most, so it is
    /// read as far as it parses and never written back by this build.
    /// </remarks>
    public async Task LoadAsync()
    {
        if (loaded) return;

        loaded = true;
        document = Parse(await storage.ReadAsync()) ?? new PreferenceDocument();
    }

    /// <summary>
    /// Reads what this browser holds WITHOUT awaiting, and says whether it managed to. Call it from
    /// <c>OnInitialized</c>, before the first await, and fall back to <see cref="LoadAsync"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="IPreferenceStorage.ReadNow"/> carries the argument for why this exists at all: sixty
    /// toggles may not flip after the first paint. False means this runtime cannot read synchronously, not
    /// that the browser holds nothing -- so a caller that treated false as "nothing saved" would show every
    /// title unstarred for ever in exactly the hosts where the fallback was supposed to cover it.
    /// </remarks>
    public bool LoadNow()
    {
        if (loaded) return true;

        if (storage.ReadNow() is not { } json) return false;

        loaded = true;
        document = Parse(json) ?? new PreferenceDocument();

        return true;
    }

    /// <summary>What the owner has saved about one title, which is an empty entry where he has saved nothing.</summary>
    public TitlePreference For(TitleKind kind, string key) =>
        Map(kind).TryGetValue(key, out var found) ? found : new TitlePreference();

    public bool IsFavourite(TitleKind kind, string key) => For(kind, key).Favourite;

    public int? RatingOf(TitleKind kind, string key) => For(kind, key).Rating;

    public bool IsInList(TitleKind kind, string key, string listId) =>
        For(kind, key).ListIds.Contains(listId, StringComparer.Ordinal);

    private Dictionary<string, TitlePreference> Map(TitleKind kind) =>
        kind == TitleKind.Film ? document.Films : document.Series;

    /// <summary>Stars or unstars a title.</summary>
    public Task SetFavouriteAsync(TitleKind kind, string key, int tmdbId, bool favourite) =>
        EditAsync(kind, key, tmdbId, entry => entry with { Favourite = favourite });

    /// <summary>Sets the owner's own score, or clears it with null. See <see cref="TitlePreference.Rating"/>.</summary>
    public Task SetRatingAsync(TitleKind kind, string key, int tmdbId, int? rating)
    {
        if (rating is { } value and (< TitlePreference.MinRating or > TitlePreference.MaxRating))
            throw new ArgumentOutOfRangeException(
                nameof(rating), value,
                $"A rating is {TitlePreference.MinRating} to {TitlePreference.MaxRating} on TMDB's own scale, "
                + "or null for a title the owner has not scored. Zero is a score and is not the same as null.");

        return EditAsync(kind, key, tmdbId, entry => entry with { Rating = rating });
    }

    /// <summary>Puts a title into one of the owner's lists, or takes it out.</summary>
    /// <remarks>
    /// Membership is stored on the TITLE and not on the list, so a list is a name and nothing else. The
    /// other arrangement -- ids of titles held on each list -- makes "which lists is this in?", the question
    /// every record page asks, a walk of every list the owner has.
    /// </remarks>
    public Task SetMembershipAsync(TitleKind kind, string key, int tmdbId, string listId, bool member)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(listId);

        if (member && !document.Lists.Any(list => string.Equals(list.Id, listId, StringComparison.Ordinal)))
            throw new ArgumentException(
                $"No list has the id '{listId}'. A title can only be added to a list that exists, or the "
                + "entry would carry a membership nothing on screen could ever show or undo.", nameof(listId));

        return EditAsync(kind, key, tmdbId, entry =>
        {
            var ids = entry.ListIds.Where(id => !string.Equals(id, listId, StringComparison.Ordinal));
            return entry with { Lists = member ? [.. ids, listId] : [.. ids] };
        });
    }

    /// <summary>
    /// Applies one change to one title, drops the entry if it now says nothing, and writes the document.
    /// </summary>
    /// <remarks>
    /// The TMDB id is recorded on every edit rather than only on the first, because the id a record carries
    /// can be corrected by the record store after an entry was saved -- and the fallback is worth nothing if
    /// it holds the id the catalogue USED to think was right. 0 is written as 0: an unmatched record has no
    /// id, and pretending otherwise would hand the recovery walk a value it could match the wrong film on.
    /// </remarks>
    private async Task EditAsync(
        TitleKind kind, string key, int tmdbId, Func<TitlePreference, TitlePreference> edit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var map = Map(kind);
        TitlePreference? before = map.TryGetValue(key, out var found) ? found : null;
        var edited = edit(For(kind, key)) with { TmdbId = tmdbId };

        if (edited.IsEmpty) map.Remove(key);
        else map[key] = edited;

        try
        {
            await SaveAsync();
        }
        catch (PreferenceStorageException)
        {
            // PUT THE DOCUMENT BACK. The caller reverts what is on screen -- FavouriteButton un-presses
            // itself -- and a document that kept the change would make that revert a lie the moment
            // anything read the store again: a tile re-rendered by paging, the same title's record page,
            // or the next SUCCESSFUL write, which would persist the change the browser refused. The entry
            // is restored exactly as it was, including its absence.
            if (before is null) map.Remove(key);
            else map[key] = before;

            throw;
        }
    }

    /// <summary>
    /// Makes a list. The name is trimmed, may not be empty, and may not repeat one the owner already has.
    /// </summary>
    /// <remarks>
    /// <b>Duplicate names are refused rather than allowed and disambiguated.</b> Membership is a set of
    /// checkboxes on a record page, and two of them reading "Christmas" is a control a screen-reader reader
    /// cannot tell apart at all -- it hears the same name twice and has nothing to choose by (SC 4.1.2 Name,
    /// Role, Value asks for a name; it does not help if two controls share one). Case is ignored for the
    /// comparison and kept in what is stored: "christmas" is the same list as "Christmas", spelled his way.
    /// </remarks>
    public async Task<(ListEdit Result, TitleList? List)> CreateListAsync(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();

        if (trimmed.Length == 0) return (ListEdit.EmptyName, null);
        if (Named(trimmed) is not null) return (ListEdit.DuplicateName, null);

        var list = new TitleList(NewId(), trimmed);
        document.Lists.Add(list);
        await SaveAsync();

        return (ListEdit.Done, list);
    }

    /// <summary>Renames a list, under the same rules as making one.</summary>
    public async Task<ListEdit> RenameListAsync(string listId, string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        var index = document.Lists.FindIndex(l => string.Equals(l.Id, listId, StringComparison.Ordinal));

        if (index < 0) return ListEdit.NoSuchList;
        if (trimmed.Length == 0) return ListEdit.EmptyName;

        // Its own name is not a duplicate of itself: correcting the case of a list's name is a rename.
        if (Named(trimmed) is { } clash && !string.Equals(clash.Id, listId, StringComparison.Ordinal))
            return ListEdit.DuplicateName;

        document.Lists[index] = document.Lists[index] with { Name = trimmed };
        await SaveAsync();

        return ListEdit.Done;
    }

    /// <summary>
    /// Deletes a list AND every title's membership of it.
    /// </summary>
    /// <remarks>
    /// Both halves, in one write. Leaving the memberships behind would keep a dangling id on every title
    /// that was in it -- invisible, since nothing names that list any more, and resurrected the day an id
    /// was ever reused. Titles whose only saved state was that membership become empty and are dropped by
    /// the same rule that drops an unstarred title.
    /// </remarks>
    public async Task<ListEdit> DeleteListAsync(string listId)
    {
        var index = document.Lists.FindIndex(l => string.Equals(l.Id, listId, StringComparison.Ordinal));
        if (index < 0) return ListEdit.NoSuchList;

        document.Lists.RemoveAt(index);

        foreach (var map in new[] { document.Films, document.Series })
        {
            foreach (var (key, entry) in map.ToList())
            {
                if (!entry.ListIds.Contains(listId, StringComparer.Ordinal)) continue;

                var kept = entry with
                {
                    Lists = [.. entry.ListIds.Where(id => !string.Equals(id, listId, StringComparison.Ordinal))],
                };

                if (kept.IsEmpty) map.Remove(key);
                else map[key] = kept;
            }
        }

        await SaveAsync();
        return ListEdit.Done;
    }

    /// <summary>
    /// Re-files entries whose published key has MOVED, using the TMDB id saved beside it. Returns how many.
    /// </summary>
    /// <param name="known">Whether the catalogue still carries this key.</param>
    /// <param name="keyOf">The key the catalogue files this TMDB id under now, or null if it carries none or several.</param>
    /// <remarks>
    /// <para>
    /// <b>This is the method <c>RecordKey</c>'s remarks ask for, and without it a favourite rots.</b> A
    /// matched key is <c>{tmdbId}-{runtimeMinutes}</c>, so Apple re-encoding a file and the sheet's runtime
    /// moving one minute re-keys the record -- the match holds, the title is the same film on screen, and
    /// the entry saved under the old key belongs to nothing. Nothing on screen would say so: the star would
    /// simply be off, which reads as "I never starred this".
    /// </para>
    /// <para>
    /// <b>Only where the id identifies exactly ONE record.</b> The owner owns several cuts of some films and
    /// every cut carries the same TMDB id, so an id matching two records says nothing about which of them
    /// the entry meant -- and the wrong guess moves his rating of the theatrical cut onto the extended one.
    /// That ambiguity is what <paramref name="keyOf"/> returns null for; such an entry is left where it is,
    /// because a later edit to the sheet can still resolve it and a deletion cannot be undone.
    /// </para>
    /// <para>
    /// An unmatched record has no id at all (0 is written for them), so its entry can never be recovered
    /// this way. That is the trade <c>RecordKey</c> records for the hash form: it re-keys on any edit to the
    /// title, runtime or directors, and a record in that state exists to be fixed.
    /// </para>
    /// </remarks>
    public async Task<int> RecoverMovedKeysAsync(
        Func<TitleKind, string, bool> known, Func<TitleKind, int, string?> keyOf)
    {
        ArgumentNullException.ThrowIfNull(known);
        ArgumentNullException.ThrowIfNull(keyOf);

        var moved = 0;

        foreach (var kind in new[] { TitleKind.Film, TitleKind.Series })
        {
            var map = Map(kind);

            foreach (var (key, entry) in map.ToList())
            {
                if (known(kind, key) || entry.TmdbId <= 0) continue;
                if (keyOf(kind, entry.TmdbId) is not { } fresh) continue;
                if (string.Equals(fresh, key, StringComparison.Ordinal)) continue;

                // The destination can already hold an entry -- two keys of one id, one of which the
                // catalogue now agrees with -- so the two are merged on the import's own rules rather than
                // one overwriting the other.
                map[fresh] = map.TryGetValue(fresh, out var there) ? Union(there, entry) : entry;
                map.Remove(key);
                moved++;
            }
        }

        if (moved > 0) await SaveAsync();

        return moved;
    }

    /// <summary>Both sides kept, on <see cref="ImportMode.Merge"/>'s rules: see there for why nulls lose.</summary>
    private static TitlePreference Union(TitlePreference here, TitlePreference other)
    {
        string[] ids = [.. here.ListIds.Concat(other.ListIds).Distinct(StringComparer.Ordinal)];

        return here with
        {
            TmdbId = here.TmdbId > 0 ? here.TmdbId : other.TmdbId,
            Favourite = here.Favourite || other.Favourite,
            Rating = here.Rating ?? other.Rating,
            Lists = ids.Length == 0 ? null : ids,
        };
    }

    /// <summary>The document exactly as it is stored, which is what the owner downloads.</summary>
    public string Export() => JsonSerializer.Serialize(document, Format);

    /// <summary>
    /// Reads a document the owner exported, from this build or another, and says what it did.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A file from a NEWER build is refused and nothing is touched.</b> This build cannot know what a
    /// later shape means, and the failure mode of guessing is a favourite landing on the wrong title -- so
    /// the sentence on screen has to be "this file was written by a newer version of the site", which is
    /// something the owner can act on, and not a merge that half worked.
    /// </para>
    /// <para>
    /// <b>A score outside TMDB's scale is dropped; the entry is kept.</b> The alternative -- refusing the
    /// whole file over one bad field -- loses 1,500 good entries to one typo in a file someone hand-edited,
    /// and the count comes back in the report so the page can say how many were dropped rather than
    /// silently mangling them.
    /// </para>
    /// </remarks>
    public async Task<ImportReport> ImportAsync(string json, ImportMode mode)
    {
        if (Parse(json) is not { } incoming) return new ImportReport(ImportOutcome.NotOurFile);

        if (incoming.Version > PreferenceDocument.CurrentVersion)
            return new ImportReport(ImportOutcome.NewerVersion);

        var dropped = Sanitise(incoming);

        if (mode == ImportMode.Replace)
        {
            document = incoming;
            await SaveAsync();

            return new ImportReport(
                ImportOutcome.Done, incoming.Films.Count, incoming.Series.Count, incoming.Lists.Count, dropped);
        }

        var lists = MergeLists(incoming.Lists);
        var films = MergeTitles(document.Films, incoming.Films, lists);
        var series = MergeTitles(document.Series, incoming.Series, lists);

        await SaveAsync();

        return new ImportReport(ImportOutcome.Done, films, series, lists.Added, dropped);
    }

    /// <summary>
    /// Drops the fields a hand-edited file can carry that this app would never write. Returns how many.
    /// </summary>
    private static int Sanitise(PreferenceDocument incoming)
    {
        var dropped = 0;

        foreach (var map in new[] { incoming.Films, incoming.Series })
        {
            foreach (var (key, entry) in map.ToList())
            {
                var rating = entry.Rating is { } r and >= TitlePreference.MinRating and <= TitlePreference.MaxRating
                    ? entry.Rating
                    : null;

                if (rating != entry.Rating) dropped++;

                var cleaned = entry with { Rating = rating };

                if (cleaned.IsEmpty) map.Remove(key);
                else map[key] = cleaned;
            }
        }

        return dropped;
    }

    private TitleList? Named(string name) =>
        document.Lists.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// An id for a new list: short, opaque and checked against the ones in hand.
    /// </summary>
    /// <remarks>
    /// Eight hex characters, as <c>RecordKey</c> uses for an unmatched film and for the same reason -- it is
    /// short enough to read in an exported file. The uniqueness check is not ceremony: these are generated
    /// on one device and MERGED with ids generated on another, so the collision this guards against is two
    /// browsers independently minting the same id, not one browser doing it twice.
    /// </remarks>
    /// <summary>
    /// Brings the file's lists in, and says which incoming id means which list HERE.
    /// </summary>
    /// <remarks>
    /// <b>The same list made twice on two devices is one list, matched by NAME.</b> Its ids differ -- they
    /// were minted independently -- so matching by id alone would leave the owner with two "Christmas"
    /// checkboxes on every record page, which is the duplicate name <c>CreateListAsync</c> refuses outright.
    /// The local id wins and the incoming memberships are remapped onto it, so the titles from both devices
    /// end up in the one list he thinks of as his.
    /// </remarks>
    private ListMerge MergeLists(IEnumerable<TitleList> incoming)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var added = 0;

        foreach (var list in incoming)
        {
            var name = (list.Name ?? string.Empty).Trim();
            if (name.Length == 0 || string.IsNullOrWhiteSpace(list.Id)) continue;

            if (Named(name) is { } mine)
            {
                map[list.Id] = mine.Id;
                continue;
            }

            // Its own id is kept where nothing here uses it, so a second import of the same file is a no-op
            // rather than a third copy of every list.
            var id = document.Lists.Any(l => string.Equals(l.Id, list.Id, StringComparison.Ordinal))
                ? NewId()
                : list.Id;

            document.Lists.Add(new TitleList(id, name));
            map[list.Id] = id;
            added++;
        }

        return new ListMerge(map, added);
    }

    private readonly record struct ListMerge(Dictionary<string, string> Ids, int Added)
    {
        /// <summary>The incoming id, translated to the one this browser uses, or null where the list was skipped.</summary>
        public string? Mine(string incoming) => Ids.TryGetValue(incoming, out var mine) ? mine : null;
    }

    /// <summary>Merges one key space, and returns how many titles the file touched.</summary>
    private static int MergeTitles(
        Dictionary<string, TitlePreference> mine, Dictionary<string, TitlePreference> theirs, ListMerge lists)
    {
        foreach (var (key, incoming) in theirs)
        {
            var here = mine.TryGetValue(key, out var found) ? found : new TitlePreference();

            // OfType rather than Where(id => id is not null): the Where leaves the element type nullable and
            // the compiler is right to refuse it. A list the merge skipped (no name, no id) maps to null,
            // and a membership of it is dropped with it rather than kept as an id nothing names.
            var ids = incoming.ListIds
                .Select(lists.Mine)
                .OfType<string>()
                .Concat(here.ListIds)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            mine[key] = here with
            {
                // The id the FILE carries, where it has one: see EditAsync on why a corrected id must win.
                TmdbId = incoming.TmdbId > 0 ? incoming.TmdbId : here.TmdbId,
                Favourite = here.Favourite || incoming.Favourite,
                Rating = incoming.Rating ?? here.Rating,
                Lists = ids.Length == 0 ? null : ids,
            };
        }

        return theirs.Count;
    }

    private string NewId()
    {
        string id;
        do
        {
            id = Guid.NewGuid().ToString("n")[..8];
        }
        while (document.Lists.Any(l => string.Equals(l.Id, id, StringComparison.Ordinal)));

        return id;
    }

    private Task SaveAsync() => storage.WriteAsync(JsonSerializer.Serialize(document, Format));

    private static PreferenceDocument? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            return JsonSerializer.Deserialize<PreferenceDocument>(json, Format);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

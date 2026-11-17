using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MovieLibrary.Core.Store;

/// <summary>Parses and validates <c>record-store.json</c>. Every rejection names the file, the entry and the fix.</summary>
/// <remarks>
/// <para><b>Comments and trailing commas are allowed.</b> This file is hand-edited by its owner, by design --
/// that is the whole point of Phase 1 shipping before the editor -- so the two things that make hand-editing
/// JSON bearable are on. Nothing else is relaxed: an unknown property is still an error, because a typed key
/// is a correction that would otherwise be dropped in silence.</para>
/// <para><b>Validation is not optional politeness.</b> Each rule below exists because the alternative is a
/// published catalogue that is quietly wrong: a store entry that binds to nothing, two entries fighting over
/// one row, an expanded row whose records all claim the bundle's runtime.</para>
/// </remarks>
public static class RecordStoreReader
{
    /// <summary>The default path, relative to the repository root, where the build looks for the store.</summary>
    public const string DefaultFileName = "record-store.json";

    /// <summary>Characters in an identity key: <c>Convert.ToHexStringLower(SHA1.HashData(...))</c>.</summary>
    private const int KeyLength = 40;

    /// <summary>
    /// Where the format is written down in full. Named in the parse failure because nothing else points a
    /// hand-editor at it: Phase 1 ships no README, no <c>record-store.example.json</c> and no seed path.
    /// </summary>
    private const string SpecPath = "docs/superpowers/specs/2026-11-16-record-store-design.md";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        // Without this, `required` only demands that a property is PRESENT, and a JSON null walks through.
        RespectNullableAnnotations = true,
    };

    /// <summary>The store at <paramref name="path"/>, or <see cref="RecordStore.Empty"/> when there is no file there.</summary>
    public static async Task<RecordStore> ReadFileAsync(string path, CancellationToken ct)
    {
        // File.Exists is false for a folder, so without this a --store that names a folder would read as "no
        // store" and every correction in the owner's file would be dropped without a word.
        if (Directory.Exists(path))
            throw new RecordStoreException(
                $"'{path}' is a folder, not a file. Point the record store at the JSON file itself, " +
                $"for example '{Path.Combine(path, DefaultFileName)}'.");

        if (!File.Exists(path)) return RecordStore.Empty;

        string json;
        try
        {
            json = await File.ReadAllTextAsync(path, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The file or the environment, and the owner can act on it -- unlike the fault Read's catch-all reports.
            throw new RecordStoreException(
                $"'{path}' exists but could not be opened: {ex.GetType().Name}: {ex.Message} Check that no other " +
                "program (an editor, a sync client, antivirus) has it locked, that this account may read it, " +
                "and that the path names a file.", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new RecordStoreException(
                $"Unexpected internal error while opening '{path}' as a record store: " +
                $"{ex.GetType().Name}: {ex.Message} This is a fault in the reader, not in the file's contents, " +
                "and editing the file will not fix it.", ex);
        }

        return Read(json, path);
    }

    /// <param name="whatItIs">The file's path, or any name the owner would recognise. Appears in every message.</param>
    public static RecordStore Read(string json, string whatItIs)
    {
        try
        {
            return ReadCore(json, whatItIs);
        }
        catch (RecordStoreException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The safety net: whatever happens in here, nothing raw escapes and the run stops with the file named.
            // Every rejection the owner can act on is a RecordStoreException thrown above, so reaching this line
            // means the READER failed, not the file. Say so, or a CI log sends someone hunting through the JSON.
            throw new RecordStoreException(
                $"Unexpected internal error while reading '{whatItIs}' as a record store: " +
                $"{ex.GetType().Name}: {ex.Message} This is a fault in the reader, not in the file's contents, " +
                "and editing the file will not fix it.", ex);
        }
    }

    private static RecordStore ReadCore(string json, string whatItIs)
    {
        RecordStore? store;
        try
        {
            store = JsonSerializer.Deserialize<RecordStore>(json, Options);
        }
        catch (JsonException ex)
        {
            // System.Text.Json's own wording is precise and assumes you know the shape. Nothing else in Phase 1
            // writes that shape down where a hand-editor meets it: there is no README, no example file and no
            // seed path (Write exists but only a test calls it), so this is where the shape gets named.
            throw new RecordStoreException(
                $"'{whatItIs}' could not be read as a record store: {ex.Message} The file is " +
                "{ \"version\": 1, \"films\": [...], \"series\": [...] }; a film entry takes \"identityKey\", " +
                "\"sheet\", \"note\" and \"records\", a series entry the same with one \"record\"; a record " +
                "takes \"tmdbId\" or \"candidates\" but never both, plus \"runtime\", \"titles\", " +
                "\"year\", \"directors\", \"appleGenre\", \"purchaseInfo\" and \"extra\", all " +
                "optional; and \"sheet\" takes " +
                "\"title\", \"runtime\" and \"directors\". Every name is camelCase, comments and trailing " +
                "commas are allowed, and the full format is " +
                $"{SpecPath} section 5. Run --print-keys for a row's \"identityKey\".", ex);
        }

        if (store is null)
            throw new RecordStoreException($"'{whatItIs}' could not be read as a record store: the body was JSON null.");

        if (store.Version != RecordStore.CurrentVersion)
            throw new RecordStoreException(
                $"'{whatItIs}' declares version {store.Version.ToString(CultureInfo.InvariantCulture)}, and this " +
                $"build reads version {RecordStore.CurrentVersion.ToString(CultureInfo.InvariantCulture)}. " +
                "A missing version reads as 0. Set \"version\": 1.");

        Validate(store, whatItIs);
        return store;
    }

    /// <summary>The store as JSON, in the same shape <see cref="Read"/> accepts. Used to seed and to rewrite the file.</summary>
    public static string Write(RecordStore store) => JsonSerializer.Serialize(store, Options);

    private static void Validate(RecordStore store, string file)
    {
        RejectNullEntries(store.Films, file, "film");
        RejectNullEntries(store.Series, file, "series");

        RejectDuplicateKeys(store.Films.Select(f => (f.IdentityKey, Name(f.Sheet, f.IdentityKey))), file, "film");
        RejectDuplicateKeys(store.Series.Select(s => (s.IdentityKey, Name(s.Sheet, s.IdentityKey))), file, "series");

        foreach (var entry in store.Films)
        {
            var where = $"In '{file}', film entry {Name(entry.Sheet, entry.IdentityKey)}";
            RequireKey(entry.IdentityKey, where);

            if (entry.Records.Count == 0)
                throw new RecordStoreException(
                    $"{where} has no records. An entry must carry at least one record, " +
                    "or it says nothing; delete the entry instead.");

            for (var i = 0; i < entry.Records.Count; i++)
            {
                var position = (i + 1).ToString(CultureInfo.InvariantCulture);
                if (entry.Records[i] is null)
                    throw new RecordStoreException(
                        $"{where}, record {position} is null. Remove it, or write the record's fields.");

                RequireUsableRecord(entry.Records[i], $"{where}, record {position}");
            }

            // An expanded row cannot share one Apple runtime: the key is {id}-{runtime}, and a record carrying
            // the bundle's length would publish a false runtime on its card.
            if (entry.Records.Count > 1)
            {
                if (entry.Records.Any(r => r.Runtime is null))
                    throw new RecordStoreException(
                        $"{where} expands into {entry.Records.Count.ToString(CultureInfo.InvariantCulture)} records, " +
                        "so every one of them needs its own \"runtime\" in minutes. " +
                        "The sheet has one runtime for the whole Apple entry and it is not any film's.");

                // A field-only record has nothing to look up: the pipeline searches TMDB from the SHEET row, and
                // an expanded row's sheet title and bundle runtime describe none of its films. Four such records
                // would run the same hopeless search four times.
                if (entry.Records.Any(r => r.TmdbId is null && r.Candidates is null))
                    throw new RecordStoreException(
                        $"{where} expands into several records, so each one needs a \"tmdbId\" or a " +
                        "\"candidates\" list. There is nothing to search for: the sheet has one title and one " +
                        "runtime for the whole Apple entry, and they describe none of the films in it.");

                // RecordKey.ForFilm is {tmdbId}-{runtime} for a matched record and reads `position` only for an
                // UNMATCHED one, so two matched records agreeing on both fields publish one key: the reconciler
                // drops one and the entry quietly publishes fewer records than it lists. Filling in a four-film
                // bundle by copying the first record and editing it is exactly how that slip happens. A record
                // offering candidates is not checked, because it publishes unmatched and position parts those.
                var repeated = entry.Records
                    .Where(r => r.TmdbId is not null)
                    .GroupBy(r => (r.TmdbId, r.Runtime))
                    .FirstOrDefault(g => g.Count() > 1);

                if (repeated is not null)
                    throw new RecordStoreException(
                        $"{where} has {repeated.Count().ToString(CultureInfo.InvariantCulture)} records with the " +
                        $"same \"tmdbId\" ({repeated.Key.TmdbId?.ToString(CultureInfo.InvariantCulture)}) and " +
                        $"\"runtime\" ({repeated.Key.Runtime?.ToString(CultureInfo.InvariantCulture)}). A matched " +
                        "record's published key is its id and its runtime and nothing else, so those records would " +
                        "publish ONE key, the catalogue would keep one of them, and the entry would publish fewer " +
                        "films than it lists. Give each record the id and the runtime of the film it is, or delete " +
                        "the copy.");
            }
        }

        foreach (var entry in store.Series)
        {
            var where = $"In '{file}', series entry {Name(entry.Sheet, entry.IdentityKey)}";
            RequireKey(entry.IdentityKey, where);
            RequireUsableRecord(entry.Record, where);

            var filmOnly = new (string Name, object? Value)[]
            {
                ("runtime", entry.Record.Runtime), ("year", entry.Record.Year),
                ("directors", entry.Record.Directors), ("purchaseInfo", entry.Record.PurchaseInfo),
            }.Where(f => f.Value is not null).Select(f => f.Name).ToList();

            if (filmOnly.Count > 0)
                throw new RecordStoreException(
                    $"{where} sets {string.Join(", ", filmOnly)}, which a series record has no field for " +
                    "(runtime and year are per-episode, and a series has no directors or purchase info of its own). " +
                    "Remove them, or move this entry to \"films\" if it is a film.");
        }
    }

    private static void RequireUsableRecord(RecordOverride record, string where)
    {
        if (record == new RecordOverride())
            throw new RecordStoreException(
                $"{where} overrides nothing. An entry which corrects nothing should be deleted; " +
                "otherwise give it a \"tmdbId\", \"candidates\" or another field.");

        if (record.TmdbId is not null && record.Candidates is not null)
            throw new RecordStoreException(
                $"{where} sets both \"tmdbId\" and \"candidates\". They mean opposite things: an id is a decision, " +
                "candidates are the absence of one. Keep whichever is true.");

        if (record.TmdbId is { } id and <= 0)
            throw new RecordStoreException(
                $"{where} has \"tmdbId\": {id.ToString(CultureInfo.InvariantCulture)}. A TMDB id is positive; " +
                "to say \"no match\", leave the record's id out altogether.");

        if (record.Candidates is { } candidates)
        {
            if (candidates.Length == 0)
                throw new RecordStoreException(
                    $"{where} has an empty \"candidates\" list. Give at least one candidate id, or remove the property.");

            if (candidates.Any(c => c <= 0))
                throw new RecordStoreException($"{where} has a candidate id that is not positive.");

            var repeated = candidates.GroupBy(c => c).FirstOrDefault(g => g.Count() > 1);
            if (repeated is not null)
                throw new RecordStoreException(
                    $"{where} lists candidate {repeated.Key.ToString(CultureInfo.InvariantCulture)} twice.");
        }

        if (record.Runtime is { } runtime and <= 0)
            throw new RecordStoreException(
                $"{where} has \"runtime\": {runtime.ToString(CultureInfo.InvariantCulture)}. " +
                "A runtime is in whole minutes and is positive.");

        if (record.Year is { } year and (< 1870 or > 2200))
            throw new RecordStoreException(
                $"{where} has \"year\": {year.ToString(CultureInfo.InvariantCulture)}, which is not a release year. " +
                "Cinema starts in the 1870s.");

        if (record.Extra is { } extra && extra.Keys.Any(string.IsNullOrWhiteSpace))
            throw new RecordStoreException(
                $"{where} has an \"extra\" key that is blank. Every extra field needs a name a human can read, " +
                "because the name is what the site would show.");
    }

    private static void RequireKey(string key, string where)
    {
        if (key.Length == KeyLength && key.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f')) return;

        throw new RecordStoreException(
            $"{where} has an \"identityKey\" that is not one: '{key}'. An identity key is " +
            $"{KeyLength.ToString(CultureInfo.InvariantCulture)} lower-case hex characters, as " +
            "IdentityKey.ForFilm and IdentityKey.ForSeries produce. A key of any other shape can never bind " +
            "to a row, so the entry would be silently ignored.");
    }

    private static void RejectNullEntries<T>(IReadOnlyList<T> entries, string file, string kind)
    {
        for (var i = 0; i < entries.Count; i++)
            if (entries[i] is null)
                throw new RecordStoreException(
                    $"In '{file}', {kind} entry {(i + 1).ToString(CultureInfo.InvariantCulture)} is null. " +
                    "Remove it, or write the entry.");
    }

    private static void RejectDuplicateKeys(IEnumerable<(string Key, string Name)> entries, string file, string kind)
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (key, name) in entries)
        {
            if (seen.TryGetValue(key, out var first))
                throw new RecordStoreException(
                    $"In '{file}', two {kind} entries carry the same identity key: {first} and {name}. " +
                    "One row cannot have two sets of corrections; merge them into one entry, whose " +
                    "\"records\" list may hold several records.");

            seen[key] = name;
        }
    }

    /// <summary>
    /// The entry as a human knows it: its sheet title AND its key. The title alone cannot tell a remake pair
    /// apart, and the key alone is not something a person recognises.
    /// </summary>
    private static string Name(SheetEcho sheet, string key)
        => string.IsNullOrWhiteSpace(sheet.Title) ? $"'{key}'" : $"'{sheet.Title}' (key {key})";
}

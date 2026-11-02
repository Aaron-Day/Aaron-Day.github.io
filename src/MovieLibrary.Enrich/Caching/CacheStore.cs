using System.Text.Json;
using MovieLibrary.Core.Caching;

namespace MovieLibrary.Enrich.Caching;

/// <summary>
/// The on-disk enrichment cache: an <c>index.json</c> of <see cref="CacheEntry"/> records plus payload files.
/// </summary>
/// <remarks>
/// <para><b>Not thread-safe. Call it sequentially.</b> <see cref="Resolve"/> is not a pure read: an alias hit
/// writes the new key back into the dictionary. Concurrent calls race, and iterating
/// <see cref="Entries"/> (a live view, not a copy) while calling <see cref="Resolve"/> throws
/// <see cref="InvalidOperationException"/>. Snapshot first: <c>store.Entries.ToList()</c>.</para>
/// <para><see cref="SaveAsync"/> truncates <c>index.json</c> in place and is not atomic. A crash mid-write
/// leaves a truncated file, and <see cref="LoadAsync"/> does NOT recover from it: it throws
/// <see cref="InvalidDataException"/> naming the file. Delete <c>index.json</c> (or restore it from git) and the
/// next run rebuilds the cache at full API cost; acceptable for a single-user tool, but callers must let the
/// exception reach the operator rather than swallow it.</para>
/// <para>The <c>relativePath</c> given to <see cref="ReadPayloadAsync{T}"/> and <see cref="WritePayloadAsync{T}"/>
/// must be a trusted, caller-constructed value (for example <c>movies/{tmdbId}.json</c>). It is joined to the
/// cache root without any containment check.</para>
/// </remarks>
public sealed class CacheStore(string rootPath)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        NewLine = "\n", // deterministic across OS: the cache is committed and CI (Linux) and dev (Windows) both write it
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>How far apart, in minutes, two runtimes of one film may be and still be a re-encode of it.</summary>
    private const int AliasToleranceMinutes = 2;

    private readonly Dictionary<string, CacheEntry> _byKey = new(StringComparer.Ordinal);

    public IReadOnlyCollection<CacheEntry> Entries => _byKey.Values;

    private string IndexPath => Path.Combine(rootPath, "index.json");

    public async Task LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(IndexPath)) return;

        List<CacheEntry>? entries;
        try
        {
            await using var stream = File.OpenRead(IndexPath);
            entries = await JsonSerializer.DeserializeAsync<List<CacheEntry>>(stream, Options, ct);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"The cache index '{IndexPath}' could not be read (truncated by a crash mid-save?). " +
                "Delete it to force a full refetch, or restore it from git.", ex);
        }

        _byKey.Clear();
        foreach (var e in entries ?? []) _byKey[e.Key] = e;
    }

    public async Task SaveAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(rootPath);
        var ordered = _byKey.Values.OrderBy(e => e.Key, StringComparer.Ordinal).ToList();

        await using var stream = File.Create(IndexPath);
        await JsonSerializer.SerializeAsync(stream, ordered, Options, ct);
    }

    /// <remarks>
    /// <see cref="CacheEntry.NormalizedTitle"/> must hold the folded <c>Value</c> from
    /// <c>TitleNormalizer</c>, never <c>Cleaned</c>. Alias lookup compares it by ordinal equality
    /// against the same folded form, and nothing enforces that: a <c>Cleaned</c> string silently
    /// never aliases, so every re-encoded film is re-fetched. <see cref="CacheEntry.NormalizedDirectors"/>
    /// is the same: it must be <c>TitleNormalizer.NormalizeDirectors</c> output, not the sheet's raw text.
    /// </remarks>
    public void Put(CacheEntry entry) => _byKey[entry.Key] = entry;

    /// <summary>
    /// Exact key, then an alias for a re-encode: the same normalized title and the same directors, with a runtime
    /// within two minutes either way. Null means fetch.
    /// </summary>
    /// <param name="key">Content identity key from <c>IdentityKey.ForFilm</c> or <c>ForSeries</c>.</param>
    /// <param name="normalizedTitle">
    /// The folded <c>Value</c> from <c>TitleNormalizer</c>, never <c>Cleaned</c>. The alias scan
    /// compares this for equality; passing <c>Cleaned</c> would silently stop every alias from
    /// matching, turning each re-encode into a fresh API call. An empty title never aliases.
    /// </param>
    /// <param name="runtimeMinutes">Runtime of the row being resolved, for the ±2-minute alias.</param>
    /// <param name="normalizedDirectors">
    /// The row's directors as <c>TitleNormalizer.NormalizeDirectors</c> returns them: the same string the
    /// identity key hashes, with its name tokens sorted, so the order the sheet lists them in does not matter.
    /// Compared for equality, so passing the raw sheet text would silently stop every alias. Empty when the row names
    /// no director, and then the row never aliases. A series passes <see cref="string.Empty"/>.
    /// </param>
    /// <param name="now">Caller-supplied clock, so expiry is deterministic in tests.</param>
    /// <remarks>
    /// <para>The alias exists for one case: Apple re-encoded a film, its runtime moved by a minute or two, and so its
    /// identity key (which includes the runtime) missed. Title and runtime alone cannot tell that from a different
    /// film. The folded title has had any trailing year moved into a hint that is not stored, so a remake shares its
    /// original's title, and nothing stops two such films running within two minutes of each other: <c>Carrie (1976)</c>
    /// and <c>Carrie (2013)</c> run a minute apart. The second to arrive would take the first's TMDB id with no
    /// search and, if that match was accepted, no review line, and the cache is committed, so the wrong poster,
    /// overview and year would outlive the run. Directors tell them apart, as they do in <c>IdentityKey</c> and in
    /// the match scorer.</para>
    /// <para>The comparison is strict: the normalized directors must be equal. A co-director gained or lost between
    /// two encodings is a miss, not a hit. A miss costs one search; a wrong merge costs a film's identity.</para>
    /// <para>A row with no director never aliases, and nor does an entry with none: the entry is null if it was written
    /// before directors were stored, and empty if the sheet named none. Two films that both lack directors and share a
    /// title and a runtime are not demonstrably the same film, and without directors a remake and a re-encode look
    /// identical. Such an entry still serves its own exact key; it is only never the target of an alias. There is no
    /// migration: the cost of an old entry is one extra search the first time a re-encode of it turns up.</para>
    /// </remarks>
    public CacheEntry? Resolve(
        string key, string normalizedTitle, int runtimeMinutes, string normalizedDirectors, DateTimeOffset now)
    {
        if (_byKey.TryGetValue(key, out var exact)) return Usable(exact, now) ? exact : null;

        // A title that folds to "" (a pure parenthetical or pure punctuation) names no film, so two of
        // them are not the same film just because their runtimes are close.
        if (normalizedTitle.Length == 0) return null;

        // No directors on the row, so nothing to tell it from a remake. The equality below would also hold between
        // two empty strings, which is exactly the pair this refuses.
        if (normalizedDirectors.Length == 0) return null;

        // The row's directors are non-empty here, so an entry with null or empty directors can never equal them.
        var alias = _byKey.Values.FirstOrDefault(e =>
            string.Equals(e.NormalizedTitle, normalizedTitle, StringComparison.Ordinal) &&
            Math.Abs(e.RuntimeMinutes - runtimeMinutes) <= AliasToleranceMinutes &&
            string.Equals(e.NormalizedDirectors, normalizedDirectors, StringComparison.Ordinal));

        if (alias is null || !Usable(alias, now)) return null;

        // Record the new key as an alias so later runs hit directly. The copy keeps the source's directors,
        // which are equal to this row's by the match above.
        _byKey[key] = alias with
        {
            Key = key,
            NormalizedTitle = normalizedTitle,
            RuntimeMinutes = runtimeMinutes
        };
        return alias;
    }

    private static bool Usable(CacheEntry e, DateTimeOffset now)
        => e.Negative
            ? !RefreshPolicy.NegativeExpired(e.FetchedUtc, now)
            : !RefreshPolicy.NeedsRefresh(e.FetchedUtc, now);

    public async Task<T?> ReadPayloadAsync<T>(string relativePath, CancellationToken ct) where T : class
    {
        var full = Path.Combine(rootPath, relativePath);
        if (!File.Exists(full)) return null;

        await using var stream = File.OpenRead(full);
        return await JsonSerializer.DeserializeAsync<T>(stream, Options, ct);
    }

    public async Task WritePayloadAsync<T>(string relativePath, T payload, CancellationToken ct)
    {
        var full = Path.Combine(rootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        await using var stream = File.Create(full);
        await JsonSerializer.SerializeAsync(stream, payload, Options, ct);
    }
}

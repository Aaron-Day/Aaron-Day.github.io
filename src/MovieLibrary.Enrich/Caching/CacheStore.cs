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
/// loses the cache and the next run rebuilds it at full API cost; acceptable for a single-user tool.</para>
/// </remarks>
public sealed class CacheStore(string rootPath)
{
    private static readonly JsonSerializerOptions Options =
        new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly Dictionary<string, CacheEntry> _byKey = new(StringComparer.Ordinal);

    public IReadOnlyCollection<CacheEntry> Entries => _byKey.Values;

    private string IndexPath => Path.Combine(rootPath, "index.json");

    public async Task LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(IndexPath)) return;

        await using var stream = File.OpenRead(IndexPath);
        var entries = await JsonSerializer.DeserializeAsync<List<CacheEntry>>(stream, Options, ct);

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
    /// never aliases, so every re-encoded film is re-fetched.
    /// </remarks>
    public void Put(CacheEntry entry) => _byKey[entry.Key] = entry;

    /// <summary>Exact key, then a ±2-minute alias on the same normalized title. Null means fetch.</summary>
    /// <param name="key">Content identity key from <c>IdentityKey.ForFilm</c> or <c>ForSeries</c>.</param>
    /// <param name="normalizedTitle">
    /// The folded <c>Value</c> from <c>TitleNormalizer</c>, never <c>Cleaned</c>. The alias scan
    /// compares this for equality; passing <c>Cleaned</c> would silently stop every alias from
    /// matching, turning each re-encode into a fresh API call.
    /// </param>
    /// <param name="runtimeMinutes">Runtime of the row being resolved, for the ±2-minute alias.</param>
    /// <param name="now">Caller-supplied clock, so expiry is deterministic in tests.</param>
    public CacheEntry? Resolve(string key, string normalizedTitle, int runtimeMinutes, DateTimeOffset now)
    {
        if (_byKey.TryGetValue(key, out var exact)) return Usable(exact, now) ? exact : null;

        var alias = _byKey.Values.FirstOrDefault(e =>
            string.Equals(e.NormalizedTitle, normalizedTitle, StringComparison.Ordinal) &&
            Math.Abs(e.RuntimeMinutes - runtimeMinutes) <= 2);

        if (alias is null || !Usable(alias, now)) return null;

        // Record the new key as an alias so later runs hit directly.
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

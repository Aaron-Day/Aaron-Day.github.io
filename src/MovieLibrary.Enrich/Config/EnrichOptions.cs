using System.Globalization;
using System.Text.Json.Serialization;

namespace MovieLibrary.Enrich.Config;

/// <remarks>
/// <b>The token never prints.</b> A record's generated ToString writes every member, and a stray log line on a
/// public repository's Actions run is world-readable, so ToString is overridden to redact <see cref="Token"/> and the
/// property is ignored by System.Text.Json, the other way a whole record gets written to a log.
/// <b>Empty is absent.</b> An unset Actions variable arrives as an empty string, so an empty or blank environment
/// variable or flag value counts as not given. <b>A budget below zero is an error</b>: RequestBudget reads anything
/// at or under 0 as unlimited, so a typo such as <c>--max-requests -5</c> would turn a 40-request run into thousands.
/// <b>A forced refresh under a budget is an error too</b>: it would replace the committed cache index with the handful
/// of entries a budget-stopped run managed to fetch. See <see cref="Load"/>.
/// </remarks>
public sealed record EnrichOptions(
    string SheetId, [property: JsonIgnore] string Token, int MaxRequests,
    string CacheRoot, string OutputRoot, bool Fixtures, bool ForceRefresh)
{
    public const string DefaultSheetId = "1gkDwOi5-Gvm48WkYz8noYUb5i6QjfQSDLpEDYV5QlHk";

    public override string ToString()
        => $"EnrichOptions {{ SheetId = {SheetId}, Token = {(Token.Length == 0 ? "<none>" : "<redacted>")}, " +
           $"MaxRequests = {MaxRequests}, CacheRoot = {CacheRoot}, OutputRoot = {OutputRoot}, " +
           $"Fixtures = {Fixtures}, ForceRefresh = {ForceRefresh} }}";

    public static EnrichOptions Load(string[] args, IDictionary<string, string> env)
    {
        var fixtures = args.Contains("--fixtures");
        var force = args.Contains("--force-refresh")
                    || Get(env, "FORCE_REFRESH") == "true";

        var max = Flag(args, "--max-requests")
                  ?? Get(env, "MAX_REQUESTS_PER_RUN")
                  ?? "40";

        var requests = int.TryParse(max, NumberStyles.Integer, CultureInfo.InvariantCulture, out var m) ? m : 40;
        if (requests < 0)
            throw new ArgumentException(
                $"The request limit must be 0 (unlimited) or more, not {requests}. " +
                "Use --max-requests 0 to ask for no limit on purpose.");

        // A full refresh of this library needs well over 1,500 requests. Under a budget it re-resolves the first few
        // films and then saves, and CacheStore.SaveAsync is File.Create - a full replace, not a merge. A forced run
        // never loaded the committed index, so those few entries become the whole of it. The payload files survive but
        // CacheStore resolves through the index, so recovering costs about 7,000 requests, and enrich-cache/ is
        // committed, so the truncated index is what gets pushed. There is no flag to force-refresh a subset, so the
        // combination has no legitimate use: it is refused here, before anything is fetched or written.
        if (force && requests > 0)
            throw new ArgumentException(
                "--force-refresh re-fetches the whole library, which needs thousands of requests, but the budget is " +
                $"{requests}. A forced run that stops on its budget replaces the committed cache index with only the " +
                "entries it managed to fetch, and every other film and series then costs a full lookup again. Pass " +
                "--max-requests 0 to force-refresh the whole library, or drop --force-refresh.");

        return new EnrichOptions(
            SheetId: Flag(args, "--sheet-id") ?? Get(env, "SHEET_ID") ?? DefaultSheetId,
            Token: fixtures ? string.Empty : Get(env, "TMDB_TOKEN") ?? string.Empty,
            MaxRequests: requests,
            CacheRoot: Flag(args, "--cache") ?? "enrich-cache",
            OutputRoot: Flag(args, "--out")
                        ?? (fixtures ? "dev-data" : "src/MovieLibrary.Web/wwwroot/data"),
            Fixtures: fixtures,
            ForceRefresh: force);
    }

    // IDictionary<,> has no GetValueOrDefault extension (only IReadOnlyDictionary<,> does).
    private static string? Get(IDictionary<string, string> env, string key)
        => env.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    /// <summary>The value of <c>--name value</c> or <c>--name=value</c>, the first of either form; empty counts as absent.</summary>
    private static string? Flag(string[] args, string name)
    {
        for (var i = 0; i < args.Length; i++)
        {
            string? value;
            if (args[i] == name) value = i + 1 < args.Length ? args[i + 1] : null;
            else if (args[i].StartsWith(name + "=", StringComparison.Ordinal)) value = args[i][(name.Length + 1)..];
            else continue;

            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return null;
    }

    /// <summary>Reads a gitignored .env for local runs. CI supplies real environment variables.</summary>
    public static Dictionary<string, string> ReadEnvironment(string envPath = ".env")
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
            if (e.Key is string k && e.Value is string v) values[k] = v;

        if (!File.Exists(envPath)) return values;

        foreach (var line in File.ReadAllLines(envPath))
        {
            var text = line.Trim();
            if (text.Length == 0 || text.StartsWith('#')) continue;

            var split = text.IndexOf('=');
            if (split <= 0) continue;

            var key = text[..split].Trim();
            var value = text[(split + 1)..].Trim().Trim('"');

            // Real environment variables win, so CI is never shadowed by a stray local file.
            if (value.Length > 0 && !values.ContainsKey(key)) values[key] = value;
        }

        return values;
    }
}

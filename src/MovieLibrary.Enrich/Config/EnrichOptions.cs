namespace MovieLibrary.Enrich.Config;

public sealed record EnrichOptions(
    string SheetId, string Token, int MaxRequests,
    string CacheRoot, string OutputRoot, bool Fixtures, bool ForceRefresh)
{
    public const string DefaultSheetId = "1gkDwOi5-Gvm48WkYz8noYUb5i6QjfQSDLpEDYV5QlHk";

    public static EnrichOptions Load(string[] args, IDictionary<string, string> env)
    {
        var fixtures = args.Contains("--fixtures");
        var force = args.Contains("--force-refresh")
                    || Get(env, "FORCE_REFRESH") == "true";

        var max = Flag(args, "--max-requests")
                  ?? Get(env, "MAX_REQUESTS_PER_RUN")
                  ?? "40";

        return new EnrichOptions(
            SheetId: Flag(args, "--sheet-id") ?? Get(env, "SHEET_ID") ?? DefaultSheetId,
            Token: fixtures ? string.Empty : Get(env, "TMDB_TOKEN") ?? string.Empty,
            MaxRequests: int.TryParse(max, out var m) ? m : 40,
            CacheRoot: Flag(args, "--cache") ?? "enrich-cache",
            OutputRoot: Flag(args, "--out")
                        ?? (fixtures ? "dev-data" : "src/MovieLibrary.Web/wwwroot/data"),
            Fixtures: fixtures,
            ForceRefresh: force);
    }

    // IDictionary<,> has no GetValueOrDefault extension (only IReadOnlyDictionary<,> does).
    private static string? Get(IDictionary<string, string> env, string key)
        => env.TryGetValue(key, out var value) ? value : null;

    private static string? Flag(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
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

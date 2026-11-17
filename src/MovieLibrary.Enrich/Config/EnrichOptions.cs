using System.Globalization;
using System.Text.Json.Serialization;
using MovieLibrary.Core.Store;

namespace MovieLibrary.Enrich.Config;

/// <remarks>
/// <b>The token never prints.</b> A record's generated ToString writes every member, and a stray log line on a
/// public repository's Actions run is world-readable, so ToString is overridden to redact <see cref="Token"/> and the
/// property is ignored by System.Text.Json, the other way a whole record gets written to a log.
/// <b>Empty is absent.</b> An unset Actions variable arrives as an empty string, so an empty or blank environment
/// variable or flag value counts as not given. <b>A boolean flag that is given a value must mean it</b>: the two
/// switches take <c>--flag</c> and <c>--flag=true</c>, and refuse anything else rather than guess (see
/// <see cref="Switch"/>). <b>The token is cleaned where it is read</b>, because it is pasted by hand (see
/// <see cref="CleanToken"/>). <b>A budget below zero is an error</b>: RequestBudget reads anything
/// at or under 0 as unlimited, so a typo such as <c>--max-requests -5</c> would turn a 40-request run into thousands.
/// <b>A forced refresh under a budget is an error too</b>: it would replace the committed cache index with the handful
/// of entries a budget-stopped run managed to fetch. See <see cref="Load"/>.
/// </remarks>
/// <param name="StoreFile">
/// Where <c>record-store.json</c> is read from, relative to the working directory. The build runs from the
/// repository root, so the default is the file beside the solution. An absent file is an empty store, which is
/// the normal state of a fresh clone. A path, never a secret.
/// </param>
/// <param name="PrintKeys">
/// <c>--print-keys</c>: print the identity key of every row on the sheet and stop. A diagnostic about the
/// SHEET, not a run - it makes no TMDB request, needs no token, reads no record store and publishes nothing.
/// It exists because an entry in <c>record-store.json</c> names its row by a 40-character SHA-1 that a human
/// cannot compute, so without it hand-editing the file means guessing a key.
/// </param>
public sealed record EnrichOptions(
    string SheetId, [property: JsonIgnore] string Token, int MaxRequests,
    string CacheRoot, string OutputRoot, bool Fixtures, bool ForceRefresh,
    string StoreFile = RecordStoreReader.DefaultFileName, bool PrintKeys = false)
{
    public const string DefaultSheetId = "1gkDwOi5-Gvm48WkYz8noYUb5i6QjfQSDLpEDYV5QlHk";

    public override string ToString()
        => $"EnrichOptions {{ SheetId = {SheetId}, Token = {(Token.Length == 0 ? "<none>" : "<redacted>")}, " +
           $"MaxRequests = {MaxRequests}, CacheRoot = {CacheRoot}, OutputRoot = {OutputRoot}, " +
           $"Fixtures = {Fixtures}, ForceRefresh = {ForceRefresh}, StoreFile = {StoreFile}, " +
           $"PrintKeys = {PrintKeys} }}";

    /// <summary>The boolean switches: <c>--flag</c> or <c>--flag=true</c>, never a following word.</summary>
    /// <remarks>
    /// This list and <see cref="ValueFlags"/> are the whole of what this tool accepts, and
    /// <see cref="RequireKnownArguments"/> refuses everything else. Adding a flag means adding it here too,
    /// or the flag parses and the argument that spells it is then rejected.
    /// </remarks>
    private static readonly string[] SwitchFlags = ["--fixtures", "--force-refresh", "--print-keys"];

    /// <inheritdoc cref="SwitchFlags"/>
    /// <summary>The value flags: <c>--flag value</c> or <c>--flag=value</c>.</summary>
    private static readonly string[] ValueFlags = ["--cache", "--max-requests", "--out", "--sheet-id", "--store"];

    /// <summary>
    /// Every argument has to be one this tool knows, or the run stops before anything is read or spent.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this is a budget guard and not tidiness.</b> Flags are read by name and anything else used
    /// to be ignored, so <c>--fixture</c> for <c>--fixtures</c>, or <c>--printkeys</c> for <c>--print-keys</c>,
    /// parsed as an ordinary live run: the real token, the default 40 requests, and the deployed data folder.
    /// The only thing between that and the owner's paid budget was the missing-token guard, which bites
    /// precisely when his token IS in the environment - that is, never when it matters.</para>
    /// <para><b>It cannot break the deploy.</b> <c>.github/workflows/main.yml</c> runs
    /// <c>dotnet run --project src/MovieLibrary.Enrich -c Release</c> with no arguments at all: <c>-c Release</c>
    /// is a <c>dotnet run</c> option and never reaches <c>args</c>.</para>
    /// <para><b>A known flag's value is not an argument.</b> The value flags take <c>--flag value</c> as well as
    /// <c>--flag=value</c> (see <see cref="Flag"/>), so the separate word is consumed here exactly as
    /// <see cref="Flag"/> consumes it - whatever it looks like, a leading hyphen included.</para>
    /// </remarks>
    private static void RequireKnownArguments(string[] args)
    {
        var unknown = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            // --flag=value names the flag up to the FIRST '=', the way Flag and Switch both read it.
            var split = args[i].IndexOf('=');
            var name = split > 0 ? args[i][..split] : args[i];

            if (SwitchFlags.Contains(name, StringComparer.Ordinal)) continue;

            if (ValueFlags.Contains(name, StringComparer.Ordinal))
            {
                if (name.Length == args[i].Length) i++;   // the separate-word form: the next argument is its value
                continue;
            }

            unknown.Add(args[i]);
        }

        if (unknown.Count == 0) return;

        throw new ArgumentException(
            $"Unrecognised argument(s): {string.Join(", ", unknown.Select(a => $"'{a}'"))}. This tool takes " +
            $"{string.Join(", ", SwitchFlags.Concat(ValueFlags).Order(StringComparer.Ordinal))} and nothing " +
            "else. These used to be ignored, which made a near-miss on a flag's spelling - '--fixture' for " +
            "'--fixtures', '--printkeys' for '--print-keys' - an ordinary live run against the TMDB budget. " +
            "Nothing was read, spent or written.");
    }

    public static EnrichOptions Load(string[] args, IDictionary<string, string> env)
    {
        // First of all: a typo must not reach the token, the budget or the sheet.
        RequireKnownArguments(args);

        var fixtures = Switch(args, "--fixtures") ?? false;
        var printKeys = Switch(args, "--print-keys") ?? false;
        var force = Switch(args, "--force-refresh")
                    ?? (Get(env, "FORCE_REFRESH") is { } refresh && Truth(refresh.Trim(), "FORCE_REFRESH"));

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
        // Not a fixture run: it makes no requests and returns before the cache is ever loaded or saved
        // (EnrichRun.RunCoreAsync), so there is nothing for it to truncate and nothing to refuse.
        if (force && !fixtures && requests > 0)
            throw new ArgumentException(
                "--force-refresh re-fetches the whole library, which needs thousands of requests, but the budget is " +
                $"{requests}. A forced run that stops on its budget replaces the committed cache index with only the " +
                "entries it managed to fetch, and every other film and series then costs a full lookup again. Pass " +
                "--max-requests 0 to force-refresh the whole library, or drop --force-refresh.");

        return new EnrichOptions(
            SheetId: Flag(args, "--sheet-id") ?? Get(env, "SHEET_ID") ?? DefaultSheetId,
            // A print-keys run is held to the same rule as a fixture run: it cannot authenticate a request,
            // whatever is in the environment, because it is not allowed to make one.
            Token: fixtures || printKeys ? string.Empty : CleanToken(Get(env, "TMDB_TOKEN")),
            MaxRequests: requests,
            CacheRoot: Flag(args, "--cache") ?? "enrich-cache",
            OutputRoot: Flag(args, "--out")
                        ?? (fixtures ? "dev-data" : "src/MovieLibrary.Web/wwwroot/data"),
            Fixtures: fixtures,
            ForceRefresh: force,
            StoreFile: Flag(args, "--store") ?? RecordStoreReader.DefaultFileName,
            PrintKeys: printKeys);
    }

    /// <summary>
    /// A boolean flag: <c>--flag</c> on its own, or <c>--flag=true</c> / <c>--flag=false</c> in any case. Null when the
    /// flag is not there at all, so a caller can fall back to the environment; the first occurrence wins, and a flag
    /// that is there beats the environment, both as they do for the value flags.
    /// </summary>
    /// <remarks>
    /// <c>args.Contains("--fixtures")</c> was not enough. The value flags take both <c>--flag value</c> and
    /// <c>--flag=value</c>, so <c>--fixtures=true</c> is a natural spelling, and it used to be read as no fixture flag
    /// at all: the run read the token, spent the default 40 real requests and wrote to the deployed data folder instead
    /// of dev-data. Silently, and it is the only way a typo can spend budget.
    /// </remarks>
    private static bool? Switch(string[] args, string name)
    {
        foreach (var arg in args)
        {
            if (arg == name) return true;
            if (!arg.StartsWith(name + "=", StringComparison.Ordinal)) continue;

            return Truth(arg[(name.Length + 1)..].Trim(), name);
        }

        return null;
    }

    /// <summary>
    /// <c>true</c> or <c>false</c> in any case; anything else is refused, naming what was given and how to write it.
    /// </summary>
    /// <remarks>
    /// A nonsense value is never guessed at. Read as true, <c>--fixtures=0</c> writes synthetic data where the real
    /// catalogue goes; read as false, it spends the budget on a live run. Neither reading is safe enough to pick
    /// silently, and the cost of being loud is a run that exits before it fetches anything. An absent or blank value is
    /// not a value and never reaches here: it means false.
    /// </remarks>
    private static bool Truth(string text, string source)
    {
        if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase)) return false;

        var how = source.StartsWith("--", StringComparison.Ordinal)
            ? $"Write {source} on its own, or {source}=true."
            : $"Set {source}=true.";
        throw new ArgumentException($"{source} must be true or false, not '{text}'. {how} Leaving it out means false.");
    }

    /// <summary>
    /// The token as it was pasted, made usable: a leading <c>Bearer </c> in any case, and surrounding single or double
    /// quotes, are stripped.
    /// </summary>
    /// <remarks>
    /// The secret is pasted by hand into GitHub's UI, and pasting <c>Bearer eyJ...</c> or <c>"eyJ..."</c> - from a curl
    /// line, or an editor that quotes - is an easy mistake with an expensive shape. Uncleaned, <c>TmdbAuth.Detect</c>'s
    /// <c>StartsWith("eyJ")</c> test fails, so a v4 token is sent as a v3 <c>api_key</c> query value, TMDB answers 401,
    /// and the run dies on request one with advice that names the secret but not what is wrong with it. Nothing here is
    /// printed: the token never reaches a log line.
    /// </remarks>
    internal static string CleanToken(string? raw)
    {
        var token = Unquote((raw ?? string.Empty).Trim());

        // The word on its own, or followed by whitespace. "Bearer" with nothing after it is not a credential either,
        // and leaving it would send the word itself to TMDB as an api_key: an empty token at least reports itself as
        // missing. A real credential is base64url or hex, so it cannot begin with this word.
        const string bearer = "Bearer";
        if (token.StartsWith(bearer, StringComparison.OrdinalIgnoreCase)
            && (token.Length == bearer.Length || char.IsWhiteSpace(token[bearer.Length])))
            token = token[bearer.Length..].Trim();

        // Again, for a value that was quoted around the prefix as well: "Bearer eyJ..." unwraps to Bearer eyJ... above.
        return Unquote(token);
    }

    /// <summary>
    /// Surrounding single or double quotes, at either end and however many: a credential never begins or ends with
    /// one, so there is nothing to lose by being forgiving, and a paste that dropped one of a pair is exactly the
    /// kind of slip this is here for.
    /// </summary>
    private static string Unquote(string text) => text.Trim('"', '\'').Trim();

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

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

    /// <summary>Both lists, ordered: what the refusals print, and what a separate-word value is tested against.</summary>
    private static readonly string[] AllFlags = [.. SwitchFlags.Concat(ValueFlags).Order(StringComparer.Ordinal)];

    /// <summary>One argument as this tool reads it, after <see cref="Read"/> has walked the line.</summary>
    /// <param name="Name">The flag it names, or the argument as given when this tool does not know it.</param>
    /// <param name="Value">What came after the first <c>=</c>, or the following word; null when nothing did.</param>
    /// <param name="Known">False for an argument this tool does not accept.</param>
    /// <param name="TookTheNextWord">True when <paramref name="Value"/> is the following argument.</param>
    private readonly record struct Given(string Name, string? Value, bool Known, bool TookTheNextWord);

    /// <summary>The command line walked ONCE, left to right, into the flags it names and the values they carry.</summary>
    /// <remarks>
    /// <para><b>Why one walk and not three readers.</b> <see cref="RequireKnownArguments"/>,
    /// <see cref="Switch"/> and <see cref="Flag"/> each used to read <c>args</c> themselves, and only the
    /// first two of those walked it positionally: <c>Switch</c> scanned every argument by name wherever it
    /// sat. So <c>--out --fixtures</c> passed the guard, the word being consumed as the value of
    /// <c>--out</c>, and then <c>Switch</c> found <c>--fixtures</c> anyway and turned a run the owner wrote
    /// as live into a synthesised catalogue without a word. <c>--out --force-refresh</c> was loud only
    /// because the budget refusal happens to fire on it. Reading one walk is what makes the guard's promise
    /// - that a known flag's value is not an argument - true of every reader rather than of two of them.</para>
    /// </remarks>
    private static List<Given> Read(string[] args)
    {
        var given = new List<Given>(args.Length);

        for (var i = 0; i < args.Length; i++)
        {
            // --flag=value names the flag up to the FIRST '='; the rest is the value, blank or not.
            var split = args[i].IndexOf('=');
            var name = split > 0 ? args[i][..split] : args[i];
            var value = split > 0 ? args[i][(split + 1)..] : null;

            if (SwitchFlags.Contains(name, StringComparer.Ordinal))
            {
                given.Add(new Given(name, value, Known: true, TookTheNextWord: false));
                continue;
            }

            if (ValueFlags.Contains(name, StringComparer.Ordinal))
            {
                // The separate-word form. A value flag at the very end of the line has no value and falls
                // back to the environment or the default, which is what Flag has always done with it.
                var next = value is null && i + 1 < args.Length;
                if (next) value = args[++i];

                given.Add(new Given(name, value, Known: true, TookTheNextWord: next));
                continue;
            }

            given.Add(new Given(args[i], null, Known: false, TookTheNextWord: false));
        }

        return given;
    }

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
    /// <c>--flag=value</c> (see <see cref="Flag"/>), so the separate word is consumed by <see cref="Read"/>,
    /// which every reader of the line now goes through - whatever the word looks like, a leading hyphen
    /// included. The one word refused outright is this tool's OWN flag name, below: that word has a second
    /// reading, and the reading it does NOT have is the one anybody typing it meant.</para>
    /// </remarks>
    private static void RequireKnownArguments(IReadOnlyList<Given> given)
    {
        var unknown = given.Where(g => !g.Known).Select(g => g.Name).ToList();

        if (unknown.Count > 0)
            throw new ArgumentException(
                $"Unrecognised argument(s): {string.Join(", ", unknown.Select(a => $"'{a}'"))}. This tool takes " +
                $"{string.Join(", ", AllFlags)} and nothing " +
                "else. These used to be ignored, which made a near-miss on a flag's spelling - '--fixture' for " +
                "'--fixtures', '--printkeys' for '--print-keys' - an ordinary live run against the TMDB budget. " +
                "Nothing was read, spent or written.");

        // The obedient reading of `--out --fixtures` is an output folder literally called "--fixtures" and a
        // live run against the budget; the only way anybody types it is as a slip for
        // `--out <folder> --fixtures`. Neither reading is safe enough to pick in silence - the same rule Truth
        // applies to a boolean's value - so it is refused. The =-form still says it on purpose, for the folder
        // that really is called that, and a word that merely LOOKS like a flag is still a value: a path may
        // begin with a hyphen, and only this tool's own names have the second reading.
        foreach (var g in given)
        {
            if (!g.TookTheNextWord || g.Value is not { } value) continue;

            // Tested on the name, not the whole word, so `--out --fixtures=true` is caught as well: that one
            // was the worse half of the same slip, because Switch read a =-form wherever it sat too.
            var split = value.IndexOf('=');
            if (!AllFlags.Contains(split > 0 ? value[..split] : value, StringComparer.Ordinal)) continue;

            throw new ArgumentException(
                $"'{g.Name} {value}' gives {g.Name} the value '{value}', because a value flag's separate word " +
                $"is its value whatever it looks like - so {value} is not read as a flag here and does nothing " +
                $"at all. Write '{g.Name} <value> {value}' if you meant both, or '{g.Name}={value}' if the " +
                $"value really is '{value}'. Nothing was read, spent or written.");
        }
    }

    public static EnrichOptions Load(string[] args, IDictionary<string, string> env)
    {
        // Walked once; every reader below reads the walk and not the raw line.
        var given = Read(args);

        // First of all: a typo must not reach the token, the budget or the sheet.
        RequireKnownArguments(given);

        var fixtures = Switch(given, "--fixtures") ?? false;
        var printKeys = Switch(given, "--print-keys") ?? false;
        var force = Switch(given, "--force-refresh")
                    ?? (Get(env, "FORCE_REFRESH") is { } refresh && Truth(refresh.Trim(), "FORCE_REFRESH"));

        var max = Flag(given, "--max-requests")
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
            SheetId: Flag(given, "--sheet-id") ?? Get(env, "SHEET_ID") ?? DefaultSheetId,
            // A print-keys run is held to the same rule as a fixture run: it cannot authenticate a request,
            // whatever is in the environment, because it is not allowed to make one.
            Token: fixtures || printKeys ? string.Empty : CleanToken(Get(env, "TMDB_TOKEN")),
            MaxRequests: requests,
            CacheRoot: Flag(given, "--cache") ?? "enrich-cache",
            OutputRoot: Flag(given, "--out")
                        ?? (fixtures ? "dev-data" : "src/MovieLibrary.Web/wwwroot/data"),
            Fixtures: fixtures,
            ForceRefresh: force,
            StoreFile: Flag(given, "--store") ?? RecordStoreReader.DefaultFileName,
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
    /// Reading <see cref="Read"/>'s walk rather than <c>args</c> closes the opposite slip: a word that is
    /// already another flag's value is not this flag, however it is spelled.
    /// </remarks>
    private static bool? Switch(IReadOnlyList<Given> given, string name)
    {
        foreach (var g in given)
        {
            if (!string.Equals(g.Name, name, StringComparison.Ordinal)) continue;

            return g.Value is null ? true : Truth(g.Value.Trim(), name);
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
    private static string? Flag(IReadOnlyList<Given> given, string name)
    {
        foreach (var g in given)
        {
            if (!string.Equals(g.Name, name, StringComparison.Ordinal)) continue;

            return string.IsNullOrWhiteSpace(g.Value) ? null : g.Value;
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

using System.Globalization;
using Microsoft.AspNetCore.Components;
using MovieLibrary.Core.Matching;
using MovieLibrary.Web.Models;

namespace MovieLibrary.Web.Services;

/// <summary>
/// The films page's address, in both directions: what the visitor's choices write into the query string, and
/// what a query string says they chose. ONE place for the parameter names and for every token's spelling, so
/// that the writer and the reader cannot drift apart.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is a service and not code in <c>Movies.razor</c>.</b>
/// <c>SourceContractTests.NoComponentRedirectsAndNoFooterIsAddedInsideTheApp</c> scans every hand-written
/// <c>.razor</c> for a navigation call and has no carve-out, and the URL contract needs that call. A
/// <c>.cs</c> file is outside the scan, and an injected service is the shape <see cref="Announcer"/> already has.
/// This is the only sanctioned place for the call.
/// </para>
/// <para>
/// <b>State is read the router's way.</b> The page declares <c>[SupplyParameterFromQuery]</c> properties and hands
/// the raw strings to <see cref="Read"/>; nothing parses <c>NavigationManager.Uri</c> by hand. Every parameter is
/// declared as a STRING (or string array), never an int, because the router's own conversion THROWS on a value it
/// cannot parse: <c>?genre=abc</c> or <c>?page=99999999999</c> typed into an address bar would otherwise be an
/// unhandled exception and the error bar. Parsing here makes junk mean "no filter".
/// </para>
/// <para>
/// <b>Defaults are dropped, not written.</b> An empty search writes no <c>q</c>, the first page no <c>page</c>,
/// title order no <c>sort</c>: the address of an unfiltered page is just <c>/movies</c>, and a visitor who clears
/// everything is back on it.
/// </para>
/// </remarks>
public sealed class QueryState(NavigationManager nav)
{
    public const string QueryKey = "q";
    public const string ConfidenceKey = "conf";
    public const string GenreKey = "genre";
    public const string DecadeKey = "decade";
    public const string SortKey = "sort";
    public const string PageKey = "page";

    /// <summary>The spelling of the "Year unknown" bucket in a <c>decade</c> parameter.</summary>
    internal const string UnknownYear = "unknown";

    /// <summary>
    /// The bands the panel offers a radio for. <see cref="Confidence.Confident"/> is not one of them, so it is not
    /// readable from an address either: a filter the panel has no control for would narrow the list invisibly.
    /// </summary>
    private static readonly Confidence[] OfferedBands = [Confidence.Uncertain, Confidence.Unmatched];

    // ================================================================ reading

    /// <summary>The filter an address asks for. Total: no input throws, and an unreadable value means none.</summary>
    public static FilterState Read(string? q, string? conf, string[]? genre, string[]? decade, string? sort)
    {
        var decades = new List<int>();
        var unknown = false;

        foreach (var value in decade ?? [])
        {
            if (string.Equals(value, UnknownYear, StringComparison.OrdinalIgnoreCase)) unknown = true;
            else if (TryNumber(value, out var year)) decades.Add(year / 10 * 10);
        }

        var genres = new List<int>();
        foreach (var value in genre ?? [])
            if (TryNumber(value, out var id)) genres.Add(id);

        return new FilterState(
            Query: (q ?? string.Empty).Trim(),
            Band: ReadBand(conf),
            Genres: [.. genres.Distinct().Order()],
            Decades: [.. decades.Distinct().Order()],
            YearUnknown: unknown,
            Sort: ReadSort(sort));
    }

    private static Confidence? ReadBand(string? conf)
    {
        foreach (var band in OfferedBands)
            if (string.Equals(BandToken(band), conf, StringComparison.OrdinalIgnoreCase)) return band;

        return null;
    }

    /// <summary>The page an address asks for, one-based. Anything that is not a positive whole number is page 1.</summary>
    public static int ReadPage(string? page) => TryNumber(page, out var number) ? number : 1;

    public static SortOrder ReadSort(string? sort)
    {
        foreach (var order in Enum.GetValues<SortOrder>())
            if (string.Equals(SortToken(order), sort, StringComparison.OrdinalIgnoreCase)) return order;

        return SortOrder.Title;
    }

    /// <summary>The spelling of a sort in the address and in the select's option values: the enum name, lower case.</summary>
    public static string SortToken(SortOrder sort) => sort.ToString().ToLowerInvariant();

    private static string BandToken(Confidence band) => band.ToString().ToLowerInvariant();

    /// <summary>Digits only (no sign, no space, no separator), positive, and in range.</summary>
    private static bool TryNumber(string? text, out int number) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out number) && number > 0;

    // ================================================================ writing

    /// <summary>
    /// The parameters that stand for a state, keyed by name. A value of null (or an empty array) means "this
    /// parameter is absent", which is how a default is dropped; this is the shape
    /// <c>GetUriWithQueryParameters</c> takes. Arrays rather than lists, because that method accepts arrays and
    /// throws on a <see cref="List{T}"/>.
    /// </summary>
    public static IReadOnlyDictionary<string, object?> ParametersFor(FilterState state, int page = 1) =>
        new Dictionary<string, object?>
        {
            [QueryKey] = string.IsNullOrWhiteSpace(state.Query) ? null : state.Query.Trim(),
            [ConfidenceKey] = state.Band is { } band ? BandToken(band) : null,
            [GenreKey] = state.Genres.ToArray(),
            [DecadeKey] = state.Decades.Select(d => d.ToString(CultureInfo.InvariantCulture))
                               .Concat(state.YearUnknown ? [UnknownYear] : []).ToArray(),
            [SortKey] = state.Sort == SortOrder.Title ? null : SortToken(state.Sort),
            [PageKey] = page > 1 ? page : null,
        };

    /// <summary>
    /// A state and a page as one comparable string, built from <see cref="ParametersFor"/> so that it contains
    /// exactly what an address would. The page compares what it last wrote with what the router hands back by
    /// this, to tell the echo of its own navigation from the visitor following a link.
    /// </summary>
    public static string Canonical(FilterState state, int page) =>
        string.Join('&', ParametersFor(state, page).Select(p => p.Value switch
        {
            null => null,
            Array { Length: 0 } => null,
            Array values => p.Key + "=" + string.Join(',', values.Cast<object>().Select(Escaped)),
            var value => p.Key + "=" + Escaped(value),
        }).Where(part => part is not null));

    private static string Escaped(object value) =>
        Uri.EscapeDataString(Convert.ToString(value, CultureInfo.InvariantCulture)!);

    /// <summary>Writes a whole filter state, and with it the first page: any change of filter returns to page 1.</summary>
    public void WriteFilters(FilterState state) => Replace(ParametersFor(state));

    /// <summary>Writes the page alone and leaves every other parameter exactly as it is.</summary>
    public void WritePage(int page) =>
        Replace(new Dictionary<string, object?> { [PageKey] = page > 1 ? page : null });

    private void Replace(IReadOnlyDictionary<string, object?> parameters)
    {
        // Always replace:true. Without it the back button walks backwards through forty filter states instead
        // of returning to wherever the user came from.
        nav.NavigateTo(nav.GetUriWithQueryParameters(parameters), replace: true);
    }
}

using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MovieLibrary.Core.Matching;
using MovieLibrary.Web.Models;
using MovieLibrary.Web.Services;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// The address contract, both directions, with no page in the way: what an address asks for, what a choice
/// writes, and that the two agree about every state the panel can produce.
/// </summary>
public class QueryStateTests : BunitContext
{
    private static FilterState Read(string? q = null, string? conf = null, string[]? genre = null,
                                    string[]? decade = null, string? sort = null) =>
        QueryState.Read(q, conf, genre, decade, sort);

    // ================================================================ reading

    [Fact]
    public void AnEmptyAddressIsTheDefaultState()
    {
        Assert.Equal(QueryState.Canonical(FilterState.Default, 1), QueryState.Canonical(Read(), 1));
        Assert.Equal(1, QueryState.ReadPage(null));
    }

    [Fact]
    public void EveryParameterIsReadIntoItsField()
    {
        var state = Read(q: "  lynch ", conf: "unmatched", genre: ["35", "18"], decade: ["1990", "unknown"], sort: "runtime");

        Assert.Equal("lynch", state.Query);
        Assert.Equal(Confidence.Unmatched, state.Band);
        Assert.Equal(new[] { 18, 35 }, state.Genres);
        Assert.Equal(new[] { 1990 }, state.Decades);
        Assert.True(state.YearUnknown);
        Assert.Equal(SortOrder.Runtime, state.Sort);
    }

    [Theory]
    [InlineData("uncertain", Confidence.Uncertain)]
    [InlineData("UNMATCHED", Confidence.Unmatched)]
    [InlineData("confident", null)]
    [InlineData("", null)]
    [InlineData("all", null)]
    [InlineData(null, null)]
    public void OnlyTheTwoBandsThePanelOffersAreReadFromTheAddress(string? conf, Confidence? expected)
    {
        Assert.Equal(expected, Read(conf: conf).Band);
    }

    /// <remarks>
    /// The router converts a typed parameter itself and THROWS on a value it cannot parse (MEASURED: "Cannot
    /// parse the value 'abc' as type 'System.Int32'"). Every parameter is therefore a string and this is the
    /// parsing, and none of these may throw or filter.
    /// </remarks>
    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("-3")]
    [InlineData("0")]
    [InlineData("+5")]
    [InlineData(" 7")]
    [InlineData("1.5")]
    [InlineData("99999999999")]
    public void AValueThatIsNotAPositiveWholeNumberIsNoFilterAndNeverThrows(string junk)
    {
        var state = Read(genre: [junk], decade: [junk]);

        Assert.Empty(state.Genres);
        Assert.Empty(state.Decades);
        Assert.Equal(1, QueryState.ReadPage(junk));
        Assert.False(state.IsFiltering);
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("12", 12)]
    [InlineData("0", 1)]
    [InlineData("-2", 1)]
    [InlineData("x", 1)]
    [InlineData(null, 1)]
    public void APageIsAPositiveWholeNumberOrTheFirst(string? page, int expected)
    {
        Assert.Equal(expected, QueryState.ReadPage(page));
    }

    [Fact]
    public void RepeatedAndUnorderedValuesAreSortedAndDistinct()
    {
        var state = Read(genre: ["35", "18", "35", "28"], decade: ["1999", "1990", "1980", "UNKNOWN"]);

        Assert.Equal(new[] { 18, 28, 35 }, state.Genres);
        Assert.Equal(new[] { 1980, 1990 }, state.Decades);   // 1999 is the 1990s
        Assert.True(state.YearUnknown);
    }

    [Theory]
    [InlineData("year", SortOrder.Year)]
    [InlineData("YEAR", SortOrder.Year)]
    [InlineData("runtime", SortOrder.Runtime)]
    [InlineData("title", SortOrder.Title)]
    [InlineData("random", SortOrder.Title)]
    [InlineData(null, SortOrder.Title)]
    public void ASortIsReadByNameAndTitleIsTheFallback(string? sort, SortOrder expected)
    {
        Assert.Equal(expected, Read(sort: sort).Sort);
    }

    // ================================================================ the parameters a state writes

    [Fact]
    public void TheDefaultStateWritesNothingAtAll()
    {
        var written = QueryState.ParametersFor(FilterState.Default);

        Assert.Equal(string.Empty, QueryState.Canonical(FilterState.Default, 1));
        Assert.All(written, p => Assert.True(p.Value is null or Array { Length: 0 }, p.Key + " is " + p.Value));
    }

    /// <remarks>An empty search must drop the parameter. It must never write <c>?q=</c>.</remarks>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptySearchDropsTheParameterRatherThanWritingAnEmptyOne(string query)
    {
        var written = QueryState.ParametersFor(FilterState.Default with { Query = query });

        Assert.Null(written[QueryState.QueryKey]);
    }

    [Fact]
    public void EveryChoiceIsWrittenUnderItsOwnNameAndNoOtherIs()
    {
        var state = FilterState.Default with { Query = "lynch", Band = Confidence.Uncertain, Sort = SortOrder.Year, YearUnknown = true };
        state = state.WithGenre(35, true).WithGenre(18, true).WithDecade(1990, true);

        Assert.Equal("q=lynch&conf=uncertain&genre=18,35&decade=1990,unknown&sort=year&page=3",
                     QueryState.Canonical(state, 3));
    }

    [Fact]
    public void ThePageIsWrittenOnlyFromTheSecondOnward()
    {
        Assert.Null(QueryState.ParametersFor(FilterState.Default, 1)[QueryState.PageKey]);
        Assert.Equal(2, QueryState.ParametersFor(FilterState.Default, 2)[QueryState.PageKey]);
    }

    // ================================================================ the round trip

    /// <remarks>
    /// THE PROPERTY THE ECHO DETECTION RESTS ON. The page writes a state, the router hands the address back, and
    /// the page tells its own echo from a link the visitor followed by comparing the two canonical strings. If
    /// any state the panel can produce did not survive the trip, the echo would read as a foreign navigation and
    /// overwrite what the visitor had just chosen.
    /// </remarks>
    public static IEnumerable<object[]> States()
    {
        var everything = FilterState.Default with { Query = "100% & more", Band = Confidence.Unmatched, YearUnknown = true, Sort = SortOrder.Year };
        everything = everything.WithGenre(28, true).WithGenre(18, true).WithDecade(1980, true).WithDecade(2000, true);

        return new (FilterState State, int Page)[]
        {
            (FilterState.Default, 1),
            (FilterState.Default with { Query = "lynch" }, 1),
            (FilterState.Default with { Band = Confidence.Uncertain }, 1),
            (FilterState.Default with { Band = Confidence.Unmatched }, 1),
            (FilterState.Default.WithGenre(18, true).WithGenre(35, true), 1),
            (FilterState.Default.WithDecade(1990, true), 1),
            (FilterState.Default with { YearUnknown = true }, 1),
            (FilterState.Default with { Sort = SortOrder.Runtime }, 7),
            (everything, 2),
        }.Select(s => new object[] { s.State, s.Page });
    }

    [Theory]
    [MemberData(nameof(States))]
    public void WhatAStateWritesIsWhatTheAddressReadsBack(FilterState state, int page)
    {
        var written = QueryState.ParametersFor(state, page);

        string[]? Many(string key) => written[key] is Array { Length: > 0 } values
            ? [.. values.Cast<object>().Select(v => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)!)]
            : null;
        string? One(string key) => Many(key)?.Single() ?? (written[key] as string);

        var back = Read(One(QueryState.QueryKey), One(QueryState.ConfidenceKey), Many(QueryState.GenreKey),
                        Many(QueryState.DecadeKey), One(QueryState.SortKey));
        var backPage = QueryState.ReadPage(written[QueryState.PageKey]?.ToString());

        Assert.Equal(QueryState.Canonical(state, page), QueryState.Canonical(back, backPage));
        Assert.Equal(page, backPage);
    }

    [Fact]
    public void EveryFieldChangesTheCanonicalText()
    {
        var baseline = QueryState.Canonical(FilterState.Default, 1);
        var changes = new[]
        {
            FilterState.Default with { Query = "a" },
            FilterState.Default with { Band = Confidence.Unmatched },
            FilterState.Default with { Band = Confidence.Uncertain },
            FilterState.Default.WithGenre(18, true),
            FilterState.Default.WithDecade(1990, true),
            FilterState.Default with { YearUnknown = true },
            FilterState.Default with { Sort = SortOrder.Year },
            FilterState.Default with { Sort = SortOrder.Runtime },
        };

        Assert.All(changes, c => Assert.NotEqual(baseline, QueryState.Canonical(c, 1)));
        Assert.Equal(changes.Length, changes.Select(c => QueryState.Canonical(c, 1)).Distinct().Count());
        Assert.NotEqual(baseline, QueryState.Canonical(FilterState.Default, 2));
    }

    // ================================================================ the writes

    private (QueryState Query, BunitNavigationManager Nav) Writer(string startAt)
    {
        var nav = Services.GetRequiredService<BunitNavigationManager>();
        nav.NavigateTo(startAt);

        return (new QueryState(nav), nav);
    }

    private static string Relative(NavigationManager nav) => nav.ToBaseRelativePath(nav.Uri);

    [Fact]
    public void EveryWriteReplacesTheHistoryEntryAndNeverAddsOne()
    {
        var (query, nav) = Writer("/movies");
        var before = nav.History.Count;

        query.WriteFilters(FilterState.Default with { Query = "a" });
        query.WritePage(3);

        // A replace takes the place of the entry on top, so the history is exactly as long as it was; a push
        // would have made it two longer. The top entry is the replacing one.
        Assert.Equal(before, nav.History.Count);
        Assert.True(nav.History.First().Options.ReplaceHistoryEntry);
    }

    [Fact]
    public void ClearingEverythingLeavesTheBareAddressAndNeverAnEmptyParameter()
    {
        var (query, nav) = Writer("/movies?q=old&conf=unmatched&genre=1&genre=2&decade=1990&sort=year&page=4");

        query.WriteFilters(FilterState.Default);

        Assert.Equal("movies", Relative(nav));
        Assert.DoesNotContain("q=", nav.Uri);
    }

    [Fact]
    public void WritingFiltersDropsThePageAndWritingAPageKeepsTheFilters()
    {
        var (query, nav) = Writer("/movies?q=old&page=4");

        query.WriteFilters(FilterState.Default with { Query = "new" });
        Assert.Equal("movies?q=new", Relative(nav));

        query.WritePage(5);
        Assert.Equal("movies?q=new&page=5", Relative(nav));

        query.WritePage(1);
        Assert.Equal("movies?q=new", Relative(nav));
    }

    [Fact]
    public void AParameterTheFilterDoesNotOwnIsLeftAlone()
    {
        var (query, nav) = Writer("/movies?utm_source=mail&q=old#results");

        query.WriteFilters(FilterState.Default.WithGenre(18, true));

        Assert.Contains("utm_source=mail", nav.Uri);
        Assert.Contains("genre=18", nav.Uri);
        Assert.EndsWith("#results", nav.Uri);
    }

    [Fact]
    public void RepeatedParametersAreWrittenAsRepeatedKeys()
    {
        var (query, nav) = Writer("/movies");

        query.WriteFilters(FilterState.Default.WithGenre(35, true).WithGenre(18, true).WithDecade(1990, true)
                           with { YearUnknown = true });

        Assert.Equal("movies?genre=18&genre=35&decade=1990&decade=unknown", Relative(nav));
    }

    [Fact]
    public void ASearchWithSpacesAndPunctuationIsEscaped()
    {
        var (query, nav) = Writer("/movies");

        query.WriteFilters(FilterState.Default with { Query = "a b&c=d" });

        Assert.Equal("movies?q=a%20b%26c%3Dd", Relative(nav));
    }
}

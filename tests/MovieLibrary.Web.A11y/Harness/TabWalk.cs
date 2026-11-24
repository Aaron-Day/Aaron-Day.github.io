namespace MovieLibrary.Web.A11y.Harness;

/// <summary>
/// How many key presses a walk of the page's tab order is allowed, and the arithmetic that number comes from.
/// </summary>
/// <remarks>
/// <para>
/// A walk presses Tab until focus leaves the document and gives up after <see cref="Presses"/>, because a focus trap
/// would otherwise walk for ever. The number has to be fitted to the PAGE THAT SHIPS and never to the catalogue the
/// gate happens to serve. It was once forty, which the gate fitted under only because its genre list was cut to
/// nine: the real library's panel has about forty-one stops and could not have finished the walk.
/// </para>
/// <para>
/// <b>To re-derive it,</b> count the stops the filter panel adds on the real library: one per genre the library
/// carries and one per decade it spans, on top of <see cref="ShellStops"/>. Measured on 10 November 2026 from
/// <c>enrich-cache/movies</c> (1,425 films with a cached TMDB record): all 19 genres and 10 decades (the 1930s to
/// the 2020s), so 11 + 19 + 10 = 40 stops, and 41 presses to leave. The sheet's own years may add a decade or an
/// unknown-year film, which is why the headroom below is computed from the CEILING and not from that measurement:
/// TMDB names 19 movie genres in all, and the whole of cinema is 14 decades (the 1890s to the 2020s), so the shell
/// and the panel together cannot exceed 44 stops. <b>On top of that come the tiles</b> - each one a link to its
/// film since the film page shipped - which is <see cref="TilesOnAFullPage"/> = 60 more and the largest term in
/// the sum, so no library can have more than <see cref="MostStopsTheRealPageCanHave"/> = 104 stops. It needs one
/// press more than that to leave, and <see cref="SpareForNewControls"/> is room for a control someone adds to the
/// shell later.
/// </para>
/// <para>
/// The genre and decade groups are <c>details</c> disclosures that start closed, so a walk that opens none of them
/// makes about 5 stops in the panel and not 29 to 33 (FacetPanelTests counts it). The ceiling above is left as it
/// is on purpose: it is still the right bound for a walk that has opened every group, and lowering it would make
/// the gate fail the day someone opens them.
/// </para>
/// </remarks>
internal static class TabWalk
{
    /// <summary>
    /// Stops that are there whatever the catalogue holds, with the error bar up: the skip link, the brand and the
    /// nav, the search box, the confidence group, Year unknown, the sort, the pager's two buttons and the error
    /// bar's two.
    /// </summary>
    public const int ShellStops = 11;

    /// <summary>The length of TMDB's movie genre list. A library cannot carry more genres than TMDB names.</summary>
    public const int GenresInTmdb = 19;

    /// <summary>Decades of film, the 1890s to the 2020s. A library cannot span more.</summary>
    public const int DecadesOfCinema = 14;

    /// <summary>Room for a control added to the shell later, so that adding one does not turn the gate red.</summary>
    public const int SpareForNewControls = 5;

    /// <summary>
    /// The tiles on a full page, each of which is now a link to that film and so a tab stop of its own.
    /// </summary>
    /// <remarks>
    /// <b>This is the largest single term in the bound, and leaving it out was the defect.</b> Until the film
    /// page shipped a tile held no interactive element at all, and <c>PosterGridTests</c> pinned that: the
    /// grid was sixty list items and nought stops. Each tile now carries one anchor around its body, so a full
    /// page of them is sixty stops BEFORE the shell and the panel, and a walk capped at the old 50 would have
    /// run out of presses in the middle of the grid - on every forward walk, at every viewport, in two test
    /// classes - and failed as though the page had a focus trap in it. The page size is not configurable from
    /// the page, so one page is the most a walk can meet.
    /// </remarks>
    public const int TilesOnAFullPage = Catalogue.PageSize;

    /// <summary>The most tab stops any library the page could be given can produce.</summary>
    public const int MostStopsTheRealPageCanHave =
        ShellStops + GenresInTmdb + DecadesOfCinema + TilesOnAFullPage;

    /// <summary>
    /// The presses a walk may make. A walk over N stops needs N + 1 of them, the last being the one that leaves the
    /// document, so a catalogue fits when its stops are FEWER than this.
    /// </summary>
    public const int Presses = MostStopsTheRealPageCanHave + 1 + SpareForNewControls;
}

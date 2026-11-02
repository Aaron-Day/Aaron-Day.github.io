namespace MovieLibrary.Enrich.Reporting;

/// <summary>
/// Everything the job summary prints, by name. A record rather than a long positional list: most of these are ints, and
/// two exchanged ints would compile and print the wrong number in the one place the owner reads.
/// </summary>
/// <param name="MatchedFilms">Films TMDB identified (<c>I != 0</c>). The denominator for the empty-cast count.</param>
/// <param name="FilmsWithEmptyCast">Matched films whose record has no cast at all.</param>
/// <param name="CastMembersSkipped">Cast members this run's TMDB responses carried that were skipped as unreadable.</param>
/// <param name="CacheSaveError">
/// Why the TMDB cache could not be saved, when a published run could not save it. Null on a healthy run. It is not a
/// reason to withhold the catalogue (see <c>EnrichRun.RunAgainstTmdbAsync</c>), so it has to be visible instead.
/// </param>
public sealed record JobReport(
    int Films, int Series, int TotalEpisodes, int OwnedEpisodes,
    int RequestsUsed, int MaxRequests, bool StoppedOnBudget, bool Fixtures,
    IReadOnlyList<string> ReviewQueue,
    int MatchedFilms, int FilmsWithEmptyCast, int CastMembersSkipped,
    string? CacheSaveError = null);

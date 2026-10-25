namespace MovieLibrary.Enrich.Sheets;

/// <summary>
/// The Overrides tab, split the way the two pipelines look it up. They use different keys, so one map cannot
/// serve both: <c>Films</c> is keyed by <c>TitleNormalizer.Normalize(title).Value</c> (the trailing year is
/// dropped, as <c>MoviePipeline</c> looks it up) and <c>Series</c> by <c>TvPipeline.OverrideKey(name)</c> (the
/// year is put back, as <c>TvPipeline</c> looks it up). A film and a series that share one folded title
/// therefore never collide. A row is a series override when its Runtime is blank or zero, a film otherwise.
/// </summary>
/// <param name="Films">TMDB ids for films, for <c>MoviePipeline.RunAsync</c>.</param>
/// <param name="Series">TMDB ids for series, for <c>TvPipeline.RunAsync</c>.</param>
/// <param name="Notes">
/// Things the owner should know and that must not pass silently: the tab was not found, so no override was
/// applied, or a row was ignored or overruled. Task 17 prints them in the job summary.
/// </param>
public sealed record SheetOverrides(
    IReadOnlyDictionary<string, int> Films,
    IReadOnlyDictionary<string, int> Series,
    IReadOnlyList<string> Notes);

using MovieLibrary.Core.Identity;
using MovieLibrary.Core.Models;
using MovieLibrary.Core.Parsing;
using MovieLibrary.Core.Store;
using MovieLibrary.Core.Text;
using MovieLibrary.Enrich.Store;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// Bound record stores for the pipeline tests: the one way a test settles a TMDB id without a search, now that
/// the Overrides tab is gone. Entries are keyed exactly as the pipelines key their rows
/// (<see cref="IdentityKey.ForFilm"/>, <see cref="IdentityKey.ForSeriesName"/>), so a test that builds a store
/// here and a run that binds one from JSON agree on where an entry lands.
/// </summary>
internal static class TestStore
{
    /// <summary>The identity key the film pipeline computes for this row: what a film entry must be filed under.</summary>
    public static string KeyFor(SheetRow row)
        => IdentityKey.ForFilm(TitleNormalizer.Normalize(row.Title),
                               RuntimeParser.FilmMinutes(row.Runtime),
                               TitleNormalizer.NormalizeDirectors(row.Directors));

    /// <summary>A store with one film entry per row, each holding the records given.</summary>
    public static BoundStore Films(params (SheetRow Row, RecordOverride[] Records)[] entries) => new()
    {
        Series = new Dictionary<string, SeriesEntry>(StringComparer.Ordinal),
        Notes = [],
        Films = entries.ToDictionary(
            e => KeyFor(e.Row),
            e => new FilmEntry
            {
                IdentityKey = KeyFor(e.Row),
                Sheet = new SheetEcho(e.Row.Title, e.Row.Runtime, e.Row.Directors),
                Records = e.Records,
            },
            StringComparer.Ordinal),
    };

    /// <summary>A store that forces one TMDB id for each row named: the replacement for a film Overrides row.</summary>
    public static BoundStore Forcing(params (SheetRow Row, int TmdbId)[] rows)
        => Films([.. rows.Select(r => (r.Row, new[] { new RecordOverride(TmdbId: r.TmdbId) }))]);

    /// <summary>A store with one series entry per name, each holding the record given.</summary>
    public static BoundStore Series(params (string Name, RecordOverride Record)[] entries) => new()
    {
        Films = new Dictionary<string, FilmEntry>(StringComparer.Ordinal),
        Notes = [],
        Series = entries.ToDictionary(
            e => IdentityKey.ForSeriesName(e.Name),
            e => new SeriesEntry
            {
                IdentityKey = IdentityKey.ForSeriesName(e.Name),
                Sheet = new SheetEcho(e.Name),
                Record = e.Record,
            },
            StringComparer.Ordinal),
    };

    /// <summary>A store that forces one TMDB id for each series named: the replacement for a series Overrides row.</summary>
    public static BoundStore ForcingSeries(params (string Name, int TmdbId)[] names)
        => Series([.. names.Select(n => (n.Name, new RecordOverride(TmdbId: n.TmdbId)))]);
}

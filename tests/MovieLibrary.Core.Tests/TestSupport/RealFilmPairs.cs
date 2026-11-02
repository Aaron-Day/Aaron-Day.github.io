using MovieLibrary.Core.Models;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// The five pairs in the owner's library (measured against the live sheet on 2026-11-02) that the cache's alias scan
/// would pair: one folded title, runtimes within two minutes. The figures, titles, runtimes and directors are the
/// sheet's own, so these are the real cases, not inventions. <see cref="SameFilm"/> is what a person would say.
/// </summary>
/// <remarks>
/// Blade Runner is the odd one out: its two rows share a runtime to the minute (117) and a director, so they also
/// share an identity key and are an exact cache hit that never reaches the alias scan. The other four differ in
/// runtime or director, so their keys differ.
/// </remarks>
internal sealed record RealFilmPair(string Name, SheetRow First, SheetRow Second, bool SameFilm)
{
    private static SheetRow Row(string title, string runtime, string directors)
        => new(title, runtime, directors, "", "", "");

    public static readonly IReadOnlyList<RealFilmPair> All =
    [
        new("BladeRunner",
            Row("Blade Runner", "1:57:10", "Ridley Scott"),
            Row("Blade Runner (The Final Cut)", "1:57:29", "Ridley Scott"), SameFilm: true),
        new("CaptainAmerica",
            Row("Captain America: The First Avenger", "2:05:15", "Joe Johnston"),
            Row("Captain America: The First Avenger", "2:04:09", "Joe Johnston"), SameFilm: true),
        new("Hitman",
            Row("Hitman (2007)", "1:32:49", "Xavier Gens"),
            Row("Hitman (Unrated)", "1:34:28", "Xavier Gens"), SameFilm: true),
        new("Carrie",
            Row("Carrie (1976)", "1:38:15", "Stephen King"),
            Row("Carrie (2013)", "1:39:43", "Kimberly Peirce"), SameFilm: false),
        new("Overboard",
            Row("Overboard (1987)", "1:52:17", "Garry Marshall"),
            Row("Overboard (2018)", "1:52:18", "Rob Greenberg"), SameFilm: false),
    ];

    public static RealFilmPair Named(string name) => All.Single(p => p.Name == name);

    public override string ToString() => Name;
}

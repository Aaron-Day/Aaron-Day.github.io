using MovieLibrary.Enrich.Pipeline;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// The 16 TMDB ids the owner's library holds twice, as measured against the published catalogue on 2026-11-03:
/// the id, the title TMDB gives the film, and the two sheet rows that took it. Thirteen are one film in two cuts the
/// owner owns, so both rows agree with the TMDB title. Three are wrong matches, flagged <see cref="Row.Impostor"/>:
/// a franchise sibling by the same director at a runtime inside the plus-or-minus-2 tolerance. <see cref="Row.Mc"/> is the
/// confidence the published catalogue gave each row, so the bands a run reports can be reproduced from these alone.
/// </summary>
internal sealed record RealCollision(int Id, string TmdbTitle, RealCollision.Row First, RealCollision.Row Second)
{
    /// <param name="Impostor">The row that does not belong to this film.</param>
    public sealed record Row(string SheetTitle, int Runtime, double Mc = 0.85, bool Impostor = false, string? Own = null, string Genre = "Drama");

    public IEnumerable<Row> Rows => [First, Second];

    public bool IsSplit => Rows.Any(r => r.Impostor);

    public IReadOnlyList<ResolvedFilm> Films()
        => [.. Rows.Select(r => ResolvedFilms.Make(Id, r.SheetTitle, r.Runtime, TmdbTitle, r.Own, mc: r.Mc, genre: r.Genre))];

    public override string ToString() => $"{Id} {TmdbTitle}";

    // Blade Runner's two rows are real and are different: two bundles the owner bought the film in, and two Apple genres.
    public const string BladeRunnerOwn = "Blade Runner / Blade Runner: The Final Cut. Colección 2 películas";
    public const string BladeRunnerFinalCutOwn = "Warner Bros Firsts: Action & Adventure Bundle";

    public static readonly IReadOnlyList<RealCollision> All =
    [
        new(78, "Blade Runner",
            new("Blade Runner", 117, Own: BladeRunnerOwn, Genre: "Action & Adventure"),
            new("Blade Runner (The Final Cut)", 117, Own: BladeRunnerFinalCutOwn, Genre: "Sci-Fi & Fantasy")),
        new(120, "The Lord of the Rings: The Fellowship of the Ring",
            new("The Lord of the Rings: The Fellowship of the Ring", 178), new("The Lord of the Rings: The Fellowship of the Ring (Extended Edition)", 228, 0.45)),
        new(121, "The Lord of the Rings: The Two Towers",
            new("The Lord of the Rings: The Two Towers", 179), new("The Lord of the Rings: The Two Towers (Extended Edition)", 235, 0.45)),
        new(122, "The Lord of the Rings: The Return of the King",
            new("The Lord of the Rings: The Return of the King", 201), new("The Lord of the Rings: The Return of the King (Extended Edition)", 263, 0.45)),
        new(280, "Terminator 2: Judgment Day",
            new("Terminator 2: Judgment Day", 137), new("Terminator 2: Judgment Day (Special Edition)", 153, 0.45)),
        new(1620, "Hitman", new("Hitman (2007)", 92, 0.55), new("Hitman (Unrated)", 94, 0.55)),
        new(1771, "Captain America: The First Avenger",
            new("Captain America: The First Avenger", 124), new("Captain America: The First Avenger", 125)),
        new(6957, "The 40 Year-Old Virgin",   // TMDB drops the hyphen the sheet keeps; the normalizer folds both to one value
            new("The 40-Year-Old Virgin", 116), new("The 40-Year-Old Virgin (Unrated)", 132, 0.45)),
        new(7446, "Tropic Thunder", new("Tropic Thunder", 106), new("Tropic Thunder (Director's Cut)", 121, 0.45)),
        new(9552, "The Exorcist", new("The Exorcist", 121), new("The Exorcist: Extended Director's Cut", 132, 0.45)),
        new(9615, "The Fast and the Furious: Tokyo Drift",
            new("Fast & Furious", 106, 0.70, Impostor: true), new("The Fast and the Furious: Tokyo Drift", 104)),
        new(13207, "Friday the 13th", new("Friday the 13th (2009)", 97, 0.95), new("Friday the 13th (Extended Cut) [2009]", 105, 0.55)),
        new(127585, "X-Men: Days of Future Past",
            new("X-Men: Days of Future Past", 131), new("X-Men: Days of Future Past (The Rogue Cut)", 149, 0.45)),
        new(263115, "Logan", new("Logan", 137), new("The Wolverine", 138, 0.70, Impostor: true)),
        new(286217, "The Martian", new("The Martian", 142), new("The Martian (Extended Version)", 151, 0.45)),
        new(365222, "Ip Man 3", new("Ip Man", 106, 0.70, Impostor: true), new("Ip Man 3", 104)),
    ];
}

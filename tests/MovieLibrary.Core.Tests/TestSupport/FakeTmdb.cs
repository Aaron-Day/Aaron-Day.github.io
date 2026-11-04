using System.Text.Json;
using System.Text.RegularExpressions;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// A TMDB that knows the films it is told about and nothing else. Every search answers with ALL of them, so which
/// one a sheet row takes is decided by the scorer, not by the stub. Each film comes back with every field the pipeline
/// reads (poster, backdrop, overview, tagline, genres, collection, cast, director), so a record that matched it holds
/// something in every TMDB-sourced field and a test can see whether one was left behind.
/// </summary>
internal sealed partial class FakeTmdb(params FakeTmdb.Film[] films)
{
    public sealed record Film(int Id, string Title, int Runtime, string Director, int Year = 2006, double Popularity = 10);

    [GeneratedRegex(@"^/3/movie/(\d+)(/credits)?$")]
    private static partial Regex MoviePath();

    public HttpResponseMessage Respond(HttpRequestMessage req)
    {
        var path = req.RequestUri!.AbsolutePath;
        if (path == "/3/search/movie")
            return TestPipeline.Json(JsonSerializer.Serialize(new
            {
                results = films.Select(f => new { id = f.Id, title = f.Title, release_date = $"{f.Year}-06-01", popularity = f.Popularity })
            }));

        var m = MoviePath().Match(path);
        if (!m.Success || films.SingleOrDefault(f => f.Id == int.Parse(m.Groups[1].Value)) is not { } film)
            return TestPipeline.NotFound();

        return TestPipeline.Json(JsonSerializer.Serialize(m.Groups[2].Success ? Credits(film) : Details(film)));
    }

    private static object Credits(Film f) => new
    {
        cast = new[] { new { name = "Lead " + f.Id, character = "Role " + f.Id, profile_path = $"/lead{f.Id}.jpg", order = 0 } },
        crew = new[] { new { job = "Director", name = f.Director } },
    };

    private static object Details(Film f) => new
    {
        id = f.Id, title = f.Title, overview = "Overview of " + f.Title, tagline = "Tagline of " + f.Title,
        runtime = f.Runtime, release_date = $"{f.Year}-06-01", vote_average = 7.5,
        poster_path = $"/poster{f.Id}.jpg", backdrop_path = $"/backdrop{f.Id}.jpg",
        genres = new object[] { new { id = 28, name = "Action" }, new { id = 1000 + f.Id % 1000, name = "Genre of " + f.Id } },
        belongs_to_collection = new { id = 7, name = "Collection of " + f.Id },
        credits = Credits(f),
    };
}

using System.Text.Json;
using MovieLibrary.Core.Models;

namespace MovieLibrary.Core.Tests;

/// <summary>CSV and TMDB bodies shared by the whole-run tests.</summary>
internal static class RunLibrary
{
    private static string Quote(string cell) => "\"" + cell.Replace("\"", "\"\"") + "\"";

    /// <summary>Movies tab rows. The export's unnamed sixth column is added.</summary>
    public static string Movies(params (string Title, string Runtime, string Directors, string Genre)[] films)
        => GoogleSheetClientTests.MoviesHeader + string.Concat(films.Select(f =>
            string.Join(",", Quote(f.Title), Quote(f.Runtime), Quote(f.Directors), Quote("Buy HD"), Quote(f.Genre), Quote("")) + "\n"));

    /// <summary>Shows tab rows: episode title, series, genre. Every episode runs 44 minutes.</summary>
    public static string Shows(params (string Episode, string Series, string Genre)[] rows)
        => GoogleSheetClientTests.ShowsHeader + string.Concat(rows.Select(r =>
            string.Join(",", Quote(r.Episode), Quote("44:00:00"), Quote(r.Series), Quote("Season Pass"), Quote(r.Genre), Quote("")) + "\n"));

    public static string Overrides(params (string Title, string Runtime, string Id)[] rows)
        => GoogleSheetClientTests.OverridesHeader + string.Concat(rows.Select(r => OverrideCsv.Line(r.Title, r.Runtime, r.Id)));

    public static readonly (string Title, string Runtime, string Directors, string Genre) Fargo =
        ("Fargo", "1:38:00", "Joel Coen & Ethan Coen", "Thriller");

    public const string FargoDetails = """
        {"id":275,"title":"Fargo","overview":"A pregnant cop investigates.","runtime":98,"release_date":"1996-03-08",
         "vote_average":7.5,"poster_path":"/p.jpg","backdrop_path":"/b.jpg",
         "genres":[{"id":80,"name":"Crime"},{"id":53,"name":"Thriller"}]}
        """;

    public const string FargoCredits = """
        {"cast":[
           {"name":"Frances McDormand","character":"Marge Gunderson","profile_path":"/fm.jpg","order":0},
           {"name":"William H. Macy","character":"Jerry Lundegaard","profile_path":"/wm.jpg","order":1}],
         "crew":[{"job":"Director","name":"Joel Coen"},{"job":"Director","name":"Ethan Coen"}]}
        """;

    /// <summary>TMDB knows Fargo and nothing else: a search finds it, its details and credits are the arguments.</summary>
    public static Func<HttpRequestMessage, HttpResponseMessage> FargoOnly(
        string details = FargoDetails, string credits = FargoCredits)
        => req => req.RequestUri!.AbsolutePath switch
        {
            "/3/search/movie" => TestPipeline.Json("""{"results":[{"id":275,"title":"Fargo","release_date":"1996-03-08","popularity":30}]}"""),
            "/3/movie/275" => TestPipeline.Json(details),
            "/3/movie/275/credits" => TestPipeline.Json(credits),
            _ => TestPipeline.NotFound()
        };

    /// <summary>A film's details as an override fetches them, in one request with its credits appended.</summary>
    public static string DetailsWithCredits(int id, string title)
        => JsonSerializer.Serialize(new
        {
            id, title, overview = "Forced.", runtime = 91, release_date = "1978-10-25", vote_average = 7.5,
            poster_path = "/p.jpg", backdrop_path = "/b.jpg",
            genres = new[] { new { id = 27, name = "Horror" } },
            credits = new
            {
                cast = new[] { new { name = "Jamie Lee Curtis", character = "Laurie Strode", profile_path = "/j.jpg", order = 0 } },
                crew = Array.Empty<object>()
            }
        });
}

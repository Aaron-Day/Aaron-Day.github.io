using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MovieLibrary.Core.Tests;

/// <summary>One season of a fake show: its number and its episode names. Episode numbers are 1-based positions.</summary>
internal sealed record FakeSeason(int Number, string[] Episodes)
{
    /// <summary>Explicit episode numbers, for a season whose numbering has gaps. Default: 1, 2, 3...</summary>
    public int[]? Numbers { get; init; }
}

internal sealed record FakeShow(int Id, string Name, FakeSeason[] Seasons)
{
    public string? PosterPath { get; init; } = "/poster.jpg";
    public string? FirstAirDate { get; init; } = "2001-09-30";

    /// <summary>What TMDB reports as <c>number_of_seasons</c>. Defaults to the highest season actually served.</summary>
    public int? ReportedSeasons { get; init; }

    /// <summary>The query that finds this show, when it is not simply <see cref="Name"/> (TMDB's name for it differs from the Sheet's).</summary>
    public string? SearchAs { get; init; }

    /// <summary>The <c>first_air_date_year</c> a search must carry to find this show. Null: the search must carry none.</summary>
    public int? SearchYear { get; init; }

    public int SeasonCount => ReportedSeasons ?? (Seasons.Length == 0 ? 0 : Seasons.Max(s => s.Number));
}

/// <summary>
/// A stand-in for the three TMDB TV endpoints the pipeline uses (<c>search/tv</c>, <c>tv/{id}</c>,
/// <c>tv/{id}/season/{n}</c>). Everything else, and any id or season it does not hold, is a 404.
/// Every episode runs 44 minutes, whatever the sheet says.
/// </summary>
internal sealed class FakeTv
{
    public const int EpisodeRuntime = 44;

    private static readonly Regex Details = new(@"^/3/tv/(\d+)$", RegexOptions.CultureInvariant);
    private static readonly Regex Season = new(@"^/3/tv/(\d+)/season/(\d+)$", RegexOptions.CultureInvariant);

    private readonly Dictionary<int, FakeShow> _byId = [];
    private readonly Dictionary<(string Query, int? Year), FakeShow> _byQuery = [];

    /// <summary>Answers a search that no registered show names, so a test need not spell the series out.</summary>
    public FakeShow? AnyQuery { get; init; }

    public FakeTv Add(FakeShow show)
    {
        _byId[show.Id] = show;
        _byQuery[(show.SearchAs ?? show.Name, show.SearchYear)] = show;
        return this;
    }

    public HttpResponseMessage Respond(HttpRequestMessage req)
    {
        var path = req.RequestUri!.AbsolutePath;

        if (path == "/3/search/tv")
        {
            var year = QueryValue(req.RequestUri, "first_air_date_year");
            var show = _byQuery.GetValueOrDefault((QueryValue(req.RequestUri, "query"), year.Length == 0 ? null : int.Parse(year))) ?? AnyQuery;
            return show is null
                ? TestPipeline.Json(TestPipeline.EmptyResults)
                : TestPipeline.Json(JsonSerializer.Serialize(new
                  {
                      results = new[] { new { id = show.Id, name = show.Name } }
                  }));
        }

        if (Details.Match(path) is { Success: true } d && ShowFor(d.Groups[1].Value) is { } found)
            return TestPipeline.Json(JsonSerializer.Serialize(new
            {
                id = found.Id,
                name = found.Name,
                overview = $"About {found.Name}.",
                poster_path = found.PosterPath,
                first_air_date = found.FirstAirDate,
                in_production = false,
                number_of_seasons = found.SeasonCount
            }));

        if (Season.Match(path) is { Success: true } s
            && ShowFor(s.Groups[1].Value) is { } owner
            && owner.Seasons.FirstOrDefault(x => x.Number == int.Parse(s.Groups[2].Value)) is { } season)
            return TestPipeline.Json(JsonSerializer.Serialize(new
            {
                season_number = season.Number,
                episodes = season.Episodes.Select((name, i) => new
                {
                    episode_number = season.Numbers?[i] ?? i + 1,
                    name,
                    overview = $"{name} overview.",
                    air_date = $"2001-10-{i % 28 + 1:00}",
                    runtime = EpisodeRuntime,
                    still_path = $"/still-{season.Number}-{i + 1}.jpg"
                })
            }));

        return TestPipeline.NotFound();
    }

    private FakeShow? ShowFor(string id)
    {
        var n = int.Parse(id);
        return _byId.GetValueOrDefault(n) ?? (AnyQuery?.Id == n ? AnyQuery : null);
    }

    private static string QueryValue(Uri uri, string name)
        => uri.Query.TrimStart('?').Split('&')
              .Select(pair => pair.Split('=', 2))
              .Where(p => p[0] == name)
              .Select(p => Uri.UnescapeDataString(p[1]))
              .FirstOrDefault() ?? "";
}

using MovieLibrary.Core.Models;
using MovieLibrary.Enrich.Config;
using MovieLibrary.Enrich.Emit;

namespace MovieLibrary.Core.Tests;

public class FixtureGeneratorTests
{
    private static SheetRow Film(string t, string rt, string dir)
        => new(t, rt, dir, "Some Collection", "Comedy", "");

    [Fact]
    public void KeepsRealSheetDataAndSynthesisesTheRest()
    {
        var (movies, _) = FixtureGenerator.Build([Film("Airplane!", "1:27:42", "David Zucker")], []);

        var m = Assert.Single(movies);
        Assert.Equal("Airplane!", m.T);          // real
        Assert.Equal(87, m.Rt);                  // real, parsed
        Assert.Equal("Comedy", m.Ag);            // real
        Assert.NotNull(m.Ov);                    // synthetic
        Assert.InRange(m.Vote, 1d, 10d);         // synthetic
    }

    [Fact]
    public void IsDeterministicSoDiffsStayReadable()
    {
        var a = FixtureGenerator.Build([Film("Alien", "1:56:45", "Ridley Scott")], []).Item1;
        var b = FixtureGenerator.Build([Film("Alien", "1:56:45", "Ridley Scott")], []).Item1;
        Assert.Equal(a.Single().Vote, b.Single().Vote);
        Assert.Equal(a.Single().Ov, b.Single().Ov);
    }

    [Fact]
    public void UsesNoPosterPathsSoNoTmdbRequestIsEverMade()
        => Assert.Null(FixtureGenerator.Build([Film("Alien", "1:56:45", "Ridley Scott")], [])
                                       .Item1.Single().P);

    [Fact]
    public void GroupsEpisodeRowsIntoShows()
    {
        var rows = new List<SheetRow>
        {
            new("Season 1, Episode 1: Pilot", "44:00:00", "", "", "Drama", "Alias"),
            new("Season 1, Episode 2: Next",  "44:00:00", "", "", "Drama", "Alias"),
        };

        var (_, shows) = FixtureGenerator.Build([], rows);

        var show = Assert.Single(shows);
        Assert.Equal("Alias", show.T);
        Assert.Equal(2, show.OwnedEpisodes);
    }

    [Fact]
    public void CommandLineFlagsOverrideEnvironment()
    {
        var env = new Dictionary<string, string> { ["TMDB_TOKEN"] = "eyJ.a.b", ["MAX_REQUESTS_PER_RUN"] = "40" };
        var opts = EnrichOptions.Load(["--fixtures", "--max-requests", "0"], env);

        Assert.True(opts.Fixtures);
        Assert.Equal(0, opts.MaxRequests);
        // The brief asserted "eyJ.a.b" here, but its own Load() blanks the token in fixture mode (and the
        // zero-requests guarantee wants that), so the two contradicted each other. The code wins; the token
        // passing through outside fixture mode is pinned in EnrichOptionsTests.
        Assert.Equal(string.Empty, opts.Token);
    }

    [Fact]
    public void FixtureModeNeedsNoToken()
    {
        var opts = EnrichOptions.Load(["--fixtures"], new Dictionary<string, string>());
        Assert.True(opts.Fixtures);
        Assert.Equal(string.Empty, opts.Token);
    }
}

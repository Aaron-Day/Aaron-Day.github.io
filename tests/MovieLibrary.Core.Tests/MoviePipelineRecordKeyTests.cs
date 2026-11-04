using MovieLibrary.Core.Identity;
using MovieLibrary.Core.Models;
using MovieLibrary.Core.Text;
using MovieLibrary.Enrich.Pipeline;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// RecordKeyTests prove the key's shape over fixtures; these prove the REAL pipeline threads its own identity
/// key into the record, which no fixture run can show (the fixtures build their own).
/// </summary>
public class MoviePipelineRecordKeyTests
{
    private static SheetRow Row(string title, string runtime, string directors)
        => new(title, runtime, directors, "", "Sci-Fi & Fantasy", "");

    [Fact]
    public async Task AnUnmatchedFilmIsKeyedByThePrefixOfItsContentIdentityKey()
    {
        var result = await TestPipeline.WithNoTmdbResults().RunAsync(
            [Row("Some Obscure Film", "1:35:00", "Nobody")], new Dictionary<string, int>(), CancellationToken.None);

        var record = Assert.Single(result.Records);
        var identity = IdentityKey.ForFilm(TitleNormalizer.Normalize("Some Obscure Film"), 95,
                                           TitleNormalizer.NormalizeDirectors("Nobody"));
        Assert.Equal(0, record.I);
        Assert.Equal("u-" + identity[..8], record.K);
    }

    // The M122 case on the real pipeline: both rows are id 0, and each still has a key of its own.
    [Fact]
    public async Task TwoUnmatchedFilmsShareAnIdButNotAKey()
    {
        var result = await TestPipeline.WithNoTmdbResults().RunAsync(
            [Row("Obscure One", "1:35:00", "Nobody"), Row("Obscure Two", "1:40:00", "Nobody")],
            new Dictionary<string, int>(), CancellationToken.None);

        Assert.All(result.Records, r => Assert.Equal(0, r.I));
        Assert.Equal(2, result.Records.Select(r => r.K).Distinct().Count());
    }

    // The collision this key scheme exists for, on the real pipeline: the owner owns two cuts of one film, TMDB
    // knows them as one id, and the rows differ only in runtime. Fixtures cannot show this - they never give two rows one id.
    [Fact]
    public async Task TwoCutsOfOneMatchedFilmShareAnIdButNotAKey()
    {
        var overrides = new Dictionary<string, int>
        {
            [MoviePipeline.OverrideKey("The Fellowship of the Ring", 178)] = 120,
            [MoviePipeline.OverrideKey("The Fellowship of the Ring", 228)] = 120,
        };

        var result = await TestPipeline.ThatFailsIfSearched(tmdbId: 120).RunAsync(
            [Row("The Fellowship of the Ring", "2:58:00", "Peter Jackson"),
             Row("The Fellowship of the Ring", "3:48:00", "Peter Jackson")],
            overrides, CancellationToken.None);

        Assert.Equal(2, result.Records.Count);
        Assert.All(result.Records, r => Assert.Equal(120, r.I));
        Assert.Equal(["120-178", "120-228"], result.Records.Select(r => r.K).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task AMatchedFilmIsKeyedByItsTmdbIdAndRuntime()
    {
        var overrides = new Dictionary<string, int> { [MoviePipeline.OverrideKey("Halloween", 91)] = 948 };

        var result = await TestPipeline.ThatFailsIfSearched(tmdbId: 948).RunAsync(
            [Row("Halloween", "1:31:00", "John Carpenter")], overrides, CancellationToken.None);

        var record = Assert.Single(result.Records);
        Assert.Equal(948, record.I);
        Assert.Equal("948-91", record.K);   // 1:31:00 is 91 minutes
    }
}

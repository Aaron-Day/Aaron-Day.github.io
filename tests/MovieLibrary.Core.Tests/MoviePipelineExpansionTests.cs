using MovieLibrary.Core.Identity;
using MovieLibrary.Core.Models;
using MovieLibrary.Core.Store;
using MovieLibrary.Core.Text;
using MovieLibrary.Enrich.Pipeline;
using MovieLibrary.Enrich.Store;

namespace MovieLibrary.Core.Tests;

public class MoviePipelineExpansionTests
{
    private const int BundleMinutes = 358;          // 5:58:00

    private static readonly SheetRow Bundle =
        new("Halloweentown", "5:58:00", "Duwayne Dunham", "iTunes", "Kids & Family", "");

    private static string Key =>
        IdentityKey.ForFilm(TitleNormalizer.Normalize(Bundle.Title), BundleMinutes,
                            TitleNormalizer.NormalizeDirectors(Bundle.Directors));

    private static readonly IReadOnlyDictionary<string, int> NoTabOverrides =
        new Dictionary<string, int>(StringComparer.Ordinal);

    private static BoundStore Four() => new()
    {
        Series = new Dictionary<string, SeriesEntry>(StringComparer.Ordinal),
        Notes = [],
        Films = new Dictionary<string, FilmEntry>(StringComparer.Ordinal)
        {
            [Key] = new FilmEntry
            {
                IdentityKey = Key,
                Sheet = new SheetEcho(Bundle.Title, Bundle.Runtime, Bundle.Directors),
                Note = "Four films in one Apple entry.",
                Records =
                [
                    new RecordOverride(TmdbId: 13589, Runtime: 84, Titles: new TitleSet(Display: "Halloweentown")),
                    new RecordOverride(TmdbId: 13590, Runtime: 84, Titles: new TitleSet(Display: "Halloweentown II: Kalabar's Revenge")),
                    new RecordOverride(TmdbId: 13591, Runtime: 83, Titles: new TitleSet(Display: "Halloweentown High")),
                    new RecordOverride(TmdbId: 13592, Runtime: 86, Titles: new TitleSet(Display: "Return to Halloweentown")),
                ],
            },
        },
    };

    private static Task<MoviePipelineResult> RunAsync() =>
        TestPipeline.ThatFailsIfSearched(13589, 13590, 13591, 13592)
                    .RunAsync([Bundle], NoTabOverrides, Four(), default);

    [Fact]
    public async Task OneRowPublishesOneRecordPerStoreRecord()
    {
        var result = await RunAsync();

        Assert.NotEmpty(result.Records);
        Assert.Equal(4, result.Records.Count);
        Assert.Equal([13589, 13590, 13591, 13592], result.Records.Select(m => m.I).Order());
    }

    [Fact]
    public async Task EachExpandedRecordKeepsItsOwnRuntimeTitleAndKey()
    {
        var result = await RunAsync();

        var high = Assert.Single(result.Records, m => m.I == 13591);
        Assert.Equal("Halloweentown High", high.T);
        Assert.Equal(83, high.Rt);
        Assert.Equal("13591-83", high.K);

        Assert.NotEmpty(result.Records);
        Assert.All(result.Records, m => Assert.NotEqual(BundleMinutes, m.Rt));
    }

    [Fact]
    public async Task TheKeysAreDistinctSoTheReconcilerCollapsesNothing()
    {
        var result = await RunAsync();

        Assert.NotEmpty(result.Records);
        Assert.Equal(result.Records.Count,
                     result.Records.Select(m => m.K).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task OnePurchaseCoversThemAllSoEveryRecordInheritsIt()
    {
        var result = await RunAsync();

        Assert.NotEmpty(result.Records);
        Assert.All(result.Records, m => Assert.Equal("iTunes", m.Own));
    }

    [Fact]
    public async Task ARowWithNoStoreEntryStillPublishesExactlyOneRecord()
    {
        var result = await TestPipeline.WithNoTmdbResults()
            .RunAsync([Bundle], NoTabOverrides, BoundStore.Empty, default);

        var film = Assert.Single(result.Records);
        Assert.Equal(0, film.I);
        Assert.Equal(BundleMinutes, film.Rt);
    }
}

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
                    .RunAsync([Bundle], Four(), default);

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
    public async Task TheRuntimeAloneKeepsFourRecordsOfOneFilmApartSoTheReconcilerCollapsesNothing()
    {
        // ONE id, four runtimes. With four different ids the keys differ whatever the runtime does, so the old form of
        // this test could only fail on an id collapse. Here the runtime is the only thing that parts them.
        var result = await TestPipeline.ThatFailsIfSearched(13589).RunAsync([Bundle],             Of(new RecordOverride(TmdbId: 13589, Runtime: 84),
               new RecordOverride(TmdbId: 13589, Runtime: 83),
               new RecordOverride(TmdbId: 13589, Runtime: 86),
               new RecordOverride(TmdbId: 13589, Runtime: 85)), default);

        Assert.Equal(4, result.Records.Count);
        Assert.Equal(["13589-83", "13589-84", "13589-85", "13589-86"],
                     result.Records.Select(m => m.K).Order(StringComparer.Ordinal));
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
            .RunAsync([Bundle], BoundStore.Empty, default);

        var film = Assert.Single(result.Records);
        Assert.Equal(0, film.I);
        Assert.Equal(BundleMinutes, film.Rt);
    }

    // ---- unmatched records of one row: the natural intermediate state of "expand now, choose ids later" ------

    private static BoundStore Of(params RecordOverride[] records) => new()
    {
        Series = new Dictionary<string, SeriesEntry>(StringComparer.Ordinal),
        Notes = [],
        Films = new Dictionary<string, FilmEntry>(StringComparer.Ordinal)
        {
            [Key] = new FilmEntry
            {
                IdentityKey = Key,
                Sheet = new SheetEcho(Bundle.Title, Bundle.Runtime, Bundle.Directors),
                Records = records,
            },
        },
    };

    private static Task<MoviePipelineResult> RunUnmatchedAsync(params RecordOverride[] records) =>
        TestPipeline.ThatFailsIfSearched(1)
                    .RunAsync([Bundle], Of(records), default);

    [Fact]
    public async Task ASingleUnmatchedRowKeepsTheKeyItAlwaysHad()
    {
        var result = await RunUnmatchedAsync(new RecordOverride(Candidates: [1, 2], Runtime: 84));

        var film = Assert.Single(result.Records);
        Assert.Equal(RecordKey.UnmatchedPrefix + Key[..RecordKey.HashLength], film.K);   // no suffix, no runtime
    }

    [Fact]
    public async Task TwoCandidatesOnlyRecordsOfOneRowGetDistinctKeysAndBothArePublished()
    {
        var result = await RunUnmatchedAsync(
            new RecordOverride(Candidates: [1, 2], Runtime: 84, Titles: new TitleSet(Display: "First")),
            new RecordOverride(Candidates: [3, 4], Runtime: 85, Titles: new TitleSet(Display: "Second")));

        Assert.Equal(2, result.Records.Count);
        Assert.Contains(result.Records, m => m.T == "First");
        Assert.Contains(result.Records, m => m.T == "Second");
        Assert.All(result.Records, m => Assert.Equal(0, m.I));
        Assert.Equal(2, result.Records.Select(m => m.K).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task TwoRecordsWithTheSameRuntimeStillGetDistinctKeysBecausePositionNotRuntimeSeparatesThem()
    {
        var result = await RunUnmatchedAsync(
            new RecordOverride(Candidates: [1, 2], Runtime: 84, Titles: new TitleSet(Display: "First")),
            new RecordOverride(Candidates: [3, 4], Runtime: 84, Titles: new TitleSet(Display: "Second")),
            new RecordOverride(Candidates: [5, 6], Runtime: 84, Titles: new TitleSet(Display: "Third")));

        // Three, not two: a scheme that suffixed the runtime onto every record after the first would still part
        // two records, and only a third at the same runtime shows that it is the position doing the work.
        Assert.Equal(3, result.Records.Count);
        Assert.All(result.Records, m => Assert.Equal(84, m.Rt));
        Assert.Equal(3, result.Records.Select(m => m.K).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(RecordKey.UnmatchedPrefix + Key[..RecordKey.HashLength], result.Records.Single(m => m.T == "First").K);
        Assert.EndsWith("-2", result.Records.Single(m => m.T == "Second").K, StringComparison.Ordinal);
        Assert.EndsWith("-3", result.Records.Single(m => m.T == "Third").K, StringComparison.Ordinal);
    }
}

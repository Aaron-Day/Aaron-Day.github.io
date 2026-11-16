using MovieLibrary.Core.Store;

namespace MovieLibrary.Core.Tests;

public class RecordStoreReaderTests
{
    private const string Key = "000dc50b084e2a2e3b0342fde1ea3550dfb48206";
    private const string OtherKey = "ffffc50b084e2a2e3b0342fde1ea3550dfb48206";

    private static string Film(string records, string key = Key) =>
        "{ \"version\": 1, \"series\": [], \"films\": [ { \"identityKey\": \"" + key + "\", " +
        "\"sheet\": { \"title\": \"A Film\", \"runtime\": \"1:40:00\", \"directors\": \"A Director\" }, " +
        "\"records\": " + records + " } ] }";

    private static RecordStoreException Rejected(string json)
        => Assert.Throws<RecordStoreException>(() => RecordStoreReader.Read(json, "test.json"));

    [Fact]
    public async Task AnAbsentFileIsAnEmptyStoreAndNotAFailure()
    {
        var path = Path.Combine(Path.GetTempPath(), "no-such-store-" + Guid.NewGuid() + ".json");

        var store = await RecordStoreReader.ReadFileAsync(path, CancellationToken.None);

        Assert.Equal(RecordStore.CurrentVersion, store.Version);
        Assert.Empty(store.Films);
        Assert.Empty(store.Series);
    }

    [Fact]
    public void AnAssertedIdAndItsOverriddenFieldsSurviveTheRoundTrip()
    {
        var store = RecordStoreReader.Read(Film(
            "[ { \"tmdbId\": 807, \"runtime\": 110, \"year\": 1995, " +
            "\"titles\": { \"display\": \"Se7en\", \"sort\": \"Se7en\" }, " +
            "\"directors\": \"David Fincher\", \"appleGenre\": \"Thriller\", \"purchaseInfo\": \"Blu-ray\", " +
            "\"extra\": { \"Purchase Date\": \"2019-03-14\" } } ]"), "test.json");

        var entry = Assert.Single(store.Films);
        Assert.Equal(Key, entry.IdentityKey);
        Assert.Equal("A Film", entry.Sheet.Title);
        var record = Assert.Single(entry.Records);
        Assert.Equal(807, record.TmdbId);
        Assert.Equal(110, record.Runtime);
        Assert.Equal(1995, record.Year);
        Assert.Equal("Se7en", record.Titles!.Display);
        Assert.Equal("Se7en", record.Titles!.Sort);
        Assert.Equal("David Fincher", record.Directors);
        Assert.Equal("Thriller", record.AppleGenre);
        Assert.Equal("Blu-ray", record.PurchaseInfo);
        Assert.Equal("2019-03-14", Assert.Contains("Purchase Date", record.Extra!));
    }

    [Fact]
    public void ASparseRecordLeavesEveryFieldUnset()
    {
        var record = Assert.Single(Assert.Single(
            RecordStoreReader.Read(Film("[ { \"tmdbId\": 11 } ]"), "t.json").Films).Records);

        Assert.Equal(11, record.TmdbId);
        Assert.Null(record.Candidates);
        Assert.Null(record.Runtime);
        Assert.Null(record.Titles);
        Assert.Null(record.Year);
        Assert.Null(record.Directors);
        Assert.Null(record.AppleGenre);
        Assert.Null(record.PurchaseInfo);
        Assert.Null(record.Extra);
    }

    [Fact]
    public void CandidatesWithoutAChoiceAreKept()
    {
        var record = Assert.Single(Assert.Single(
            RecordStoreReader.Read(Film("[ { \"candidates\": [13589, 13590] } ]"), "t.json").Films).Records);

        Assert.Null(record.TmdbId);
        Assert.Equal([13589, 13590], record.Candidates!);
    }

    [Theory]
    [InlineData("{ \"version\": 2, \"films\": [], \"series\": [] }", "version 2")]
    [InlineData("{ \"films\": [], \"series\": [] }", "version")]
    [InlineData("not json at all", "could not be read")]
    public void AStoreThatIsNotThisVersionOrNotJsonIsRejected(string json, string mustSay)
        => Assert.Contains(mustSay, Rejected(json).Message, StringComparison.OrdinalIgnoreCase);

    [Theory]
    [InlineData("[]", "at least one record")]
    [InlineData("[ { \"tmdbId\": 807, \"candidates\": [1, 2] } ]", "both")]
    [InlineData("[ { \"tmdbId\": 0 } ]", "positive")]
    [InlineData("[ { \"candidates\": [] } ]", "at least one candidate")]
    [InlineData("[ { \"candidates\": [7, 7] } ]", "twice")]
    [InlineData("[ { \"tmdbId\": 1, \"runtime\": 0 } ]", "positive")]
    [InlineData("[ { \"tmdbId\": 1, \"extra\": { \"  \": \"x\" } } ]", "blank")]
    [InlineData("[ { \"tmdbId\": 1 }, { \"tmdbId\": 2 } ]", "runtime")]
    [InlineData("[ { \"tmdbId\": 1, \"runtime\": 84 }, { \"runtime\": 88 } ]", "tmdbId")]
    public void ARecordThatCannotMeanAnythingIsRejected(string records, string mustSay)
    {
        var message = Rejected(Film(records)).Message;

        Assert.Contains(mustSay, message, StringComparison.OrdinalIgnoreCase);
        // A message has to be actionable, so it names the file and the entry, not only the rule.
        Assert.Contains("test.json", message, StringComparison.Ordinal);
        Assert.Contains("A Film", message, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoEntriesForOneRowAreRejectedRatherThanOneSilentlyWinning()
    {
        var json =
            "{ \"version\": 1, \"series\": [], \"films\": [ " +
            "{ \"identityKey\": \"" + Key + "\", \"sheet\": { \"title\": \"First\" }, \"records\": [ { \"tmdbId\": 1 } ] }, " +
            "{ \"identityKey\": \"" + Key + "\", \"sheet\": { \"title\": \"Second\" }, \"records\": [ { \"tmdbId\": 2 } ] } ] }";

        Assert.Contains("same identity key", Rejected(json).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnIdentityKeyThatIsNotOneIsRejectedBecauseItCouldNeverBind()
        => Assert.Contains("identity key",
                           Rejected(Film("[ { \"tmdbId\": 1 } ]", key: "ABC123")).Message,
                           StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void TwoFilmsAreBothKeptWhenTheirKeysDiffer()
    {
        var json =
            "{ \"version\": 1, \"series\": [], \"films\": [ " +
            "{ \"identityKey\": \"" + Key + "\", \"records\": [ { \"tmdbId\": 1 } ] }, " +
            "{ \"identityKey\": \"" + OtherKey + "\", \"records\": [ { \"tmdbId\": 2 } ] } ] }";

        var films = RecordStoreReader.Read(json, "t.json").Films;

        Assert.NotEmpty(films);              // T1: the Assert.All below would pass over nothing
        Assert.Equal(2, films.Count);
        Assert.All(films, f => Assert.Single(f.Records));
    }

    [Fact]
    public void CommentsAndATrailingCommaAreToleratedBecauseAHumanEditsThisFile()
    {
        var json =
            "{ \"version\": 1, \"series\": [], \n" +
            "  // Halloweentown is four films in one Apple row.\n" +
            "  \"films\": [ { \"identityKey\": \"" + Key + "\", \"records\": [ { \"tmdbId\": 1, \"runtime\": 84 }, ] }, ] }";

        Assert.Single(RecordStoreReader.Read(json, "t.json").Films);
    }

    [Fact]
    public void ASeriesEntryMayNotCarryAFilmOnlyField()
    {
        var json =
            "{ \"version\": 1, \"films\": [], \"series\": [ { \"identityKey\": \"" + Key + "\", " +
            "\"sheet\": { \"title\": \"A Show\" }, \"record\": { \"tmdbId\": 1399, \"runtime\": 60 } } ] }";

        Assert.Contains("series", Rejected(json).Message, StringComparison.OrdinalIgnoreCase);
    }
}

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

    private static string Series(string record) =>
        "{ \"version\": 1, \"films\": [], \"series\": [ { \"identityKey\": \"" + Key + "\", " +
        "\"sheet\": { \"title\": \"A Show\" }, \"record\": " + record + " } ] }";

    [Fact]
    public void AValidSeriesEntryIsAccepted()
    {
        var entry = Assert.Single(RecordStoreReader.Read(
            Series("{ \"tmdbId\": 1399, \"titles\": { \"display\": \"GoT\" }, \"extra\": { \"Box\": \"3\" } }"),
            "t.json").Series);

        Assert.Equal(Key, entry.IdentityKey);
        Assert.Equal(1399, entry.Record.TmdbId);
        Assert.Equal("GoT", entry.Record.Titles!.Display);
        Assert.Equal("3", Assert.Contains("Box", entry.Record.Extra!));
    }

    [Theory]
    [InlineData("runtime", "60")]
    [InlineData("year", "2008")]
    [InlineData("directors", "\"A Director\"")]
    [InlineData("purchaseInfo", "\"Blu-ray\"")]
    public void ASeriesEntryMayNotCarryAFilmOnlyField(string field, string value)
    {
        var message = Rejected(Series("{ \"tmdbId\": 1399, \"" + field + "\": " + value + " }")).Message;

        // "runtime and year" appear in the fix text of every series message, so match the "sets <field>," lead.
        Assert.Contains("sets " + field + ",", message, StringComparison.Ordinal);
        Assert.Contains("A Show", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingVersionIsRejectedWithTheFixSpelledOut()
        => Assert.Contains("Set \"version\": 1",
                           Rejected("{ \"films\": [], \"series\": [] }").Message, StringComparison.Ordinal);

    [Theory]
    [InlineData("{ \"version\": 1, \"films\": [] }")]
    [InlineData("{ \"version\": 1, \"series\": [] }")]
    [InlineData("{ \"version\": 1 }")]
    public void ATabLeftOutIsAnEmptyTabAndNotAnError(string json)
    {
        var store = RecordStoreReader.Read(json, "t.json");

        Assert.Empty(store.Films);
        Assert.Empty(store.Series);
    }

    [Fact]
    public void AnOmittedTabDoesNotHideTheOneThatIsPresent()
    {
        var json = "{ \"version\": 1, \"films\": [ { \"identityKey\": \"" + Key + "\", \"records\": [ { \"tmdbId\": 1 } ] } ] }";

        var store = RecordStoreReader.Read(json, "t.json");

        Assert.Single(store.Films);
        Assert.Empty(store.Series);
    }

    [Theory]
    [InlineData("{ \"version\": 1, \"films\": [], \"series\": [], \"nonsense\": 1 }", "nonsense")]
    [InlineData("{ \"version\": 1, \"series\": [], \"films\": [ { \"identityKey\": \"" + Key + "\", \"records\": [ { \"tmbdId\": 1 } ] } ] }", "tmbdId")]
    public void AnUnknownPropertyIsRejectedBecauseATypoWouldOtherwiseBeDroppedInSilence(string json, string property)
    {
        var message = Rejected(json).Message;

        Assert.Contains(property, message, StringComparison.Ordinal);
        Assert.Contains("test.json", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{ \"version\": 1, \"films\": null, \"series\": [] }")]
    [InlineData("{ \"version\": 1, \"films\": [], \"series\": null }")]
    [InlineData("{ \"version\": 1, \"series\": [], \"films\": [ { \"identityKey\": null, \"records\": [ { \"tmdbId\": 1 } ] } ] }")]
    [InlineData("{ \"version\": 1, \"series\": [], \"films\": [ { \"identityKey\": \"" + Key + "\", \"records\": null } ] }")]
    [InlineData("{ \"version\": 1, \"films\": [], \"series\": [ { \"identityKey\": \"" + Key + "\", \"record\": null } ] }")]
    [InlineData("{ \"version\": 1, \"series\": [], \"films\": [ null ] }")]
    [InlineData("{ \"version\": 1, \"films\": [], \"series\": [ null ] }")]
    [InlineData("null")]
    public void AJsonNullIsARejectionThatNamesTheFileAndNeverARawException(string json)
        => Assert.Contains("test.json", Rejected(json).Message, StringComparison.Ordinal);

    [Theory]
    [InlineData("{ \"version\": 1, \"series\": [], \"films\": [ null ] }", "film entry 1 is null. Remove it, or write the entry.")]
    [InlineData("{ \"version\": 1, \"series\": [], \"films\": [ { \"identityKey\": \"" + Key + "\", \"records\": [ { \"tmdbId\": 1 } ] }, null ] }", "film entry 2 is null. Remove it, or write the entry.")]
    [InlineData("{ \"version\": 1, \"films\": [], \"series\": [ null ] }", "series entry 1 is null. Remove it, or write the entry.")]
    public void ANullEntryIsRejectedByNameAndOneBasedPositionNotByTheSafetyNet(string json, string mustSay)
    {
        var message = Rejected(json).Message;

        Assert.Contains("'test.json'", message, StringComparison.Ordinal);
        Assert.Contains(mustSay, message, StringComparison.Ordinal);
        Assert.DoesNotContain("Unexpected internal error", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABodyOfJsonNullSaysSoAndIsNotTheSafetyNet()
    {
        var message = Rejected("null").Message;

        Assert.Contains("the body was JSON null", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Unexpected internal error", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{ \"version\": 1, \"series\": [], \"films\": [ { \"identityKey\": null, \"records\": [ { \"tmdbId\": 1 } ] } ] }", "$.films[0].identityKey")]
    [InlineData("{ \"version\": 1, \"series\": [], \"films\": [ { \"identityKey\": \"" + Key + "\", \"records\": null } ] }", "$.films[0].records")]
    [InlineData("{ \"version\": 1, \"films\": [], \"series\": [ { \"identityKey\": \"" + Key + "\", \"record\": null } ] }", "$.series[0].record")]
    [InlineData("{ \"version\": 1, \"films\": null, \"series\": [] }", "$.films")]
    public void AJsonNullInARequiredPropertyNamesTheJsonPath(string json, string path)
    {
        var message = Rejected(json).Message;

        Assert.Contains(path, message, StringComparison.Ordinal);
        Assert.DoesNotContain("Unexpected internal error", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMalformedBodyKeepsTheJsonExceptionAsItsInnerException()
    {
        var rejected = Rejected("{ \"version\": 1, \"films\": [ ");

        var inner = Assert.IsAssignableFrom<System.Text.Json.JsonException>(rejected.InnerException);
        Assert.NotNull(inner.LineNumber);
    }

    [Fact]
    public void AFaultInTheReaderIsReportedAsOneAndNotAsABadFile()
    {
        // A null body is a coding error by the caller, not something the file can contain; it reaches the safety net.
        var rejected = Assert.Throws<RecordStoreException>(() => RecordStoreReader.Read(null!, "record-store.json"));

        Assert.Contains("'record-store.json'", rejected.Message, StringComparison.Ordinal);
        Assert.Contains("Unexpected internal error", rejected.Message, StringComparison.Ordinal);
        Assert.Contains("editing the file will not fix it", rejected.Message, StringComparison.Ordinal);
        Assert.IsType<ArgumentNullException>(rejected.InnerException);
    }

    [Fact]
    public void ANullRecordNamesTheEntryAndThePosition()
    {
        var message = Rejected(Film("[ { \"tmdbId\": 1, \"runtime\": 84 }, null ]")).Message;

        Assert.Contains("A Film", message, StringComparison.Ordinal);
        Assert.Contains("record 2", message, StringComparison.Ordinal);
        Assert.Contains("null", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyRecordIsRejectedAndTheEntryAndPositionAreNamed()
    {
        var message = Rejected(Film("[ { \"tmdbId\": 1, \"runtime\": 84 }, { } ]")).Message;

        Assert.Contains("overrides nothing", message, StringComparison.Ordinal);
        Assert.Contains("should be deleted", message, StringComparison.Ordinal);
        Assert.Contains("A Film", message, StringComparison.Ordinal);
        Assert.Contains("record 2", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMessageNamesWhichRecordOfAnEntryIsBad()
    {
        var message = Rejected(Film("[ { \"tmdbId\": 1, \"runtime\": 84 }, { \"tmdbId\": 2, \"runtime\": 0 } ]")).Message;

        Assert.Contains("record 2", message, StringComparison.Ordinal);
        Assert.DoesNotContain("record 1", message, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoRowsWithTheSameTitleAreToldApartByTheirKeys()
    {
        var json =
            "{ \"version\": 1, \"series\": [], \"films\": [ " +
            "{ \"identityKey\": \"" + Key + "\", \"sheet\": { \"title\": \"Halloween\" }, \"records\": [ { \"tmdbId\": 1 } ] }, " +
            "{ \"identityKey\": \"" + Key + "\", \"sheet\": { \"title\": \"Halloween\" }, \"records\": [ { \"tmdbId\": 2 } ] } ] }";

        var message = Rejected(json).Message;

        Assert.Contains("Halloween", message, StringComparison.Ordinal);
        Assert.Contains(Key, message, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteThenReadKeepsEveryValueAndWriteOmitsWhatIsNull()
    {
        var store = new RecordStore
        {
            Version = RecordStore.CurrentVersion,
            Films =
            [
                new FilmEntry
                {
                    IdentityKey = Key,
                    Sheet = new SheetEcho("A Film", "1:40:00", "A Director"),
                    Note = "kept",
                    Records = [new RecordOverride(TmdbId: 807, Runtime: 110, Titles: new TitleSet("Se7en"))],
                },
            ],
            Series =
            [
                new SeriesEntry { IdentityKey = OtherKey, Record = new RecordOverride(Candidates: [1, 2]) },
            ],
        };

        var json = RecordStoreReader.Write(store);
        var back = RecordStoreReader.Read(json, "round.json");

        var film = Assert.Single(back.Films);
        Assert.Equal(Key, film.IdentityKey);
        Assert.Equal("kept", film.Note);
        Assert.Equal("1:40:00", film.Sheet.Runtime);
        var record = Assert.Single(film.Records);
        Assert.Equal(807, record.TmdbId);
        Assert.Equal(110, record.Runtime);
        Assert.Equal("Se7en", record.Titles!.Display);
        Assert.Null(record.Titles!.Sort);
        var series = Assert.Single(back.Series);
        Assert.Equal(OtherKey, series.IdentityKey);
        Assert.Equal([1, 2], series.Record.Candidates!);
        Assert.Null(series.Record.TmdbId);

        // Members left null are left out, not written as null.
        Assert.DoesNotContain("null", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"year\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"appleGenre\"", json, StringComparison.Ordinal);
    }
}
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
    [InlineData("{ \"version\": 3, \"films\": [], \"series\": [] }", "version 3")]
    [InlineData("not json at all", "could not be read")]
    public void AStoreThatIsNotAReadableVersionOrNotJsonIsRejected(string json, string mustSay)
        => Assert.Contains(mustSay, Rejected(json).Message, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Version 1 added nothing this build cannot read: it is version 2 with <c>publishedKey</c> omitted
    /// everywhere, which that member's own contract already allows. So it is READ, and upgraded in place --
    /// and the upgrade is asserted, because <c>StoreDraft.From</c> refuses any version but the current one, so
    /// a store that loaded but kept its old number would load and then be unusable by the editor.
    /// </summary>
    [Fact]
    public void AVersion1StoreIsReadAndUpgradedToTheCurrentVersion()
    {
        var store = RecordStoreReader.Read(
            "{ \"version\": 1, \"films\": [ { \"identityKey\": \"" + Key
            + "\", \"records\": [ { \"tmdbId\": 807 } ] } ], \"series\": [] }", "old.json");

        Assert.Equal(RecordStore.CurrentVersion, store.Version);
        Assert.Null(Assert.Single(Assert.Single(store.Films).Records).PublishedKey);
    }

    /// <summary>
    /// The member version 2 exists for. Round-tripped rather than merely read, because the editor rewrites the
    /// whole file on every save: a key that reads but does not write back is a key that is lost on the next
    /// correction, which is the exact failure the member was added to prevent.
    /// </summary>
    [Fact]
    public void APublishedKeyIsReadAndSurvivesARoundTrip()
    {
        var store = RecordStoreReader.Read(
            Film("[ { \"tmdbId\": 807, \"publishedKey\": \"u-01c8f4d6-1\" } ]"), "t.json");

        Assert.Equal("u-01c8f4d6-1", Assert.Single(Assert.Single(store.Films).Records).PublishedKey);

        var again = RecordStoreReader.Read(RecordStoreReader.Write(store), "again.json");

        Assert.Equal("u-01c8f4d6-1", Assert.Single(Assert.Single(again.Films).Records).PublishedKey);
    }

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

    /// <summary>
    /// A copy-paste slip while filling in a bundle. <c>RecordKey.ForFilm</c> is <c>{tmdbId}-{runtime}</c> for a
    /// matched record and reads <c>position</c> only for an unmatched one, so two records agreeing on both
    /// fields publish one key, <c>CatalogueReconciler</c> drops one, and the entry publishes three films where
    /// it lists four. Nothing else covers it: the expansion tests use one id at four DIFFERENT runtimes.
    /// </summary>
    [Fact]
    public void TwoRecordsOfOneEntryThatWouldPublishOneKeyAreRejected()
    {
        var message = Rejected(Film(
            "[ { \"tmdbId\": 11, \"runtime\": 121 }, { \"tmdbId\": 11, \"runtime\": 121 } ]")).Message;

        Assert.Contains("test.json", message, StringComparison.Ordinal);
        Assert.Contains("A Film", message, StringComparison.Ordinal);
        Assert.Contains("2 records with the same \"tmdbId\" (11) and \"runtime\" (121)", message, StringComparison.Ordinal);
        // The reason, not just the rule: without it the fix looks like "renumber something" rather than
        // "one of these two films is wrong".
        Assert.Contains("publish ONE key", message, StringComparison.Ordinal);
        Assert.Contains("fewer films than it lists", message, StringComparison.Ordinal);
    }

    [Theory]
    // The same id at two runtimes is the ordinary expanded bundle and must stay legal: that is how two cuts
    // of one film, or two films TMDB has as one, are published apart.
    [InlineData("[ { \"tmdbId\": 11, \"runtime\": 121 }, { \"tmdbId\": 11, \"runtime\": 122 } ]")]
    // Two runtimes, two ids: nothing shared at all.
    [InlineData("[ { \"tmdbId\": 11, \"runtime\": 121 }, { \"tmdbId\": 12, \"runtime\": 122 } ]")]
    // Candidates publish UNMATCHED, and RecordKey.ForFilm parts unmatched records by position, so two
    // identical candidate lists at one runtime are not the same defect and are not refused.
    [InlineData("[ { \"candidates\": [11], \"runtime\": 121 }, { \"candidates\": [11], \"runtime\": 121 } ]")]
    public void RecordsOfOneEntryThatWouldPublishTheirOwnKeysAreKept(string records)
    {
        var entry = Assert.Single(RecordStoreReader.Read(Film(records), "test.json").Films);

        Assert.Equal(2, entry.Records.Count);
    }

    [Fact]
    public void OneRecordRepeatingItsOwnIdAndRuntimeIsImpossibleSoTheRuleNeverFiresOnASingleRecord()
    {
        // The rule lives inside the Records.Count > 1 arm. Stated so that moving it out, where it would cost
        // a GroupBy over every single-record entry in the file, is a visible change rather than a tidy-up.
        var entry = Assert.Single(RecordStoreReader.Read(
            Film("[ { \"tmdbId\": 11, \"runtime\": 121 } ]"), "test.json").Films);

        Assert.Single(entry.Records);
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
        => Assert.Contains("Set \"version\": 2",
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

    /// <summary>
    /// Phase 1 ships no README and no seed path (<c>Write</c> exists, but only a test calls it), and the whole
    /// of the owner's written guidance is "paste the correct TMDB id into record-store.json". So a parse
    /// failure is the one place a hand-editor is guaranteed to meet, and System.Text.Json's own wording
    /// assumes he already knows the shape. The members and the example file are named here instead.
    /// </summary>
    /// <remarks>
    /// <b>It names <c>record-store.example.json</c> and no longer the design spec.</b> The spec lives under
    /// <c>docs/</c>, which is gitignored (<c>.gitignore:465</c>), so the message cited a document that is in
    /// the owner's working copy and in no clone at all - worse than citing nothing. The example is committed,
    /// and <c>RepositoryFileTests.TheExampleRecordStoreIsInTheRepositoryAndTheReaderAcceptsIt</c> is the other
    /// half of this pair: it proves the file the message names is there and parses.
    /// </remarks>
    [Fact]
    public void AParseFailureNamesTheShapeOfTheFileAndWhereItIsWrittenDown()
    {
        var message = Rejected("{ not json at all").Message;

        Assert.Contains("test.json", message, StringComparison.Ordinal);
        Assert.Contains("\"version\": 1", message, StringComparison.Ordinal);
        Assert.Contains("\"identityKey\"", message, StringComparison.Ordinal);
        Assert.Contains("\"records\"", message, StringComparison.Ordinal);
        Assert.Contains("\"tmdbId\"", message, StringComparison.Ordinal);
        Assert.Contains("\"appleGenre\"", message, StringComparison.Ordinal);
        // The one pairing a copied example can get wrong, named where it is read rather than left to the
        // rejection that follows (spec 5.2's own "all members" block showed both until this pass).
        Assert.Contains("\"tmdbId\" or \"candidates\" but never both", message, StringComparison.Ordinal);
        Assert.Contains("\"purchaseInfo\"", message, StringComparison.Ordinal);
        Assert.Contains("camelCase", message, StringComparison.Ordinal);
        Assert.Contains("comments and trailing commas are allowed", message, StringComparison.Ordinal);
        // The literal, not RecordStoreReader.ExampleFileName: the message is built from that constant, so
        // asserting it against itself would pass whatever it was renamed to and however little was shipped.
        Assert.Contains("record-store.example.json", message, StringComparison.Ordinal);
        Assert.Contains("--print-keys", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every member the message lists has to be one the reader actually accepts, or the guidance sends its
    /// reader to write a file that is then refused for an unknown property. Measured, not transcribed: the
    /// file below sets every name the message mentions and must round-trip.
    /// </summary>
    [Fact]
    public void EveryMemberTheParseFailureNamesIsOneTheReaderAccepts()
    {
        const string json = """
            { "version": 1,
              "films": [ { "identityKey": "000dc50b084e2a2e3b0342fde1ea3550dfb48206",
                           "sheet": { "title": "A Film", "runtime": "1:40:00", "directors": "A Director" },
                           "note": "Mine.",
                           "records": [ { "tmdbId": 11, "runtime": 84,
                                          "titles": { "display": "A Film", "sort": "Film, A" },
                                          "year": 1998, "directors": "A Director",
                                          "appleGenre": "Kids & Family", "purchaseInfo": "iTunes",
                                          "extra": { "Purchase Date": "2019-03-14" } } ] } ],
              "series": [ { "identityKey": "ffffc50b084e2a2e3b0342fde1ea3550dfb48206",
                            "sheet": { "title": "A Show" },
                            "note": "Also mine.",
                            "record": { "candidates": [1399, 1400] } } ] }
            """;

        var store = RecordStoreReader.Read(json, "test.json");

        var film = Assert.Single(store.Films).Records.Single();
        Assert.Equal("Film, A", film.Titles!.Sort);
        Assert.Equal("Kids & Family", film.AppleGenre);
        Assert.Equal("iTunes", film.PurchaseInfo);
        Assert.Equal([1399, 1400], Assert.Single(store.Series).Record.Candidates!);
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

    [Fact]
    public async Task AFolderWhereTheFileShouldBeIsRejectedAndNamedNotTreatedAsNoStore()
    {
        var dir = Directory.CreateTempSubdirectory("store-dir-").FullName;
        try
        {
            var ex = await Assert.ThrowsAsync<RecordStoreException>(
                () => RecordStoreReader.ReadFileAsync(dir, CancellationToken.None));

            Assert.Contains(dir, ex.Message, StringComparison.Ordinal);
            Assert.Contains("folder", ex.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("fault in the reader", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir);
        }
    }

    [Fact]
    public async Task ALockedFileIsRejectedAsTheFilesProblemNotTheReaders()
    {
        var path = Path.Combine(Path.GetTempPath(), "locked-store-" + Guid.NewGuid() + ".json");
        await File.WriteAllTextAsync(path, "{}");
        try
        {
            using var hold = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            var ex = await Assert.ThrowsAsync<RecordStoreException>(
                () => RecordStoreReader.ReadFileAsync(path, CancellationToken.None));

            Assert.Contains(path, ex.Message, StringComparison.Ordinal);
            Assert.Contains("locked", ex.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("fault in the reader", ex.Message, StringComparison.Ordinal);
            Assert.IsAssignableFrom<IOException>(ex.InnerException);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

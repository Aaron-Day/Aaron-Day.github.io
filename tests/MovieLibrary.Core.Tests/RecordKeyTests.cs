using MovieLibrary.Core.Identity;

namespace MovieLibrary.Core.Tests;

public class RecordKeyTests
{
    private const string Key = "3f9a21c80000000000000000000000000000abcd";

    [Fact]
    public void AMatchedFilmIsIdentifiedByItsTmdbIdAndItsRuntime()
        => Assert.Equal("807-127", RecordKey.ForFilm(807, 127, Key));

    // 16 TMDB ids are each held by two sheet rows. 15 of those pairs differ in runtime, so {id}-{runtime} separates them:
    // 13 of the 16 are one film in two cuts the owner owns (Blade Runner is the one of them whose runtimes are equal),
    // and 3 are wrong matches that the pipeline rejects rather than keys. Here are the two Fellowship cuts as the URL
    // shows them: the TMDB id stays readable, the runtime follows it, and the bare id would have merged them.
    [Fact]
    public void TheTwoFellowshipCutsHaveTheseLiteralKeys()
    {
        Assert.Equal("120-178", RecordKey.ForFilm(120, 178, Key));
        Assert.Equal("120-228", RecordKey.ForFilm(120, 228, Key));
    }

    // Intended: two rows that agree on id AND runtime are ONE key. The two Blade Runner rows are id 78 at
    // 117 minutes with the same director - a genuine duplicate row in the sheet with nothing to tell them apart,
    // so there is nothing a key could honestly separate them by.
    [Fact]
    public void TwoRowsThatAgreeOnIdAndRuntimeCollapseToOneKey()
    {
        var first = RecordKey.ForFilm(78, 117, Key);
        var second = RecordKey.ForFilm(78, 117, "ffffffff" + Key);   // a different identity key does not matter once matched
        Assert.Equal("78-117", first);
        Assert.Equal(first, second);
    }

    // Different films that happen to run the same length are not confused: the id comes first.
    [Fact]
    public void TwoFilmsOfTheSameLengthDoNotShareAKey()
        => Assert.NotEqual(RecordKey.ForFilm(120, 178, Key), RecordKey.ForFilm(121, 178, Key));

    // The 17 unmatched films all arrive as id 0. Keyed on the id they would be one record.
    [Fact]
    public void AnUnmatchedFilmIsIdentifiedByItsContentHash()
        => Assert.Equal("u-3f9a21c8", RecordKey.ForFilm(null, 90, Key));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AnIdThatIsNotAPositiveNumberIsNotAnIdentity(int notAnId)
        => Assert.StartsWith("u-", RecordKey.ForFilm(notAnId, 90, Key));

    // The prefix is what keeps the two spaces from ever colliding: no TMDB id starts with "u-".
    [Fact]
    public void TheTwoFormsCannotCollide()
        => Assert.NotEqual(RecordKey.ForFilm(3, 90, Key), RecordKey.ForFilm(null, 90, Key));

    [Fact]
    public void TheHashIsTakenFromTheFrontSoItIsStableAcrossKeyLengths()
        => Assert.Equal("u-3f9a21c8", RecordKey.ForFilm(null, 90, Key[..20]));

    [Fact]
    public void AnIdentityKeyTooShortToHashIsAProgrammerError()
        => Assert.Throws<ArgumentException>(() => RecordKey.ForFilm(null, 90, "3f9a"));

    // M122 reached production because every fixture film had a unique non-zero id. These two assertions
    // are about the SHAPE of a catalogue, so they hold for fixtures and for a real run alike.
    [Fact]
    public void EveryRecordInACatalogueHasADistinctKey()
    {
        var records = TestCatalogue.Movies();   // fixtures; one in 80 is unmatched
        Assert.Contains(records, r => r.I == 0);                   // the case exists at all
        Assert.Equal(records.Count, records.Select(r => r.K).Distinct().Count());
    }

    [Fact]
    public void TheUnmatchedShareAnIdButNotAKey()
    {
        var unmatched = TestCatalogue.Movies().Where(r => r.I == 0).ToList();
        Assert.NotEmpty(unmatched);   // All() over nothing passes, so the case has to exist
        Assert.All(unmatched, r => Assert.StartsWith(RecordKey.UnmatchedPrefix, r.K));
        Assert.Equal(unmatched.Count, unmatched.Select(r => r.K).Distinct().Count());
    }

    // Over a whole catalogue, a matched film's key is exactly its own id and its own runtime.
    [Fact]
    public void EveryMatchedFilmIsKeyedByItsIdAndItsRuntime()
    {
        var matched = TestCatalogue.Movies().Where(r => r.I != 0).ToList();
        Assert.NotEmpty(matched);
        Assert.All(matched, r => Assert.Equal($"{r.I}-{r.Rt}", r.K));
    }

    // ---- position: what keeps two unmatched records of ONE expanded row apart --------------------------------

    [Fact]
    public void ThePositionDefaultsToOneAndLeavesTheUnmatchedKeyExactlyAsItWas()
    {
        Assert.Equal("u-3f9a21c8", RecordKey.ForFilm(null, 90, Key, 1));
        Assert.Equal(RecordKey.ForFilm(null, 90, Key), RecordKey.ForFilm(null, 90, Key, 1));
    }

    [Fact]
    public void ALaterPositionAppendsItselfToTheUnmatchedHash()
    {
        Assert.Equal("u-3f9a21c8-2", RecordKey.ForFilm(null, 90, Key, 2));
        Assert.Equal("u-3f9a21c8-12", RecordKey.ForFilm(null, 90, Key, 12));
    }

    [Fact]
    public void ThePositionNeverTouchesAMatchedKey()
        => Assert.Equal("807-127", RecordKey.ForFilm(807, 127, Key, 3));

    [Fact]
    public void ThePositionIsOneBased()
        => Assert.Throws<ArgumentOutOfRangeException>(() => RecordKey.ForFilm(null, 90, Key, 0));
}

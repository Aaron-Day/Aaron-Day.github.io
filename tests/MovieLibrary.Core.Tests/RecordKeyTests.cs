using MovieLibrary.Core.Identity;

namespace MovieLibrary.Core.Tests;

public class RecordKeyTests
{
    private const string Key = "3f9a21c80000000000000000000000000000abcd";

    [Fact]
    public void AMatchedFilmIsIdentifiedByItsTmdbId()
        => Assert.Equal("807", RecordKey.ForFilm(807, Key));

    // The 17 unmatched films all arrive as id 0. Keyed on the id they would be one record.
    [Fact]
    public void AnUnmatchedFilmIsIdentifiedByItsContentHash()
        => Assert.Equal("u-3f9a21c8", RecordKey.ForFilm(null, Key));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AnIdThatIsNotAPositiveNumberIsNotAnIdentity(int notAnId)
        => Assert.StartsWith("u-", RecordKey.ForFilm(notAnId, Key));

    // The prefix is what keeps the two spaces from ever colliding: no TMDB id starts with "u-".
    [Fact]
    public void TheTwoFormsCannotCollide()
        => Assert.NotEqual(RecordKey.ForFilm(3, Key), RecordKey.ForFilm(null, Key));

    [Fact]
    public void TheHashIsTakenFromTheFrontSoItIsStableAcrossKeyLengths()
        => Assert.Equal("u-3f9a21c8", RecordKey.ForFilm(null, Key[..20]));

    [Fact]
    public void AnIdentityKeyTooShortToHashIsAProgrammerError()
        => Assert.Throws<ArgumentException>(() => RecordKey.ForFilm(null, "3f9a"));

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
}

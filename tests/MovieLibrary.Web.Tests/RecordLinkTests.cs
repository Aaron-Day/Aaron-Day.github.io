using MovieLibrary.Core.Identity;
using MovieLibrary.Core.Models;
using MovieLibrary.Core.Store;
using MovieLibrary.Core.Text;
using MovieLibrary.Web.Editor.Store;

namespace MovieLibrary.Web.Tests;

public sealed class RecordLinkTests
{
    /// <summary>A record shaped the way MoviePipeline shapes one: title and directors straight from the sheet.</summary>
    private static MovieRecord Published(string title, int runtime, string[] directors, int tmdbId) =>
        new(I: tmdbId,
            K: RecordKey.ForFilm(tmdbId == 0 ? null : tmdbId, runtime, Key(title, runtime, directors)),
            T: title, Y: 1995, Rt: runtime, P: null, B: null, G: [], Ag: "Drama", Vote: 8.4,
            Dir: directors, Ov: null, Tag: null, Cast: [], Col: null, Own: null, Mc: 0.95);

    private static string Key(string title, int runtime, string[] directors) =>
        IdentityKey.ForFilm(TitleNormalizer.Normalize(title), runtime,
                            TitleNormalizer.NormalizeDirectors(string.Join(" & ", directors)));

    [Fact]
    public void ThePublishedRecordYieldsTheIdentityKeyThePipelineHashed()
    {
        var record = Published("Se7en", 110, ["David Fincher"], 807);

        Assert.Equal(Key("Se7en", 110, ["David Fincher"]), RecordLink.IdentityKeyOf(record));
    }

    [Fact]
    public void RejoiningTheDirectorArrayCannotChangeTheKey()
    {
        var a = Key("Heat", 170, ["Michael Mann&Someone Else"]);
        var b = Key("Heat", 170, ["Someone Else & Michael Mann"]);
        var record = Published("Heat", 170, ["Michael Mann", "Someone Else"], 949);

        Assert.Equal(a, b);
        Assert.Equal(a, RecordLink.IdentityKeyOf(record));
    }

    [Fact]
    public void AnUnmatchedRecordsOwnKeyAgreesWithTheComputedOne()
    {
        var record = Published("Obscure Film", 95, ["Nobody Known"], 0);

        Assert.StartsWith(RecordKey.UnmatchedPrefix, record.K, StringComparison.Ordinal);
        Assert.True(RecordLink.Agrees(record, RecordLink.IdentityKeyOf(record)));
    }

    [Fact]
    public void AnUnmatchedRecordDisagreesWithAKeyThatIsNotItsOwn()
    {
        var record = Published("Obscure Film", 95, ["Nobody Known"], 0);

        Assert.False(RecordLink.Agrees(record, new string('a', 40)));
    }

    [Fact]
    public void AgreementIsUnknowableForAMatchedRecordAndSaysSoRatherThanGuessing()
    {
        var record = Published("Se7en", 110, ["David Fincher"], 807);

        Assert.Null(RecordLink.Agrees(record, RecordLink.IdentityKeyOf(record)));
    }

    [Fact]
    public void AStoreEntrysPublishedKeyIsFoundFromTheEntryAlone()
    {
        var entry = new FilmEntry
        {
            IdentityKey = new string('b', 40),
            Sheet = new SheetEcho("Seven", "2:06:53", "David Fincher"),
            Records = [new RecordOverride(TmdbId: 807)],
        };

        Assert.Equal("807-126", RecordLink.PublishedKeyOf(entry, 1));
    }

    [Fact]
    public void ARecordsOwnRuntimeBeatsTheEcho()
    {
        var entry = new FilmEntry
        {
            IdentityKey = new string('b', 40),
            Sheet = new SheetEcho("Halloweentown", "5:58:00", "Duwayne Dunham"),
            Records = [new RecordOverride(TmdbId: 13589, Runtime: 84)],
        };

        Assert.Equal("13589-84", RecordLink.PublishedKeyOf(entry, 1));
    }

    [Fact]
    public void AnUnmatchedEntrysSecondRecordCarriesItsPosition()
    {
        var key = "0123456789abcdef0123456789abcdef01234567";
        var entry = new FilmEntry
        {
            IdentityKey = key,
            Records = [new RecordOverride(Runtime: 80), new RecordOverride(Runtime: 90)],
        };

        Assert.Equal("u-" + key[..8], RecordLink.PublishedKeyOf(entry, 1));
        Assert.Equal("u-" + key[..8] + "-2", RecordLink.PublishedKeyOf(entry, 2));
    }

    [Fact]
    public void AnEntryWithNoRuntimeAnywhereCannotBePlacedAndSaysSo()
    {
        var entry = new FilmEntry
        {
            IdentityKey = new string('b', 40),
            Sheet = new SheetEcho(),
            Records = [new RecordOverride(TmdbId: 807)],
        };

        Assert.Null(RecordLink.PublishedKeyOf(entry, 1));
    }
}

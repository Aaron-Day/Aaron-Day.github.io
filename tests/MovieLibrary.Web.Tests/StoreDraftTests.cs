using MovieLibrary.Core.Store;
using MovieLibrary.Web.Editor.Store;

namespace MovieLibrary.Web.Tests;

public sealed class StoreDraftTests
{
    private static FilmEntry Entry(char keyChar, RecordOverride? record = null) =>
        new() { IdentityKey = new string(keyChar, 40), Records = [record ?? new RecordOverride(TmdbId: 807, Runtime: 110)] };

    [Fact]
    public void ANewEntryIsAppendedAndSurvivesARoundTrip()
    {
        var draft = StoreDraft.From(RecordStore.Empty).With(Entry('a'));

        Assert.Single(draft.Store.Films);
        var read = RecordStoreReader.Read(draft.ToValidatedJson(), "a draft");
        var back = Assert.Single(read.Films);
        Assert.Equal(new string('a', 40), back.IdentityKey);
        Assert.Equal(807, back.Records[0].TmdbId);
    }

    [Fact]
    public void AnExistingEntryIsReplacedWhereItSatRatherThanAppended()
    {
        var draft = StoreDraft.From(RecordStore.Empty).With(Entry('a')).With(Entry('b'));
        var replacement = Entry('a', new RecordOverride(TmdbId: 999, Runtime: 100));

        var next = draft.With(replacement);

        Assert.Equal(2, next.Store.Films.Count);
        Assert.Same(replacement, next.Store.Films[0]);
        Assert.Equal(new string('b', 40), next.Store.Films[1].IdentityKey);
    }

    [Fact]
    public void TheDraftIsImmutable()
    {
        var draft = StoreDraft.From(RecordStore.Empty).With(Entry('a'));

        var added = draft.With(Entry('b'));
        var removed = draft.Without(new string('a', 40));

        Assert.NotSame(draft, added);
        Assert.NotSame(draft, removed);
        Assert.Single(draft.Store.Films);
    }

    [Fact]
    public void WithoutRemovesTheNamedEntryAndLeavesTheRest()
    {
        var draft = StoreDraft.From(RecordStore.Empty).With(Entry('a')).With(Entry('b'));

        var next = draft.Without(new string('a', 40));

        var left = Assert.Single(next.Store.Films);
        Assert.Equal(new string('b', 40), left.IdentityKey);
    }

    [Fact]
    public void RemovingAKeyThatIsNotThereChangesNothing()
    {
        var draft = StoreDraft.From(RecordStore.Empty).With(Entry('a'));

        var next = draft.Without(new string('c', 40));

        Assert.Single(next.Store.Films);
    }

    [Fact]
    public void KeysAreComparedOrdinally()
    {
        var draft = StoreDraft.From(RecordStore.Empty).With(Entry('a'));

        Assert.NotNull(draft.Film(new string('a', 40)));
        Assert.Null(draft.Film(new string('A', 40)));
    }

    [Fact]
    public void TheReaderIsTheValidatorAndTheDraftDoesNotSecondGuessIt()
    {
        var bad = Entry('a', new RecordOverride(TmdbId: 807, Candidates: [807, 808]));
        var draft = StoreDraft.From(RecordStore.Empty).With(bad);

        Assert.Throws<RecordStoreException>(() => draft.ToValidatedJson());
    }

    [Fact]
    public void SeriesEntriesPassThroughUntouched()
    {
        var series = new SeriesEntry { IdentityKey = new string('d', 40), Record = new RecordOverride(TmdbId: 1396) };
        var draft = StoreDraft.From(RecordStore.Empty with { Series = [series] });

        var viaWith = draft.With(Entry('a'));
        var viaWithout = viaWith.Without(new string('a', 40));

        Assert.Same(series, Assert.Single(viaWith.Store.Series));
        Assert.Same(series, Assert.Single(viaWithout.Store.Series));
    }
}

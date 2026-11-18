using MovieLibrary.Core.Store;

namespace MovieLibrary.Web.Editor.Store;

/// <summary>
/// A store being edited: the loaded <see cref="RecordStore"/> plus the three operations the editor performs on
/// it, and one way to turn it back into bytes.
/// </summary>
/// <remarks>
/// <para><b>The reader is the only validator.</b> <see cref="ToValidatedJson"/> serialises and then reads the
/// result straight back through <see cref="RecordStoreReader"/>, so a draft that would fail the next build
/// cannot reach the disk -- and the editor never re-implements a store rule. It does not know that an id and
/// candidates are mutually exclusive, that a key is 40 hex characters, or that an expanded entry needs a runtime
/// on every record. The reader knows, its messages already name the entry and the fix, and a rule added there
/// later is enforced here with no change at all. Validation duplicated in a form is validation that drifts.</para>
/// <para><b>Immutable, and upserts in place.</b> A component holding a draft that mutates is a component whose
/// render sees a different value than the one it was given. Replacing an entry keeps its position, because this
/// file is committed and read in a diff: an upsert that appended and deleted would move an unrelated line.</para>
/// </remarks>
public sealed record StoreDraft
{
    private StoreDraft(RecordStore store) => Store = store;

    public RecordStore Store { get; }

    public static StoreDraft From(RecordStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        if (store.Version != RecordStore.CurrentVersion)
            throw new ArgumentException(
                $"A draft can only be built on a store of version {RecordStore.CurrentVersion}, and this one "
                + $"says {store.Version}. RecordStoreReader rejects an unknown version at load, so a store that "
                + "reached this point with the wrong one did not come through the reader.",
                nameof(store));

        return new StoreDraft(store);
    }

    public FilmEntry? Film(string identityKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identityKey);

        return Store.Films.FirstOrDefault(f => string.Equals(f.IdentityKey, identityKey, StringComparison.Ordinal));
    }

    public StoreDraft With(FilmEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var films = Store.Films.ToList();
        var at = films.FindIndex(f => string.Equals(f.IdentityKey, entry.IdentityKey, StringComparison.Ordinal));

        if (at >= 0) films[at] = entry;
        else films.Add(entry);

        return new StoreDraft(Store with { Films = films });
    }

    public StoreDraft Without(string identityKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identityKey);

        var films = Store.Films
            .Where(f => !string.Equals(f.IdentityKey, identityKey, StringComparison.Ordinal))
            .ToList();

        return films.Count == Store.Films.Count ? this : new StoreDraft(Store with { Films = films });
    }

    /// <exception cref="RecordStoreException">The draft would not load. The message is the reader's own.</exception>
    public string ToValidatedJson()
    {
        var json = RecordStoreReader.Write(Store);

        // Read it back before anybody writes it anywhere. The parse is the validation.
        RecordStoreReader.Read(json, "the record store about to be saved");

        return json;
    }
}

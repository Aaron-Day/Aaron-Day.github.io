namespace MovieLibrary.Core.Store;

/// <summary>
/// The store could not be read, or says something that cannot mean anything. Always fatal: a build that
/// carried on would publish a catalogue missing the owner's own corrections, with nothing going red.
/// </summary>
public sealed class RecordStoreException : Exception
{
    public RecordStoreException(string message) : base(message) { }

    public RecordStoreException(string message, Exception inner) : base(message, inner) { }
}

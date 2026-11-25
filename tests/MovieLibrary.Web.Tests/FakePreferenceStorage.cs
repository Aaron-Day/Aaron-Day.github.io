using MovieLibrary.Web.Services;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// The owner's saved state, in memory, with the two failures the real browser has.
/// </summary>
/// <remarks>
/// <b>It counts writes, and that is not decoration.</b> The store's rule is that a query answers from memory
/// and the write is what is awaited, so "did this edit reach storage at all" and "did it reach it once" are
/// exactly the assertions that separate a working store from one that keeps everything in RAM and loses it
/// on reload. <see cref="Refuse"/> is the storage that is full or blocked, which is the case the owner has
/// to be TOLD about rather than the one that can be swallowed.
/// </remarks>
internal sealed class FakePreferenceStorage : IPreferenceStorage
{
    public FakePreferenceStorage(string? initial = null) => Json = initial;

    /// <summary>What is "in the browser". Set it directly to stand in for a document written by another build.</summary>
    public string? Json { get; set; }

    public int Reads { get; private set; }

    public int Writes { get; private set; }

    /// <summary>When true, every write fails the way a full or blocked localStorage does.</summary>
    public bool Refuse { get; set; }

    public Task<string?> ReadAsync()
    {
        Reads++;
        return Task.FromResult(Json);
    }

    public Task WriteAsync(string json)
    {
        Writes++;

        if (Refuse) throw new PreferenceStorageException(new InvalidOperationException("the stub was asked to refuse"));

        Json = json;
        return Task.CompletedTask;
    }
}

using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MovieLibrary.Web.Services;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// The owner's saved state, registered the way the app registers it, for a test that renders UI.
/// </summary>
/// <remarks>
/// <b>One instance, reachable from the test.</b> The store is the thing under test in
/// <c>PreferenceStoreTests</c> and a DEPENDENCY here, so what a UI test needs is the storage BEHIND it: to
/// seed a document before the first render, to set <see cref="FakePreferenceStorage.Refuse"/>, and to count
/// writes afterwards. Registering the fake as itself as well as as the interface is what makes those the
/// same object rather than two.
/// <para>
/// Separate from <c>AnnouncerTestExtensions</c>'s registrations only in the file it lives in: that helper
/// calls this one, so every test that renders a page gets a store without asking, and a test that renders a
/// component on its own asks for it here. There is exactly one way to register it either way.
/// </para>
/// </remarks>
internal static class PreferenceTestExtensions
{
    /// <summary>
    /// Registers an empty in-memory store and hands back the storage behind it.
    /// </summary>
    /// <param name="document">
    /// What this browser already holds, as the JSON a previous visit would have written. Null is a first
    /// visit, which is what nearly every test wants.
    /// </param>
    public static FakePreferenceStorage AddPreferences(this BunitContext context, string? document = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        var storage = new FakePreferenceStorage(document);

        // Singletons rather than scoped: bUnit resolves everything from one provider, so the two coincide,
        // and a captured instance is the point -- the test and the store have to see the same storage.
        context.Services.AddSingleton(storage);
        context.Services.AddSingleton<IPreferenceStorage>(storage);
        context.Services.AddScoped<PreferenceStore>();

        return storage;
    }
}

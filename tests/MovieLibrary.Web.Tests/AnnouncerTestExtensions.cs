using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MovieLibrary.Web.Services;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// Which state a contract wants the films page in. Without this every one of the 320 contracts would render
/// whichever single state the stub happened to produce, and Task 6's grid contracts would be proving things
/// about "Loading films...".
/// </summary>
// Public, not internal: xunit theory data crosses the public surface, and a [Theory] method taking an
// internal enum will not compile.
public enum CatalogOutcome
{
    /// <summary>The default, because it is the ordinary state and the one a grid contract needs.</summary>
    Ready,

    /// <summary>The fetch fails, so the failure sentence and the Try again button are in the markup.</summary>
    Failed,

    /// <summary>The fetch never answers, so the page stays on the loading copy.</summary>
    NeverCompletes,
}

internal static class AnnouncerTestExtensions
{
    /// <summary>
    /// A tiny catalogue, inline rather than read from tests/fixtures, because 320 contracts should not each
    /// read a file. PublishedShapeTests is where the real published shape is pinned.
    /// </summary>
    private const string TwoFilms =
        """
        [{"i":807,"k":"807-110","t":"Se7en","y":1995,"rt":110,"g":[],"ag":"Thriller","vote":8.4,"dir":[],"cast":[],"mc":0.95},
         {"i":0,"k":"u-d2f1c6e2","t":"An Unlisted Film","rt":101,"g":[],"ag":"Documentary","vote":0,"dir":[],"cast":[],"mc":0}]
        """;

    /// <summary>
    /// Registers the announcer the way Program.cs does (one instance for the session) but on a clock the test
    /// owns, and the catalogue the way Program.cs does but against a stub handler. Returns the clock.
    /// Anything that renders MainLayout or App needs this, and since Task 5 anything that renders the films
    /// page needs the catalogue too -- which is why both live here and nowhere else: a second private copy of
    /// either means a new service has to be registered in two places.
    /// </summary>
    public static ManualTimeProvider AddAnnouncer(this BunitContext context, CatalogOutcome catalog = CatalogOutcome.Ready)
        => context.AddAnnouncerAndCatalog(Responder(catalog)).Clock;

    /// <summary>
    /// As <see cref="AddAnnouncer"/>, but hands the catalogue stub back as well, for a test that needs to
    /// script more than one response -- a retry -- or to count requests. Responders are consumed in order and
    /// the last one repeats.
    /// </summary>
    public static (ManualTimeProvider Clock, StubCatalog Catalog) AddAnnouncerAndCatalog(
        this BunitContext context, params Func<Task<HttpResponseMessage>>[] responders)
    {
        var clock = new ManualTimeProvider();
        context.Services.AddSingleton(clock);
        context.Services.AddSingleton(new Announcer(clock));

        var stub = new StubCatalog().Serves(StubCatalog.Movies, responders);
        var client = stub.Client();

        // Both, because Program.cs registers both and a later component may inject the client directly.
        context.Services.AddSingleton(client);
        context.Services.AddSingleton(new CatalogService(client));

        return (clock, stub);
    }

    public static Func<Task<HttpResponseMessage>> Responder(CatalogOutcome outcome) => outcome switch
    {
        CatalogOutcome.Ready => StubCatalog.Json(TwoFilms),
        CatalogOutcome.Failed => StubCatalog.Throws(new HttpRequestException("the stub was asked to fail")),
        CatalogOutcome.NeverCompletes => StubCatalog.NeverAnswers(),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "no responder for this outcome"),
    };
}

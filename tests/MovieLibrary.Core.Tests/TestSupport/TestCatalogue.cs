using MovieLibrary.Core.Models;
using MovieLibrary.Enrich.Emit;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// A full-size synthetic catalogue (1,457 films, about one in 80 of them unmatched) for assertions about the SHAPE of a
/// catalogue rather than any one record. It is the fixture generator's output, built once per call, so a test
/// that needs a catalogue asks here instead of inlining its own generation.
/// </summary>
internal static class TestCatalogue
{
    public static IReadOnlyList<MovieRecord> Movies()
        => FixtureGenerator.Build(FixtureGeneratorCatalogueTests.Library(), []).Item1;
}

using MovieLibrary.Core.Models;
using MovieLibrary.Enrich.Run;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// Task 17 Step F. <c>meta.Genres</c> is the id to name table the UI reads to draw the genre facet, so an id without a
/// name is a bare number on screen. The writer cannot see that, and this is the last place it can be caught.
/// </summary>
public class GenreMapTests
{
    private static MovieRecord Film(params int[] genres)
        => new(1, "T", null, 90, null, null, genres, "", 0, [], null, null, [], null, null, 0);

    private static readonly IReadOnlyDictionary<int, string> Names =
        new Dictionary<int, string> { [27] = "Horror", [35] = "Comedy" };

    [Fact]
    public void ACompleteTableHasNothingMissing()
        => Assert.Empty(GenreMap.MissingNames(Names, [Film(27), Film(35, 27)]));

    [Fact]
    public void AnIdWithNoEntryIsMissing()
        => Assert.Equal([53], GenreMap.MissingNames(Names, [Film(27, 53)]));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEntryWithABlankNameIsMissingToo(string blank)
        => Assert.Equal([27], GenreMap.MissingNames(new Dictionary<int, string> { [27] = blank }, [Film(27)]));

    [Fact]
    public void EveryMissingIdIsListedOnceInAscendingOrder_FromAnyFilm()
        => Assert.Equal([12, 80, 99], GenreMap.MissingNames(Names, [Film(99, 27), Film(80, 12), Film(99), Film()]));

    [Fact]
    public void AnEntryNoFilmUsesIsNotAProblem()
        => Assert.Empty(GenreMap.MissingNames(Names, [Film(27)]));

    [Fact]
    public void NoFilmsAndNoNamesIsNothingMissing()
        => Assert.Empty(GenreMap.MissingNames(new Dictionary<int, string>(), []));

    [Fact]
    public void TheMetaTableHasStringKeysInAscendingIdOrder()
    {
        var table = GenreMap.ForMeta(new Dictionary<int, string> { [878] = "Science Fiction", [28] = "Action", [9648] = "Mystery" });

        Assert.Equal(["28=Action", "878=Science Fiction", "9648=Mystery"], table.Select(kv => $"{kv.Key}={kv.Value}"));
    }

    [Fact]
    public void TheMetaTableIsACopy_NotTheCallersDictionary()
    {
        var source = new Dictionary<int, string> { [27] = "Horror" };

        var table = GenreMap.ForMeta(source);
        source[35] = "Comedy";

        Assert.Equal(["27"], table.Keys);
    }
}

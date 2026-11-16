using MovieLibrary.Core.Text;

namespace MovieLibrary.Core.Tests;

public class SortTitleTests
{
    [Theory]
    [InlineData("The Matrix", "Matrix")]
    [InlineData("the matrix", "matrix")]
    [InlineData("THE MATRIX", "MATRIX")]
    [InlineData("A Quiet Place", "Quiet Place")]
    [InlineData("An American Tail", "American Tail")]
    public void ALeadingArticleIsStripped(string display, string expected)
        => Assert.Equal(expected, SortTitle.From(display));

    [Theory]
    // Only ONE article comes off, and only a whole word at the start.
    [InlineData("The The", "The")]
    // Two articles then a real word: the second must stay. A recursive or looping strip would give "Team" for either.
    [InlineData("The A Team", "A Team")]
    [InlineData("A The Thing", "The Thing")]
    [InlineData("Theodore Rex", "Theodore Rex")]
    [InlineData("Analyze This", "Analyze This")]
    [InlineData("Alien", "Alien")]
    [InlineData("Se7en", "Se7en")]
    [InlineData("WALL-E", "WALL-E")]
    [InlineData("2001: A Space Odyssey", "2001: A Space Odyssey")]
    [InlineData("Star Wars: A New Hope", "Star Wars: A New Hope")]
    public void NothingElseIsTouched(string display, string expected)
        => Assert.Equal(expected, SortTitle.From(display));

    [Theory]
    // char.IsWhiteSpace, not == ' ': a tab and a non-breaking space after the article still end the word.
    [InlineData("The\tMatrix", "Matrix")]
    [InlineData("A\u00A0Quiet Place", "Quiet Place")]
    public void AnyWhitespaceAfterTheArticleCountsNotJustASpace(string display, string expected)
        => Assert.Equal(expected, SortTitle.From(display));

    [Theory]
    [InlineData("  The Matrix  ", "Matrix")]
    [InlineData("The   Matrix", "Matrix")]
    public void SurroundingAndInnerWhitespaceDoesNotDefeatIt(string display, string expected)
        => Assert.Equal(expected, SortTitle.From(display));

    [Theory]
    // A title that is nothing but an article keeps it: stripping it would leave an empty sort key, which
    // sorts before everything and belongs to no title at all.
    [InlineData("The", "The")]
    [InlineData("A", "A")]
    [InlineData("The ", "The")]
    public void ATitleThatIsOnlyAnArticleIsLeftAlone(string display, string expected)
        => Assert.Equal(expected, SortTitle.From(display));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyTitleComesBackEmptyRatherThanThrowing(string display)
        => Assert.Equal(string.Empty, SortTitle.From(display));
}

using MovieLibrary.Core.Text;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// Every <c>true</c> row here is a real pair from the owner's library (the sheet's spelling against TMDB's,
/// measured 2026-11-03). Each was costing 0.30 of the score - accepted against flagged - on a film whose id
/// was already right.
/// </summary>
public class PersonNameTests
{
    private static bool Agrees(string sheetRow, string credited)
        => PersonName.Agrees(credited, TitleNormalizer.NameTokens(sheetRow));

    [Theory]
    [InlineData("Juan Antonio Bayona", "J. A. Bayona")]                     // Jurassic World: Fallen Kingdom
    [InlineData("Alejandro González Iñárritu", "Alejandro G. Iñárritu")]    // Birdman, The Revenant
    [InlineData("Alejandro Iñárritu", "Alejandro G. Iñárritu")]             // a middle initial with nothing to answer it
    [InlineData("Callan Brunker", "Cal Brunker")]                          // Escape from Planet Earth
    [InlineData("Joel Coen & Ethan Coen", "Joel Coen")]                    // the plain subset rule, unchanged
    [InlineData("David Zucker, Jerry Zucker & Jim Abrahams", "Jim Abrahams")]
    public void AShorterWritingOfTheSameNameAgrees(string sheetRow, string credited)
        => Assert.True(Agrees(sheetRow, credited));

    [Theory]
    [InlineData("Pedro Bayona", "J. A. Bayona")]        // initials alone never carry a match
    [InlineData("Tony Scott", "Ridley Scott")]          // the surname agrees and the given name does not
    [InlineData("Jerry Zucker", "David Zucker")]
    [InlineData("Joel Coen", "Joel Cohen")]             // the surname is not loosened
    [InlineData("Joel Coen", "Jo Coen")]                // two letters is an abbreviation of too much
    [InlineData("Anthony Leondis", "Tony Leondis")]     // a nickname, not a shortening: see PersonName's remarks
    [InlineData("Jerry Zucker", "Jerry Zucker Jr")]     // a word the row does not have at all
    [InlineData("Nicholas Sparks", "Nick Cassavetes")]  // the row credits the novelist: a real unmatch, not a near miss
    [InlineData("Joel Coen", "")]                       // a blank credit names nobody
    public void ADifferentPersonDoesNotAgree(string sheetRow, string credited)
        => Assert.False(Agrees(sheetRow, credited));

    /// <remarks>
    /// The surname is read as the last word written, so it is the one part of a name that has to be in the row
    /// verbatim. Nothing in the library writes it short, and loosening it would let every Scott answer for
    /// every other.
    /// </remarks>
    [Fact]
    public void TheSurnameIsTakenFromTheEndOfTheCreditedName()
    {
        Assert.True(Agrees("Juan Antonio Bayona", "J. Bayona"));
        Assert.False(Agrees("Juan Antonio Bayona", "Bayona J."));
    }
}

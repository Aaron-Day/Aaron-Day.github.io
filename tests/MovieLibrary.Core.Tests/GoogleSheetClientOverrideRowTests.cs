using MovieLibrary.Enrich.Sheets;

namespace MovieLibrary.Core.Tests;

/// <summary>
/// An Overrides row that cannot be applied must say so in <see cref="SheetOverrides.Notes"/>, because the alternative is
/// the owner's hand-correction vanishing without a word. Each note names the sheet row (the header is row 1), says what
/// was read, and says what to do. Task 17 prints these in the job summary.
/// </summary>
public class GoogleSheetClientOverrideRowTests
{
    private const string Header = OverrideCsv.Header;
    private static Task<SheetOverrides> Fetch(string body) => OverrideCsv.Fetch(body);
    private static string Line(string title, string runtime, string id, string note = "")
        => OverrideCsv.Line(title, runtime, id, note);
    private static List<string> Pairs(IReadOnlyDictionary<string, int> map) => OverrideCsv.Pairs(map);

    // ---- TmdbId ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("948.5")]
    [InlineData("12 34")]
    [InlineData("99999999999")]
    [InlineData("+948")]
    [InlineData("1.234")]
    [InlineData("1,23")]        // group of two
    [InlineData("12,34")]
    [InlineData(",234")]
    [InlineData("1,2345")]      // group of four
    [InlineData("1,,234")]
    [InlineData("1234,")]
    [InlineData("1,234,56")]
    [InlineData("1234,567")]    // group of four first
    public async Task ARowWhoseTmdbIdIsNotAPositiveWholeNumberIsSkippedWithANoteThatSaysWhatToDo(string id)
    {
        var o = await Fetch(Header
            + Line("Alias", "", id, "my note")
            + Line("Halloween", "1:31:00", "948"));

        Assert.Equal(["halloween|91=948"], Pairs(o.Films));   // the good row is still applied
        Assert.Empty(o.Series);
        var note = Assert.Single(o.Notes);
        Assert.Contains("row 2", note);
        Assert.Contains("'Alias'", note);
        Assert.Contains($"TmdbId '{id}'", note);                  // what was read
        Assert.Contains("whole number", note);                    // what is wrong
        Assert.Contains("TMDB page address", note);               // what to do
        Assert.Contains("was ignored", note);
    }

    [Theory]
    [InlineData("1,234", 1234)]           // a Number-formatted column: gviz carries the formatted value
    [InlineData("12,345", 12345)]
    [InlineData("1,234,567", 1234567)]
    [InlineData("948", 948)]
    [InlineData("  948 ", 948)]
    [InlineData("0948", 948)]
    public async Task TheTmdbIdMayCarryThousandsSeparatorsAndSpaces(string id, int expected)
    {
        var o = await Fetch(Header + Line("Halloween", "1:31:00", id));

        Assert.Equal([$"halloween|91={expected}"], Pairs(o.Films));
        Assert.Empty(o.Notes);
    }

    // ---- Runtime --------------------------------------------------------------------------------------

    [Theory]
    [InlineData("n/a")]
    [InlineData("TV")]
    [InlineData("1:xx:00")]
    [InlineData("22:30")]       // a Shows runtime pasted in: two parts, not a film runtime
    [InlineData("1:31")]
    [InlineData("91")]
    [InlineData("9")]
    [InlineData("-0")]
    [InlineData("1:31:00:00")]
    public async Task ARuntimeThatIsNeitherBlankNorAFilmRuntimeIsSkippedWithANote(string runtime)
    {
        var o = await Fetch(Header
            + Line("Alias", runtime, "4490")
            + Line("Halloween", "1:31:00", "948"));

        Assert.Equal(["halloween|91=948"], Pairs(o.Films));   // the good row is still applied
        Assert.Empty(o.Series);                               // and it did NOT become a series by default
        var note = Assert.Single(o.Notes);
        Assert.Contains("row 2", note);
        Assert.Contains("'Alias'", note);
        Assert.Contains($"Runtime '{runtime}'", note);
        Assert.Contains("h:mm:ss", note);                     // what it should look like
        Assert.Contains("blank for a series", note);          // and the other valid choice
    }

    // ---- Title ----------------------------------------------------------------------------------------

    [Theory]
    [InlineData("", "1:31:00")]
    [InlineData("   ", "")]
    [InlineData("(2020)", "")]          // nothing left once the year is peeled off
    [InlineData("(Extended)", "1:31:00")]
    public async Task ARowWithAnIdButNoUsableTitleIsSkippedWithANoteThatSaysWhatToDo(string title, string runtime)
    {
        var o = await Fetch(Header + Line(title, runtime, "55") + Line("Alias", "", "4490"));

        Assert.Empty(o.Films);
        Assert.Equal(["alias=4490"], Pairs(o.Series));
        var note = Assert.Single(o.Notes);
        Assert.Contains("row 2", note);
        Assert.Contains($"Title '{title.Trim()}'", note);                  // what was read (cells are trimmed)
        Assert.Contains("no name left", note);                             // why
        Assert.Contains("year or edition in brackets", note);
        Assert.Contains("Movies or Shows tab", note);                      // what to do
    }

    // ---- padding versus a mistake -----------------------------------------------------------------------

    [Theory]
    [InlineData("Alias", "", "", "")]
    [InlineData("", "1:31:00", "", "")]
    [InlineData("", "", "55", "")]
    [InlineData("", "", "", "just a note")]
    public async Task ARowWithAnythingInItsFourNamedCellsIsNotPadding(
        string title, string runtime, string id, string note)
    {
        var o = await Fetch(Header + Line(title, runtime, id, note));

        Assert.Empty(o.Films);
        Assert.Empty(o.Series);
        Assert.Contains("row 2", Assert.Single(o.Notes));
    }

    [Fact]
    public async Task PaddingRowsAreSkippedSilently()
    {
        var o = await Fetch(Header
            + Line("", "", "")
            + "\"\",\"\",\"\",\"\",\"stray\"\n"   // only the unnamed column is filled
            + Line("Halloween", "1:31:00", "948"));

        Assert.Equal(["halloween|91=948"], Pairs(o.Films));
        Assert.Empty(o.Notes);
    }

    [Fact]
    public async Task NotesNameTheSheetRowSoTheOwnerCanFindIt()
    {
        var o = await Fetch(Header
            + Line("Halloween", "1:31:00", "948")
            + Line("Alias", "", "oops")
            + Line("Heat", "2:50:00", "949")
            + Line("Lost", "", "nope"));

        Assert.Equal(2, o.Notes.Count);
        Assert.Contains("row 3", o.Notes[0]);   // the header is row 1
        Assert.Contains("row 5", o.Notes[1]);
    }

    // ---- two rows for one thing -------------------------------------------------------------------------

    [Fact]
    public async Task TwoRowsForOneFilmWithDifferentIdsConflictSoTheLaterWinsAndTheNoteNamesBothRows()
    {
        // Same title, year and runtime to the minute, so the same film twice: one of the two ids is wrong.
        var o = await Fetch(Header
            + Line("Halloween (1978)", "1:31:00", "948")
            + Line("Halloween (1978)", "1:31:30", "424"));

        Assert.Equal(["halloween 1978|91=424"], Pairs(o.Films));
        var note = Assert.Single(o.Notes);
        Assert.Contains("rows 2 and 3", note);                   // the earlier row, and the later one
        Assert.Contains("948 and 424", note);                    // earlier id first
        Assert.Contains("film", note);
        Assert.Contains("row 3, was used", note);                // which one won
        Assert.Contains("Delete the wrong one", note);           // what to do
    }

    [Fact]
    public async Task ASeriesConflictNamesBothRowsToo()
    {
        var o = await Fetch(Header + Line("Alias", "", "1") + Line("Heat", "", "5") + Line("Alias", "", "2"));

        Assert.Equal(["alias=2", "heat=5"], Pairs(o.Series));
        var note = Assert.Single(o.Notes);
        Assert.Contains("rows 2 and 4", note);
        Assert.Contains("series", note);
        Assert.Contains("1 and 2", note);
    }

    [Fact]
    public async Task ARepeatedRowWithTheSameIdIsNotAConflict()
    {
        var o = await Fetch(Header + Line("Alias", "", "7") + Line("Alias", "", "7")
            + Line("Heat", "2:50:00", "9") + Line("Heat", "2:50:00", "9"));

        Assert.Equal(["alias=7"], Pairs(o.Series));
        Assert.Equal(["heat|170=9"], Pairs(o.Films));
        Assert.Empty(o.Notes);
    }

    [Fact]
    public async Task TheThirdRowForOneKeyNamesTheSecondAsTheEarlierRow()
    {
        var o = await Fetch(Header + Line("Alias", "", "1") + Line("Alias", "", "2") + Line("Alias", "", "3"));

        Assert.Equal(["alias=3"], Pairs(o.Series));
        Assert.Equal(2, o.Notes.Count);
        Assert.Contains("rows 2 and 3", o.Notes[0]);
        Assert.Contains("rows 3 and 4", o.Notes[1]);   // against the one that had won, not the first
    }
}

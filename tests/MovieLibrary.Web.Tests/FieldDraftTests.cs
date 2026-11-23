using MovieLibrary.Core.Models;
using MovieLibrary.Core.Store;
using MovieLibrary.Web.Editor.Store;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// The unset / set-to-empty / set-to-a-value triple as a type, with no markup anywhere near it. Tested first and
/// hardest, because every sentence the form shows and every value it writes comes from here.
/// </summary>
/// <remarks>
/// <b>Every rule the reader applies to one field is checked twice over</b>: once as the message this layer gives
/// and once as the exception the reader would have thrown. Shape-only validation does not meet SC 3.3.1 -- the
/// reader refuses values that ARE whole numbers -- and a field message that merely looks right is a message free
/// to drift away from the rule it is paraphrasing. Pairing the two is what stops that.
/// </remarks>
public sealed class FieldDraftTests
{
    private static readonly string Key = new('a', 40);

    private static MovieRecord Film(int id = 550, string title = "Fight Club", int runtime = 139) =>
        EditorCatalogueTests.Film(id, title, runtime, 0.5, "David Fincher");

    /// <summary>What the reader says about a record, so a field message can be held against it.</summary>
    private static RecordStoreException Refused(RecordOverride record)
    {
        var entry = new FilmEntry { IdentityKey = Key, Records = [record] };
        var draft = StoreDraft.From(RecordStore.Empty with { Films = [entry] });

        return Assert.Throws<RecordStoreException>(() => draft.ToValidatedJson());
    }

    [Fact]
    public void AnUntouchedFieldIsNotAnOverrideAndYieldsNothing()
    {
        var draft = RecordDraft.For(Film());

        Assert.All(draft.Fields, f => Assert.False(f.Overridden));
        Assert.All(draft.Fields, f => Assert.Equal(string.Empty, f.Typed));
        Assert.Null(draft.DisplayTitle.AsText);
        Assert.Null(draft.TmdbId.AsNumber);
        Assert.Null(draft.Candidates.AsList);
        Assert.False(draft.AnyProblem);

        // The entry simply omits the member, which is what keeps the store sparse: about 1,456 films are
        // expected to need no entry at all.
        var record = draft.Build(139);
        Assert.Null(record.TmdbId);
        Assert.Null(record.Titles);
        Assert.Null(record.Extra);
        Assert.True(draft.OverridesNothing);
    }

    [Fact]
    public void AnOverriddenEmptyFieldIsAnOverrideToEmptyAndNotAnOmission()
    {
        var draft = RecordDraft.For(Film());
        draft.AppleGenre.Overridden = true;

        Assert.Equal(string.Empty, draft.AppleGenre.AsText);
        Assert.NotNull(draft.AppleGenre.AsText);
        Assert.Equal(string.Empty, draft.Build(139).AppleGenre);
        Assert.False(draft.OverridesNothing);
        Assert.Null(draft.ProblemFor(draft.AppleGenre));
    }

    [Fact]
    public void TheThreeStatesOfATextFieldEachHaveTheirOwnSentence()
    {
        var draft = RecordDraft.For(Film());
        var field = draft.DisplayTitle;

        var unset = field.Describe();
        field.Overridden = true;
        var empty = field.Describe();
        field.Typed = "Se7en";
        var value = field.Describe();

        string[] three = [unset, empty, value];
        Assert.All(three, s => Assert.False(string.IsNullOrWhiteSpace(s)));
        Assert.Equal(3, three.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("Se7en", value, StringComparison.Ordinal);
    }

    [Fact]
    public void AnIntegerFieldHasTwoStatesAndATickedOverrideWithNothingTypedIsItsOwnError()
    {
        var draft = RecordDraft.For(Film());
        var field = draft.Year;

        var unset = field.Describe();
        field.Overridden = true;
        var ticked = field.Describe();
        var tickedProblem = draft.ProblemFor(field);
        field.Typed = "1999";
        var value = field.Describe();

        // Two states and not three. "Overridden to empty" is unreachable on an int?, so no state of this field
        // says it: a sentence for it would describe something that cannot happen.
        Assert.Equal("Overridden, but no value is entered yet.", ticked);
        Assert.Equal("Overridden, but no value is entered yet.", tickedProblem);
        Assert.Null(draft.ProblemFor(field));
        Assert.All([unset, ticked, value], s => Assert.DoesNotContain("empty", s, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("1999", value, StringComparison.Ordinal);

        // The text field next to it does have that third state, so the difference is the type and not an oversight.
        draft.DisplayTitle.Overridden = true;
        Assert.Contains("empty", draft.DisplayTitle.Describe(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnUnsetFieldsSentenceNamesTheResolvedValue()
    {
        var movie = Film() with { Ag = "Drama", Own = "Bought 12/04/2019", Srt = "Seven" };
        var draft = RecordDraft.For(movie);

        // The three that appear NOWHERE else on screen: the row toggle shows the band, the title, the year and
        // the runtime, so without the value in these sentences he is editing three fields blind.
        Assert.Contains("David Fincher", draft.Directors.Describe(), StringComparison.Ordinal);
        Assert.Contains("Drama", draft.AppleGenre.Describe(), StringComparison.Ordinal);
        Assert.Contains("Bought 12/04/2019", draft.PurchaseInfo.Describe(), StringComparison.Ordinal);

        Assert.Contains("Fight Club", draft.DisplayTitle.Describe(), StringComparison.Ordinal);
        Assert.Contains("Seven", draft.SortTitle.Describe(), StringComparison.Ordinal);
        Assert.Contains("550", draft.TmdbId.Describe(), StringComparison.Ordinal);
        Assert.Contains("1995", draft.Year.Describe(), StringComparison.Ordinal);

        // M3: the runtime is written whether the box is ticked or not, so the unset sentence says what will
        // actually be in the file rather than claiming the pipeline decides.
        Assert.Equal(
            "Not overridden — saved as 139 minutes so this record can still be found after the next build.",
            draft.Runtime.Describe());
        Assert.Equal(139, draft.Build(139).Runtime);
    }

    [Fact]
    public void AnIntegerFieldRejectsWhatIsNotAWholeNumber()
    {
        var draft = RecordDraft.For(Film());
        draft.TmdbId.Overridden = true;

        foreach (var typed in new[] { "12a", "1.5", "-", " " })
        {
            draft.TmdbId.Typed = typed;
            var problem = draft.ProblemFor(draft.TmdbId);

            Assert.Equal("Enter a whole number, for example 550.", problem);
            Assert.Null(draft.TmdbId.AsNumber);
        }

        draft.TmdbId.Typed = "550";
        Assert.Null(draft.ProblemFor(draft.TmdbId));
        Assert.Equal(550, draft.TmdbId.AsNumber);
    }

    [Fact]
    public void AnIntegerFieldAcceptsSurroundingSpace()
    {
        var draft = RecordDraft.For(Film());
        draft.TmdbId.Overridden = true;
        draft.TmdbId.Typed = " 550 ";

        Assert.Null(draft.ProblemFor(draft.TmdbId));
        Assert.Equal(550, draft.TmdbId.AsNumber);
        Assert.Equal(550, draft.Build(139).TmdbId);
    }

    [Fact]
    public void AYearOutsideTheRangeTheReaderAcceptsIsAFieldError()
    {
        var draft = RecordDraft.For(Film());
        draft.Year.Overridden = true;
        draft.Year.Typed = "1500";

        Assert.Equal("Enter a release year between 1870 and 2200.", draft.ProblemFor(draft.Year));

        // 1500 is a whole number, so a shape-only field layer would have passed it to the reader, which refuses
        // it -- as an operation-level sentence about a record he cannot see rather than a described field error.
        Assert.Contains("1500", Refused(new RecordOverride(Year: 1500, Runtime: 139)).Message, StringComparison.Ordinal);

        draft.Year.Typed = "1995";
        Assert.Null(draft.ProblemFor(draft.Year));
    }

    [Fact]
    public void ANonPositiveIdOrRuntimeIsAFieldError()
    {
        var draft = RecordDraft.For(Film());
        draft.TmdbId.Overridden = true;
        draft.Runtime.Overridden = true;

        foreach (var typed in new[] { "0", "-5" })
        {
            draft.TmdbId.Typed = typed;
            draft.Runtime.Typed = typed;

            Assert.Equal(
                "Enter a TMDB id of 1 or more. To say there is no match at all, untick the override instead.",
                draft.ProblemFor(draft.TmdbId));
            Assert.Equal("Enter a runtime of 1 minute or more.", draft.ProblemFor(draft.Runtime));
        }

        Assert.Contains("positive", Refused(new RecordOverride(TmdbId: 0, Runtime: 139)).Message, StringComparison.Ordinal);
        Assert.Contains("positive", Refused(new RecordOverride(TmdbId: 550, Runtime: -5)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACandidateListAcceptsSeveralIdsAndRejectsADuplicate()
    {
        var draft = RecordDraft.For(Film());
        draft.Candidates.Overridden = true;
        draft.Candidates.Typed = "807, 808";

        Assert.Null(draft.ProblemFor(draft.Candidates));
        Assert.Equal([807, 808], draft.Candidates.AsList!);
        Assert.Equal([807, 808], draft.Build(139).Candidates!);

        draft.Candidates.Typed = "807, 807";
        Assert.Equal(
            "Candidate 807 is listed twice. Each id appears once; remove the repeat.",
            draft.ProblemFor(draft.Candidates));

        // The reader would refuse it anyway, so the field is saying the same rule and not a second idea of it.
        Assert.Contains("807 twice", Refused(new RecordOverride(Candidates: [807, 807], Runtime: 139)).Message,
            StringComparison.Ordinal);

        draft.Candidates.Typed = "807, eight";
        Assert.Equal("Separate ids with commas, for example 807, 808.", draft.ProblemFor(draft.Candidates));
    }

    [Fact]
    public void ABlankExtraFieldNameWithAValueIsAFieldError()
    {
        var draft = RecordDraft.For(Film());
        var row = draft.AddExtra();

        // A row with neither half filled in is omitted in silence: A30 hands him one, and demanding he fill it
        // in or press Remove would be an error he did not make.
        Assert.True(row.Empty);
        Assert.Null(draft.ProblemFor(row));
        Assert.Null(draft.Build(139).Extra);

        row.Value = "1996";
        Assert.Equal(
            "Give this field a name, or clear its value. The name is what the site would show.",
            draft.ProblemFor(row));
        Assert.Contains("blank", Refused(new RecordOverride(TmdbId: 550, Runtime: 139,
            Extra: new Dictionary<string, string> { [" "] = "1996" })).Message, StringComparison.Ordinal);

        row.Name = "first release";
        Assert.Null(draft.ProblemFor(row));
        Assert.Equal(new Dictionary<string, string> { ["first release"] = "1996" }, draft.Build(139).Extra);
    }

    [Fact]
    public void TwoExtraFieldsWithTheSameNameIsAFieldError()
    {
        var draft = RecordDraft.For(Film());
        var first = draft.AddExtra();
        var second = draft.AddExtra();
        first.Name = "collection";
        first.Value = "Criterion";
        second.Name = "collection";
        second.Value = "Arrow";

        // On the LATER row, which is the one he just typed, and not on the row that was already right.
        Assert.Null(draft.ProblemFor(first));
        Assert.Equal(
            "Another field above is already called \"collection\". Give each extra field its own name.",
            draft.ProblemFor(second));
        Assert.True(draft.AnyProblem);

        // THE REASON THIS IS A FIELD ERROR AND NOT THE READER'S JOB: a JSON object cannot express a duplicate
        // key, so the reader has nothing to check, and the map itself throws from a path no save handler
        // catches -- which reaches the host page's error bar, an assertive source of its own, on a save that
        // reports nothing he could act on.
        var map = new Dictionary<string, string> { ["collection"] = "Criterion" };
        Assert.Throws<ArgumentException>(() => map.Add("collection", "Arrow"));

        second.Name = "edition";
        Assert.Null(draft.ProblemFor(second));
        Assert.False(draft.AnyProblem);
    }

    [Fact]
    public void AnIdAndACandidateListCannotBothBeOverridden()
    {
        // Saved WITH a shortlist, which is the only state the chooser exists in: A31 offers the ids he already
        // saw as values, so the field opens pre-filled and the chooser's undecided radio is the checked one.
        var draft = RecordDraft.For(Film(), new RecordOverride(Candidates: [807, 808], Runtime: 139));
        Assert.True(draft.Undecided);
        draft.TmdbId.Overridden = true;
        draft.TmdbId.Typed = "550";

        var problem = draft.ProblemFor(draft.Candidates);
        Assert.NotNull(problem);
        Assert.Contains("opposite things", problem, StringComparison.Ordinal);
        Assert.True(draft.AnyProblem);

        // The reader's rule, asserted rather than assumed, so the field message and the rule cannot drift apart.
        Assert.Contains("opposite things",
            Refused(new RecordOverride(TmdbId: 550, Candidates: [807, 808], Runtime: 139)).Message,
            StringComparison.Ordinal);

        // A31's chooser writes both fields at once, which is what makes the exclusion structural rather than a
        // rule to be remembered: choosing an id clears the shortlist, and keeping the shortlist clears the id.
        draft.Choose(807);
        Assert.False(draft.AnyProblem);
        Assert.True(draft.Chose(807));
        Assert.False(draft.Undecided);

        draft.Choose(null);
        Assert.False(draft.AnyProblem);
        Assert.True(draft.Undecided);
        Assert.Equal([807, 808], draft.Build(139).Candidates!);
    }
}

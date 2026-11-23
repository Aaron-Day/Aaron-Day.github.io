using System.Globalization;
using MovieLibrary.Core.Models;
using MovieLibrary.Core.Store;

namespace MovieLibrary.Web.Editor.Store;

/// <summary>
/// How many states one field can be in, which the type system decides and not the form.
/// </summary>
/// <remarks>
/// A27 asks for three states on every field -- unset, set to empty, set to a value -- and the store's own types
/// allow three for only five of them. <see cref="Text"/> is the <c>string?</c> case, where <c>""</c> is a value
/// the reader accepts and so a real third state. <see cref="Integer"/> is <c>int?</c>, which has no empty, so
/// "overridden to empty" is unreachable and a sentence for it would describe a state that cannot happen.
/// <see cref="Candidates"/> is <c>int[]?</c>, and the reader rejects an empty list outright
/// (RecordStoreReader.cs:262), so it has two states for the same reason.
/// </remarks>
public enum FieldShape
{
    Text,
    Integer,
    Candidates,
}

/// <summary>
/// What the reader will accept in one integer field, as a message the owner can act on.
/// </summary>
/// <remarks>
/// Shape alone does not meet SC 3.3.1 (M2). The reader rejects values that ARE whole numbers -- a non-positive
/// id or runtime, a year outside 1870 to 2200 -- and a value that passes a shape-only field layer throws at
/// <see cref="StoreDraft.ToValidatedJson"/> instead, arriving as an operation-level sentence for a field-level
/// error: nothing marked invalid, nothing described, no focus move, and the item in error never identified. So
/// every reader rule attributable to one field lives here, with the reader's own limits.
/// </remarks>
/// <param name="NotAWholeNumber">A42: what is wrong AND what to do, with an example of the right shape.</param>
/// <param name="OutOfRange">The same, for a number the reader would refuse.</param>
public sealed record IntegerRule(string NotAWholeNumber, int Least, int Most, string OutOfRange)
{
    /// <summary>RecordStoreReader.cs:255 -- a TMDB id is positive.</summary>
    public static IntegerRule TmdbId { get; } = new(
        "Enter a whole number, for example 550.", 1, int.MaxValue,
        "Enter a TMDB id of 1 or more. To say there is no match at all, untick the override instead.");

    /// <summary>RecordStoreReader.cs:280 -- cinema starts in the 1870s.</summary>
    public static IntegerRule Year { get; } = new(
        "Enter a whole number, for example 1995.", 1870, 2200,
        "Enter a release year between 1870 and 2200.");

    /// <summary>RecordStoreReader.cs:275 -- a runtime is in whole minutes and is positive.</summary>
    public static IntegerRule Runtime { get; } = new(
        "Enter a whole number, for example 126.", 1, int.MaxValue,
        "Enter a runtime of 1 minute or more.");
}

/// <summary>
/// One field of the form as a value: whether it overrides anything, what has been typed into it, the sentence
/// that says which state it is in, and the problem with it if there is one. No markup and no ids.
/// </summary>
/// <remarks>
/// <para><b>The three parts of A27 are one object.</b> The checkbox ("the pipeline decides" when it is clear),
/// the input, and the sentence are a single state, and splitting them across the component is how two of them
/// end up disagreeing. With CSS off the three states are still distinguishable, because the sentence says which
/// one it is in words -- never a placeholder (not a value, no contrast guarantee, gone on the first keystroke)
/// and never <c>opacity</c> or a muted class (opacity turns axe's colour-contrast INCOMPLETE).</para>
/// <para><b>The unset sentence names the resolved value and is handed in</b> (M5). "The pipeline decides" says
/// nothing about what it decided, and directors, Apple genre and purchase information appear nowhere else on
/// this screen, so without the value he would be editing three fields blind. The caller composes it from
/// <see cref="MovieRecord"/>, which keeps this type free of the catalogue.</para>
/// <para><b>Errors are computed, never stored.</b> <see cref="Problem"/> is a function of the state, so it can
/// never be stale; whether it is SHOWN is the form's decision, and m7 makes that "once the field has been left,
/// or once Save has been pressed" rather than per keystroke.</para>
/// </remarks>
public sealed class FieldDraft
{
    private FieldDraft(string name, string? label, FieldShape shape, string unset, string unit, IntegerRule? rule)
    {
        Name = name;
        Label = label ?? char.ToUpperInvariant(name[0]) + name[1..];
        Shape = shape;
        Unset = unset;
        Unit = unit;
        this.rule = rule ?? IntegerRule.TmdbId;
    }

    private readonly IntegerRule rule;

    /// <summary>The field's name as the owner reads it, and the second half of the checkbox's label.</summary>
    public string Name { get; }

    /// <summary>
    /// The value input's own visible caption, which is deliberately not the checkbox's label: two controls
    /// sharing an accessible name is the duplicate the form's own test forbids (A29).
    /// </summary>
    public string Label { get; }

    public FieldShape Shape { get; }

    /// <summary>The whole sentence for the unset state, naming the value the site shows (M5).</summary>
    public string Unset { get; }

    /// <summary>What a number in this field is counted in, included in the overridden sentence, or empty.</summary>
    public string Unit { get; }

    /// <summary>Whether this field overrides the pipeline at all. Clear means the member is omitted.</summary>
    public bool Overridden { get; set; }

    /// <summary>Exactly what is in the box, untrimmed: the caret belongs to the owner and not to this type.</summary>
    public string Typed { get; set; } = string.Empty;

    /// <summary>Whether the field has been left at least once, which is half of m7's condition.</summary>
    public bool Left { get; set; }

    /// <summary>A27's checkbox label. "Override" first, so a reader hears what the box does before which field.</summary>
    public string OverrideLabel => $"Override {Name}";

    public static FieldDraft Text(string name, string unset, string? saved = null, string? label = null)
    {
        var field = new FieldDraft(name, label, FieldShape.Text, unset, string.Empty, null);
        if (saved is not null) { field.Overridden = true; field.Typed = saved; }
        return field;
    }

    public static FieldDraft Integer(
        string name, string unset, IntegerRule rule, int? saved = null, string unit = "", string? label = null)
    {
        var field = new FieldDraft(name, label, FieldShape.Integer, unset, unit, rule);
        if (saved is { } number) { field.Overridden = true; field.Typed = Spell(number); }
        return field;
    }

    public static FieldDraft Candidates(string name, string unset, int[]? saved = null, string? label = null)
    {
        var field = new FieldDraft(name, label, FieldShape.Candidates, unset, string.Empty, null);
        if (saved is { Length: > 0 }) { field.Overridden = true; field.Typed = Join(saved); }
        return field;
    }

    // ============================================================ what the field contributes

    /// <summary>
    /// The string this field writes, or null when it writes nothing. Empty is a VALUE and not an omission: it is
    /// the whole point of A27, and the reader accepts it.
    /// </summary>
    public string? AsText => Shape == FieldShape.Text && Overridden ? Typed.Trim() : null;

    /// <summary>The number this field writes, or null when it writes nothing or what is typed is unusable.</summary>
    public int? AsNumber =>
        Shape == FieldShape.Integer && Overridden && Read() is { } number ? number : null;

    /// <summary>The ids this field writes, or null. Never an empty array: the reader refuses one.</summary>
    public int[]? AsList =>
        Shape == FieldShape.Candidates && Overridden && ReadList() is { Length: > 0 } ids ? ids : null;

    // ============================================================ the sentence and the problem

    /// <summary>
    /// Which state the field is in, in words. A27's third part, referenced by the input's own
    /// <c>aria-describedby</c>, and the only thing that distinguishes the states with CSS off.
    /// </summary>
    public string Describe()
    {
        if (!Overridden) return Unset;
        if (Typed.Length == 0) return Shape == FieldShape.Text ? "Overridden to empty." : NothingEntered;

        return Shape switch
        {
            // Trimmed, because the value really written is trimmed: a sentence quoting the untrimmed text
            // would describe something the file will not hold.
            FieldShape.Text => Typed.Trim().Length == 0
                ? "Overridden to empty."
                : $"Overridden to \"{Typed.Trim()}\".",

            // The value is quoted back as typed when it cannot be read as a number. The sentence says what the
            // field holds and the error beside it says what is wrong with it, so neither repeats the other.
            FieldShape.Integer => Read() is { } number
                ? $"Overridden to {Spell(number)}{Unit}."
                : $"Overridden to \"{Typed.Trim()}\".",

            _ => ReadList() is { Length: > 0 } ids
                ? $"Overridden to candidates {Join(ids)}."
                : $"Overridden to \"{Typed.Trim()}\".",
        };
    }

    /// <summary>The state sentence AND the error for a ticked override on a field that has no empty.</summary>
    internal const string NothingEntered = "Overridden, but no value is entered yet.";

    /// <summary>
    /// What is wrong with this field, or null. A42's shape throughout: what is wrong and what to do, never
    /// "Invalid", and never carried by colour or an icon.
    /// </summary>
    public string? Problem
    {
        get
        {
            // Typed into, override clear. The value would be dropped at save with nothing said, which is the
            // silent loss A57 forbids -- so it is a field error he can act on, in both directions.
            if (!Overridden)
                return Typed.Trim().Length == 0
                    ? null
                    : $"This value is not saved while \"{OverrideLabel}\" is clear. Tick that box to save it, "
                      + "or clear this field.";

            if (Shape == FieldShape.Text) return null;
            if (Typed.Length == 0) return NothingEntered;

            if (Shape == FieldShape.Integer)
            {
                if (Read() is not { } number) return rule.NotAWholeNumber;
                return number < rule.Least || number > rule.Most ? rule.OutOfRange : null;
            }

            if (ReadList() is not { } ids) return ListShape;
            if (ids.Length == 0) return ListShape;
            if (ids.Any(id => id <= 0)) return "Every candidate id is 1 or more. Remove the one that is not.";

            // The reader would refuse a repeat anyway (RecordStoreReader.cs:269). Saying so in the field is
            // kinder than saying it at save, and it is the same rule rather than a second idea of the rule.
            var repeated = ids.GroupBy(id => id).FirstOrDefault(g => g.Count() > 1);
            return repeated is null
                ? null
                : $"Candidate {Spell(repeated.Key)} is listed twice. Each id appears once; remove the repeat.";
        }
    }

    /// <summary>m4's format instruction doubles as the error: the format he needs is the same either way.</summary>
    internal const string ListShape = "Separate ids with commas, for example 807, 808.";

    // NumberStyles.Integer allows surrounding space and a leading sign and nothing else, which is what is
    // wanted: " 550 " is 550 because ids get pasted out of a browser's address bar, while "1.5", "12a" and "-"
    // are refused, and "-5" parses so that the reader's own positivity rule can be the message instead of a
    // vaguer one about shape. InvariantCulture, so a reader whose machine groups digits differently is not
    // offered a format the file cannot hold.
    private int? Read() =>
        int.TryParse(Typed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private int[]? ReadList()
    {
        var parts = Typed.Split(',');
        var ids = new int[parts.Length];

        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) return null;
            ids[i] = id;
        }

        return ids;
    }

    private static string Spell(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Join(IEnumerable<int> ids) => string.Join(", ", ids.Select(Spell));
}

/// <summary>One of the owner's own extra fields: a name, a value, and an id that outlives both.</summary>
/// <remarks>
/// <b>The id is synthetic and is created with the row</b> (M12). Keying a repeating row on the loop index makes
/// Blazor recreate every input below a removed one, and keying it on the typed name recreates the input on every
/// keystroke: either way focus drops to the body and the caret is lost mid-word. The id is neither.
/// </remarks>
public sealed class ExtraRow(int id)
{
    public int Id { get; } = id;

    public string Name { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    /// <summary>Whether this row has been left at least once, which is half of m7's condition.</summary>
    public bool Left { get; set; }

    /// <summary>A row with neither half filled in. Omitted in silence and never an error (C4).</summary>
    public bool Empty => Name.Trim().Length == 0 && Value.Trim().Length == 0;

    /// <summary>
    /// The map the entry writes, or null when there is nothing to write.
    /// </summary>
    /// <remarks>
    /// A NEW dictionary every time, in the rows' own order. The order is the one the file was read in, because
    /// the file is committed and read in a diff (m10), and a fresh instance because <see cref="StoreDraft"/> is
    /// immutable only down to the reference: handing the entry's own dictionary to a form that mutates it would
    /// change the draft under the component that is rendering it.
    /// </remarks>
    public static Dictionary<string, string>? Map(IEnumerable<ExtraRow> rows)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var row in rows.Where(r => !r.Empty))
            map[row.Name.Trim()] = row.Value.Trim();

        return map.Count == 0 ? null : map;
    }
}

/// <summary>
/// One record being corrected: the nine typed fields, the owner's extra rows, and the one place that turns all
/// of it back into a <see cref="RecordOverride"/>.
/// </summary>
/// <remarks>
/// <para><b>It is held by the page and not by the form</b> (M11). Worklist closes the open row on every debounced
/// query change, so a draft owned by the component would be destroyed 150 ms after one character reached the
/// search box, with no warning and no way back. The page keeps these in a dictionary on the published key, and
/// reopening the row restores this object -- which is also A31's "pre-fill, never re-ask" for the unsaved case.</para>
/// <para><b>Runtime is written on every record and is excluded from the "overrides nothing" judgment</b> (M3).
/// RecordLink.PublishedKeyOf falls back to the sheet echo's runtime, and RuntimeParser.FilmMinutes throws on a
/// bare minute count, so a record saved without a runtime becomes unplaceable. The reader's own test for an
/// empty record is <c>record == new RecordOverride()</c> (RecordStoreReader.cs:245), and an always-written
/// runtime means no record can ever equal that -- hence <see cref="OverridesNothing"/> compares the built record
/// with the runtime the SAVE supplied taken back out and the one he CHOSE left in. What the save supplies is
/// the ROW's own minutes (<see cref="uncorrectedMinutes"/>), which after a build is no longer the same thing as
/// what the catalogue publishes. It is <see cref="FieldDraft.AsNumber"/> that tells a typed runtime from an
/// empty box: null exactly when the box is empty. Nulling the
/// runtime unconditionally would read a runtime-only correction as no correction at all -- and the entry that
/// already held one would be deleted, with the page calling it a success.</para>
/// </remarks>
public sealed class RecordDraft
{
    private RecordDraft(MovieRecord movie, RecordOverride? saved, int? rowMinutes)
    {
        var titles = saved?.Titles;
        var uncorrected = rowMinutes ?? movie.Rt;

        TmdbId = FieldDraft.Integer(
            "TMDB id",
            movie.I == 0
                ? "Not overridden — the site has no TMDB match for this record."
                : $"Not overridden — the site uses TMDB id {Spell(movie.I)}.",
            IntegerRule.TmdbId, saved?.TmdbId);

        Candidates = FieldDraft.Candidates(
            "candidate ids",
            "Not overridden — no candidate ids are kept for this record.",
            saved?.Candidates, label: "Candidate TMDB ids");

        DisplayTitle = FieldDraft.Text(
            "display title", $"Not overridden — the site shows \"{movie.T}\".", titles?.Display);

        SortTitle = FieldDraft.Text(
            "sort title", $"Not overridden — the site sorts this under \"{movie.Srt ?? movie.T}\".", titles?.Sort);

        Year = FieldDraft.Integer(
            "release year",
            movie.Y is { } year
                ? $"Not overridden — the site shows {Spell(year)}."
                : "Not overridden — the site shows no year.",
            IntegerRule.Year, saved?.Year);

        // M3's exact sentence, because the unchecked state does NOT mean "the pipeline decides" here: the
        // record's own published runtime is written either way, and a sentence saying otherwise would be a lie
        // about the file. See the remarks above for why it has to be written.
        //
        // AND THE PRE-FILL TICKS THE BOX ONLY FOR A RUNTIME HE CHOSE, judged against the ROW's own minutes and
        // never against what the catalogue publishes now (n3). Every record the editor writes carries a
        // runtime, so a saved runtime equal to the row's own is the one the save supplied and says nothing:
        // ticking the box for it would show an override he never made. But one build later the catalogue
        // PUBLISHES his correction -- MoviePipeline writes over?.Runtime ?? sheetRuntime into Rt and Apply
        // never touches it -- so judging against movie.Rt read his own correction back as the save's own
        // value, left the box clear, and let a bare Save delete the entry. See uncorrectedMinutes.
        Runtime = FieldDraft.Integer(
            "runtime",
            $"Not overridden — saved as {Spell(movie.Rt)} minutes so this record can still be found after the "
            + "next build.",
            IntegerRule.Runtime, saved?.Runtime == uncorrected ? null : saved?.Runtime,
            unit: " minutes", label: "Runtime in minutes");

        Directors = FieldDraft.Text(
            "directors",
            movie.Dir.Length == 0
                ? "Not overridden — the site shows no directors."
                : $"Not overridden — the site shows \"{string.Join(" & ", movie.Dir)}\".",
            saved?.Directors);

        AppleGenre = FieldDraft.Text(
            "Apple genre",
            string.IsNullOrWhiteSpace(movie.Ag)
                ? "Not overridden — the site shows no Apple genre."
                : $"Not overridden — the site shows \"{movie.Ag}\".",
            saved?.AppleGenre);

        PurchaseInfo = FieldDraft.Text(
            "purchase information",
            string.IsNullOrWhiteSpace(movie.Own)
                ? "Not overridden — the site shows no purchase information."
                : $"Not overridden — the site shows \"{movie.Own}\".",
            saved?.PurchaseInfo);

        Offered = saved?.Candidates is { Length: > 0 } offered ? [.. offered] : [];

        // The entry's own order, because the file is committed and read in a diff (m10), and copied out
        // key by key rather than handed over: the entry's dictionary is not the form's to mutate.
        if (saved?.Extra is { } extra)
            foreach (var pair in extra)
                Extras.Add(new ExtraRow(nextId++) { Name = pair.Key, Value = pair.Value });

        Fields = [TmdbId, Candidates, DisplayTitle, SortTitle, Year, Runtime, Directors, AppleGenre, PurchaseInfo];

        uncorrectedMinutes = uncorrected;
    }

    /// <summary>
    /// The runtime this record has with NO correction in it: the row's own minutes, taken from the entry's
    /// sheet echo where there is one and from the published record otherwise. A runtime equal to this says
    /// nothing, whether it was typed or read back out of the file.
    /// </summary>
    /// <remarks>
    /// Both halves read this one value -- the pre-fill above and <see cref="OverridesNothing"/> -- and them
    /// disagreeing about it has been a defect twice. First the box opened clear for a saved 139 while a typed
    /// 139 kept alive an entry that corrects nothing (m1). Then, one build after a runtime-only correction,
    /// <c>MovieRecord.Rt</c> WAS that correction, so both halves read it as the value the save supplies: the
    /// box opened clear, the record was judged to override nothing, and a bare Save deleted the entry and
    /// called it a success. The entry's echo is what tells them apart -- <c>Place</c> writes it once from the
    /// then-published minutes and never rewrites an existing entry's, and for a hand-written entry it is the
    /// sheet's own text. <c>Build</c> still takes the published minutes as a parameter, because that is the
    /// runtime a save WRITES; this is the runtime it is judged against.
    /// </remarks>
    private readonly int uncorrectedMinutes;

    /// <summary>A draft of <paramref name="movie"/>, pre-filled from <paramref name="saved"/> when there is one.</summary>
    /// <param name="rowMinutes">
    /// The row's own runtime in minutes, where something other than the published record still knows it:
    /// the entry's sheet echo. Null takes <c>movie.Rt</c>, which is right for a row with no entry and
    /// wrong for one whose correction a build has since published -- see <see cref="uncorrectedMinutes"/>.
    /// </param>
    public static RecordDraft For(MovieRecord movie, RecordOverride? saved = null, int? rowMinutes = null)
    {
        ArgumentNullException.ThrowIfNull(movie);

        return new RecordDraft(movie, saved, rowMinutes);
    }

    private int nextId;

    public FieldDraft TmdbId { get; }
    public FieldDraft Candidates { get; }
    public FieldDraft DisplayTitle { get; }
    public FieldDraft SortTitle { get; }
    public FieldDraft Year { get; }
    public FieldDraft Runtime { get; }
    public FieldDraft Directors { get; }
    public FieldDraft AppleGenre { get; }
    public FieldDraft PurchaseInfo { get; }

    /// <summary>The nine fields in the order they are rendered, which is the order A43 looks for the first error in.</summary>
    public IReadOnlyList<FieldDraft> Fields { get; }

    public List<ExtraRow> Extras { get; } = [];

    /// <summary>The candidate ids already in the store, which A31 offers as values rather than asking him to retype.</summary>
    public IReadOnlyList<int> Offered { get; }

    /// <summary>Whether Save has been pressed on this draft, which is the other half of m7's condition.</summary>
    public bool SaveAttempted { get; set; }

    // ============================================================ the problems

    /// <summary>
    /// What is wrong with one field. The field's own rule, plus the one rule that belongs to no single field.
    /// </summary>
    /// <remarks>
    /// The chooser makes the id/candidates exclusion structural, but both override checkboxes stay
    /// independently operable, so the pair can still be set. The reader refuses it
    /// (RecordStoreReader.cs:250) and refusing it here instead makes it a field error with a described
    /// message and a focus move, rather than an operation-level sentence about a record he cannot see.
    /// </remarks>
    public string? ProblemFor(FieldDraft field)
    {
        ArgumentNullException.ThrowIfNull(field);

        if (ReferenceEquals(field, Candidates) && Candidates.Overridden && TmdbId.Overridden)
            return "A TMDB id is set as well, and the two mean opposite things: an id is a decision, candidates "
                   + "are the absence of one. Clear one of the two override boxes.";

        return field.Problem;
    }

    /// <summary>What is wrong with one extras row, reported on its NAME input (C4).</summary>
    /// <remarks>
    /// <c>RecordOverride.Extra</c> is a <c>Dictionary</c>, so two rows with one name throw
    /// <c>ArgumentException</c> from a path no save handler catches, reaching the host page's error bar -- which
    /// carries an assertive role of its own -- on a save that reports nothing actionable. The reader cannot
    /// check it either, because a JSON object cannot express a duplicate key. So it is refused here, on the
    /// LATER row, which is the one he just typed.
    /// </remarks>
    public string? ProblemFor(ExtraRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (row.Empty) return null;

        if (row.Name.Trim().Length == 0)
            return "Give this field a name, or clear its value. The name is what the site would show.";

        foreach (var earlier in Extras)
        {
            if (ReferenceEquals(earlier, row)) break;
            if (!earlier.Empty && string.Equals(earlier.Name.Trim(), row.Name.Trim(), StringComparison.Ordinal))
                return $"Another field above is already called \"{row.Name.Trim()}\". Give each extra field its "
                       + "own name.";
        }

        return null;
    }

    /// <summary>Whether anything at all is wrong, which is what blocks a save (A43).</summary>
    public bool AnyProblem =>
        Fields.Any(f => ProblemFor(f) is not null) || Extras.Any(r => ProblemFor(r) is not null);

    /// <summary>
    /// Whether a field's error is SHOWN yet. m7: on blur and on Save, never per keystroke -- an aria-invalid
    /// flipping on every character is noise he cannot stop, and a freshly ticked override would otherwise
    /// announce an error he has not made yet.
    /// </summary>
    public bool ShowProblem(FieldDraft field) =>
        (SaveAttempted || field.Left) && ProblemFor(field) is not null;

    public bool ShowProblem(ExtraRow row) =>
        (SaveAttempted || row.Left) && ProblemFor(row) is not null;

    // ============================================================ the extras rows and the chooser

    /// <summary>Adds a row and hands it back, so the caller can put focus in it (A30).</summary>
    public ExtraRow AddExtra()
    {
        var row = new ExtraRow(nextId++);
        Extras.Add(row);
        return row;
    }

    /// <summary>The row focus goes to after <paramref name="row"/> is removed, or null for the Add button (A30).</summary>
    public ExtraRow? RemoveExtra(ExtraRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        var at = Extras.IndexOf(row);
        if (at < 0) return null;

        Extras.RemoveAt(at);
        return at < Extras.Count ? Extras[at] : null;
    }

    /// <summary>Whether the chooser's radio for <paramref name="id"/> is the one that is checked.</summary>
    public bool Chose(int id) => TmdbId.Overridden && !Candidates.Overridden && TmdbId.AsNumber == id;

    /// <summary>Whether the chooser's "undecided" radio is the one that is checked.</summary>
    public bool Undecided => Candidates.Overridden && !TmdbId.Overridden;

    /// <summary>
    /// A31's chooser: null keeps the whole shortlist, an id decides. It writes the two fields rather than
    /// adding a third piece of state, so the exclusion is structural and the checked radio is the announcement.
    /// </summary>
    /// <remarks>
    /// Both boxes are cleared of their text as well as their tick, because a value left behind in a field whose
    /// override is clear is a value that would not be saved -- which this type reports as a field error, and
    /// which would be an error he did not make.
    /// </remarks>
    public void Choose(int? id)
    {
        if (id is { } chosen)
        {
            TmdbId.Overridden = true;
            TmdbId.Typed = Spell(chosen);
            Candidates.Overridden = false;
            Candidates.Typed = string.Empty;
            return;
        }

        TmdbId.Overridden = false;
        TmdbId.Typed = string.Empty;
        Candidates.Overridden = true;
        Candidates.Typed = string.Join(", ", Offered.Select(Spell));
    }

    // ============================================================ what gets written

    /// <summary>
    /// The record this draft would write. <paramref name="publishedMinutes"/> is the record's own
    /// <c>MovieRecord.Rt</c> and is used whenever the runtime field overrides nothing, so a runtime is always
    /// present: see the remarks on this type for why that is not a convenience.
    /// </summary>
    /// <param name="publishedKey">
    /// The record's own <c>MovieRecord.K</c> -- the key the PREVIOUS build published for it, straight out of
    /// movies.json. Not a form field and never typed: it is evidence of what happened, and the whole reason
    /// the member exists is that a key derived from the store moves when a correction asserts an id.
    /// <para>Null only from <see cref="OverridesNothing"/>, which asks what this draft CORRECTS. The key
    /// corrects nothing, so counting it there would make every draft look like a correction and no entry
    /// would ever be removed.</para>
    /// </param>
    public RecordOverride Build(int publishedMinutes, string? publishedKey) => new(
        TmdbId: TmdbId.AsNumber,
        Candidates: Candidates.AsList,
        Runtime: Runtime.AsNumber ?? publishedMinutes,
        Titles: DisplayTitle.AsText is null && SortTitle.AsText is null
            ? null
            : new TitleSet(DisplayTitle.AsText, SortTitle.AsText),
        Year: Year.AsNumber,
        Directors: Directors.AsText,
        AppleGenre: AppleGenre.AsText,
        PurchaseInfo: PurchaseInfo.AsText,
        PublishedKey: publishedKey,
        Extra: ExtraRow.Map(Extras));

    /// <summary>
    /// Whether this draft corrects nothing, which is the page's cue to remove the entry rather than write one
    /// that says nothing. The reader's own test, with the runtime the save writes for him taken back out and
    /// the runtime he CHOSE left in (M3): a runtime-only override is a correction the reader keeps.
    /// </summary>
    /// <remarks>
    /// "Chose" and not "typed", which is m1's correction. The runtime the save supplies is the row's own
    /// minutes, so a value equal to those says the same nothing however it got into the box -- and
    /// <see cref="For"/> refuses to tick the box for it when it comes back out of the file. The two halves
    /// judge one value and must judge it alike, and that value is <see cref="uncorrectedMinutes"/> and not
    /// what the catalogue publishes today: a build publishes a runtime correction into <c>Rt</c>, after which
    /// measuring against <c>Rt</c> deletes the very entry that holds it.
    /// </remarks>
    public bool OverridesNothing =>
        (Build(1, null) with { Runtime = Runtime.AsNumber == uncorrectedMinutes ? null : Runtime.AsNumber })
        == new RecordOverride();
    private static string Spell(int value) => value.ToString(CultureInfo.InvariantCulture);
}

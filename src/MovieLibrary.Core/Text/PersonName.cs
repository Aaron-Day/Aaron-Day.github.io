namespace MovieLibrary.Core.Text;

/// <summary>
/// Whether a name TMDB credits is one of the names the sheet credits.
/// </summary>
/// <remarks>
/// <para>The sheet's side is a bag of folded words, not a list of people: <see cref="TitleNormalizer.NameTokens"/>
/// throws the order away, and a row routinely names several directors. So a candidate's name is tested against
/// the whole bag, exactly as the plain subset rule was.</para>
/// <para><b>Why the subset rule alone was not enough.</b> TMDB writes a great many directors in a shorter form
/// than Apple does - <c>J. A. Bayona</c> for Juan Antonio Bayona, <c>Alejandro G. Iñárritu</c> for Alejandro
/// González Iñárritu, <c>Cal Brunker</c> for Callan Brunker - and every one of those fails a word-for-word
/// subset test. Each cost 0.30 of the score, which is the difference between an accepted match and a flagged
/// one, on films whose id was already right.</para>
/// <para><b>What is deliberately NOT handled.</b> A nickname that is not a shortening of the name it stands for:
/// <c>Tony Leondis</c> for Anthony Leondis, where <c>tony</c> is not inside <c>anthony</c> at all. Only a table of
/// nicknames could pair those, there is no rule to infer, and a table would be a list of this library's films
/// wearing a general name. Those rows stay flagged, with the right id, for a person to confirm or override.</para>
/// <para><b>Why the surname must still match exactly.</b> Everything loosened here is a given name, and only once
/// the surname is a word the sheet actually wrote. <c>Ridley Scott</c> against a row naming Tony Scott shares the
/// surname and is still rejected, because <c>ridley</c> answers to nothing in the row.</para>
/// </remarks>
public static class PersonName
{
    /// <summary>
    /// The shortest a word may be to pass as a short form of a longer one. Two letters would make <c>jo</c>
    /// answer for Joel and for Johnson alike.
    /// </summary>
    private const int MinShortForm = 3;

    public static bool Agrees(string credited, IReadOnlySet<string> sheetNames)
    {
        var parts = TitleNormalizer.NameParts(credited);
        if (parts.Count == 0) return false;                     // a blank credit names nobody

        // The original rule, kept as it was: every word of the credited name is a word the sheet wrote.
        // Anything it already accepted must keep the same score, so it is tried first and on its own.
        if (parts.All(sheetNames.Contains)) return true;

        if (!sheetNames.Contains(parts[^1])) return false;      // the surname is not negotiable

        // A given name may be written short or as an initial, but it may not be silently dropped: a name
        // whose only given names are unanswered initials is not a match, or "J. A. Bayona" would answer for
        // every other Bayona in the row. An initial IS forgiven once a full given name has agreed, which is
        // the dropped middle initial of "Alejandro G. Iñárritu" against "Alejandro Iñárritu".
        var fullGivenNameAgreed = false;
        var initialUnanswered = false;

        for (var i = 0; i < parts.Count - 1; i++)
        {
            var part = parts[i];

            if (sheetNames.Any(name => WordsAgree(part, name)))
            {
                if (part.Length > 1) fullGivenNameAgreed = true;
            }
            else if (part.Length == 1) initialUnanswered = true;
            else return false;
        }

        return !initialUnanswered || fullGivenNameAgreed;
    }

    /// <summary>
    /// Two words naming the same given name: the same word, an initial of it, or a short form of it.
    /// </summary>
    private static bool WordsAgree(string a, string b)
    {
        if (a == b) return true;
        if (a.Length == 1) return a[0] == b[0];
        if (b.Length == 1) return b[0] == a[0];
        return ShortFormOf(a, b) || ShortFormOf(b, a);
    }

    /// <summary>
    /// A short form is the start of the full name: <c>Cal</c> for Callan, <c>Chris</c> for Christopher. Only the
    /// start, because that is the shape the library actually has, and because a rule that also read the end
    /// would pair <c>Ron</c> with <c>Aaron</c> for nothing in return.
    /// </summary>
    private static bool ShortFormOf(string shortForm, string full)
        => shortForm.Length >= MinShortForm
           && shortForm.Length < full.Length
           && full.StartsWith(shortForm, StringComparison.Ordinal);
}

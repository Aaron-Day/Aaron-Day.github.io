namespace MovieLibrary.Web.Models;

/// <summary>
/// The text a film's own numbers turn into on screen, in ONE place because two pages render them now.
/// </summary>
/// <remarks>
/// Extracted from <c>PosterTile</c> when the detail page needed the same strings. It is the same argument
/// <c>PagePath</c> was extracted on: a second copy of a format is a second thing to change, and the two
/// diverging is invisible until a tile and a detail page disagree about the same film in front of the owner.
/// <para>
/// Nothing here is localised, deliberately. The grid's COUNT is culture-formatted on purpose (a shipped
/// acceptance criterion requires "1.456" under de-DE), but a runtime is not a quantity the reader compares --
/// it is a label on one film -- and "1h 50m" interpolated unformatted can never grow a group separator, which
/// is what a 1,353-minute boxed set would otherwise read as.
/// </para>
/// </remarks>
internal static class FilmText
{
    /// <summary>
    /// A runtime as hours and minutes: "1h 50m", "50m", and "2h" on the exact hour rather than "2h 0m".
    /// </summary>
    /// <remarks>
    /// Interpolated, so never N0 -- a 1,353 minute boxed set reads "22h 33m" and never "1,353". The unit
    /// letters are deliberate: "1h 50m" is what every library UI shows, and SC 3.1.4 Abbreviations is
    /// Level AAA, so it conforms at the level this project is held to.
    /// </remarks>
    public static string Runtime(int minutes)
    {
        var hours = minutes / 60;
        var rest = minutes % 60;

        if (hours == 0) return $"{rest}m";

        return rest == 0 ? $"{hours}h" : $"{hours}h {rest}m";
    }

    /// <summary>
    /// A TMDB vote as the text that carries its own scale: "8.4/10".
    /// </summary>
    /// <remarks>
    /// "/10" IS PART OF THE TEXT AND NEVER AN ATTRIBUTE. A bare "8.4" leaves its meaning to position alone,
    /// which is SC 1.3.1 Info and Relationships, and the tile is forbidden a title or an aria-label to carry
    /// it in. The caller still decides WHETHER to render it: Vote is 0d on every unmatched record, and "0.0/10"
    /// would read as a terrible film rather than as an absent measurement.
    /// </remarks>
    /// <para>
    /// <b>Culture-dependent, because that is what the tile already did</b> -- <c>ToString("0.0")</c> and not
    /// the invariant culture, so a German browser keeps reading "8,4/10" exactly as it did before this was
    /// lifted out of the tile. An extraction that quietly changed what a reader sees would be a behaviour
    /// change wearing a refactor's clothes. Whether a rating on a /10 scale SHOULD follow the browser's
    /// decimal separator is a real question, and it is a separate decision with its own test, not a side
    /// effect of moving a method.
    /// </para>
    public static string Rating(double vote) => vote.ToString("0.0") + "/10";
}

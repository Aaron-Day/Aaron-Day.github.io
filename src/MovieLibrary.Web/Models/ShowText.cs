using System.Globalization;

namespace MovieLibrary.Web.Models;

/// <summary>
/// The text a series' own numbers turn into on screen, in ONE place because the list and the show page
/// render the same counts.
/// </summary>
/// <remarks>
/// The sibling of <see cref="FilmText"/> and extracted on the same argument: a second copy of a format is a
/// second thing to change, and the two diverging is invisible until a tile and a detail page disagree about
/// the same series in front of the owner.
/// </remarks>
internal static class ShowText
{
    /// <summary>How many NUMBERED seasons: "9 seasons", and "1 season" for the singular.</summary>
    /// <remarks>
    /// <para>
    /// Numbered seasons only, which is <see cref="Core.Models.ShowSummary.SeasonCount"/>'s own definition:
    /// season 0 is the specials, TMDB carries them for most series, and no reader thinks of them as a
    /// season. "9 seasons" therefore means nine numbered ones however many specials sit beside them.
    /// </para>
    /// <para>
    /// A COUNT, so culture-formatted -- unlike a season NUMBER, which is an identifier and must never take
    /// N0, because "Season 1.001" would be nonsense. No published series comes near a separator either way;
    /// the formatting is here so that the rule is visible rather than accidental.
    /// </para>
    /// </remarks>
    public static string Seasons(int seasons)
        => seasons.ToString("N0", CultureInfo.CurrentCulture) + (seasons == 1 ? " season" : " seasons");

    /// <summary>
    /// How much of a series is owned: "45 of 984 episodes", and "1 of 1 episode" for the singular.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE DENOMINATOR IS ALWAYS THERE, including when it equals the numerator.</b> A complete series
    /// reads "233 of 233 episodes" rather than "233 episodes": the shorter form would make a complete
    /// series and a series of unknown length look the same at a glance, and the wall is scanned rather
    /// than read. It also keeps one format for the owner to learn instead of two.
    /// </para>
    /// <para>
    /// The noun follows the TOTAL and not the owned count, so a one-episode series reads "0 of 1 episode"
    /// and never "0 of 1 episodes".
    /// </para>
    /// <para>
    /// Both are quantities the owner compares across the wall, so both are culture-formatted. The words
    /// are not translated and neither is "of": this is an English-language page, and SC 3.1.2 Language of
    /// Parts is about marking a change of language, not about making one. No published series reaches four
    /// figures today -- the longest is 984 episodes -- so no separator appears under any culture yet, and
    /// the formatting is here so that the first series that does reach it reads correctly rather than
    /// having to be found.
    /// </para>
    /// </remarks>
    public static string Episodes(int owned, int total)
        => owned.ToString("N0", CultureInfo.CurrentCulture)
         + " of " + total.ToString("N0", CultureInfo.CurrentCulture)
         + (total == 1 ? " episode" : " episodes");
}

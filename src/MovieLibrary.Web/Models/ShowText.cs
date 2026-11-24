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
    /// season 0 here is NOT the specials but <c>TvPipeline</c>'s bucket for episodes it could not place, and
    /// counting it would show "6 seasons" for a five-season show. "9 seasons" therefore means nine numbered
    /// ones however many unplaced episodes sit beside them, and <see cref="Unplaced"/> is what names those.
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
    /// <summary>
    /// How many episodes, with no denominator: "24 episodes", and "1 episode" for the singular.
    /// </summary>
    /// <remarks>
    /// <b>The low-confidence form, and the whole point of it is what it does NOT say.</b> Where the match
    /// is uncertain or absent the TMDB total may belong to a different series altogether, so a page that
    /// printed "24 of 61 episodes" would be stating a gap in the owner's library on the authority of a
    /// match nobody trusts. What is certain is how many episodes he has, so that is all this says.
    /// </remarks>
    public static string Owned(int owned)
        => owned.ToString("N0", CultureInfo.CurrentCulture) + (owned == 1 ? " episode" : " episodes");

    /// <summary>
    /// A season's name for its disclosure control: "Season 3, 24 of 24 episodes", or "Season unknown, 29
    /// of 29 episodes" for the unplaced bucket.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>SEASON 0 IS NOT A SEASON AND IS NEVER CALLED "Season 0".</b> It is the build's bucket for
    /// episodes it could not place -- owned rows that matched nothing, or every row of an unmatched series
    /// -- and there are 29 of them on the owner's copy of 24. "Season 0" would read as the one before the
    /// first, and "Specials" would be worse still: that is a different thing, it is what most libraries
    /// call season 0, and this bucket is not it. "Season unknown" says what is actually known, which is
    /// that these are his episodes and nothing could tell which season they belong to.
    /// </para>
    /// <para>
    /// Counted nowhere: <see cref="Core.Models.ShowRecord.NumberedSeasons"/> excludes it, which is why a
    /// page that counted the records it was given said "10 seasons" where the list that links to it said
    /// "9", about the same series, one click apart.
    /// </para>
    /// <para>
    /// <b>The season NUMBER is never culture-formatted and the episode counts always are.</b> The number
    /// identifies the season -- "Season 1.001" would be nonsense -- while the counts are quantities the
    /// owner compares between seasons. Plain interpolation of an <c>int</c> groups in no culture, which is
    /// what makes the first half safe without a format string to get wrong.
    /// </para>
    /// <para>
    /// The counts are IN THE NAME rather than beside the control, because the name is what a screen reader
    /// reads when the reader arrives at a collapsed season and it is what the rotor lists. "Season 3" alone
    /// would make every season sound alike and give no reason to open one.
    /// </para>
    /// </remarks>
    public static string Season(int number, int owned, int total, bool withTotal)
        => (number == 0 ? Unplaced : $"Season {number}") + ", "
         + (withTotal ? Episodes(owned, total) : Owned(owned));

    /// <summary>What season 0 -- the episodes nothing could place -- is called wherever it is named.</summary>
    internal const string Unplaced = "Season unknown";

    public static string Episodes(int owned, int total)
        => owned.ToString("N0", CultureInfo.CurrentCulture)
         + " of " + total.ToString("N0", CultureInfo.CurrentCulture)
         + (total == 1 ? " episode" : " episodes");
}

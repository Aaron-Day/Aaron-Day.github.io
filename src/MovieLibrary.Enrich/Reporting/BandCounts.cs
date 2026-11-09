using MovieLibrary.Core.Matching;
using MovieLibrary.Core.Models;

namespace MovieLibrary.Enrich.Reporting;

/// <summary>
/// How far the pipeline trusted each film's match, counted by band: the one number that says whether a run's
/// matching went well, which neither the request count nor the review queue can say.
/// </summary>
/// <remarks>
/// A record rather than three loose <see cref="int"/>s on <see cref="JobReport"/>, for the reason that type
/// already gives: three ints of the same type in a row exchange silently and print the wrong number in the one
/// page the owner reads.
/// </remarks>
public sealed record BandCounts(int Confident, int Uncertain, int Unmatched)
{
    /// <summary>Every film in some band, so a caller can check that none was dropped.</summary>
    public int Total => Confident + Uncertain + Unmatched;

    /// <summary>Bands a run's films.</summary>
    /// <remarks>
    /// Through <see cref="ConfidenceBand.Of"/>, which lives in Core because the site bands the same films by
    /// the same rule from the same published record. A second copy of the threshold here would let this page
    /// call a film confident that the site badges "Needs review", and nothing would catch it: the two would be
    /// read months apart by the same person, who would believe the more recent one.
    /// </remarks>
    public static BandCounts Of(IEnumerable<MovieRecord> films)
    {
        int confident = 0, uncertain = 0, unmatched = 0;

        foreach (var film in films)
            switch (ConfidenceBand.Of(film))
            {
                case Confidence.Confident: confident++; break;
                case Confidence.Uncertain: uncertain++; break;
                default: unmatched++; break;   // Unmatched, and anything a later band adds, counted loudly
            }

        return new BandCounts(confident, uncertain, unmatched);
    }
}

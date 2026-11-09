using System.Globalization;

namespace MovieLibrary.Web.Tests;

/// <summary>Sets the culture for one test and puts it back.</summary>
/// <remarks>
/// Only <see cref="CultureInfo.CurrentCulture"/>, never <c>DefaultThreadCurrentCulture</c>: that one is
/// process-wide and would reach whatever else xunit is running in parallel. CurrentCulture flows with the
/// execution context, which is what bUnit's renderer dispatch rides on, so it reaches both a component
/// rendered directly and a whole app rendered through the router.
/// <para>
/// Shared rather than nested in one test class because two files need it: PosterGridTests pins the grid
/// head's formatting, and MoviesGridTests pins that the page sentence above it groups the same number the
/// same way. That pair is the point -- the defect it caught was one number formatted two ways by two
/// components, which a culture helper private to either one of them could not have seen.
/// </para>
/// </remarks>
internal sealed class CultureScope : IDisposable
{
    private readonly CultureInfo previous = CultureInfo.CurrentCulture;

    public CultureScope(string name) => CultureInfo.CurrentCulture = new CultureInfo(name);

    public void Dispose() => CultureInfo.CurrentCulture = previous;
}

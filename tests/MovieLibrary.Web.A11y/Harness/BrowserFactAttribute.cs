namespace MovieLibrary.Web.A11y.Harness;

/// <summary>
/// A <see cref="FactAttribute"/> that reports itself Skipped, with the command that would make it run, whenever
/// <see cref="Gate.Unavailable"/> says this machine has no published site or no browser.
/// </summary>
/// <remarks>
/// xunit v2 declares <c>Skip</c> virtual and reads it off the live attribute instance during discovery, so
/// overriding the getter is the only mechanism on this stack that produces a real Skipped result. Rule T3
/// explains why the obvious alternatives do not: <c>Assert.Skip</c> is v3, and the v2 engine reports a thrown
/// <c>SkipException</c> as Failed. An explicit <c>Skip</c> still wins, so a test can be parked by hand.
/// </remarks>
public sealed class BrowserFactAttribute : FactAttribute
{
    private string? parked;

    public override string? Skip
    {
        get => parked ?? Gate.Unavailable;
        set => parked = value;
    }
}

/// <summary><see cref="BrowserFactAttribute"/> for a theory. Same mechanism, same reasons.</summary>
public sealed class BrowserTheoryAttribute : TheoryAttribute
{
    private string? parked;

    public override string? Skip
    {
        get => parked ?? Gate.Unavailable;
        set => parked = value;
    }
}

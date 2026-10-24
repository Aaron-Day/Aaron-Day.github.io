namespace MovieLibrary.Core.Throttling;

/// <summary>
/// Per-run ceiling on outbound TMDB requests. Zero <em>or negative</em> means unlimited.
/// Not thread-safe.
/// </summary>
public sealed class RequestBudget(int max)
{
    public int Used { get; private set; }

    public bool Unlimited => max <= 0;
    public int Remaining => Unlimited ? int.MaxValue : Math.Max(0, max - Used);
    public bool IsExhausted => !Unlimited && Used >= max;

    public bool TryConsume()
    {
        if (IsExhausted) return false;
        Used++;
        return true;
    }
}

namespace MovieLibrary.Web.Editor.Components;

/// <summary>Which records the worklist offers. The default is the ones that need a decision, not all 1,488.</summary>
public enum WorklistBand
{
    /// <summary>The unmatched and the uncertain records together: the ones the editor exists for.</summary>
    NeedAttention,

    Unmatched,

    Uncertain,

    /// <summary>Every record, so any of the library is reachable and not only the worklist.</summary>
    All,
}

/// <summary>How the list is ordered.</summary>
public enum WorklistSort
{
    /// <summary>Unmatched first, then the lowest-scoring matches: the worst stay permanently on top.</summary>
    WorstFirst,

    Title,
}

/// <summary>The worklist's filter, handed to the page so it can know what the visitor is looking at.</summary>
public sealed record WorklistFilter(string Query, WorklistBand Band, WorklistSort Sort)
{
    public static WorklistFilter Default { get; } = new(string.Empty, WorklistBand.NeedAttention, WorklistSort.WorstFirst);
}

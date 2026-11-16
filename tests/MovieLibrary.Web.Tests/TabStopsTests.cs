namespace MovieLibrary.Web.Tests;

/// <summary>
/// The counter is itself a check, and a counter that cannot disagree with the markup would let every count above
/// it pass. Each case is a fragment whose answer is known by hand, and each rule was proved by breaking it.
/// </summary>
public class TabStopsTests
{
    private static int Count(string html, string? boundary = null)
    {
        var root = MarkupRules.Parse(html);
        return TabStops.Count(root, boundary is null ? null : root.QuerySelector(boundary));
    }

    [Fact]
    public void AClosedDisclosureKeepsOnlyItsSummary() =>
        Assert.Equal(1, Count("<details><summary>G</summary><input type=checkbox><input type=checkbox></details>"));

    [Fact]
    public void AnOpenDisclosureKeepsItsSummaryAndEverythingInside() =>
        Assert.Equal(3, Count("<details open><summary>G</summary><input type=checkbox><input type=checkbox></details>"));

    [Fact]
    public void ARadioGroupIsOneStopAndASecondGroupIsAnother() =>
        Assert.Equal(2, Count("<input type=radio name=a><input type=radio name=a><input type=radio name=b>"));

    [Fact]
    public void NegativeTabindexDisabledAndHiddenAreNotStops() =>
        Assert.Equal(1, Count("<button tabindex=-1>a</button><button disabled>b</button><input type=hidden><button>c</button>"));

    [Fact]
    public void AriaDisabledIsStillAStopBecauseItIsFocusable() =>
        Assert.Equal(1, Count("<button aria-disabled=true>a</button>"));

    [Fact]
    public void ABoundaryEndsTheCountBeforeItselfAndEverythingAfter() =>
        Assert.Equal(2, Count("<button>a</button><input><nav id=b><button>c</button></nav><button>d</button>", "#b"));
}

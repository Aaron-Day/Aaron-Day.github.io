namespace MovieLibrary.Web.Services;

/// <summary>
/// Where a PAGE puts focus, so the helper in <c>index.html</c> can tell "nobody has touched it" from "the
/// user moved it themselves". <c>&lt;body&gt;</c> and <c>&lt;html&gt;</c> are handled there.
/// </summary>
/// <remarks>
/// One array, not one per page, on the same argument <c>PagePath</c> was extracted on: a page that kept its
/// own copy and gained a keeper would hand focus to a reader on one route and refuse it on the next, and
/// nothing on screen would say which page was the odd one. The assertions live with the callers --
/// <c>MoviesFilterTests</c> pins the exact pair that reaches the interop call -- so the values are still
/// checked where they are used and not merely where they are declared.
/// <para>
/// A <c>Try again</c> button is deliberately NOT a keeper, which is what leaves a failed retry's focus where
/// the user put it -- <b>in every browser that focuses a button when it is clicked.</b> Safari, desktop and
/// iOS, does not: a mouse or touch click on a <c>&lt;button&gt;</c> leaves <c>document.activeElement</c> at
/// <c>&lt;body&gt;</c>, which IS a keeper, so the helper agrees to the move and a sighted Safari user loses
/// their place on every failed retry. VoiceOver's own double-tap does set focus, so the screen-reader case
/// is probably sound. Recorded rather than worked around: the behaviour is right wherever a click focuses
/// the button, and the alternative -- adding the button to the keepers -- would hand focus back to a control
/// the user may have deliberately left. One for Task 9's device list.
/// </para>
/// </remarks>
internal static class PageFocus
{
    /// <summary>The selectors a page is allowed to move focus away from, in the order the helper tries them.</summary>
    public static readonly string[] Keepers = ["#main-content", "#app h1"];
}

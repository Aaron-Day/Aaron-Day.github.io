using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

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

    /// <summary>
    /// Moves focus to <paramref name="element"/> unless the user has moved focus themselves, and says
    /// whether it happened.
    /// </summary>
    /// <remarks>
    /// The guard cannot be written in C#: there is no way to read <c>document.activeElement</c> from Blazor,
    /// and <c>ElementReference.FocusAsync()</c> focuses unconditionally. index.html defines the helper and
    /// says why the move is a conformance route rather than a workaround.
    /// </remarks>
    public static Task<bool> IfUnmoved(IJSRuntime js, ElementReference element)
    {
        ArgumentNullException.ThrowIfNull(js);

        return Move(() => js.InvokeAsync<bool>("movieLibrary.focusIfUnmoved", element, Keepers).AsTask());
    }

    /// <summary>Moves focus to <paramref name="element"/> whatever holds it now.</summary>
    /// <remarks>
    /// The successful retry only: the button the user was standing on has gone, so focus would otherwise
    /// drop to <c>&lt;body&gt;</c>. Nothing to ask the helper here -- the control that had focus no longer
    /// exists.
    /// </remarks>
    public static Task<bool> Outright(ElementReference element)
        => Move(async () => { await element.FocusAsync(); return true; });

    /// <summary>Runs one focus move and says whether it happened, swallowing the ways interop can fail.</summary>
    /// <remarks>
    /// EVERY focus move on every page goes through here, and that is the point of it living here. It was
    /// written three times before this -- <c>Movies</c>, <c>FilmDetail</c> and very nearly <c>Tv</c> -- and
    /// the three copies are what moved it: the keepers were already shared for the reason a third page made
    /// unanswerable, which is that a page with its own copy behaves differently from its neighbours and
    /// nothing on screen says which one is the odd one.
    /// <para>
    /// <c>ElementReference.FocusAsync()</c> is interop exactly as the helper call is -- it invokes
    /// <c>Blazor._internal.domWrapper.focus</c> -- so an escape from either reveals <c>#blazor-error-ui</c>,
    /// which carries an assertive role of its own, on a page that is already reporting a failure through the
    /// announcer or has just recovered from one: two assertive sources for one event, which is the thing
    /// this project forbids outright. A caller's <c>if (disposed) return;</c> narrows the window for the
    /// disposal race but does not close it, which is why <see cref="ObjectDisposedException"/> is in the
    /// list rather than being prevented.
    /// </para>
    /// </remarks>
    private static async Task<bool> Move(Func<Task<bool>> move)
    {
        try
        {
            return await move();
        }
        catch (Exception ex) when (ex is JSException or TaskCanceledException or ObjectDisposedException)
        {
            return false;
        }
    }
}

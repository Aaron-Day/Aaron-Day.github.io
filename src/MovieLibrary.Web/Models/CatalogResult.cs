namespace MovieLibrary.Web.Models;

/// <summary>What has happened to one fetch of the published catalogue.</summary>
public enum CatalogState
{
    /// <summary>
    /// A component is awaiting its task and has nothing yet.
    /// <para>
    /// <b><c>CatalogService</c> never returns this.</b> There is no code path that produces it, so do not go
    /// looking for one: it is the state a component is in BEFORE its task completes, which the component itself
    /// holds between its first render and the render that carries the result.
    /// </para>
    /// </summary>
    Loading,

    /// <summary>The fetch produced a catalogue. <c>Value</c> is what was published, which may be legitimately empty.</summary>
    Ready,

    /// <summary>
    /// The fetch did not produce a catalogue: the network, the server, or a body that was not a catalogue at all.
    /// <c>Value</c> is a safe default and says nothing about the library.
    /// </summary>
    Failed,
}

/// <summary>A fetch's outcome and its value, so that an empty value can never stand for a failure.</summary>
/// <remarks>
/// <para>
/// <b><see cref="State"/> carries what happened; <see cref="Value"/> never does.</b> On a failure the value is a
/// safe default -- an empty list, <c>CatalogMeta.Empty</c> -- purely so that callers need no null checks. It is
/// not a statement that the library is empty. That distinction is the whole reason this type exists: with an
/// empty list standing for a failed fetch, a grid renders "No titles match these filters." over a connection
/// that dropped, which is a page stating something false.
/// </para>
/// <para>
/// <b>Do not use <c>default(CatalogResult&lt;T&gt;)</c>.</b> It is <see cref="CatalogState.Loading"/> -- which is
/// the right starting state -- paired with a NULL value, which contradicts everything above: a page field left
/// at the default throws on <c>Value.Count</c> on its very first render. Initialise the field explicitly with
/// the same empty value the service would hand back, e.g.
/// <c>new(CatalogState.Loading, Array.Empty&lt;MovieRecord&gt;())</c>. There is no parameterless shape of this
/// type that could be made safe, because only the caller knows what empty means for its own <c>T</c>.
/// </para>
/// </remarks>
public readonly record struct CatalogResult<T>(CatalogState State, T Value);

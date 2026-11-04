using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using MovieLibrary.Web.Services;

namespace MovieLibrary.Web.Tests;

/// <summary>
/// A page stand-in whose buttons call the announcer from inside a Blazor event handler, which is how Tasks 6 to 8
/// will call it. That context matters: while an event handler runs the renderer is mid-batch, so two renders
/// requested inside it are merged into one, and only the final state ever reaches the DOM.
/// </summary>
public sealed class AnnounceButtons : ComponentBase
{
    [Inject] private Announcer Announcer { get; set; } = default!;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        Button(builder, 0, "say-status", () => Announcer.Status("12 films"));
        Button(builder, 10, "say-alert", () => Announcer.Alert("Could not load the film list."));
        Button(builder, 20, "say-route", () => Announcer.Route("Films"));
        Button(builder, 30, "say-twice", () => { Announcer.Status("12 films"); Announcer.Status("12 films"); });
    }

    private void Button(RenderTreeBuilder b, int seq, string id, Action click)
    {
        b.OpenElement(seq, "button");
        b.AddAttribute(seq + 1, "id", id);
        b.AddAttribute(seq + 2, "onclick", EventCallback.Factory.Create(this, click));
        b.CloseElement();
    }
}

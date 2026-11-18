namespace MovieLibrary.Web.Editor;

/// <summary>
/// The page's one polite region, as the editor sees it. The announcer lives in <c>MovieLibrary.Web</c>, which
/// this project cannot reference, so the dependency is inverted for the same reason <c>ICatalogueSource</c>
/// is: the web app implements this over its own announcer and there is still exactly one region.
/// </summary>
/// <remarks>
/// <b>Exactly one component on the editor page may call this</b> -- the worklist, for its debounced result
/// count. Within the region the latest message wins, and one still inside its settle delay when a newer arrives
/// is dropped unspoken, so a second caller would silence one of the two unpredictably. Everything else reports
/// by moving focus to a stable container whose own text is the message.
/// </remarks>
public interface IStatusRegion
{
    /// <summary>Speaks <paramref name="text"/> politely, or cancels a pending message when it is empty.</summary>
    void Status(string text);
}

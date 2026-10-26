namespace MovieLibrary.Enrich.Sheets;

/// <summary>
/// Google answered a tab request with a web page (usually a sign-in page) instead of CSV: the Sheet is probably
/// no longer shared as "Anyone with the link can view". Distinct from <c>SheetSchemaException</c>, which means the
/// CSV came back but is the wrong tab or has the wrong columns.
/// </summary>
public sealed class SheetAccessException(string message) : Exception(message);

namespace MovieLibrary.Core.Tests;

/// <summary>
/// An <see cref="HttpMessageHandler"/> that answers every request from a delegate and records it,
/// so no test can reach the real network. Shared by the pipeline tests; <c>TmdbClientTests</c>
/// still carries its own nested copy, which shadows this one inside that class.
/// </summary>
internal sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
    : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        return Task.FromResult(respond(request));
    }
}

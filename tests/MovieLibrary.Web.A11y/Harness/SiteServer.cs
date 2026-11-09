using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MovieLibrary.Web.A11y.Harness;

/// <summary>
/// Serves a published <c>wwwroot</c> over loopback, from the ROOT path, for the life of the test run.
/// </summary>
/// <remarks>
/// <para>
/// <b>From the root, not a subdirectory.</b> index.html declares <c>&lt;base href="/" /&gt;</c>, so a site
/// served under <c>/something/</c> resolves every asset and every in-app link against the wrong prefix and the
/// whole run stops being representative of the deployed site.
/// </para>
/// <para>
/// <b>Unknown paths get index.html's body with status 404</b>, which is exactly what GitHub Pages does: Pages
/// serves <c>404.html</c> - a copy of index.html, made by the deploy workflow - with a real HTTP 404 rather
/// than rewriting to a 200. The rendered DOM is identical either way, so the status affects no assertion here;
/// it is matched anyway so that nothing in the harness can come to depend on a status the live site does not
/// send.
/// </para>
/// <para>
/// <b>In-process rather than a backgrounded workflow step.</b> A separate server process needs a readiness
/// wait, a port and a teardown, and all three are flake a gate does not need. The OS picks the port, and the
/// server dies with the test host.
/// </para>
/// </remarks>
internal sealed class SiteServer : IAsyncDisposable
{
    private readonly WebApplication app;

    private SiteServer(WebApplication app, string baseUrl)
    {
        this.app = app;
        BaseUrl = baseUrl;
    }

    /// <summary>The origin to point a browser at, with no trailing slash.</summary>
    public string BaseUrl { get; }

    public static async Task<SiteServer> StartAsync(string root)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var app = builder.Build();
        var full = Path.GetFullPath(root);
        app.Run(context => Serve(context, full));

        await app.StartAsync();

        var url = app.Urls.First().TrimEnd('/');
        return new SiteServer(app, url);
    }

    private static async Task Serve(HttpContext context, string root)
    {
        var path = context.Request.Path.Value ?? "/";
        var file = Resolve(root, path);

        if (file is null)
        {
            // The deployed site's own deep-link behaviour, status included. See the remarks.
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.SendFileAsync(Path.Combine(root, "index.html"));
            return;
        }

        context.Response.ContentType = ContentTypeOf(file);
        await context.Response.SendFileAsync(file);
    }

    /// <summary>
    /// The file a request path names, or null for anything that is not a file under <paramref name="root"/>.
    /// The containment check is on the RESOLVED path, so "/../../secret" cannot escape the published site.
    /// </summary>
    private static string? Resolve(string root, string path)
    {
        var relative = Uri.UnescapeDataString(path).TrimStart('/');
        if (relative.Length == 0) relative = "index.html";

        string candidate;
        try
        {
            candidate = Path.GetFullPath(Path.Combine(root, relative));
        }
        catch (ArgumentException)
        {
            return null;   // an invalid character in the path is simply not a file here
        }

        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;

        return File.Exists(candidate) ? candidate : null;
    }

    /// <summary>
    /// Enough of a content-type table for a published Blazor WebAssembly app. <c>.wasm</c> is the one entry
    /// that is load-bearing: the browser refuses to instantiate a module served as octet-stream, so the app
    /// would never boot and every scan would be of the boot shell - the exact false pass a static scanner gives.
    /// Everything unlisted falls back to octet-stream rather than failing the request.
    /// </summary>
    private static string ContentTypeOf(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".html" or ".htm" => "text/html; charset=utf-8",
        ".js" or ".mjs" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" or ".map" => "application/json; charset=utf-8",
        ".wasm" => "application/wasm",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".ico" => "image/x-icon",
        ".woff2" => "font/woff2",
        ".woff" => "font/woff",
        ".txt" => "text/plain; charset=utf-8",
        _ => "application/octet-stream",
    };

    public async ValueTask DisposeAsync()
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }
}

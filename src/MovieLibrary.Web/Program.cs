using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MovieLibrary.Web;
#if DEBUG
using MovieLibrary.Web.Editor;
#endif
using MovieLibrary.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped<Announcer>();

// The clock the films page's two debounce timers run on. The announcer takes it too, which changes nothing: it was
// already defaulting to this very instance. Registered so that a test can hand the page a clock it owns.
builder.Services.AddSingleton(TimeProvider.System);

// The one place the films page writes its filter into the address. A service rather than code in the page because
// SourceContractTests forbids a navigation call in any hand-written .razor.
builder.Services.AddScoped<QueryState>();

// One instance for the session, so the catalogue is fetched once and every page shares the cached task.
// SourceContractTests scans for this line as well as the Announcer's: every bUnit test registers its own, so
// losing it here would pass the whole suite and then fail at boot with a DI error on the first page render.
builder.Services.AddScoped<CatalogService>();

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

#if DEBUG
// The local record editor's registrations. Debug only: the Release payload does not reference the project.
builder.Services.AddRecordEditor();
#endif

await builder.Build().RunAsync();

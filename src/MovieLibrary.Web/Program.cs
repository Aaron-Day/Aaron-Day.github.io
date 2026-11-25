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

// The owner's own state -- stars, his scores, his lists -- and the browser storage behind it. One instance for
// the session for the same reason the catalogue has one: it holds the document in memory, every page reads from
// it synchronously, and a second instance would answer from a copy that is already stale.
builder.Services.AddScoped<IPreferenceStorage, LocalPreferenceStorage>();
builder.Services.AddScoped<PreferenceStore>();

#if DEBUG
// The local record editor's registrations. Debug only: the Release payload does not reference the project.
// The two types are the web app's side of the two seams the editor cannot reach across: the catalogue service
// and the announcer both live here, and this project is the one that references the editor.
builder.Services.AddRecordEditor<EditorCatalogueSource, EditorStatusRegion>();
#endif

await builder.Build().RunAsync();

using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MovieLibrary.Web;
using MovieLibrary.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped<Announcer>();

// One instance for the session, so the catalogue is fetched once and every page shares the cached task.
// SourceContractTests scans for this line as well as the Announcer's: every bUnit test registers its own, so
// losing it here would pass the whole suite and then fail at boot with a DI error on the first page render.
builder.Services.AddScoped<CatalogService>();

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

await builder.Build().RunAsync();

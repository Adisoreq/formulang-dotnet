using System.Globalization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using WebPreview;
using WebPreview.Model;

// Numbers in the inputs and outputs of the preview do not depend on the language of the browser
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

builder.Services.AddSingleton<PreviewState>();

await builder.Build().RunAsync();

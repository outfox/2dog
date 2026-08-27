using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

// Blazor boots the .NET runtime (with Godot linked in); the home page's GodotView starts the engine.
var builder = WebAssemblyHostBuilder.CreateDefault(args);
await builder.Build().RunAsync();

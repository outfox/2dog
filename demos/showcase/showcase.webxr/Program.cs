using Godot;
using showcase.web;
using Engine = twodog.Engine;

namespace showcase.webxr;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.WriteLine("2dog webxr showcase starting...");

        // The game assembly owns the source-generated plugins initializer;
        // register it before Start() (there is no GodotPlugins.dll on web).
        Engine.RegisterWebPluginsInitializer(TwoDogWebBoot.PluginsInitializer());

        // args come from the JS shell (GODOT_CONFIG.args plus the
        // '--main-pack godot.pck' the engine loader prepends).
        var engine = new Engine("showcase.web", args: args);
        try
        {
            engine.Start();
            GD.Print("Hello from GodotSharp (browser).");
            GD.Print("Scene Root: ", engine.Tree.CurrentScene.Name);

            GodotApiSmoke.RunAll(engine.Tree);

            // Web-only: the WebXR module must be compiled in, its JS library linked,
            // and the interface registered on XRServer (desktop has no instance).
            if (XRServer.FindInterface("WebXR") is null)
            {
                throw new InvalidOperationException("WebXR interface is not registered");
            }

            // The browser loop owns the lifetime after Run() returns.
            engine.Run();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"2DOG_WASM_SMOKE_FAILED: {exception}");
            JavaScriptBridge.Eval("document.documentElement.setAttribute('data-twodog-smoke', 'failed')");
            await engine.DisposeAsync();
            throw;
        }

        // Marked only after Run() returns, so a failed loop installation
        // cannot report a passing smoke.
        JavaScriptBridge.Eval("document.documentElement.setAttribute('data-twodog-smoke', 'passed')");
        Console.WriteLine("2DOG_WASM_SMOKE_PASSED");

        return 0;
    }
}

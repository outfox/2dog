using Godot;
using Engine = twodog.Engine;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.WriteLine("2dog web demo starting...");

        // The game assembly owns the source-generated plugins initializer;
        // register it before Start() (there is no GodotPlugins.dll on web).
        Engine.RegisterWebPluginsInitializer(TwoDogWebBoot.PluginsInitializer());

        // args come from the JS shell (GODOT_CONFIG.args plus the
        // '--main-pack godot.pck' the engine loader prepends).
        var engine = new Engine("demo.web", null, args);
        engine.Start();

        try
        {
            GD.Print("Hello from GodotSharp (browser).");
            GD.Print("Scene Root: ", engine.Tree.CurrentScene.Name);

            GodotApiSmoke.RunAll(engine.Tree);
            JavaScriptBridge.Eval("document.documentElement.setAttribute('data-twodog-smoke', 'passed')");
            Console.WriteLine("2DOG_WASM_SMOKE_PASSED");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"2DOG_WASM_SMOKE_FAILED: {exception}");
            JavaScriptBridge.Eval("document.documentElement.setAttribute('data-twodog-smoke', 'failed')");
            throw;
        }

        // Hands the loop to emscripten and returns immediately; the engine
        // destroys itself when Godot requests quit. Do not dispose here.
        engine.Run();

        return 0;
    }
}

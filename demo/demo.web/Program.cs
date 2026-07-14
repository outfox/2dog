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

        GD.Print("Hello from GodotSharp (browser).");
        GD.Print("Scene Root: ", engine.Tree.CurrentScene.Name);

        var ticker = engine.Tree.CurrentScene.GetNode<Ticker>("Ticker");
        GD.Print("Ticker: ", ticker);

        // Hands the loop to emscripten and returns immediately; the engine
        // destroys itself when Godot requests quit. Do not dispose here.
        engine.Run();

        return 0;
    }
}

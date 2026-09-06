using Godot;
using Engine = twodog.Engine;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.WriteLine("TPLRAWNAME (web) starting...");

        // The Godot project's assembly owns the source-generated plugins
        // initializer; register it before Start() (there is no
        // GodotPlugins.dll on web).
        Engine.RegisterWebPluginsInitializer(TwoDogWebBoot.PluginsInitializer());

        // args come from the page's GODOT_CONFIG.args plus the
        // '--main-pack godot.pck' the engine loader prepends.
        var engine = new Engine("TPLRAWNAME", args: args);
        try
        {
            engine.Start();
            GD.Print("2dog is running in the browser!");
            GD.Print("Scene Root: ", engine.Tree.CurrentScene.Name);
            // The browser loop owns the lifetime after Run() returns.
            engine.Run();
        }
        catch
        {
            await engine.DisposeAsync();
            throw;
        }

        return 0;
    }
}

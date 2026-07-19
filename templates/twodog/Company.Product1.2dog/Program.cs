using Godot;
using Engine = twodog.Engine;

internal static class Program
{
    // STA matches how godot.exe runs its main thread on Windows: OLE (drag & drop,
    // IME, native dialogs) fails to initialize on the MTA thread .NET uses by default.
    // No effect on Linux/macOS.
    [STAThread]
    private static void Main(string[] args)
    {
        // Create and start the Godot engine with your project. Start() runs the
        // main scene configured in project.godot (run/main_scene), exactly like
        // launching godot.exe would - no manual scene loading needed. Command-line
        // arguments are forwarded to Godot (--headless, --quit-after, ...).
        using var engine = new Engine("Company.Product1", Engine.ResolveProjectDir(), args);
        using var godot = engine.Start();

        if (engine.Tree.CurrentScene is { } scene)
            GD.Print($"2dog is running '{scene.Name}'!");
        else
            GD.Print("2dog is running (no run/main_scene set in project.godot).");
        Console.WriteLine("Close the window to quit.");

        // Main game loop - Iteration() returns true when the engine wants to
        // quit (window closed, SceneTree.Quit(), --quit-after N, ...).
        while (!godot.Iteration())
        {
            // Your per-frame logic here
        }

        Console.WriteLine("Shutting down...");
    }
}

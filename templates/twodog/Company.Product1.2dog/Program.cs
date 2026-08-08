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
        // The default constructor finds raw project content during development
        // and the exe-adjacent .pck after publish. Arguments are forwarded to Godot.
        using var engine = new Engine("Company.Product1", args: args);
        using var godot = engine.Start();

        if (engine.Tree.CurrentScene is { } scene)
            GD.Print($"2dog is running '{scene.Name}'!");
        else
            GD.Print("2dog is running (no run/main_scene set in project.godot).");
        Console.WriteLine("Close the window to quit.");

        // Iteration() returns true when Godot wants to quit.
        while (!godot.Iteration())
        {
            // Your per-frame logic here
        }

        Console.WriteLine("Shutting down...");
    }
}

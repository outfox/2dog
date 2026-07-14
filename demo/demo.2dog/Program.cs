using Godot;
using Engine = twodog.Engine;

internal static class Program
{
    // STA matches how godot.exe runs its main thread on Windows: OLE (drag & drop,
    // IME, native dialogs) fails to initialize on the MTA thread .NET uses by default.
    // No effect on Linux/macOS.
    [STAThread]
    private static void Main()
    {
        using var engine = new Engine("demo", Engine.ResolveProjectDir());
        using var godotInstance = engine.Start();
        GD.Print("Hello from GodotSharp.");
        GD.Print("Scene Root: ", engine.Tree.CurrentScene.Name);

        var ticker = engine.Tree.CurrentScene.GetNode<Ticker>("Ticker");
        GD.Print("Ticker: ", ticker);

        // You can access the SceneTree via engine.Tree

        Console.WriteLine("Godot is running, close window or press 'Q' to quit.");

        // Key polling requires a real console; skip it when input is redirected
        // (piped, CI) - Console.KeyAvailable throws there.
        var interactive = !Console.IsInputRedirected;

        while (!godotInstance.Iteration())
        {
            if (interactive && Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Q)
                break;
        }

        Console.WriteLine($"Engine exited after {ticker.localAccumulator} ticks. (iterations / _process calls)");

        Console.WriteLine("Godot is shutting down. Thank you for using 2dog. 🦴");
    }
}

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

        // You can access the SceneTree via engine.Tree

        // The blue cubes spin themselves via SpinningCube._Process (Godot side);
        // the white ones are plain MeshInstance3Ds we drive from this loop.
        var whiteCubes = engine.Tree.CurrentScene
            .GetNode<Node3D>("Flair/WhiteCubes")
            .GetChildren().OfType<Node3D>().ToArray();
        var whiteSpinAxis = new Vector3(1, 1, 0).Normalized();

        Console.WriteLine("Godot is running, close window or press 'Q' to quit.");

        // Key polling requires a real console; skip it when input is redirected
        // (piped, CI) - Console.KeyAvailable throws there.
        var interactive = !Console.IsInputRedirected;

        while (!godotInstance.Iteration())
        {
            var delta = (float)engine.Tree.Root.GetProcessDeltaTime();
            foreach (var cube in whiteCubes)
                cube.Rotate(whiteSpinAxis, 1.8f * delta);

            if (interactive && Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Q)
                break;
        }

        Console.WriteLine("Godot is shutting down. Thank you for using 2dog. 🦴");
    }
}

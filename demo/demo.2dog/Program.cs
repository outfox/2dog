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
        // Touch the game assembly before Start() so it loads into the default
        // AssemblyLoadContext and the engine reuses it for script binding
        // instead of loading a second copy (type identity across contexts).
        _ = typeof(SpinningCube).Assembly;

        // Command-line arguments are forwarded to Godot (--headless, --quit-after, ...).
        using var engine = new Engine("demo", Engine.ResolveProjectDir(), args);
        using var godotInstance = engine.Start();
        GD.Print("Hello from GodotSharp.");
        GD.Print("Scene Root: ", engine.Tree.CurrentScene.Name);

        // You can access the SceneTree via engine.Tree - including nodes with
        // their generated C# script types. The platform smoke tests grep for
        // this marker to prove script classes bound in this build.
        if (engine.Tree.CurrentScene.GetNodeOrNull<SpinningCube>("Flair/BlueCubes/BlueCube1") is not null)
            Console.WriteLine("2DOG_CSHARP_SCRIPT_SMOKE_PASSED");

        // The blue cubes spin themselves via SpinningCube._Process (Godot side);
        // the white ones are plain MeshInstance3Ds we drive from this loop.
        var whiteCubes = engine.Tree.CurrentScene
            .GetNode<Node3D>("Flair/WhiteCubes")
            .GetChildren().OfType<Node3D>().ToArray();
        var whiteSpinAxis = new Vector3(1, 1, 0).Normalized();

        Console.WriteLine("Godot is running, close the window to quit.");

        // Iteration() returns true when the engine wants to quit (window
        // closed, SceneTree.Quit(), --quit-after N, ...).
        while (!godotInstance.Iteration())
        {
            var delta = (float)engine.Tree.Root.GetProcessDeltaTime();
            foreach (var cube in whiteCubes)
                cube.Rotate(whiteSpinAxis, 1.8f * delta);
        }

        Console.WriteLine("Godot is shutting down. Thank you for using 2dog. 🦴");
    }
}

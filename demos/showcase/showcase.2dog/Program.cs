using Godot;
using Engine = twodog.Engine;

namespace showcase;

internal static class Program
{
    // STA matches how godot.exe runs its main thread on Windows: OLE (drag & drop,
    // IME, native dialogs) fails to initialize on the MTA thread .NET uses by default.
    // No effect on Linux/macOS.
    [STAThread]
    private static void Main(string[] args)
    {
        // The console event runs on another thread. Only signal the owner thread;
        // calling Godot here, or terminating immediately, skips orderly teardown.
        var quitRequested = 0;
        ConsoleCancelEventHandler requestQuit = (_, e) =>
        {
            e.Cancel = true;
            Interlocked.Exchange(ref quitRequested, 1);
        };
        Console.CancelKeyPress += requestQuit;
        try
        {
            Run(args, () => Volatile.Read(ref quitRequested) != 0);
        }
        finally
        {
            Console.CancelKeyPress -= requestQuit;
        }
    }

    private static void Run(string[] args, Func<bool> quitRequested)
    {
        // Touch the game assembly before Start() so it loads into the default
        // AssemblyLoadContext and the engine reuses it for script binding
        // instead of loading a second copy (type identity across contexts).
        _ = typeof(SpinningCube).Assembly;

        // Content is resolved from the source project or published .pck.
        using var engine = new Engine("showcase", args: args);
        engine.Start();
        var smokeRequested = OS.GetCmdlineUserArgs().Contains("--2dog-smoke-markers");
        Node3D[] whiteCubes = [];
        if (Godot.Engine.IsEditorHint())
        {
            // The editor owns the tree; there is no running game scene to probe or animate.
            if (smokeRequested) Console.WriteLine("2DOG_EDITOR_SMOKE_PASSED");
        }
        else
        {
            var scene = engine.Tree.CurrentScene
                ?? throw new InvalidOperationException("The showcase requires a running main scene.");
            // The window title defaults to application/config/name, which the API smoke pins to "showcase".
            DisplayServer.WindowSetTitle("2dog showcase — desktop host");
            GD.Print("Hello from GodotSharp.");
            GD.Print("Scene Root: ", scene.Name);

            if (smokeRequested)
            {
                // Published host checks opt in, including the NativeAOT API probe.
                if (scene.GetNodeOrNull<SpinningCube>("Flair/BlueCubes/BlueCube1") is not null)
                    Console.WriteLine("2DOG_CSHARP_SCRIPT_SMOKE_PASSED");
                GodotApiSmoke.RunAll(engine.Tree);
                Console.WriteLine("2DOG_API_SMOKE_PASSED");
            }

            // Blue cubes spin in SpinningCube._Process; the host drives the white ones.
            whiteCubes = scene.GetNode<Node3D>("Flair/WhiteCubes")
                .GetChildren().OfType<Node3D>().ToArray();
        }
        var whiteSpinAxis = new Vector3(1, 1, 0).Normalized();

        Console.WriteLine("Godot is running, close the window to quit.");

        // Update() returns false when the engine wants to quit (window
        // closed, SceneTree.Quit(), --quit-after N, ...).
        while (engine.Update())
        {
            if (quitRequested())
                engine.Tree.Quit();

            var delta = (float)engine.Tree.Root.GetProcessDeltaTime();
            foreach (var cube in whiteCubes)
                cube.Rotate(whiteSpinAxis, 1.8f * delta);
        }

        Console.WriteLine("Godot is shutting down. Thank you for using 2dog. 🦴");
    }
}

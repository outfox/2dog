using Godot;
using Qt.Quick;
using twodog;

namespace showcase;

internal static class Program
{
    internal static QtGodotSession Session { get; private set; } = null!;

    // Qt Bridge runs Main on its own thread; Qt's GUI thread pumps the engine (see QtGodotSession).
    private static void Main(string[] args)
    {
        // Command-line arguments are forwarded to Godot (--quit-after, --verbose, ...).
        using var session = Session = new QtGodotSession("showcase", args: args);
        session.Started += (_, _) => DriveWhiteCubes(session);
        Qml.LoadFromRootModule("Main");
        Qml.WaitForExit();
    }

    // The blue cubes spin themselves via SpinningCube._Process (Godot side); the white ones are plain
    // MeshInstance3Ds this host drives per frame, like the other hosts.
    private static void DriveWhiteCubes(QtGodotSession session)
    {
        var tree = session.Engine.Tree;
        var whiteCubes = tree.CurrentScene.GetNode<Node3D>("Flair/WhiteCubes").GetChildren().OfType<Node3D>().ToArray();
        var axis = new Vector3(1, 1, 0).Normalized();
        session.FrameAdvanced += (_, _) =>
        {
            // The root's delta keeps ticking while the tree is paused.
            if (tree.Paused) return;
            var delta = (float)tree.Root.GetProcessDeltaTime();
            foreach (var cube in whiteCubes)
                cube.Rotate(axis, 1.8f * delta);
        };
    }
}

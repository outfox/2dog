using Foundation;
using Godot;
using twodog.Ios;
using UIKit;

namespace showcase.ios;

[Register(nameof(AppDelegate))]
public class AppDelegate : UIApplicationDelegate
{
    // Godot reads the app delegate's window for the interface orientation; SceneDelegate assigns it.
    public override UIWindow? Window { get; set; }

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        // Godot's view controller owns the display link, input and lifecycle; SceneDelegate starts the engine
        // when the window scene connects.
        GodotHost.Started += OnMainLoopStarted;
        return true;
    }

    public override UISceneConfiguration GetConfiguration(UIApplication application, UISceneSession connectingSceneSession,
        UISceneConnectionOptions options) =>
        new UISceneConfiguration("Default Configuration", connectingSceneSession.Role);

    // Godot's display link runs the loop on the main thread; this runs there once the main scene is in the tree.
    private static void OnMainLoopStarted()
    {
        try
        {
            var tree = (SceneTree)Godot.Engine.GetMainLoop();
            GD.Print("Hello from GodotSharp (iOS).");
            GD.Print("Scene Root: ", tree.CurrentScene.Name);

            // Everything but the GDExtension probe and the signal table that includes its ticker: iOS GDExtensions
            // (static libraries registered at link time) are later work.
            GodotApiSmoke.MathPrecision();
            GodotApiSmoke.CoreTypesAndNativeHelpers(tree);
            GodotApiSmoke.ManagedCallables();
            GodotApiSmoke.ErrorReportingAndTypedCollections();
            GodotApiSmoke.ImagesAndResources();
            GodotApiSmoke.EngineSingletons();
            GodotApiSmoke.LowLevelServers();
            GodotApiSmoke.SceneAndGeneratedScript();
            GodotApiSmoke.GDScriptOnlyEngineFeatures(tree);

            // The blue cubes spin themselves via SpinningCube._Process (Godot side), the red ones via GDScript;
            // the white ones are plain MeshInstance3Ds this host drives once per frame.
            var whiteCubes = tree.CurrentScene
                .GetNode<Node3D>("Flair/WhiteCubes")
                .GetChildren().OfType<Node3D>().ToArray();
            var whiteSpinAxis = new Vector3(1, 1, 0).Normalized();
            tree.ProcessFrame += () =>
            {
                var delta = (float)tree.Root.GetProcessDeltaTime();
                foreach (var cube in whiteCubes)
                    cube.Rotate(whiteSpinAxis, 1.8f * delta);
            };

            // Both sinks: Godot's logger (os_log) and the process stdout the Simulator console shows.
            GD.Print("2DOG_IOS_SHOWCASE_SMOKE_PASSED");
            Console.WriteLine("2DOG_IOS_SHOWCASE_SMOKE_PASSED");
        }
        catch (Exception exception)
        {
            // Must not unwind into the display-link frame; the Simulator test waits for the passed marker instead.
            GD.PrintErr($"2DOG_IOS_SHOWCASE_SMOKE_FAILED: {exception}");
            Console.Error.WriteLine($"2DOG_IOS_SHOWCASE_SMOKE_FAILED: {exception}");
        }
    }
}

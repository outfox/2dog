using Android.Runtime;
using Godot;
using twodog;

namespace showcase.android;

[Application]
public class ShowcaseApplication : Application
{
    // Java holds the listener for the process lifetime; this keeps its managed peer reachable too.
    private static Java.Lang.Runnable? _mainLoopStarted;

    public ShowcaseApplication(nint handle, JniHandleOwnership ownership) : base(handle, ownership) { }

    public override void OnCreate()
    {
        base.OnCreate();

        // Application runs before any Activity, including when Android recreates the Godot Activity directly after
        // process death. Touch the game assembly so it loads into the default AssemblyLoadContext and Godot reuses
        // it for script binding instead of loading a second copy (type identity across contexts).
        _ = typeof(SpinningCube).Assembly;

        // The native module checks for an assembly file before asking GodotPlugins for the loaded assembly.
        var directory = Path.Combine(FilesDir!.AbsolutePath, "2dog-assemblies");
        Directory.CreateDirectory(directory);
        using (var input = Assets!.Open("2dog/showcase.dll"))
        using (var output = File.Create(Path.Combine(directory, "showcase.dll")))
            input.CopyTo(output);
        AndroidHost.Register(directory);

        _mainLoopStarted = new Java.Lang.Runnable(OnMainLoopStarted);
        AddMainLoopStartedListener(_mainLoopStarted);
    }

    // Godot's Activity owns the loop, so the scene is reachable only from its render thread (the engine's main
    // thread). TwoDogActivity runs this there once the main scene is in the tree.
    private static void OnMainLoopStarted()
    {
        try
        {
            var tree = (SceneTree)Godot.Engine.GetMainLoop();
            GD.Print("Hello from GodotSharp (Android).");
            GD.Print("Scene Root: ", tree.CurrentScene.Name);

            GodotApiSmoke.RunAll(tree);

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

            GD.Print("2DOG_ANDROID_SHOWCASE_SMOKE_PASSED");
        }
        catch (Exception exception)
        {
            // Must not unwind into Godot's JNI frame; the device test waits for the passed marker instead.
            GD.PrintErr($"2DOG_ANDROID_SHOWCASE_SMOKE_FAILED: {exception}");
        }
    }

    private static void AddMainLoopStartedListener(Java.Lang.Runnable listener)
    {
        var activity = JNIEnv.FindClass("dev/twodog/host/TwoDogActivity");
        try
        {
            var method = JNIEnv.GetStaticMethodID(activity, "addMainLoopStartedListener", "(Ljava/lang/Runnable;)V");
            JNIEnv.CallStaticVoidMethod(activity, method, new JValue(listener));
        }
        finally
        {
            JNIEnv.DeleteGlobalRef(activity);
        }
    }
}

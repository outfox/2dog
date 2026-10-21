using Foundation;
using Godot;
using twodog.Ios;
using UIKit;

namespace Company.Product1;

[Register(nameof(AppDelegate))]
public class AppDelegate : UIApplicationDelegate
{
    // Godot reads the app delegate's window for the interface orientation; SceneDelegate assigns it.
    public override UIWindow? Window { get; set; }

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        // Godot's view controller owns the display link, input and lifecycle; SceneDelegate starts the engine
        // when the window scene connects. Never call Engine.Start() on iOS.
        GodotHost.Started += OnStarted;
        return true;
    }

    public override UISceneConfiguration GetConfiguration(UIApplication application, UISceneSession connectingSceneSession,
        UISceneConnectionOptions options) =>
        new UISceneConfiguration("Default Configuration", connectingSceneSession.Role);

    // Runs on the main thread once the main scene is in the tree: the first point where the host can use it.
    private static void OnStarted()
    {
        var tree = (SceneTree)Godot.Engine.GetMainLoop();
        GD.Print("2dog is running on iOS!");
        GD.Print("Scene Root: ", tree.CurrentScene.Name);
    }
}

using Foundation;
using twodog.Ios;
using UIKit;

[Register(nameof(SceneDelegate))]
public class SceneDelegate : UIResponder, IUIWindowSceneDelegate
{
    [Export("window")]
    public UIWindow? Window { get; set; }

    [Export("scene:willConnectToSession:options:")]
    public void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions connectionOptions)
    {
        if (scene is not UIWindowScene windowScene) return;
        // Keep the game in the default load context before Godot binds its C# scripts: the generated
        // initializer lives in the game assembly, and taking its pointer loads it. The engine outlives the scene.
        Window ??= new UIWindow(windowScene)
        {
            RootViewController = GodotHost.ViewController
                ?? GodotHost.Start("ios-smoke-game", TwoDogWebBoot.PluginsInitializer()),
        };
        if (UIApplication.SharedApplication.Delegate is AppDelegate app) app.Window = Window;
        Window.MakeKeyAndVisible();
    }
}

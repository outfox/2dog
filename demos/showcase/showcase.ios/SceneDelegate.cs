using Foundation;
using twodog.Ios;
using UIKit;

namespace showcase.ios;

[Register(nameof(SceneDelegate))]
public class SceneDelegate : UIResponder, IUIWindowSceneDelegate
{
    [Export("window")]
    public UIWindow? Window { get; set; }

    [Export("scene:willConnectToSession:options:")]
    public void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions connectionOptions)
    {
        if (scene is not UIWindowScene windowScene) return;
        // The game assembly's generated plugins initializer registers its scripts with the statically linked
        // engine. One engine runs per process, so a reconnecting scene shows the existing view controller.
        Window ??= new UIWindow(windowScene)
        {
            RootViewController = GodotHost.ViewController
                ?? GodotHost.Start("showcase", TwoDogWebBoot.PluginsInitializer(), args:
                    Environment.GetEnvironmentVariable("CI") == "true" ? ["--audio-driver", "Dummy"] : []),
        };
        if (UIApplication.SharedApplication.Delegate is AppDelegate app) app.Window = Window;
        Window.MakeKeyAndVisible();
    }
}

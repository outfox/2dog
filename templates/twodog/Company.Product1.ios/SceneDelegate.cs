using Foundation;
using twodog.Ios;
using UIKit;

namespace Company.Product1;

[Register(nameof(SceneDelegate))]
public class SceneDelegate : UIResponder, IUIWindowSceneDelegate
{
    [Export("window")]
    public UIWindow? Window { get; set; }

    [Export("scene:willConnectToSession:options:")]
    public void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions connectionOptions)
    {
        if (scene is not UIWindowScene windowScene) return;
        // The game assembly's generated plugins initializer registers its scripts with the engine. One engine
        // runs per process, so a reconnecting scene shows the existing view controller.
        Window ??= new UIWindow(windowScene)
        {
            RootViewController = GodotHost.ViewController
                ?? GodotHost.Start("TPLRAWNAME", TwoDogWebBoot.PluginsInitializer()),
        };
        if (UIApplication.SharedApplication.Delegate is AppDelegate app) app.Window = Window;
        Window.MakeKeyAndVisible();
    }
}

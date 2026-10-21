using Foundation;
using UIKit;

[Register(nameof(AppDelegate))]
public class AppDelegate : UIApplicationDelegate
{
    // Godot reads the app delegate's window for the interface orientation; SceneDelegate assigns it.
    public override UIWindow? Window { get; set; }

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions) => true;

    public override UISceneConfiguration GetConfiguration(UIApplication application, UISceneSession connectingSceneSession,
        UISceneConnectionOptions options) =>
        new UISceneConfiguration("Default Configuration", connectingSceneSession.Role);
}

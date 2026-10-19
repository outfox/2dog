using Foundation;

namespace showcase.Maui;

// The iOS 26 SDK requires the UIScene lifecycle; MAUI's scene delegate hosts the app's window.
[Register(nameof(SceneDelegate))]
public sealed class SceneDelegate : MauiUISceneDelegate
{
}

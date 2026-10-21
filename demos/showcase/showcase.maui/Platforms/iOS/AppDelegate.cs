using Foundation;
using twodog;

namespace showcase.Maui;

[Register(nameof(AppDelegate))]
public sealed class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp()
    {
        // The game assembly's generated initializer registers its scripts with the statically linked engine;
        // the page's GodotView then starts Godot on Godot's own view controller.
        IosHost.Register(TwoDogWebBoot.PluginsInitializer());
        return MauiProgram.CreateMauiApp();
    }
}

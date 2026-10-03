using Android.Content;

namespace showcase.android;

// Launcher trampoline: starts 2dog.android's Godot Activity and gets out of the way (NoDisplay draws nothing).
[Activity(MainLauncher = true, Exported = true, Theme = "@android:style/Theme.NoDisplay")]
public class MainActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var intent = new Intent();
        intent.SetClassName(PackageName!, "dev.twodog.host.TwoDogActivity");
        StartActivity(intent);
        Finish();
    }
}

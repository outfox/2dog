using Android.App;
using Android.Runtime;
using twodog;

[Application]
public class SmokeApplication : Application
{
    public SmokeApplication(nint handle, JniHandleOwnership ownership) : base(handle, ownership) { }

    public override void OnCreate()
    {
        base.OnCreate();
        // Application runs even when Android recreates the Godot Activity directly after process death.
        // Keep the game in the default load context before Godot resolves its C# scripts.
        _ = typeof(Smoke).Assembly;
        var directory = Path.Combine(FilesDir!.AbsolutePath, "2dog-assemblies");
        Directory.CreateDirectory(directory);
        using (var input = Assets!.Open("2dog/android-smoke-game.dll"))
        using (var output = File.Create(Path.Combine(directory, "android-smoke-game.dll")))
            input.CopyTo(output);
        AndroidHost.Register(directory);
    }
}

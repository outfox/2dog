using System.Reflection;
using Android.Runtime;
using twodog;

namespace Company.Product1;

// Android creates the Application before any Activity, also when it recreates Godot's Activity after the process
// was killed: the one place to register .NET with Godot before it starts.
[Application(Icon = "@mipmap/appicon", RoundIcon = "@mipmap/appicon")]
public class GameApplication : Application
{
    private const string GameAssembly = "TPLRAWNAME";

    public GameApplication(nint handle, JniHandleOwnership ownership) : base(handle, ownership) { }

    public override void OnCreate()
    {
        base.OnCreate();

        // Godot binds the game's C# scripts in the default AssemblyLoadContext.
        Assembly.Load(GameAssembly);

        // The engine checks for the game assembly as a file, so copy it out of the APK's assets.
        var directory = Path.Combine(FilesDir!.AbsolutePath, "2dog-assemblies");
        Directory.CreateDirectory(directory);
        using (var input = Assets!.Open($"2dog/{GameAssembly}.dll"))
        using (var output = File.Create(Path.Combine(directory, $"{GameAssembly}.dll")))
            input.CopyTo(output);
        AndroidHost.Register(directory);
    }
}

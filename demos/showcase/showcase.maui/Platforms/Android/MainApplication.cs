using System.Reflection;
using Android.App;
using Android.Runtime;
using twodog;

namespace showcase.Maui;

[Application]
public sealed class MainApplication : MauiApplication
{
    public MainApplication(nint handle, JniHandleOwnership ownership) : base(handle, ownership) { }

    protected override MauiApp CreateMauiApp()
    {
        const string gameAssembly = "showcase";
        Assembly.Load(gameAssembly);
        var directory = Path.Combine(FilesDir!.AbsolutePath, "2dog-assemblies");
        Directory.CreateDirectory(directory);
        using (var input = Assets!.Open($"2dog/{gameAssembly}.dll"))
        using (var output = File.Create(Path.Combine(directory, $"{gameAssembly}.dll")))
            input.CopyTo(output);
        AndroidHost.Register(directory);
        return MauiProgram.CreateMauiApp();
    }
}

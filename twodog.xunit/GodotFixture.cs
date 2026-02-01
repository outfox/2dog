using System.Reflection;
using System.Runtime.InteropServices;
using Godot;
using JetBrains.Annotations;

namespace twodog.xunit;

[UsedImplicitly]
public class GodotFixture : IDisposable
{
    [DllImport("libc", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int setenv(string name, string value, int overwrite);

    public GodotFixture()
    {
        Console.WriteLine("Initializing Godot...");

        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var projectPath = Engine.ResolveProjectDir();

        if (File.Exists(Path.Combine(assemblyDir, "GodotPlugins.dll")))
            setenv("GODOTSHARP_DIR", assemblyDir, 1);

        Console.WriteLine("Godot project: " + projectPath);
        Engine = new Engine("twodog.tests", projectPath);
        GodotInstance = Engine.Start();
        Console.WriteLine("Godot initialized successfully.");
    }

    public Engine Engine { get; }

    public GodotInstance GodotInstance { get; }

    public SceneTree Tree => Engine.Tree;

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        Console.WriteLine("Shutting down Godot...");
        GodotInstance.Dispose();
        Engine.Dispose();
        Console.WriteLine("Godot shut down successfully.");
    }
}
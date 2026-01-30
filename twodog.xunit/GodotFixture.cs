using System.Reflection;
using System.Runtime.InteropServices;
using Godot;
using JetBrains.Annotations;
using Environment = System.Environment;

namespace twodog.tests;

[UsedImplicitly]
public class GodotFixture : IDisposable
{
    [DllImport("libc", SetLastError = true)]
    private static extern int setenv(string name, string value, int overwrite);

    public GodotFixture()
    {
        Console.WriteLine("Initializing Godot...");

        // Resolve the project path relative to the assembly location
        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var projectPath = Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "..", "..", "project"));

        if (File.Exists(Path.Combine(assemblyDir, "GodotPlugins.dll")))
            setenv("GODOTSHARP_DIR", assemblyDir, 1);

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
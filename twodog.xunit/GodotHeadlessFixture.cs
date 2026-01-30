using System.Reflection;
using System.Runtime.InteropServices;
using Godot;
using JetBrains.Annotations;
using Environment = System.Environment;

namespace twodog.tests;

[UsedImplicitly]
public class GodotHeadlessFixture : IDisposable
{
    // .NET's Environment.SetEnvironmentVariable does not propagate to native getenv()
    // on Linux/.NET 8+. We must call setenv directly for Godot's native code to see it.
    [DllImport("libc", SetLastError = true)]
    private static extern int setenv(string name, string value, int overwrite);

    public GodotHeadlessFixture()
    {
        Console.WriteLine("Initializing Godot...");
        Console.WriteLine("cwd: " + Environment.CurrentDirectory);

        // Resolve the project path relative to the assembly location
        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var projectPath = Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "..", "..", "game"));

        // Set GODOTSHARP_DIR so Godot finds GodotPlugins.dll in the output directory.
        // When running via dotnet test, the host process is /usr/share/dotnet/dotnet,
        // so Godot's exe_dir fallback resolves to the wrong directory.
        // Must use native setenv because .NET's SetEnvironmentVariable doesn't propagate
        // to native getenv() on Linux/.NET 8+.
        if (File.Exists(Path.Combine(assemblyDir, "GodotPlugins.dll")))
            setenv("GODOTSHARP_DIR", assemblyDir, 1);

        Engine = new Engine("twodog.tests", projectPath, "--headless");
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
using System.Reflection;
using System.Runtime.InteropServices;
using Godot;
using JetBrains.Annotations;

namespace twodog.xunit;

[UsedImplicitly]
public class GodotFixture : IDisposable
{
    // .NET's Environment.SetEnvironmentVariable does not propagate to native getenv()
    // on Linux/.NET 8+. We must call setenv directly for Godot's native code to see it.
    // On Windows, Environment.SetEnvironmentVariable works fine.
    [DllImport("libc", SetLastError = true)]
    private static extern int setenv(string name, string value, int overwrite);

    public GodotFixture()
    {
        Console.WriteLine("Initializing Godot...");

        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var projectPath = Engine.ResolveProjectDir();

        // Set GODOTSHARP_DIR so Godot finds GodotPlugins.dll in the output directory.
        // On Linux/.NET 8+, must use native setenv() because .NET's SetEnvironmentVariable
        // doesn't propagate to native getenv(). On Windows, the .NET API works fine.
        if (File.Exists(Path.Combine(assemblyDir, "GodotPlugins.dll")))
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                System.Environment.SetEnvironmentVariable("GODOTSHARP_DIR", assemblyDir);
            else
                setenv("GODOTSHARP_DIR", assemblyDir, 1);
        }

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
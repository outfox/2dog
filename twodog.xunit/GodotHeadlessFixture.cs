using System.Reflection;
using System.Runtime.InteropServices;
using Godot;
using JetBrains.Annotations;
using Environment = System.Environment;

namespace twodog.xunit;

[UsedImplicitly]
public class GodotHeadlessFixture : IDisposable
{
    // .NET's Environment.SetEnvironmentVariable does not propagate to native getenv()
    // on Linux/.NET 8+. We must call setenv directly for Godot's native code to see it.
    // On Windows, Environment.SetEnvironmentVariable works fine.
    [DllImport("libc", SetLastError = true)]
    private static extern int setenv(string name, string value, int overwrite);

    public GodotHeadlessFixture()
    {
        Console.WriteLine("Initializing Godot...");
        Console.WriteLine("cwd: " + Environment.CurrentDirectory);

        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var projectPath = Engine.ResolveProjectDir();

        // Pre-load game assemblies into Default context BEFORE starting Godot.
        // This prevents type identity issues where Godot loads assemblies into
        // PluginLoadContext while test code expects them in Default context.
        AssemblyPreloader.PreloadGameAssemblies(projectPath);

        // Set GODOTSHARP_DIR so Godot finds GodotPlugins.dll in the output directory.
        // When running via dotnet test, the host process is /usr/share/dotnet/dotnet,
        // so Godot's exe_dir fallback resolves to the wrong directory.
        // On Linux/.NET 8+, must use native setenv() because .NET's SetEnvironmentVariable
        // doesn't propagate to native getenv(). On Windows, the .NET API works fine.
        if (File.Exists(Path.Combine(assemblyDir, "GodotPlugins.dll")))
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Environment.SetEnvironmentVariable("GODOTSHARP_DIR", assemblyDir);
            else
                setenv("GODOTSHARP_DIR", assemblyDir, 1);
        }

        Console.WriteLine("Godot project: " + projectPath);
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
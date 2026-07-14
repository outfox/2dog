using Godot;
using System;

namespace twodog.fixture;

public abstract class GodotFixtureBase : IDisposable
{
    protected GodotFixtureBase(params string[] cmdLineArgs)
    {
        Console.WriteLine("Initializing Godot...");
        Console.WriteLine("cwd: " + System.Environment.CurrentDirectory);

        var projectPath = Engine.ResolveProjectDir();

        // Pre-load game assemblies into Default context BEFORE starting Godot.
        // This prevents type identity issues where Godot loads assemblies into
        // PluginLoadContext while test code expects them in Default context.
        AssemblyPreloader.PreloadGameAssemblies(projectPath);

        // GODOTSHARP_DIR (needed because dotnet test's host process is not in
        // the output directory) is set by Engine.Start() for both the flat and
        // the nested editor GodotPlugins layouts.

        Console.WriteLine("Godot project: " + projectPath);
        Engine = new Engine("twodog.tests", projectPath, cmdLineArgs);
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

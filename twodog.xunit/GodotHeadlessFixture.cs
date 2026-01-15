using System.Reflection;
using Godot;
using JetBrains.Annotations;
using Environment = System.Environment;

namespace twodog.tests;

[UsedImplicitly]
public class GodotHeadlessFixture : IDisposable
{
    public GodotHeadlessFixture()
    {
        Console.WriteLine("Initializing Godot...");
        Console.WriteLine("cwd: " + Environment.CurrentDirectory);

        // Resolve the project path relative to the assembly location
        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var projectPath = Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "..", "..", "project"));

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
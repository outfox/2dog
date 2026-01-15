using System.Reflection;
using Godot;
using JetBrains.Annotations;

namespace twodog.tests;

[UsedImplicitly]
public class GodotFixture : IDisposable
{
    public GodotFixture()
    {
        Console.WriteLine("Initializing Godot...");

        // Resolve the project path relative to the assembly location
        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var projectPath = Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "..", "..", "project"));

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
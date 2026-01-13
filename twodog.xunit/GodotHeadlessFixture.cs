using System.Reflection;
using Godot;
using JetBrains.Annotations;
using Environment = System.Environment;

namespace twodog.tests;

[UsedImplicitly]
public class GodotHeadlessFixture : IDisposable
{
    private readonly Engine _engine;
    private readonly GodotInstance _godotInstance;
    public Engine Engine => _engine;
    public GodotInstance GodotInstance => _godotInstance;
    public SceneTree Tree => _engine.Tree;

    public GodotHeadlessFixture()
    {
        Console.WriteLine("Initializing Godot...");
        Console.WriteLine("cwd: " + Environment.CurrentDirectory);
        
        // Resolve the project path relative to the assembly location
        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var projectPath = Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "..", "..", "project"));
        
        _engine = new Engine("twodog.tests", projectPath, "--headless");
        _godotInstance = _engine.Start();
        Console.WriteLine("Godot initialized successfully.");
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        Console.WriteLine("Shutting down Godot...");
        _godotInstance.Dispose();
        _engine.Dispose();
        Console.WriteLine("Godot shut down successfully.");
    }
}
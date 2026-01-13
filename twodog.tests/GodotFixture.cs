using Godot;
using JetBrains.Annotations;

namespace twodog.tests;

[UsedImplicitly]
public class GodotFixture : IDisposable
{
    private readonly Engine _engine;
    private readonly GodotInstance _godotInstance;
    public Engine Engine => _engine;
    public GodotInstance GodotInstance => _godotInstance;
    public SceneTree Tree => _engine.Tree;

    public GodotFixture()
    {
        Console.WriteLine("Initializing Godot...");
        _engine = new Engine("twodog.tests", @"P:\2dog\project");
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
using Godot;
using Engine = twodog.Engine;
using GodotInstance = twodog.GodotInstance;

namespace twodog.Hosting.Runtime;

/// <summary>
/// A unit of engine work executed by <see cref="ResidentProgram"/> on the
/// instance's engine thread, between main-loop iterations. Implementations live
/// in the app/test assembly (loaded into the same instance ALC) and must have a
/// parameterless constructor. Return a CoreLib string - it is the only thing
/// that crosses back to the host.
/// </summary>
public interface IEngineScenario
{
    string Run(EngineSession session, string? argument);
}

/// <summary>Godot-typed surface handed to scenarios: the running engine and its tree.</summary>
public sealed class EngineSession(Engine engine, GodotInstance godot)
{
    public Engine Engine { get; } = engine;
    public GodotInstance Godot { get; } = godot;
    public SceneTree Tree => Engine.Tree;

    /// <summary>Advances the engine up to <paramref name="count"/> frames (stops early on quit).</summary>
    public void PumpFrames(int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (Godot.Iteration()) break;
        }
    }
}

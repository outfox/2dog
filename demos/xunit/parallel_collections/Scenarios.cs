using System.Runtime.Loader;
using Godot;
using twodog.Hosting.Runtime;

namespace ParallelCollectionsDemo;

// Scenarios execute inside their collection's engine instance - its own load
// context, on its engine thread. Godot types never leave the instance; only the
// returned report string crosses back to the test, which asserts on it.

public sealed class SpawnNodeScenario : IEngineScenario
{
    public string Run(EngineSession session, string? argument)
    {
        var root = session.Tree.Root ?? throw new InvalidOperationException("no root");
        var before = root.GetChildCount();
        var node = new Node { Name = $"spawned_by_{argument}" };
        root.AddChild(node);
        session.PumpFrames(1);
        var report = $"delta={root.GetChildCount() - before};name={node.Name}";
        node.Free();
        return report;
    }
}

/// <summary>The isolation payoff: this instance's ProjectSettings reflect its own
/// project, and its GodotSharp lives in a non-default load context.</summary>
public sealed class WhoAmIScenario : IEngineScenario
{
    public string Run(EngineSession session, string? argument)
    {
        var alc = AssemblyLoadContext.GetLoadContext(typeof(GodotObject).Assembly);
        var name = ProjectSettings.GetSetting("application/config/name").AsString();
        return $"project={name};alcIsDefault={alc == AssemblyLoadContext.Default}";
    }
}

public sealed class NativePathScenario : IEngineScenario
{
    public string Run(EngineSession session, string? argument) => twodog.Engine.LoadedNativePath ?? "null";
}

/// <summary>Reads the main scene booted from the copied source project.</summary>
public sealed class ReadGreetingScenario : IEngineScenario
{
    public string Run(EngineSession session, string? argument)
    {
        var scene = session.Tree.CurrentScene ?? throw new InvalidOperationException("no current scene");
        var label = scene.GetNode<Label>("Greeting");
        return $"scene={scene.Name};greeting={label.Text}";
    }
}

using Godot;
using twodog.Hosting.Runtime;

namespace twodog.Hosting.Tests;

// Scenarios execute inside the collection's instance ALC on its engine thread;
// only the returned report string crosses back to the test.

public sealed class AddNodeScenario : IEngineScenario
{
    public string Run(EngineSession session, string? argument)
    {
        var root = session.Tree.Root ?? throw new InvalidOperationException("no root");
        var before = root.GetChildCount();
        var node = new Node { Name = $"n_{argument}" };
        root.AddChild(node);
        var delta = root.GetChildCount() - before;
        var name = $"{node.Name}";
        node.Free();
        return $"delta={delta};name={name}";
    }
}

public sealed class ChurnScenario : IEngineScenario
{
    public string Run(EngineSession session, string? argument)
    {
        var frames = int.Parse(argument ?? "30");
        var root = session.Tree.Root ?? throw new InvalidOperationException("no root");
        var before = root.GetChildCount();
        for (var frame = 0; frame < frames; frame++)
        {
            var nodes = new List<Node>();
            for (var i = 0; i < 50; i++)
            {
                var node = new Node();
                root.AddChild(node);
                nodes.Add(node);
            }
            foreach (var node in nodes) node.Free();
            session.PumpFrames(1);
        }
        using var rc = new RefCounted();
        return $"frames={frames};rc={rc.GetReferenceCount()};childDelta={root.GetChildCount() - before}";
    }
}

public sealed class NativePathScenario : IEngineScenario
{
    public string Run(EngineSession session, string? argument) => twodog.Engine.LoadedNativePath ?? "null";
}

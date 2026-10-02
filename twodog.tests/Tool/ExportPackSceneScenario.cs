using Godot;
using twodog.Hosting.Runtime;
using twodog.Hosting.Xunit;

namespace twodog.tests.ToolTests;

public sealed class ExportPackEngineFixture : EngineInstanceFixture
{
    protected override string Tag => "export-pack";
}

[CollectionDefinition(nameof(ExportPackCollection))]
public sealed class ExportPackCollection : ICollectionFixture<ExportPackEngineFixture>;

// Read the exported pack through Godot itself: serialized property names can exist even when their values
// were lost and saved as NIL. This runs in the fixture's isolated engine, separate from the export subprocess.
public sealed class ExportPackSceneScenario : IEngineScenario
{
    public string Run(EngineSession session, string? argument)
    {
        if (!ProjectSettings.LoadResourcePack(argument!))
            throw new InvalidOperationException("Could not load exported pack: " + argument);
        using var scene = ResourceLoader.Load<PackedScene>("res://main.tscn", cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
        using var state = scene.GetState();
        using var source = ReadProperty(state, "Receiver", "Source");
        using var interval = ReadProperty(state, "Emitter", "Interval");
        return $"connections={state.GetConnectionCount()};signal={state.GetConnectionSignal(0)};" +
            $"from={state.GetConnectionSource(0)};to={state.GetConnectionTarget(0)};method={state.GetConnectionMethod(0)};" +
            $"Source={DescribeValue(source)};Interval={DescribeValue(interval)}";
    }

    // Variant.ToString() renders NIL as an empty string; name it so a lost value is obvious in the failure.
    private static string DescribeValue(Variant value) =>
        value.VariantType == Variant.Type.Nil ? "Nil" : value.ToString();

    private static Variant ReadProperty(SceneState state, string nodeName, string propertyName)
    {
        for (var node = 0; node < state.GetNodeCount(); node++)
        {
            if (state.GetNodeName(node).ToString() != nodeName) continue;
            for (var property = 0; property < state.GetNodePropertyCount(node); property++)
                if (state.GetNodePropertyName(node, property).ToString() == propertyName)
                    return state.GetNodePropertyValue(node, property);
        }
        throw new InvalidOperationException($"Exported scene is missing {nodeName}.{propertyName}");
    }
}

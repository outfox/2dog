using Godot;
using showcase;
using twodog.Testing;
using twodog.Testing.Xunit;

namespace twodog.tests.EngineTests;

[Collection<HeadlessCollection>]
public class GodotApiCoverageTests(HeadlessFixture godot)
{
    [Fact]
    public void CoreTypesAndNativeHelpers_RoundTripAcrossInterop() =>
        GodotApiSmoke.CoreTypesAndNativeHelpers(godot.Tree);

    [Fact]
    public void ManagedCallables_PreserveInvocationAndSignalIdentity() =>
        GodotApiSmoke.ManagedCallables();

    [Fact]
    public void ErrorReportingAndTypedCollections_ReachTheirNativeShapes()
    {
        GodotApiSmoke.ErrorReportingAndTypedCollections();
        var diagnostics = godot.Errors.Expect("2dog smoke: expected");
        Assert.Collection(diagnostics,
            warning => Assert.Equal(GodotErrorType.Warning, warning.Type),
            error => Assert.Equal(GodotErrorType.Error, error.Type));
    }

    [Fact]
    public void ImagesAndResources_ExerciseCodecsAndImporters() =>
        GodotApiSmoke.ImagesAndResources();

    [Fact]
    public void EngineSingletons_AreCallable() =>
        GodotApiSmoke.EngineSingletons();

    [Fact]
    public void LowLevelServers_CreateAndFreeResources() =>
        GodotApiSmoke.LowLevelServers();

    [Fact]
    public void SceneAndGeneratedScript_InstantiateFromThePack() =>
        GodotApiSmoke.SceneAndGeneratedScript();

    [Fact]
    public void GDScriptOnlyEngineFeatures_RunWithoutManagedReferences() =>
        WithMainScene(GodotApiSmoke.GDScriptOnlyEngineFeatures);

    [Fact]
    public void GDExtensionProbe_LoadsAndAnswersEveryCallPath() =>
        WithMainScene(GodotApiSmoke.GDExtensionProbe);

    [Fact]
    public void SignalTable_EverySourceReachesCSharpAndGDScript() =>
        WithMainScene(GodotApiSmoke.SignalTable);

    private void WithMainScene(Action<SceneTree> test)
    {
        using var scene = GD.Load<PackedScene>("res://main.tscn");
        var instance = scene.Instantiate();
        try
        {
            godot.Tree.Root.AddChild(instance);
            godot.Tree.CurrentScene = instance;
            test(godot.Tree);
        }
        finally { instance.Free(); }
    }
}

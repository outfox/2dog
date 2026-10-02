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
        GodotApiSmoke.GDScriptOnlyEngineFeatures(godot.Tree);

    [Fact]
    public void GDExtensionProbe_LoadsAndAnswersEveryCallPath() =>
        GodotApiSmoke.GDExtensionProbe(godot.Tree);

    [Fact]
    public void SignalTable_EverySourceReachesCSharpAndGDScript() =>
        GodotApiSmoke.SignalTable(godot.Tree);
}

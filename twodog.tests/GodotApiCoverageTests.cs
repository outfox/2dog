using twodog.fixture;
using twodog.xunit;

namespace twodog.tests;

[Collection<GodotHeadlessCollection>]
public class GodotApiCoverageTests(GodotHeadlessFixture godot)
{
    [Fact]
    public void CoreTypesAndNativeHelpers_RoundTripAcrossInterop() =>
        GodotApiSmoke.CoreTypesAndNativeHelpers(godot.Tree);

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
}

#if EDITOR
using Godot;

namespace twodog.tests;

/// <summary>
/// Tests that verify the Editor configuration works as promised.
/// These tests should only pass when running with: dotnet test -c Editor
///
/// The Editor configuration provides:
/// 1. Compile-time access to editor-only types (EditorInterface, EditorPlugin, etc.)
/// 2. Ability to inherit from and use editor types in your code
/// 3. Access to editor APIs for tool/import workflows
///
/// Note: Editor types are available as managed C# types for compile-time use,
/// but their native constructors and ClassDB entries are NOT registered in
/// embedded libgodot mode (no full editor subsystem). Tests here verify
/// managed type availability, not native instantiation.
/// </summary>
[Collection("Godot")]
public class GodotEditorTests(GodotHeadlessFixture godot)
{
    // ── Type availability ────────────────────────────────────────────

    [Fact]
    public void EditorInterface_Type_IsAvailable()
    {
        var type = typeof(EditorInterface);

        Assert.NotNull(type);
        Assert.True(type.IsSubclassOf(typeof(Godot.GodotObject)));

        Assert.NotNull(type.GetMethod("GetEditorSettings"));
        Assert.NotNull(type.GetMethod("GetResourceFilesystem"));
        Assert.NotNull(type.GetMethod("GetResourcePreviewer"));
    }

    [Fact]
    public void EditorPlugin_Type_IsAvailable()
    {
        var type = typeof(EditorPlugin);
        Assert.NotNull(type);
        Assert.True(type.IsSubclassOf(typeof(Godot.Node)));
    }

    [Fact]
    public void EditorImportPlugin_Type_IsAvailable()
    {
        var type = typeof(EditorImportPlugin);

        Assert.NotNull(type);
        Assert.True(type.IsSubclassOf(typeof(Godot.ResourceImporter)));

        Assert.NotNull(type.GetMethod("_GetImporterName"));
        Assert.NotNull(type.GetMethod("_GetRecognizedExtensions"));
    }

    [Fact]
    public void EditorExportPlugin_Type_IsAvailable()
    {
        var type = typeof(EditorExportPlugin);
        Assert.NotNull(type);
        Assert.True(type.IsSubclassOf(typeof(Godot.RefCounted)));
    }

    [Fact]
    public void EditorSceneFormatImporter_Type_IsAvailable()
    {
        var type = typeof(EditorSceneFormatImporter);
        Assert.NotNull(type);
        Assert.True(type.IsSubclassOf(typeof(Godot.RefCounted)));
    }

    [Fact]
    public void ResourceImporter_Type_IsAvailable()
    {
        var type = typeof(ResourceImporter);
        Assert.NotNull(type);
        Assert.True(type.IsSubclassOf(typeof(Godot.RefCounted)));
    }

    [Fact]
    public void EditorFileSystem_Type_IsAvailable()
    {
        var type = typeof(EditorFileSystem);

        Assert.NotNull(type);
        Assert.True(type.IsSubclassOf(typeof(Godot.Node)));

        Assert.NotNull(type.GetMethod("GetFilesystem"));
        Assert.NotNull(type.GetMethod("GetFilesystemPath"));
        Assert.NotNull(type.GetMethod("Scan"));
    }

    [Fact]
    public void EditorSettings_Type_IsAvailable()
    {
        var type = typeof(EditorSettings);

        Assert.NotNull(type);
        Assert.True(type.IsSubclassOf(typeof(Godot.Resource)));

        Assert.NotNull(type.GetMethod("GetSetting"));
        Assert.NotNull(type.GetMethod("SetSetting"));
    }

    [Fact]
    public void ResourceImporterTexture_Type_IsAvailable()
    {
        var type = typeof(ResourceImporterTexture);
        Assert.NotNull(type);
        Assert.True(type.IsSubclassOf(typeof(ResourceImporter)));
    }

    // ── [Tool] script via scene ──────────────────────────────────────

    [Fact]
    public void ToolScene_CanLoad()
    {
        var scene = GD.Load<PackedScene>("res://tool_test.tscn");
        Assert.NotNull(scene);
    }

    [Fact]
    public void ToolNode_IsToolScript()
    {
        var scene = GD.Load<PackedScene>("res://tool_test.tscn");
        var node = scene.Instantiate();

        var script = node.GetScript().As<Script>();
        Assert.NotNull(script);
        Assert.True(script.IsTool());

        node.Free();
    }

    [Fact]
    public void ToolNode_ReadyCalled_WhenAddedToTree()
    {
        var scene = GD.Load<PackedScene>("res://tool_test.tscn");
        var node = scene.Instantiate();
        godot.Tree.Root.AddChild(node);

        // Godot C# source generators register exports with PascalCase names
        var readyCalled = (bool)node.Get("ReadyCalled");
        Assert.True(readyCalled);

        node.QueueFree();
        godot.GodotInstance.Iteration();
    }

    [Fact]
    public void ToolNode_ProcessCount_IncrementsOnIteration()
    {
        var scene = GD.Load<PackedScene>("res://tool_test.tscn");
        var node = scene.Instantiate();
        godot.Tree.Root.AddChild(node);

        for (var i = 0; i < 5; i++)
            godot.GodotInstance.Iteration();

        var count = (int)node.Get("ProcessCount");
        Assert.True(count >= 5);

        node.QueueFree();
        godot.GodotInstance.Iteration();
    }
}
#endif

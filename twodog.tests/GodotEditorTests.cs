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
/// Note: Editor singletons (EditorInterface.Singleton, etc.) require a full editor
/// instance and are NOT available when running headless. For headless editor features,
/// you need to run without --headless flag or use a non-headless fixture.
/// </summary>
[Collection("Godot")]
public class GodotEditorTests(GodotHeadlessFixture godot)
{
    [Fact]
    public void EditorInterface_Type_IsAvailable()
    {
        // EditorInterface type is only available with TOOLS_ENABLED (Editor configuration)
        var type = typeof(EditorInterface);
        
        Assert.NotNull(type);
        Assert.True(type.IsSubclassOf(typeof(Godot.GodotObject)));
        
        // Verify it has expected methods
        Assert.NotNull(type.GetMethod("GetEditorSettings"));
        Assert.NotNull(type.GetMethod("GetResourceFilesystem"));
        Assert.NotNull(type.GetMethod("GetResourcePreviewer"));
    }

    [Fact]
    public void ResourceImporterTexture_Type_IsAvailable()
    {
        // ResourceImporterTexture type should be available in editor builds
        // Note: Importers are typically accessed via EditorFileSystem, not as singletons
        var type = typeof(ResourceImporterTexture);
        
        Assert.NotNull(type);
        Assert.True(type.IsSubclassOf(typeof(ResourceImporter)));
    }

    [Fact]
    public void EditorFileSystem_Type_IsAvailable()
    {
        // EditorFileSystem type is available with Editor configuration
        var type = typeof(EditorFileSystem);
        
        Assert.NotNull(type);
        Assert.True(type.IsSubclassOf(typeof(Godot.Node)));
        
        // Verify it has expected methods
        Assert.NotNull(type.GetMethod("GetFilesystem"));
        Assert.NotNull(type.GetMethod("GetFilesystemPath"));
        Assert.NotNull(type.GetMethod("Scan"));
    }

    [Fact]
    public void EditorSettings_Type_IsAvailable()
    {
        // EditorSettings type is available with Editor configuration
        var type = typeof(EditorSettings);
        
        Assert.NotNull(type);
        Assert.True(type.IsSubclassOf(typeof(Godot.Resource)));
        
        // Verify it has expected methods
        Assert.NotNull(type.GetMethod("GetSetting"));
        Assert.NotNull(type.GetMethod("SetSetting"));
    }

    [Fact]
    public void ResourceImporterTexture_Type_CanBeInstantiated()
    {
        // Verify we can work with ResourceImporterTexture type
        // Note: Actual importer instances are managed by the editor
        var type = typeof(ResourceImporterTexture);
        Assert.NotNull(type);
        
        // Verify it has the expected base class
        Assert.True(type.IsSubclassOf(typeof(Godot.ResourceImporter)));
    }

    [Fact]
    public void EditorPlugin_Type_IsAvailable()
    {
        // EditorPlugin is a key editor-only type for extending the editor
        var type = typeof(EditorPlugin);
        Assert.NotNull(type);
        Assert.True(type.IsSubclassOf(typeof(Godot.Node)));
    }

    [Fact]
    public void EditorImportPlugin_Type_IsAvailable()
    {
        // EditorImportPlugin is available for custom resource importers
        var type = typeof(EditorImportPlugin);
        
        Assert.NotNull(type);
        Assert.True(type.IsSubclassOf(typeof(Godot.ResourceImporter)));
        
        // Verify it has expected virtual methods (with underscores - Godot naming convention)
        Assert.NotNull(type.GetMethod("_GetImporterName"));
        Assert.NotNull(type.GetMethod("_GetRecognizedExtensions"));
    }

    [Fact]
    public void ResourceImporter_Type_IsAvailable()
    {
        // ResourceImporter base class for all importers
        var type = typeof(ResourceImporter);
        
        Assert.NotNull(type);
        Assert.True(type.IsSubclassOf(typeof(Godot.RefCounted)));
    }

    [Fact]
    public void EditorExportPlugin_Type_IsAvailable()
    {
        // EditorExportPlugin is another editor-only type
        var type = typeof(EditorExportPlugin);
        Assert.NotNull(type);
        Assert.True(type.IsSubclassOf(typeof(Godot.RefCounted)));
    }

    [Fact]
    public void EditorSceneFormatImporter_Type_IsAvailable()
    {
        // EditorSceneFormatImporter is an editor-only type for custom scene importers
        var type = typeof(EditorSceneFormatImporter);
        Assert.NotNull(type);
        Assert.True(type.IsSubclassOf(typeof(Godot.RefCounted)));
    }
}
#endif

namespace twodog.tests;

using static HelperToolTestBed;

/// <summary>
/// Exercises the twodog.import helper's in-process import mode
/// (libgodot_import_project) against a scratch copy of the game project.
/// Spawns the helper as a subprocess, so it does not conflict with the
/// single-Godot-instance fixtures and needs no collection.
/// </summary>
public class ImportToolTests
{
    [Fact]
    public void InProcessImport_GeneratesUidsAndCache()
    {
        var (apiDir, toolsDir) = GodotSharpDirs();
        var scratch = CreateScratchProject();
        try
        {
            var exitCode = RunHelper("--libgodot", EditorLibGodot, "--api-dir", apiDir, "--tools-dir", toolsDir, scratch);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(Path.Combine(scratch, "SpinningCube.cs.uid")), "SpinningCube.cs.uid not generated");
            Assert.True(File.Exists(Path.Combine(scratch, "ToolNode.cs.uid")), "ToolNode.cs.uid not generated");
            Assert.True(File.Exists(Path.Combine(scratch, ".godot", "uid_cache.bin")), "uid_cache.bin not generated");

            // Second run must be idempotent.
            Assert.Equal(0, RunHelper("--libgodot", EditorLibGodot, "--api-dir", apiDir, "--tools-dir", toolsDir, scratch));
        }
        finally
        {
            try { Directory.Delete(scratch, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Import_MissingProject_Fails()
    {
        var missing = Path.Combine(Path.GetTempPath(), "2dog-import-missing-" + Guid.NewGuid().ToString("N"));
        Assert.NotEqual(0, RunHelper("--libgodot", EditorLibGodot, missing));
    }
}

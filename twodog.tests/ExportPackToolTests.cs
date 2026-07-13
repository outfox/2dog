namespace twodog.tests;

using static HelperToolTestBed;

/// <summary>
/// Exercises the twodog.import helper's export-pack mode
/// (libgodot_export_pack) against a scratch copy of the game project - the
/// desktop half of the web content pipeline (TwoDogExportGamePack runs this
/// during a browser-wasm publish). Spawns the helper as a subprocess, so it
/// does not conflict with the single-Godot-instance fixtures.
/// </summary>
public class ExportPackToolTests
{
    [Fact]
    public void InProcessExportPack_ProducesPck()
    {
        var (apiDir, toolsDir) = GodotSharpDirs();
        var scratch = CreateScratchProject();
        try
        {
            // Fresh scratch: --export-pack imports the project first
            // (wait_for_import), so this also covers the unimported case.
            var pck = Path.Combine(scratch, "out", "game.pck");
            var exitCode = RunHelper(
                "--export-pack", "Web", "--output", pck,
                "--libgodot", EditorLibGodot, "--api-dir", apiDir, "--tools-dir", toolsDir, scratch);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(pck), "game.pck not produced");

            // Godot pack magic + sanity size (must contain the game's scenes
            // and imported resources, not just a header).
            using var stream = File.OpenRead(pck);
            var magic = new byte[4];
            stream.ReadExactly(magic);
            Assert.Equal("GDPC"u8.ToArray(), magic);
            Assert.True(stream.Length > 4096, $"pck suspiciously small: {stream.Length} bytes");
        }
        finally
        {
            try { Directory.Delete(scratch, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void ExportPack_WithoutOutput_Fails()
    {
        var scratch = CreateScratchProject();
        try
        {
            // --export-pack requires --output; must fail with usage, not export.
            Assert.NotEqual(0, RunHelper("--export-pack", "Web", "--libgodot", EditorLibGodot, scratch));
        }
        finally
        {
            try { Directory.Delete(scratch, recursive: true); } catch { /* best effort */ }
        }
    }
}

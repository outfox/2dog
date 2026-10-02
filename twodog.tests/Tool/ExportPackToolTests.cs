namespace twodog.tests.ToolTests;

using System.Diagnostics;
using System.Security;
using System.Text;
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
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExportPack_RetainsCSharpSceneConnections_AndRepairsOldCache(bool web)
    {
        var (apiDir, toolsDir) = GodotSharpDirs();
        using var scratch = new TempProjectDir();
        scratch.Write("project.godot", """
            config_version=5
            [application]
            config/name="showcase"
            run/main_scene="res://main.tscn"
            [dotnet]
            project/assembly_name="showcase"
            """);
        scratch.Write("export_presets.cfg", """
            [preset.0]
            name="Web"
            platform="Web"
            runnable=true
            export_filter="all_resources"
            [preset.0.options]
            """);
        scratch.Write("main.tscn", """
            [gd_scene load_steps=3 format=3]
            [ext_resource type="Script" path="res://signals/CSharpTicker.cs" id="1"]
            [ext_resource type="Script" path="res://signals/SignalCounter.cs" id="2"]
            [node name="Main" type="Node"]
            [node name="Source" type="Node" parent="."]
            script = ExtResource("1")
            [node name="Receiver" type="Label" parent="."]
            script = ExtResource("2")
            [connection signal="Ticked" from="Source" to="Receiver" method="OnTicked"]
            """);
        foreach (var file in new[] { "CSharpTicker.cs", "SignalCounter.cs" })
            scratch.Write("signals/" + file, File.ReadAllText(Path.Combine(RepoRoot, "demos", "showcase", "signals", file)));

        // No Debug assembly, and no copy in the host's TargetDir: the exporter must use the resolved
        // game reference. This is the Release publish / early Blazor asset-discovery case.
        string Escape(string value) => SecurityElement.Escape(value)!;
        var assembly = typeof(showcase.CSharpTicker).Assembly.Location;
        var target = web ? "TwoDogExportGamePack" : "TwoDogExportDesktopPack";
        var pck = Path.Combine(scratch.Dir, web ? "obj/twodog-web/godot.pck" : "out/host.pck");
        var harness = $"""
            <Project>
              <PropertyGroup>
                <Configuration>Release</Configuration>
                <RuntimeIdentifier>{(web ? "browser-wasm" : "")}</RuntimeIdentifier>
                <GodotProjectDir>{Escape(scratch.Dir)}</GodotProjectDir>
                <TargetDir>{Escape(scratch.Dir)}/empty-host-output/</TargetDir>
                <TargetName>host</TargetName>
                <PublishDir>{Escape(scratch.Dir)}/out/</PublishDir>
                <BaseIntermediateOutputPath>obj/</BaseIntermediateOutputPath>
                <TwoDogExportPack>true</TwoDogExportPack>
                <TwoDogDesktopExportPreset>Web</TwoDogDesktopExportPreset>
                <TwoDogImportHelperPath>{Escape(HelperPath)}</TwoDogImportHelperPath>
                <TwoDogEditorLibGodotPath>{Escape(EditorLibGodot)}</TwoDogEditorLibGodotPath>
                <TwoDogGodotApiSourcePath>{Escape(apiDir)}</TwoDogGodotApiSourcePath>
                <TwoDogGodotToolsDir>{Escape(toolsDir)}</TwoDogGodotToolsDir>
                <TwoDogWebSideModuleExports>false</TwoDogWebSideModuleExports>
              </PropertyGroup>
              <Import Project="{Escape(Path.Combine(RepoRoot, "twodog.engine/build/2dog.engine.targets"))}" />
              <Import Project="{Escape(Path.Combine(RepoRoot, "platforms/twodog.browser-wasm/build/2dog.browser-wasm.targets"))}" />
              <Target Name="ResolveReferences">
                <ItemGroup>
                  <_ResolvedProjectReferencePaths Include="{Escape(assembly)}">
                    <MSBuildSourceProjectFile>{Escape(scratch.Dir)}/showcase.csproj</MSBuildSourceProjectFile>
                  </_ResolvedProjectReferencePaths>
                </ItemGroup>
              </Target>
              BASELINE_OVERRIDE
            </Project>
            """;
        var project = scratch.Write("export.proj", harness.Replace("BASELINE_OVERRIDE",
            "<Target Name=\"TwoDogPrepareExport\" DependsOnTargets=\"TwoDogResolveContentCapability\" />"));
        RunExportTarget(project, target);
        // Binary scene strings include a NUL terminator; the C# source also shipped in this minimal pack
        // contains the method name as plain text and must not satisfy the connection check.
        Assert.DoesNotContain("OnTicked\0", Encoding.UTF8.GetString(File.ReadAllBytes(pck)), StringComparison.Ordinal);

        // Run the fixed targets against the SAME cache and pack. The newly supplied script metadata must
        // force a fresh scene conversion; merely setting GODOT_PROJECT_ASSEMBLY_DIR leaves the bad cache intact.
        File.WriteAllText(project, harness.Replace("BASELINE_OVERRIDE", ""));
        RunExportTarget(project, target);
        Assert.Contains("OnTicked\0", Encoding.UTF8.GetString(File.ReadAllBytes(pck)), StringComparison.Ordinal);

        var modified = File.GetLastWriteTimeUtc(pck);
        var cachedScene = Directory.GetFiles(Path.Combine(scratch.Dir, ".godot", "exported"), "*.scn", SearchOption.AllDirectories).Single();
        RunExportTarget(project, target);
        Assert.Equal(modified, File.GetLastWriteTimeUtc(pck));
        Assert.True(File.Exists(cachedScene), "An unchanged managed assembly must preserve the scene export cache.");
    }

    private static void RunExportTarget(string project, string target)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { "msbuild", project, "-target:" + target, "-verbosity:quiet" })
            start.ArgumentList.Add(argument);
        start.Environment.Remove("GODOT_PROJECT_ASSEMBLY_DIR");
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        var timedOut = !process.WaitForExit(TimeSpan.FromMinutes(3));
        if (timedOut)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }
        Task.WaitAll(stdout, stderr);
        var output = stdout.Result + stderr.Result;
        if (timedOut)
            Assert.Fail("Export target timed out" + Environment.NewLine + output);
        Assert.True(process.ExitCode == 0, output);
    }

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

namespace twodog.tests.ToolTests;

using System.Diagnostics;
using System.Security;
using System.Text;
using static HelperToolTestBed;

/// <summary>
/// Exercises the twodog.import helper's export-pack mode
/// (libgodot_export_pack) against a scratch copy of the game project - the
/// desktop half of the web content pipeline (TwoDogExportGamePack runs this
/// during a browser-wasm publish). Exports run in subprocesses; scene-state
/// verification runs in a separate isolated engine fixture.
/// </summary>
[Collection(nameof(ExportPackCollection))]
public class ExportPackToolTests(ExportPackEngineFixture fixture)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExportPack_RetainsCSharpConnectionsAndProperties_AndRepairsOldCache(bool web)
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
            [node name="Emitter" type="Node" parent="."]
            script = ExtResource("1")
            Interval = 3.25
            [node name="Receiver" type="Label" parent="."]
            script = ExtResource("2")
            Source = NodePath("../Emitter")
            [connection signal="Ticked" from="Emitter" to="Receiver" method="OnTicked"]
            """);
        foreach (var file in new[] { "CSharpTicker.cs", "SignalCounter.cs" })
            scratch.Write("signals/" + file, File.ReadAllText(Path.Combine(RepoRoot, "demos", "showcase", "signals", file)));

        // No Debug assembly, and no copy in the host's TargetDir: the exporter must use the resolved
        // game reference. This is the Release publish / early Blazor asset-discovery case.
        string Escape(string value) => SecurityElement.Escape(value)!;
        var assembly = typeof(showcase.CSharpTicker).Assembly.Location;
        var target = web ? "TwoDogExportGamePack" : "TwoDogExportDesktopPack";
        var pck = Path.Combine(scratch.Dir, web ? "obj/twodog-web/Release/release/godot.pck" : "out/host.pck");
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
        // Release assemblies also need exported-property defaults, or placeholders save [Export] values as NIL
        // under intact names. Only the actual scene state shows that; macOS cannot host the engine instance.
        if (EngineHost.IsSupported)
            Assert.Equal("connections=1;signal=Ticked;from=./Emitter;to=./Receiver;method=OnTicked;Source=../Emitter;Interval=3.25",
                fixture.Run<ExportPackSceneScenario>(pck));

        var modified = File.GetLastWriteTimeUtc(pck);
        var cachedScene = Directory.GetFiles(Path.Combine(scratch.Dir, ".godot", "exported"), "*.scn", SearchOption.AllDirectories).Single();
        RunExportTarget(project, target);
        Assert.Equal(modified, File.GetLastWriteTimeUtc(pck));
        Assert.True(File.Exists(cachedScene), "An unchanged managed assembly must preserve the scene export cache.");
    }

    private static void RunExportTarget(string project, string target)
    {
        var start = new ProcessStartInfo("dotnet");
        foreach (var argument in new[] { "msbuild", project, "-target:" + target, "-verbosity:quiet" })
            start.ArgumentList.Add(argument);
        start.Environment.Remove("GODOT_PROJECT_ASSEMBLY_DIR");
        var (exitCode, output) = RunProcess(start, "Export target");
        Assert.True(exitCode == 0, output);
    }

    [Fact]
    public void WebExportPack_SelectsMatchingGDExtensionAndIsolatesVariants()
    {
        var (apiDir, toolsDir) = GodotSharpDirs();
        using var scratch = new TempProjectDir();
        scratch.Write("project.godot", "config_version=5\n[application]\nconfig/name=\"VariantProbe\"\n");
        scratch.Write("export_presets.cfg", """
            [preset.0]
            name="Web"
            platform="Web"
            export_filter="all_resources"
            [preset.0.options]
            variant/extensions_support=true
            variant/thread_support=false
            """);
        var native = OperatingSystem.IsWindows() ? "twodog_probe.windows.x86_64.dll"
            : OperatingSystem.IsLinux() ? "libtwodog_probe.linux.x86_64.so" : "libtwodog_probe.macos.dylib";
        var source = Path.Combine(RepoRoot, "demos/showcase/gdextension/bin", native);
        Assert.SkipWhen(!File.Exists(source), "Build the showcase's native GDExtension probe first.");
        scratch.Write("bin/.gdignore", "");
        File.Copy(source, Path.Combine(scratch.Dir, "bin", native));
        // The host editor loads the native probe. The web payloads only need distinct bytes here:
        // the browser smoke test covers loading, and this test verifies export selection.
        scratch.Write("bin/debug.wasm", "debug side module");
        scratch.Write("bin/release.wasm", "release side module");
        scratch.Write("probe.gdextension", $$"""
            [configuration]
            entry_symbol="twodog_probe_init"
            compatibility_minimum="4.7"
            [libraries]
            windows.x86_64="bin/{{native}}"
            linux.x86_64="bin/{{native}}"
            macos="bin/{{native}}"
            web.debug.wasm32="bin/debug.wasm"
            web.release.wasm32="bin/release.wasm"
            """);
        string Escape(string value) => SecurityElement.Escape(value)!;
        var project = scratch.Write("export.proj", $"""
            <Project>
              <PropertyGroup>
                <RuntimeIdentifier>browser-wasm</RuntimeIdentifier>
                <GodotProjectDir>{Escape(scratch.Dir)}</GodotProjectDir>
                <BaseIntermediateOutputPath>obj/</BaseIntermediateOutputPath>
                <TwoDogExportPack>true</TwoDogExportPack>
                <TwoDogWebSideModuleExports>false</TwoDogWebSideModuleExports>
                <TwoDogImportHelperPath>{Escape(HelperPath)}</TwoDogImportHelperPath>
                <TwoDogEditorLibGodotPath>{Escape(EditorLibGodot)}</TwoDogEditorLibGodotPath>
                <TwoDogGodotApiSourcePath>{Escape(apiDir)}</TwoDogGodotApiSourcePath>
                <TwoDogGodotToolsDir>{Escape(toolsDir)}</TwoDogGodotToolsDir>
              </PropertyGroup>
              <Import Project="{Escape(Path.Combine(RepoRoot, "twodog.engine/build/2dog.engine.targets"))}" />
              <Import Project="{Escape(Path.Combine(RepoRoot, "platforms/twodog.browser-wasm/build/2dog.browser-wasm.targets"))}" />
              <Target Name="TwoDogPrepareExport" DependsOnTargets="TwoDogResolveContentCapability" />
            </Project>
            """);
        // Same game inputs, changing configurations and explicit native overrides. Returning to
        // Release must use its own cached pack instead of the debug pack exported immediately before it.
        foreach (var (configuration, variant) in new[] {
            ("Release", "release"), ("Debug", "debug"), ("Release", "debug"),
            ("Debug", "release"), ("Release", "release") })
        {
            var start = new ProcessStartInfo("dotnet");
            foreach (var arg in new[] { "msbuild", project, "-t:TwoDogExportGamePack", "-v:quiet",
                "-p:Configuration=" + configuration, "-p:TwoDogWebVariant=" + variant })
                start.ArgumentList.Add(arg);
            start.Environment["GODOT_EDITOR"] = Path.Combine(scratch.Dir, "unrelated-editor.exe");
            start.Environment["GODOT4"] = Path.Combine(scratch.Dir, "unrelated-ide-editor.exe");
            var (exitCode, output) = RunProcess(start, "Variant export");
            Assert.True(exitCode == 0, output);
            var pck = Path.Combine(scratch.Dir, "obj/twodog-web", configuration, variant, "godot.pck");
            var entries = twodog.pck.GdpcPack.Read(pck).Entries.Select(entry => entry.Path).ToArray();
            Assert.Contains("bin/" + variant + ".wasm", entries);
            Assert.DoesNotContain("bin/" + (variant == "debug" ? "release" : "debug") + ".wasm", entries);
        }
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

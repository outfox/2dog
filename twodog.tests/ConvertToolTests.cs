using twodog.cli;

namespace twodog.tests;

// Coverage for the `2dog convert` tool (twodog/), which mutates users'
// Godot projects in place. Pure filesystem tests on temp directories - no
// Godot instance involved, so none of these join the "Godot" collection.
// The end-to-end tests additionally shell out to `dotnet sln`, exactly like
// a real conversion does.

/// <summary>A throwaway directory acting as a fake Godot project root.</summary>
internal sealed class TempProjectDir : IDisposable
{
    public string Dir { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "2dog-convert-test-" + Guid.NewGuid().ToString("N"));

    public TempProjectDir() => Directory.CreateDirectory(Dir);

    public string Write(string relativePath, string content)
    {
        var path = System.IO.Path.Combine(Dir, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        try { Directory.Delete(Dir, recursive: true); }
        catch { /* best effort; temp dir */ }
    }
}

public class GodotProjectFileTests
{
    private const string Sample =
        """
        ; Engine configuration file.
        config_version=5

        [application]

        config/name="Space Miner"
        config/features=PackedStringArray("4.7")

        [dotnet]

        project/assembly_name="SpaceMiner"
        """;

    [Fact]
    public void Get_ReadsQuotedValueInSection()
    {
        using var tmp = new TempProjectDir();
        var file = new GodotProjectFile(tmp.Write("project.godot", Sample));
        Assert.Equal("Space Miner", file.Get("application", "config/name"));
        Assert.Equal("SpaceMiner", file.Get("dotnet", "project/assembly_name"));
    }

    [Fact]
    public void Get_IsScopedToTheSection()
    {
        using var tmp = new TempProjectDir();
        var file = new GodotProjectFile(tmp.Write("project.godot", Sample));
        Assert.Null(file.Get("application", "project/assembly_name"));
        Assert.Null(file.Get("dotnet", "config/name"));
        Assert.Null(file.Get("application", "config/nonexistent"));
    }

    [Fact]
    public void HasSection_DetectsPresence()
    {
        using var tmp = new TempProjectDir();
        var file = new GodotProjectFile(tmp.Write("project.godot", Sample));
        Assert.True(file.HasSection("dotnet"));
        Assert.False(file.HasSection("rendering"));
    }

    [Fact]
    public void AppendDotnetSection_LeavesExistingBytesUntouched()
    {
        using var tmp = new TempProjectDir();
        const string original = "config_version=5\n\n[application]\n\nconfig/name=\"Game\"\n";
        var path = tmp.Write("project.godot", original);

        new GodotProjectFile(path).AppendDotnetSection("Game");

        var text = File.ReadAllText(path);
        Assert.StartsWith(original, text);
        Assert.Equal("Game", new GodotProjectFile(path).Get("dotnet", "project/assembly_name"));
    }

    [Fact]
    public void AppendDotnetSection_ThrowsWhenSectionExists()
    {
        using var tmp = new TempProjectDir();
        var file = new GodotProjectFile(tmp.Write("project.godot", Sample));
        Assert.Throws<InvalidOperationException>(() => file.AppendDotnetSection("Other"));
    }
}

public class DeriveBaseNameTests
{
    private static string Derive(TempProjectDir tmp, string projectGodot, string? nameOverride = null)
    {
        var path = tmp.Write("project.godot", projectGodot);
        var options = new ConvertOptions { NameOverride = nameOverride };
        return ConvertCommand.DeriveBaseName(options, tmp.Dir, new GodotProjectFile(path));
    }

    [Fact]
    public void AssemblyName_IsAuthoritative()
    {
        using var tmp = new TempProjectDir();
        tmp.Write("Foo.csproj", "<Project/>");
        var name = Derive(tmp, "[application]\nconfig/name=\"Something Else\"\n[dotnet]\nproject/assembly_name=\"Foo\"\n");
        Assert.Equal("Foo", name);
    }

    [Fact]
    public void AssemblyName_MismatchingCsproj_Throws()
    {
        using var tmp = new TempProjectDir();
        tmp.Write("Bar.csproj", "<Project/>");
        var ex = Assert.Throws<ConvertException>(() =>
            Derive(tmp, "[dotnet]\nproject/assembly_name=\"Foo\"\n"));
        Assert.Contains("Foo.csproj", ex.Message);
    }

    [Fact]
    public void SoleCsproj_NamesTheProject()
    {
        using var tmp = new TempProjectDir();
        tmp.Write("Bar.csproj", "<Project/>");
        Assert.Equal("Bar", Derive(tmp, "config_version=5\n"));
    }

    [Fact]
    public void MultipleCsprojs_WithoutAssemblyName_Throw()
    {
        using var tmp = new TempProjectDir();
        tmp.Write("Foo.csproj", "<Project/>");
        tmp.Write("Bar.csproj", "<Project/>");
        var ex = Assert.Throws<ConvertException>(() => Derive(tmp, "config_version=5\n"));
        Assert.Contains("--name", ex.Message);
    }

    [Fact]
    public void ConfigName_IsSanitizedToAValidStem()
    {
        using var tmp = new TempProjectDir();
        Assert.Equal("MyGame", Derive(tmp, "[application]\nconfig/name=\"My Game!\"\n"));
    }

    [Fact]
    public void DirectoryName_IsTheLastResort()
    {
        using var tmp = new TempProjectDir();
        var name = Derive(tmp, "config_version=5\n");
        Assert.Equal(System.IO.Path.GetFileName(tmp.Dir), name);
    }

    [Fact]
    public void NameOverride_Wins()
    {
        using var tmp = new TempProjectDir();
        Assert.Equal("My.Tool", Derive(tmp, "[application]\nconfig/name=\"Whatever\"\n", "My.Tool!"));
    }

    [Fact]
    public void NameOverride_ConflictingWithAssemblyName_Throws()
    {
        using var tmp = new TempProjectDir();
        Assert.Throws<ConvertException>(() =>
            Derive(tmp, "[dotnet]\nproject/assembly_name=\"Foo\"\n", "Bar"));
    }
}

public class CsprojPatcherTests
{
    private static readonly string[] HostFolders = ["MyGame.2dog", "MyGame.web", "MyGame.tests"];

    // Pinned to the SDK version the tool targets so the "older than" warning
    // doesn't fire (the fixture would go stale on every Godot version bump).
    private static readonly string Bare =
        $"""
        <Project Sdk="Godot.NET.Sdk/{ToolVersions.GodotSdkVersion}">
            <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
            </PropertyGroup>
        </Project>
        """;

    [Fact]
    public void AddsAllMissingProperties()
    {
        using var tmp = new TempProjectDir();
        var result = CsprojPatcher.Patch(tmp.Write("MyGame.csproj", Bare), HostFolders);

        Assert.NotNull(result.NewContent);
        Assert.Empty(result.Warnings);
        Assert.Equal(4, result.Added.Count);
        Assert.Contains("<EnableDynamicLoading>true</EnableDynamicLoading>", result.NewContent);
        Assert.Contains("<AllowUnsafeBlocks>true</AllowUnsafeBlocks>", result.NewContent);
        Assert.Contains("LIBGODOT_ENABLED", result.NewContent);
        Assert.Contains("MyGame.2dog/**;MyGame.web/**;MyGame.tests/**", result.NewContent);
        // The original content is preserved verbatim.
        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", result.NewContent);
    }

    [Fact]
    public void RePatch_IsIdempotent()
    {
        using var tmp = new TempProjectDir();
        var path = tmp.Write("MyGame.csproj", Bare);
        var first = CsprojPatcher.Patch(path, HostFolders);
        File.WriteAllText(path, first.NewContent!);

        var second = CsprojPatcher.Patch(path, HostFolders);
        Assert.Null(second.NewContent);
        Assert.Empty(second.Added);
    }

    [Fact]
    public void PropertyInConditionedGroup_CountsAsPresent()
    {
        using var tmp = new TempProjectDir();
        var path = tmp.Write("MyGame.csproj",
            """
            <Project Sdk="Godot.NET.Sdk/4.7.0">
                <PropertyGroup Condition="'$(Configuration)' == 'Debug'">
                    <EnableDynamicLoading>true</EnableDynamicLoading>
                </PropertyGroup>
            </Project>
            """);
        var result = CsprojPatcher.Patch(path, HostFolders);
        Assert.DoesNotContain("EnableDynamicLoading", result.Added);
    }

    [Fact]
    public void PartialDefaultItemExcludes_OnlyAddsMissingFolders()
    {
        using var tmp = new TempProjectDir();
        var path = tmp.Write("MyGame.csproj",
            """
            <Project Sdk="Godot.NET.Sdk/4.7.0">
                <PropertyGroup>
                    <DefaultItemExcludes>$(DefaultItemExcludes);MyGame.2dog/**</DefaultItemExcludes>
                </PropertyGroup>
            </Project>
            """);
        var result = CsprojPatcher.Patch(path, HostFolders);
        Assert.NotNull(result.NewContent);
        Assert.Contains(";MyGame.web/**;MyGame.tests/**", result.NewContent);
        Assert.DoesNotContain(";MyGame.2dog/**;MyGame.web/**", result.NewContent);
    }

    [Fact]
    public void NonGodotSdk_WarnsButStillPatches()
    {
        using var tmp = new TempProjectDir();
        var path = tmp.Write("MyGame.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\">\n</Project>");
        var result = CsprojPatcher.Patch(path, HostFolders);
        Assert.Contains(result.Warnings, w => w.Contains("Godot.NET.Sdk"));
        Assert.NotNull(result.NewContent);
    }

    [Fact]
    public void OlderGodotSdk_Warns()
    {
        using var tmp = new TempProjectDir();
        var path = tmp.Write("MyGame.csproj", "<Project Sdk=\"Godot.NET.Sdk/4.2.0\">\n</Project>");
        var result = CsprojPatcher.Patch(path, HostFolders);
        Assert.Contains(result.Warnings, w => w.Contains("older"));
    }

    [Fact]
    public void XmlDeclaration_IsPreserved()
    {
        using var tmp = new TempProjectDir();
        var path = tmp.Write("MyGame.csproj",
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" + Bare);
        var result = CsprojPatcher.Patch(path, HostFolders);
        Assert.NotNull(result.NewContent);
        Assert.StartsWith("<?xml", result.NewContent);
    }

    [Fact]
    public void ScaffoldedGodotCsproj_NeedsNoPatch()
    {
        // Invariant: the csproj the tool itself scaffolds (GDScript-only
        // conversions) must already contain everything the patcher would add.
        using var tmp = new TempProjectDir();
        var path = tmp.Write("MyGame.csproj", TemplateAssets.GodotCsproj("MyGame"));
        var result = CsprojPatcher.Patch(path, HostFolders);
        Assert.Null(result.NewContent);
        Assert.Empty(result.Warnings);
    }
}

public class SolutionOpsTests
{
    // Classic sln skeleton with one project entry; the separator in the
    // project path is parameterized because solution tooling has differed
    // across SDK versions and platforms.
    private static string WebSln(string sep) =>
        $$"""
        Microsoft Visual Studio Solution File, Format Version 12.00
        # Visual Studio Version 17
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "MyGame.web", "MyGame.web{{sep}}MyGame.web.csproj", "{11111111-2222-3333-4444-555555555555}"
        EndProject
        Global
        	GlobalSection(ProjectConfigurationPlatforms) = postSolution
        		{11111111-2222-3333-4444-555555555555}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
        		{11111111-2222-3333-4444-555555555555}.Debug|Any CPU.Build.0 = Debug|Any CPU
        		{11111111-2222-3333-4444-555555555555}.Release|Any CPU.ActiveCfg = Release|Any CPU
        		{11111111-2222-3333-4444-555555555555}.Release|Any CPU.Build.0 = Release|Any CPU
        	EndGlobalSection
        EndGlobal
        """;

    [Theory]
    [InlineData("\\")]
    [InlineData("/")]
    public void ExcludeFromSolutionBuild_StripsBuildLines_KeepsActiveCfg(string sep)
    {
        using var tmp = new TempProjectDir();
        var sln = tmp.Write("MyGame.sln", WebSln(sep));
        const string webRelative = "MyGame.web/MyGame.web.csproj";

        Assert.True(SolutionOps.HasSolutionBuildEntries(sln, webRelative));
        Assert.True(SolutionOps.ExcludeFromSolutionBuild(sln, webRelative));

        var text = File.ReadAllText(sln);
        Assert.DoesNotContain(".Build.0", text);
        Assert.Contains(".Debug|Any CPU.ActiveCfg", text);
        Assert.Contains(".Release|Any CPU.ActiveCfg", text);

        // Nothing left to strip: both probes now report clean.
        Assert.False(SolutionOps.HasSolutionBuildEntries(sln, webRelative));
        Assert.False(SolutionOps.ExcludeFromSolutionBuild(sln, webRelative));
    }

    [Fact]
    public void ExcludeFromSolutionBuild_UnknownProject_ReturnsFalse()
    {
        using var tmp = new TempProjectDir();
        var sln = tmp.Write("MyGame.sln", WebSln("\\"));
        Assert.False(SolutionOps.ExcludeFromSolutionBuild(sln, "Other.web/Other.web.csproj"));
        Assert.Contains(".Build.0", File.ReadAllText(sln));
    }

    [Fact]
    public void ExcludeFromSolutionBuild_Slnx_AddsBuildFalseWithoutOtherChanges()
    {
        using var tmp = new TempProjectDir();
        var slnx = tmp.Write("MyGame.slnx",
            """
            <Solution>
              <Project Path="MyGame.web/MyGame.web.csproj" />
            </Solution>
            """);

        Assert.True(SolutionOps.ExcludeFromSolutionBuild(slnx, "MyGame.web/MyGame.web.csproj"));

        Assert.Equal(
            """
            <Solution>
              <Project Path="MyGame.web/MyGame.web.csproj">
                <Build Project="false" />
              </Project>
            </Solution>
            """,
            File.ReadAllText(slnx));
    }

    [Fact]
    public void MigrateToSlnx_ReplacesClassicSolution()
    {
        using var tmp = new TempProjectDir();
        var sln = tmp.Write("MyGame.sln", WebSln("\\"));

        SolutionOps.MigrateToSlnx(sln);

        var slnx = System.IO.Path.ChangeExtension(sln, ".slnx");
        Assert.False(File.Exists(sln));
        Assert.True(File.Exists(slnx));
        Assert.Contains("<Solution>", File.ReadAllText(slnx));
        Assert.True(SolutionOps.ContainsProject(slnx, "MyGame.web.csproj"));
    }

    [Fact]
    public void Locate_NoSolution_YieldsCreationPath()
    {
        using var tmp = new TempProjectDir();
        var (path, exists) = SolutionOps.Locate(tmp.Dir, "MyGame");
        Assert.False(exists);
        Assert.Equal(System.IO.Path.Combine(tmp.Dir, "MyGame.sln"), path);
    }

    [Fact]
    public void Locate_SingleSolution_IsReused()
    {
        using var tmp = new TempProjectDir();
        var sln = tmp.Write("Whatever.sln", "");
        var (path, exists) = SolutionOps.Locate(tmp.Dir, "MyGame");
        Assert.True(exists);
        Assert.Equal(sln, path);
    }

    [Fact]
    public void Locate_MultipleSolutions_PrefersTheOneContainingTheProject()
    {
        using var tmp = new TempProjectDir();
        tmp.Write("Other.sln", "");
        var containing = tmp.Write("Real.sln", "\"MyGame.csproj\"");
        var (path, exists) = SolutionOps.Locate(tmp.Dir, "MyGame");
        Assert.True(exists);
        Assert.Equal(containing, path);
    }

    [Fact]
    public void Locate_AmbiguousSolutions_Throw()
    {
        using var tmp = new TempProjectDir();
        tmp.Write("A.sln", "");
        tmp.Write("B.sln", "");
        Assert.Throws<ConvertException>(() => SolutionOps.Locate(tmp.Dir, "MyGame"));
    }
}

public class TemplateAssetsTests
{
    private static void AssertNoTokens(string text)
    {
        Assert.DoesNotContain("Company.Product1", text);
        Assert.DoesNotContain("TPLRAWNAME", text);
        Assert.DoesNotContain("PKG_VERSION", text);
        Assert.DoesNotContain("GODOT_SDK_VERSION", text);
    }

    [Theory]
    [InlineData("2dog")]
    [InlineData("web")]
    [InlineData("tests")]
    public void HostFiles_SubstituteTokensInPathsAndContent(string suffix)
    {
        var files = TemplateAssets.HostFiles(suffix, "MyGame").ToList();
        Assert.NotEmpty(files);
        Assert.Contains(files, f => f.RelativePath == $"MyGame.{suffix}/MyGame.{suffix}.csproj");
        Assert.Contains(files, f => f.RelativePath.EndsWith(".gdignore"));

        foreach (var (path, content) in files)
        {
            Assert.StartsWith($"MyGame.{suffix}/", path);
            AssertNoTokens(path);
            AssertNoTokens(content);
        }
    }

    [Fact]
    public void GodotCsproj_HasNoTokens_AndExcludesHostFolders()
    {
        var csproj = TemplateAssets.GodotCsproj("MyGame");
        AssertNoTokens(csproj);
        Assert.Contains("MyGame.2dog/**", csproj);
        Assert.Contains("MyGame.web/**", csproj);
        Assert.Contains("MyGame.tests/**", csproj);
    }

    [Fact]
    public void WebBootSource_IsVerbatimAndGuarded()
    {
        var source = TemplateAssets.WebBootSource();
        Assert.Contains("LIBGODOT_ENABLED", source);
    }
}

public class ExportPresetOpsTests
{
    private const string DesktopPreset =
        """
        [preset.0]

        name="Windows Desktop"
        platform="Windows Desktop"

        [preset.0.options]

        binary_format/architecture="x86_64"
        """;

    [Fact]
    public void HasPreset_MatchesExactName()
    {
        Assert.True(ExportPresetOps.HasPreset(DesktopPreset, "Windows Desktop"));
        Assert.False(ExportPresetOps.HasPreset(DesktopPreset, "Web"));
        Assert.False(ExportPresetOps.HasPreset(DesktopPreset, "Windows"));
    }

    [Fact]
    public void AppendText_RenumbersPastExistingPresets()
    {
        var text = ExportPresetOps.AppendText(DesktopPreset);
        Assert.Contains("[preset.1]", text);
        Assert.Contains("[preset.1.options]", text);
        Assert.DoesNotContain("[preset.0]", text);
        Assert.Contains("name=\"Web\"", text);
    }

    [Fact]
    public void AppendText_OnEmptyFile_StartsAtZero()
    {
        var text = ExportPresetOps.AppendText("");
        Assert.Contains("[preset.0]", text);
        Assert.Contains("[preset.0.options]", text);
    }
}

// Full conversions on a scratch GDScript-only project, including the
// `dotnet sln` subprocess steps (restore is skipped). These prove the two
// contractual behaviors: a fresh convert scaffolds the complete nested
// layout, and a re-run changes nothing.
public class ConvertEndToEndTests
{
    private const string GdScriptProject =
        """
        ; Engine configuration file.
        config_version=5

        [application]

        config/name="Space Miner"
        """;

    private static ConvertOptions Options(string dir, bool dryRun = false, bool web = true, bool tests = true) =>
        new() { ProjectPath = dir, DryRun = dryRun, IncludeWeb = web, IncludeTests = tests, Restore = false };

    private static string Snapshot(string dir) =>
        string.Join("\n---\n", Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(f => f + "\n" + File.ReadAllText(f)));

    [Fact]
    public void DryRun_ChangesNothing()
    {
        using var tmp = new TempProjectDir();
        tmp.Write("project.godot", GdScriptProject);
        var before = Snapshot(tmp.Dir);

        Assert.Equal(0, ConvertCommand.Run(Options(tmp.Dir, dryRun: true)));

        Assert.Equal(before, Snapshot(tmp.Dir));
        Assert.Single(Directory.EnumerateFileSystemEntries(tmp.Dir, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void Convert_GdScriptOnlyProject_ScaffoldsFullLayout_AndReRunIsNoOp()
    {
        using var tmp = new TempProjectDir();
        tmp.Write("project.godot", GdScriptProject);
        var originalProjectGodot = File.ReadAllText(System.IO.Path.Combine(tmp.Dir, "project.godot"));

        Assert.Equal(0, ConvertCommand.Run(Options(tmp.Dir)));

        // Nested-always layout: Godot csproj + sln at root, hosts nested
        // behind .gdignore.
        foreach (var expected in new[]
                 {
                    "SpaceMiner.csproj", "SpaceMiner.slnx", "Directory.Build.targets", "TwoDogWebBoot.cs", "export_presets.cfg", "global.json",
                     "SpaceMiner.2dog/SpaceMiner.2dog.csproj", "SpaceMiner.2dog/.gdignore",
                     "SpaceMiner.web/SpaceMiner.web.csproj", "SpaceMiner.web/.gdignore",
                     "SpaceMiner.tests/SpaceMiner.tests.csproj", "SpaceMiner.tests/.gdignore",
                 })
            Assert.True(File.Exists(System.IO.Path.Combine(tmp.Dir, expected)), $"missing {expected}");

        // project.godot: original bytes untouched, [dotnet] section appended.
        var projectGodot = File.ReadAllText(System.IO.Path.Combine(tmp.Dir, "project.godot"));
        Assert.StartsWith(originalProjectGodot, projectGodot);
        Assert.Contains("project/assembly_name=\"SpaceMiner\"", projectGodot);

        // No template tokens leak into any created file.
        foreach (var file in Directory.EnumerateFiles(tmp.Dir, "*", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("Company.Product1", text);
            Assert.DoesNotContain("TPLRAWNAME", text);
        }

        // The sln contains all four projects; the web host keeps ActiveCfg
        // entries but is excluded from plain solution builds (wasm-tools).
        var sln = System.IO.Path.Combine(tmp.Dir, "SpaceMiner.slnx");
        foreach (var project in new[]
                 {
                     "SpaceMiner.csproj", "SpaceMiner.2dog.csproj",
                     "SpaceMiner.web.csproj", "SpaceMiner.tests.csproj",
                 })
            Assert.True(SolutionOps.ContainsProject(sln, project), $"{project} missing from sln");
        Assert.Contains("<Build Project=\"false\" />", File.ReadAllText(sln));

        // Re-run: byte-identical no-op.
        var snapshot = Snapshot(tmp.Dir);
        Assert.Equal(0, ConvertCommand.Run(Options(tmp.Dir)));
        Assert.Equal(snapshot, Snapshot(tmp.Dir));
    }

    [Fact]
    public void Convert_ExistingExportPresets_AppendsWebPreset_WithoutRewriting()
    {
        using var tmp = new TempProjectDir();
        tmp.Write("project.godot", GdScriptProject);
        const string existing =
            """
            [preset.0]

            name="Windows Desktop"
            platform="Windows Desktop"

            [preset.0.options]

            binary_format/architecture="x86_64"
            """;
        tmp.Write("export_presets.cfg", existing);

        Assert.Equal(0, ConvertCommand.Run(Options(tmp.Dir)));

        var text = File.ReadAllText(System.IO.Path.Combine(tmp.Dir, "export_presets.cfg"));
        Assert.StartsWith(existing, text);
        Assert.Contains("[preset.1]", text);
        Assert.Contains("name=\"Web\"", text);

        // Re-run: the Web preset is detected, nothing appended twice.
        var snapshot = Snapshot(tmp.Dir);
        Assert.Equal(0, ConvertCommand.Run(Options(tmp.Dir)));
        Assert.Equal(snapshot, Snapshot(tmp.Dir));
    }

    [Fact]
    public void Convert_ExistingGlobalJson_IsNeverTouched()
    {
        using var tmp = new TempProjectDir();
        tmp.Write("project.godot", GdScriptProject);
        // latestMajor: the convert pipeline runs `dotnet sln` inside this
        // directory, so the pin must resolve with whatever SDK the CI runner
        // has (a bare version pin means latestPatch and only accepts the
        // 10.0.1xx band - broke on a runner with only 10.0.3xx).
        const string existing = """{ "sdk": { "version": "10.0.100", "rollForward": "latestMajor" } }""";
        tmp.Write("global.json", existing);

        // The root global.json is the user's SDK policy: --force overwrites
        // scaffolded files, but never this one.
        var options = Options(tmp.Dir);
        options.Force = true;
        Assert.Equal(0, ConvertCommand.Run(options));

        Assert.Equal(existing, File.ReadAllText(System.IO.Path.Combine(tmp.Dir, "global.json")));
    }

    [Fact]
    public void Convert_NoWebNoTests_PrunesScaffoldingAndExcludes()
    {
        using var tmp = new TempProjectDir();
        tmp.Write("project.godot", GdScriptProject);

        Assert.Equal(0, ConvertCommand.Run(Options(tmp.Dir, web: false, tests: false)));

        Assert.True(Directory.Exists(System.IO.Path.Combine(tmp.Dir, "SpaceMiner.2dog")));
        Assert.False(Directory.Exists(System.IO.Path.Combine(tmp.Dir, "SpaceMiner.web")));
        Assert.False(Directory.Exists(System.IO.Path.Combine(tmp.Dir, "SpaceMiner.tests")));
        // The root global.json (wasm SDK pin) only comes with the web host.
        Assert.False(File.Exists(System.IO.Path.Combine(tmp.Dir, "global.json")));

        var csproj = File.ReadAllText(System.IO.Path.Combine(tmp.Dir, "SpaceMiner.csproj"));
        Assert.Contains("SpaceMiner.2dog/**", csproj);
        Assert.DoesNotContain("SpaceMiner.web", csproj);
        Assert.DoesNotContain("SpaceMiner.tests", csproj);
    }
}

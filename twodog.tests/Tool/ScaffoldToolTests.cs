using System.Text.RegularExpressions;
using twodog.cli;

namespace twodog.tests.ToolTests;

// Coverage for the `2dog` tool (twodog/), which creates Godot projects and
// scaffolds host projects into existing ones. Pure filesystem tests on temp
// directories - no Godot instance involved, so none of these join the "Godot"
// collection. The end-to-end tests additionally shell out to `dotnet sln`,
// exactly like a real run does. The interactive layer (Tui) only gathers the
// values tested here, so it is deliberately not exercised.

/// <summary>Host kinds by their folder suffix, for [InlineData].</summary>
internal static class HostKinds
{
    public static HostKind Of(string suffix) => Hosts.All.Single(k => Hosts.Suffix(k) == suffix);
}

/// <summary>A throwaway directory acting as a fake Godot project root.</summary>
internal sealed class TempProjectDir : IDisposable
{
    public string Dir { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "2dog-add-test-" + Guid.NewGuid().ToString("N"));

    public TempProjectDir() => Directory.CreateDirectory(Dir);

    public string Write(string relativePath, string content)
    {
        var path = System.IO.Path.Combine(Dir, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public string Write(string relativePath, byte[] content)
    {
        var path = System.IO.Path.Combine(Dir, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
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
        var options = new ScaffoldOptions { NameOverride = nameOverride };
        return ScaffoldCommand.DeriveBaseName(options, tmp.Dir, new GodotProjectFile(path));
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
        var ex = Assert.Throws<ToolException>(() =>
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
        var ex = Assert.Throws<ToolException>(() => Derive(tmp, "config_version=5\n"));
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
        Assert.Throws<ToolException>(() =>
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
    public void UpgradesTargetFrameworkToNet10()
    {
        using var tmp = new TempProjectDir();
        var path = tmp.Write("MyGame.csproj", Bare.Replace("net10.0", "net8.0"));

        var first = CsprojPatcher.Patch(path, HostFolders);

        Assert.NotNull(first.NewContent);
        Assert.Contains("TargetFramework: net10.0", first.Added);
        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", first.NewContent);
        Assert.DoesNotContain("net8.0", first.NewContent);

        File.WriteAllText(path, first.NewContent);
        var second = CsprojPatcher.Patch(path, HostFolders);
        Assert.Null(second.NewContent);
    }

    [Fact]
    public void AddsTargetFrameworkWhenMissing()
    {
        using var tmp = new TempProjectDir();
        var path = tmp.Write("MyGame.csproj", $"<Project Sdk=\"Godot.NET.Sdk/{ToolVersions.GodotSdkVersion}\">\n</Project>");

        var result = CsprojPatcher.Patch(path, HostFolders);

        Assert.Contains("TargetFramework: net10.0", result.Added);
        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", result.NewContent);
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
        // conversions) must already contain everything the patcher would add -
        // including the guarded web bootstrap Compile Include.
        using var tmp = new TempProjectDir();
        var path = tmp.Write("MyGame.csproj", TemplateAssets.GodotCsproj("MyGame"));
        var result = CsprojPatcher.Patch(path, HostFolders, "MyGame.web/TwoDogWebBoot.cs");
        Assert.Null(result.NewContent);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void WebBootPath_AddsGuardedCompileInclude_Once()
    {
        using var tmp = new TempProjectDir();
        var path = tmp.Write("MyGame.csproj", Bare);

        var first = CsprojPatcher.Patch(path, HostFolders, "MyGame.web/TwoDogWebBoot.cs");
        Assert.NotNull(first.NewContent);
        Assert.Contains("Compile Include=\"MyGame.web/TwoDogWebBoot.cs\"", first.NewContent);
        Assert.Contains("Exists('$(MSBuildThisFileDirectory)MyGame.web/TwoDogWebBoot.cs')", first.NewContent);

        File.WriteAllText(path, first.NewContent!);
        var second = CsprojPatcher.Patch(path, HostFolders, "MyGame.web/TwoDogWebBoot.cs");
        Assert.Null(second.NewContent);
    }

    [Fact]
    public void WebBootPath_ReplacesAStaleGuardedInclude()
    {
        // A no-web scaffold leaves the template's Exists-guarded include
        // behind; when a web host lands in a DIFFERENT folder later, that
        // stale include must not count as wired (its guard is false and
        // nothing would compile the bootstrap).
        using var tmp = new TempProjectDir();
        var path = tmp.Write("MyGame.csproj",
            $"""
            <Project Sdk="Godot.NET.Sdk/{ToolVersions.GodotSdkVersion}">
                <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <EnableDynamicLoading>true</EnableDynamicLoading>
                    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
                    <DefineConstants>$(DefineConstants);LIBGODOT_ENABLED</DefineConstants>
                    <DefaultItemExcludes>$(DefaultItemExcludes);MyGame.2dog/**;MyGame.web/**;MyGame.tests/**</DefaultItemExcludes>
                </PropertyGroup>
                <ItemGroup>
                    <Compile Include="MyGame.web/TwoDogWebBoot.cs" Condition="Exists('MyGame.web/TwoDogWebBoot.cs')"/>
                </ItemGroup>
            </Project>
            """);
        var result = CsprojPatcher.Patch(path, HostFolders, "Browser/TwoDogWebBoot.cs");
        Assert.NotNull(result.NewContent);
        Assert.Contains("Compile Include=\"Browser/TwoDogWebBoot.cs\"", result.NewContent);
    }

    [Fact]
    public void WebBootPath_RespectsAnAuthorsExistingInclude()
    {
        // A Compile item pointing at a TwoDogWebBoot.cs that actually exists
        // counts as wired, wherever the author chose to keep the file.
        using var tmp = new TempProjectDir();
        tmp.Write("boot/TwoDogWebBoot.cs", "// author's custom layout\n");
        var path = tmp.Write("MyGame.csproj",
            $"""
            <Project Sdk="Godot.NET.Sdk/{ToolVersions.GodotSdkVersion}">
                <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <EnableDynamicLoading>true</EnableDynamicLoading>
                    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
                    <DefineConstants>$(DefineConstants);LIBGODOT_ENABLED</DefineConstants>
                    <DefaultItemExcludes>$(DefaultItemExcludes);MyGame.2dog/**;MyGame.web/**;MyGame.tests/**</DefaultItemExcludes>
                </PropertyGroup>
                <ItemGroup>
                    <Compile Include="boot/TwoDogWebBoot.cs"/>
                </ItemGroup>
            </Project>
            """);
        var result = CsprojPatcher.Patch(path, HostFolders, "MyGame.web/TwoDogWebBoot.cs");
        Assert.Null(result.NewContent);
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

        CliConsole.Capture(() =>
        {
            SolutionOps.MigrateToSlnx(sln);
            return 0;
        });

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
        Assert.Throws<ToolException>(() => SolutionOps.Locate(tmp.Dir, "MyGame"));
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

    // Theories take the folder suffix rather than the HostKind itself: xUnit
    // needs public test methods, and the tool's types are internal.
    [Theory]
    [InlineData("2dog")]
    [InlineData("web")]
    [InlineData("webxr")]
    [InlineData("tests")]
    [InlineData("winforms")]
    [InlineData("winui")]
    [InlineData("avalonia")]
    public void HostFiles_SubstituteTokensInPathsAndContent(string suffix)
    {
        var files = TemplateAssets.HostFiles(HostKinds.Of(suffix), "MyGame", $"MyGame.{suffix}").ToList();
        Assert.NotEmpty(files);
        Assert.Contains(files, f => f.RelativePath == $"MyGame.{suffix}/MyGame.{suffix}.csproj");
        Assert.Contains(files, f => f.RelativePath.EndsWith(".gdignore"));

        foreach (var file in files)
        {
            Assert.StartsWith($"MyGame.{suffix}/", file.RelativePath);
            AssertNoTokens(file.RelativePath);
            AssertNoTokens(file.Text);
        }
    }

    [Theory]
    [InlineData("2dog")]
    [InlineData("web")]
    [InlineData("webxr")]
    [InlineData("tests")]
    [InlineData("winforms")]
    [InlineData("winui")]
    [InlineData("avalonia")]
    public void HostFiles_CustomFolder_RenamesFolderAndProject(string suffix)
    {
        // A second host of the same kind lives in a freely named folder; the
        // csproj follows the folder, the game assembly reference does not.
        var kind = HostKinds.Of(suffix);
        var files = TemplateAssets.HostFiles(kind, "MyGame", "MyGame.extra").ToList();

        Assert.Contains(files, f => f.RelativePath == "MyGame.extra/MyGame.extra.csproj");
        foreach (var file in files)
        {
            Assert.StartsWith("MyGame.extra/", file.RelativePath);
            AssertNoTokens(file.RelativePath);
            AssertNoTokens(file.Text);
        }

        var csproj = files.Single(f => f.RelativePath.EndsWith(".csproj")).Text;
        Assert.Contains("../MyGame.csproj", csproj);
        Assert.DoesNotContain($"MyGame.{Hosts.Suffix(kind)}", csproj);
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

    [Fact]
    public void WebHost_IncludesReferencedFavicons()
    {
        foreach (var suffix in new[] { "web", "webxr" })
        {
            var files = TemplateAssets.HostFiles(HostKinds.Of(suffix), "MyGame", $"MyGame.{suffix}").ToList();
            var index = files.Single(f => f.RelativePath.EndsWith("wwwroot/index.html")).Text;

            foreach (var favicon in new[]
                     {
                         "favicon.ico", "favicon.svg", "favicon-16x16.png", "favicon-32x32.png", "favicon-96x96.png",
                     })
            {
                Assert.Single(files, f => f.RelativePath.EndsWith($"wwwroot/{favicon}"));
                Assert.Contains($"href=\"{favicon}\"", index);
            }
        }
    }

    [Fact]
    public void WebXrHost_ShipsAndWiresTheLayersPolyfill()
    {
        var files = TemplateAssets.HostFiles(HostKind.WebXr, "MyGame", "MyGame.webxr").ToList();
        var index = files.Single(f => f.RelativePath.EndsWith("wwwroot/index.html")).Text;

        Assert.Single(files, f => f.RelativePath.EndsWith("wwwroot/webxr-layers-polyfill.min.js"));
        Assert.Contains("src=\"webxr-layers-polyfill.min.js\"", index);
        Assert.Contains("new WebXRLayersPolyfill()", index);
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
        var text = ExportPresetOps.AppendText(DesktopPreset, ExportPresetOps.WebPresetName);
        Assert.Contains("[preset.1]", text);
        Assert.Contains("[preset.1.options]", text);
        Assert.DoesNotContain("[preset.0]", text);
        Assert.Contains("name=\"Web\"", text);
    }

    [Fact]
    public void AppendText_OnEmptyFile_StartsAtZero()
    {
        var text = ExportPresetOps.AppendText("", ExportPresetOps.WebPresetName);
        Assert.Contains("[preset.0]", text);
        Assert.Contains("[preset.0.options]", text);
    }

    [Theory]
    [InlineData("Windows Desktop")]
    [InlineData("Linux")]
    [InlineData("macOS")]
    public void AppendText_ExtractsSingleDesktopPreset(string presetName)
    {
        var text = ExportPresetOps.AppendText(DesktopPreset, presetName);
        Assert.Contains("[preset.1]", text);
        Assert.Contains("[preset.1.options]", text);
        Assert.True(ExportPresetOps.HasPreset(text, presetName));
        // Only the requested preset: never the whole template file.
        Assert.False(ExportPresetOps.HasPreset(text, ExportPresetOps.WebPresetName));
        Assert.DoesNotContain("[preset.2]", text);
    }
}

// Full `2dog add` runs on a scratch GDScript-only project, including the
// `dotnet sln` subprocess steps (restore is skipped). These prove the two
// contractual behaviors: a fresh add scaffolds the complete nested
// layout, and a re-run changes nothing.
public class AddEndToEndTests
{
    private const string GdScriptProject =
        """
        ; Engine configuration file.
        config_version=5

        [application]

        config/name="Space Miner"
        """;

    private static ScaffoldOptions Options(string dir, bool dryRun = false, bool web = true, bool tests = true)
    {
        var options = new ScaffoldOptions { ProjectPath = dir, DryRun = dryRun, Restore = false };
        var excluded = new List<HostKind>();
        if (!web) excluded.Add(HostKind.Web);
        if (!tests) excluded.Add(HostKind.Tests);
        options.Hosts = HostSelection.Defaults(excluded, ScaffoldCommand.Open(options));
        return options;
    }

    /// <summary>Plans and applies a run the way the CLI does, with console
    /// output captured so plan/progress lines stay out of the test log.</summary>
    private static int Run(ScaffoldOptions options) => RunCaptured(options).ExitCode;

    private static (int ExitCode, string Stdout, string Stderr) RunCaptured(ScaffoldOptions options) =>
        CliConsole.Capture(() => ScaffoldCommand.Run(ScaffoldCommand.Open(options), options));

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

        Assert.Equal(0, Run(Options(tmp.Dir, dryRun: true)));

        Assert.Equal(before, Snapshot(tmp.Dir));
        Assert.Single(Directory.EnumerateFileSystemEntries(tmp.Dir, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void Add_GdScriptOnlyProject_ScaffoldsFullLayout_AndReRunIsNoOp()
    {
        using var tmp = new TempProjectDir();
        tmp.Write("project.godot", GdScriptProject);
        var originalProjectGodot = File.ReadAllText(System.IO.Path.Combine(tmp.Dir, "project.godot"));

        Assert.Equal(0, Run(Options(tmp.Dir)));

        // Nested-always layout: Godot csproj + sln at root, hosts nested
        // behind .gdignore.
        foreach (var expected in new[]
                 {
                    "SpaceMiner.csproj", "SpaceMiner.slnx", "Directory.Build.targets", "export_presets.cfg", "global.json",
                     "SpaceMiner.2dog/SpaceMiner.2dog.csproj", "SpaceMiner.2dog/.gdignore",
                     "SpaceMiner.web/SpaceMiner.web.csproj", "SpaceMiner.web/.gdignore", "SpaceMiner.web/Directory.Build.props",
                     "SpaceMiner.web/TwoDogWebBoot.cs",
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
        Assert.Equal(0, Run(Options(tmp.Dir)));
        Assert.Equal(snapshot, Snapshot(tmp.Dir));
    }

    [Fact]
    public void Add_ExistingCsproj_PutsWebBootInWebFolderAndPatchesInclude()
    {
        using var tmp = new TempProjectDir();
        tmp.Write("project.godot", GdScriptProject);
        tmp.Write("SpaceMiner.csproj",
            $"""
            <Project Sdk="Godot.NET.Sdk/{ToolVersions.GodotSdkVersion}">
                <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                </PropertyGroup>
            </Project>
            """);

        Assert.Equal(0, Run(Options(tmp.Dir)));

        Assert.True(File.Exists(System.IO.Path.Combine(tmp.Dir, "SpaceMiner.web", "TwoDogWebBoot.cs")));
        Assert.False(File.Exists(System.IO.Path.Combine(tmp.Dir, "TwoDogWebBoot.cs")));
        var csproj = File.ReadAllText(System.IO.Path.Combine(tmp.Dir, "SpaceMiner.csproj"));
        Assert.Contains("Compile Include=\"SpaceMiner.web/TwoDogWebBoot.cs\"", csproj);
        Assert.Contains("Exists('$(MSBuildThisFileDirectory)SpaceMiner.web/TwoDogWebBoot.cs')", csproj);
    }

    [Fact]
    public void Add_WebIntoADifferentFolderLater_RepairsTheStaleBootInclude()
    {
        // A no-web scaffold leaves the template's guarded include pointing at
        // SpaceMiner.web; adding a web host under another folder later must
        // still wire the bootstrap (the stale include's guard stays false).
        using var tmp = new TempProjectDir();
        tmp.Write("project.godot", GdScriptProject);
        Assert.Equal(0, Run(Options(tmp.Dir, web: false)));

        var options = new ScaffoldOptions { ProjectPath = tmp.Dir, Restore = false };
        var project = ScaffoldCommand.Open(options);
        options.Hosts = HostSelection.FromFlags(CommandLine.Parse(["add", "--web", "Browser"]), project);
        Assert.Equal(0, ScaffoldCommand.Run(project, options));

        Assert.True(File.Exists(System.IO.Path.Combine(tmp.Dir, "Browser", "TwoDogWebBoot.cs")));
        Assert.Contains("Compile Include=\"Browser/TwoDogWebBoot.cs\"",
            File.ReadAllText(System.IO.Path.Combine(tmp.Dir, "SpaceMiner.csproj")));
    }

    [Fact]
    public void Add_LegacyRootWebBoot_IsLeftAloneAndGetsNoFolderCopy()
    {
        using var tmp = new TempProjectDir();
        tmp.Write("project.godot", GdScriptProject);
        tmp.Write("SpaceMiner.csproj",
            $"""
            <Project Sdk="Godot.NET.Sdk/{ToolVersions.GodotSdkVersion}">
                <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                </PropertyGroup>
            </Project>
            """);
        tmp.Write("TwoDogWebBoot.cs", "// legacy layout\n");

        var run = RunCaptured(Options(tmp.Dir));
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("TwoDogWebBoot.cs sits at the project root", run.Stdout);

        // The root file (older layout, compiled by the default globs) is
        // untouched, no folder copy appears, and no Include is patched in.
        Assert.Equal("// legacy layout\n", File.ReadAllText(System.IO.Path.Combine(tmp.Dir, "TwoDogWebBoot.cs")));
        Assert.False(File.Exists(System.IO.Path.Combine(tmp.Dir, "SpaceMiner.web", "TwoDogWebBoot.cs")));
        Assert.DoesNotContain("TwoDogWebBoot", File.ReadAllText(System.IO.Path.Combine(tmp.Dir, "SpaceMiner.csproj")));
    }

    [Fact]
    public void Add_WithoutWeb_CreatesNoWebBoot()
    {
        using var tmp = new TempProjectDir();
        tmp.Write("project.godot", GdScriptProject);

        Assert.Equal(0, Run(Options(tmp.Dir, web: false)));

        Assert.Empty(Directory.EnumerateFiles(tmp.Dir, "TwoDogWebBoot.cs", SearchOption.AllDirectories));
    }

    [Fact]
    public void Add_ExistingNet8Project_UpgradesToNet10()
    {
        using var tmp = new TempProjectDir();
        tmp.Write("project.godot", GdScriptProject);
        tmp.Write("SpaceMiner.csproj",
            $"""
            <Project Sdk="Godot.NET.Sdk/{ToolVersions.GodotSdkVersion}">
                <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                </PropertyGroup>
            </Project>
            """);

        Assert.Equal(0, Run(Options(tmp.Dir, web: false, tests: false)));

        var csproj = File.ReadAllText(System.IO.Path.Combine(tmp.Dir, "SpaceMiner.csproj"));
        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", csproj);
        Assert.DoesNotContain("net8.0", csproj);
    }

    [Fact]
    public void Add_LeavesAnUnrelatedFolderThatLooksLikeAHostAlone()
    {
        using var tmp = new TempProjectDir();
        tmp.Write("project.godot", GdScriptProject);
        tmp.Write("SpaceMiner.tests/notes.md", "hand-written GDScript test notes");

        Assert.Equal(0, Run(Options(tmp.Dir)));

        Assert.Equal(
            ["notes.md"],
            Directory.EnumerateFileSystemEntries(System.IO.Path.Combine(tmp.Dir, "SpaceMiner.tests"))
                .Select(e => System.IO.Path.GetFileName(e)!).ToArray());
        Assert.True(File.Exists(System.IO.Path.Combine(tmp.Dir, "SpaceMiner.tests2", "SpaceMiner.tests2.csproj")));
    }

    [Fact]
    public void Add_ExistingExportPresets_AppendsMissingPresets_WithoutRewriting()
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

        Assert.Equal(0, Run(Options(tmp.Dir)));

        // Web plus the two missing desktop presets are appended; the
        // pre-existing Windows Desktop preset is kept, never duplicated.
        var text = File.ReadAllText(System.IO.Path.Combine(tmp.Dir, "export_presets.cfg"));
        Assert.StartsWith(existing, text);
        Assert.Contains("[preset.1]", text);
        Assert.Contains("[preset.2]", text);
        Assert.Contains("[preset.3]", text);
        Assert.DoesNotContain("[preset.4]", text);
        Assert.Contains("name=\"Web\"", text);
        Assert.Contains("name=\"Linux\"", text);
        Assert.Contains("name=\"macOS\"", text);
        Assert.Single(Regex.Matches(text, "name=\"Windows Desktop\""));

        // Re-run: every preset is detected, nothing appended twice.
        var snapshot = Snapshot(tmp.Dir);
        Assert.Equal(0, Run(Options(tmp.Dir)));
        Assert.Equal(snapshot, Snapshot(tmp.Dir));
    }

    [Fact]
    public void Add_ExistingGlobalJson_IsNeverTouched()
    {
        using var tmp = new TempProjectDir();
        tmp.Write("project.godot", GdScriptProject);
        // latestMajor from the lowest 10.0 band: the add pipeline runs
        // `dotnet sln` inside this directory, so the pin must resolve with
        // whatever SDK the runner has (a bare version pin means latestPatch
        // and only accepts one band - broke on a runner with only 10.0.3xx,
        // and a 10.0.302 floor broke on one with only 10.0.301).
        const string existing = """{ "sdk": { "version": "10.0.100", "rollForward": "latestMajor" } }""";
        tmp.Write("global.json", existing);

        // The root global.json is the user's SDK policy: --force overwrites
        // scaffolded files, but never this one.
        var options = Options(tmp.Dir);
        options.Force = true;
        var run = RunCaptured(options);
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("global.json already exists - left untouched", run.Stdout);

        Assert.Equal(existing, File.ReadAllText(System.IO.Path.Combine(tmp.Dir, "global.json")));
    }

    // Opt-in host kinds (never in the default set) share one end-to-end scaffold flow;
    // per-kind rows carry the flag, folder suffix, a csproj marker, and the expected files
    // (the kind itself comes from HostKinds.Of - internal types can't be theory parameters).
    [Theory]
    [InlineData("--winforms", "winforms", "<UseWindowsForms>true</UseWindowsForms>",
        new[] { "Program.cs", "MainForm.cs", "app.manifest" })]
    [InlineData("--winui", "winui", "<UseWinUI>true</UseWinUI>",
        new[] { "Program.cs", "App.cs", "MainWindow.cs", "app.manifest" })]
    [InlineData("--avalonia", "avalonia", "2dog.avalonia",
        new[] { "Program.cs", "App.cs", "MainWindow.cs", "app.manifest" })]
    [InlineData("--webxr", "webxr", "<TwoDogWebXR>true</TwoDogWebXR>",
        new[] { "Program.cs", "wwwroot/index.html", "wwwroot/webxr-layers-polyfill.min.js" })]
    public void Add_OptInHost_ScaffoldsOnExplicitFlagOnly(
        string flag, string suffix, string csprojMarker, string[] extraFiles)
    {
        var kind = HostKinds.Of(suffix);
        using var tmp = new TempProjectDir();
        tmp.Write("project.godot", GdScriptProject);

        // The default set never contains the opt-in host ...
        Assert.Equal(0, Run(Options(tmp.Dir)));
        Assert.False(Directory.Exists(System.IO.Path.Combine(tmp.Dir, $"SpaceMiner.{suffix}")));

        // ... but its flag scaffolds it beside the others.
        var options = new ScaffoldOptions { ProjectPath = tmp.Dir, Restore = false };
        var project = ScaffoldCommand.Open(options);
        options.Hosts = HostSelection.FromFlags(CommandLine.Parse(["add", flag]), project);
        Assert.Equal(0, ScaffoldCommand.Run(project, options));

        var host = System.IO.Path.Combine(tmp.Dir, $"SpaceMiner.{suffix}");
        foreach (var expected in (string[])[$"SpaceMiner.{suffix}.csproj", ".gdignore", .. extraFiles])
            Assert.True(File.Exists(System.IO.Path.Combine(host, expected)), $"missing {expected}");

        var csproj = File.ReadAllText(System.IO.Path.Combine(host, $"SpaceMiner.{suffix}.csproj"));
        Assert.Contains(csprojMarker, csproj);
        Assert.Contains("../SpaceMiner.csproj", csproj);

        // The game project excludes the new folder, the sln picked it up, and
        // a re-scan recognizes the host by content.
        Assert.Contains($"SpaceMiner.{suffix}/**",
            File.ReadAllText(System.IO.Path.Combine(tmp.Dir, "SpaceMiner.csproj")));
        Assert.True(SolutionOps.ContainsProject(
            System.IO.Path.Combine(tmp.Dir, "SpaceMiner.slnx"), $"SpaceMiner.{suffix}.csproj"));
        Assert.Contains(HostScan.Find(tmp.Dir),
            h => h.Kind == kind && h.Folder == $"SpaceMiner.{suffix}");
    }

    [Fact]
    public void Add_NoWebNoTests_PrunesScaffoldingAndExcludes()
    {
        using var tmp = new TempProjectDir();
        tmp.Write("project.godot", GdScriptProject);

        Assert.Equal(0, Run(Options(tmp.Dir, web: false, tests: false)));

        Assert.True(Directory.Exists(System.IO.Path.Combine(tmp.Dir, "SpaceMiner.2dog")));
        Assert.False(Directory.Exists(System.IO.Path.Combine(tmp.Dir, "SpaceMiner.web")));
        Assert.False(Directory.Exists(System.IO.Path.Combine(tmp.Dir, "SpaceMiner.tests")));
        // The root global.json (wasm SDK pin) only comes with the web host.
        Assert.False(File.Exists(System.IO.Path.Combine(tmp.Dir, "global.json")));

        // DefaultItemExcludes lists only the hosts that exist; the guarded web
        // bootstrap Compile Include stays (matching dotnet-new output - inert
        // via its Exists condition until a web host appears).
        var csproj = File.ReadAllText(System.IO.Path.Combine(tmp.Dir, "SpaceMiner.csproj"));
        Assert.Contains("SpaceMiner.2dog/**", csproj);
        Assert.DoesNotContain("SpaceMiner.web/**", csproj);
        Assert.DoesNotContain("SpaceMiner.tests", csproj);
    }
}

public class HostsTests
{
    [Fact]
    public void AllocateFolder_UsesTheDefaultNameWhenFree()
    {
        Assert.Equal("MyGame.2dog", Hosts.AllocateFolder(HostKind.Desktop, "MyGame", []));
        Assert.Equal("MyGame.tests", Hosts.AllocateFolder(HostKind.Tests, "MyGame", []));
    }

    [Fact]
    public void AllocateFolder_NumbersPastTakenNames()
    {
        string[] taken = ["MyGame.2dog", "MyGame.2dog2"];
        Assert.Equal("MyGame.2dog3", Hosts.AllocateFolder(HostKind.Desktop, "MyGame", taken));
        // Matching is case-insensitive: folders collide on Windows regardless.
        Assert.Equal("MyGame.web2", Hosts.AllocateFolder(HostKind.Web, "MyGame", ["mygame.web"]));
    }

    [Theory]
    [InlineData("My Game!", "MyGame")]
    [InlineData("my-game_1.x", "my-game_1.x")]
    public void SanitizeName_KeepsFolderSafeCharacters(string input, string expected) =>
        Assert.Equal(expected, Hosts.SanitizeName(input));

    // Names that survive the character filter but are pure path syntax would
    // write outside the project root once combined into a path.
    [Theory]
    [InlineData("")]
    [InlineData("!!")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../..")]
    [InlineData("._-")]
    public void SanitizeName_RejectsNamesWithoutALetterOrDigit(string input) =>
        Assert.Null(Hosts.SanitizeName(input));
}

public class HostScanTests
{
    [Theory]
    [InlineData("2dog")]
    [InlineData("web")]
    [InlineData("webxr")]
    [InlineData("tests")]
    [InlineData("winforms")]
    [InlineData("winui")]
    [InlineData("avalonia")]
    public void Classify_RecognizesScaffoldedHosts_ByContentNotName(string suffix)
    {
        var kind = HostKinds.Of(suffix);
        var csproj = TemplateAssets.HostFiles(kind, "MyGame", "some.folder")
            .Single(f => f.RelativePath.EndsWith(".csproj")).Text;
        Assert.Equal(kind, HostScan.Classify(csproj, "some.folder"));
    }

    [Fact]
    public void Classify_IgnoresUnrelatedProjects()
    {
        Assert.Null(HostScan.Classify("<Project Sdk=\"Microsoft.NET.Sdk\"></Project>", "tools"));
    }

    [Fact]
    public void Find_ListsEveryHostInTheProject()
    {
        using var tmp = new TempProjectDir();
        tmp.Write("project.godot", "config_version=5\n");
        foreach (var (kind, folder) in new[]
                 {
                     (HostKind.Desktop, "MyGame.2dog"), (HostKind.Desktop, "MyGame.editor"),
                     (HostKind.Web, "MyGame.web"), (HostKind.WebXr, "MyGame.webxr"), (HostKind.Tests, "MyGame.tests"),
                     (HostKind.WinForms, "MyGame.winforms"), (HostKind.WinUi, "MyGame.winui"),
                     (HostKind.Avalonia, "MyGame.avalonia"),
                 })
            foreach (var (path, content) in TemplateAssets.HostFiles(kind, "MyGame", folder))
                tmp.Write(path, content);

        // Neither one is a host: no csproj named after the folder, and a
        // plain project that knows nothing about Godot.
        tmp.Write("assets/readme.txt", "");
        tmp.Write("tools/tools.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");

        var hosts = HostScan.Find(tmp.Dir);

        Assert.Equal(
            [("MyGame.2dog", HostKind.Desktop), ("MyGame.avalonia", HostKind.Avalonia),
             ("MyGame.editor", HostKind.Desktop), ("MyGame.tests", HostKind.Tests),
             ("MyGame.web", HostKind.Web), ("MyGame.webxr", HostKind.WebXr),
             ("MyGame.winforms", HostKind.WinForms), ("MyGame.winui", HostKind.WinUi)],
            hosts.Select(h => (h.Folder, h.Kind)).Order().ToArray());
    }
}

public class CommandLineTests
{
    [Fact]
    public void NoArguments_MeanVersionInfoPlusUsage()
    {
        var cmd = CommandLine.Parse([]);
        Assert.Equal(Verb.None, cmd.Verb);
        Assert.False(cmd.HostFlagsSeen);
        Assert.Null(cmd.Options.ProjectPath);
    }

    [Theory]
    [InlineData("add")]
    [InlineData("convert")]
    public void AddAndConvert_AreTheSameVerb(string verb)
    {
        var cmd = CommandLine.Parse([verb, "some/path"]);
        Assert.Equal(Verb.Add, cmd.Verb);
        Assert.Equal("some/path", cmd.Options.ProjectPath);
    }

    [Fact]
    public void HostFlags_TakeAnOptionalFolderName()
    {
        var cmd = CommandLine.Parse(["add", "--desktop", "--web", "MyGame.web2", "--tests"]);

        Assert.True(cmd.HostFlagsSeen);
        Assert.Equal(
            [(HostKind.Desktop, null), (HostKind.Web, "MyGame.web2"), (HostKind.Tests, null)],
            cmd.Requested.Select(r => (r.Kind, r.Folder)).ToArray());
    }

    [Fact]
    public void HostFlags_DoNotSwallowAProjectPath()
    {
        var cmd = CommandLine.Parse(["add", "--web", "./MyGame"]);
        Assert.Equal([(HostKind.Web, (string?)null)], cmd.Requested.Select(r => (r.Kind, r.Folder)).ToArray());
        Assert.Equal("./MyGame", cmd.Options.ProjectPath);
    }

    [Fact]
    public void RepeatedHostFlag_RequestsTwoHosts()
    {
        var cmd = CommandLine.Parse(["add", "--desktop", "One", "--desktop", "Two"]);
        Assert.Equal(["One", "Two"], cmd.Requested.Select(r => r.Folder!).ToArray());
    }

    [Fact]
    public void NegativeHostFlags_CountAsAChoice()
    {
        var cmd = CommandLine.Parse(
            ["convert", "--no-web", "--no-webxr", "--no-tests", "--no-winforms", "--no-winui", "--no-avalonia"]);
        Assert.True(cmd.HostFlagsSeen);
        Assert.Empty(cmd.Requested);
        Assert.Equal([HostKind.Web, HostKind.WebXr, HostKind.Tests, HostKind.WinForms, HostKind.WinUi, HostKind.Avalonia],
            cmd.Excluded.Order().ToArray());
    }

    [Fact]
    public void New_TakesANameAndADirectory()
    {
        var cmd = CommandLine.Parse(["new", "MyGame", "games/mine", "--no-restore"]);
        Assert.Equal(Verb.New, cmd.Verb);
        Assert.Equal("MyGame", cmd.Options.NameOverride);
        Assert.Equal("games/mine", cmd.OutputDir);
        Assert.False(cmd.Options.Restore);
    }

    [Fact]
    public void MalformedCommandLines_AreUsageErrors()
    {
        Assert.Throws<UsageException>(() => CommandLine.Parse(["add", "--nope"]));
        Assert.Throws<UsageException>(() => CommandLine.Parse(["add", "one", "two"]));
        Assert.Throws<UsageException>(() => CommandLine.Parse(["--name"]));
    }

    [Fact]
    public void HelpAndVersion_ShortCircuit()
    {
        Assert.Equal(Verb.Help, CommandLine.Parse(["help"]).Verb);
        Assert.Equal(Verb.Help, CommandLine.Parse(["add", "--help"]).Verb);
        Assert.Equal(Verb.Version, CommandLine.Parse(["--version"]).Verb);
        Assert.Equal(Verb.Version, CommandLine.Parse(["version"]).Verb);
    }

    // --pin does not exist in the tool: pinning happens in the launcher (dnx /
    // dotnet tool install) before 2dog runs, so the flag explains just that.
    [Fact]
    public void Pin_ExplainsLauncherPinning()
    {
        var ex = Assert.Throws<ToolException>(() => CommandLine.Parse(["--pin", "4.7.1.42"]));
        Assert.Contains("dnx 2dog@4.7.1.42", ex.Message);
        Assert.Contains("--version 4.7.1.42", ex.Message);

        ex = Assert.Throws<ToolException>(() => CommandLine.Parse(["--pin"]));
        Assert.Contains("dnx 2dog@<version>", ex.Message);
    }

    // A command line that answers the questions itself never prompts - not for
    // the host selection, and not for the final confirmation either.
    [Theory]
    [InlineData(true, "new", "MyGame")]
    [InlineData(true, "add")]
    [InlineData(false, "add", "--desktop")]
    [InlineData(false, "add", "--desktop", "MyGame.editor")]
    [InlineData(false, "convert", "--no-web")]
    [InlineData(false, "new", "MyGame", "--tests")]
    [InlineData(false, "add", "--yes")]
    [InlineData(false, "add", "--non-interactive")]
    // --dry-run answers nothing, so it still gathers choices - it just prints
    // the plan instead of applying it.
    [InlineData(true, "add", "--dry-run")]
    public void Prompting_IsOffWheneverTheFlagsDecide(bool expected, params string[] args) =>
        Assert.Equal(expected, Program.WantsPrompts(CommandLine.Parse(args)));
}

public class HostSelectionTests
{
    private static ProjectContext Project(params (HostKind Kind, string Folder)[] existing) =>
        new()
        {
            Dir = "/tmp/MyGame",
            BaseName = "MyGame",
            ExistingHosts = existing.Select(e => new ExistingHost(e.Kind, e.Folder)).ToList(),
            ExistingFolders = existing.Select(e => e.Folder).ToList(),
        };

    /// <summary>A project whose only subdirectories are none of 2dog's doing.</summary>
    private static ProjectContext ProjectWithFolders(params string[] folders) =>
        new() { Dir = "/tmp/MyGame", BaseName = "MyGame", ExistingFolders = folders.ToList() };

    [Fact]
    public void Defaults_AreEveryKindTheProjectLacks()
    {
        var hosts = HostSelection.Defaults([], Project((HostKind.Desktop, "MyGame.2dog")));
        Assert.Equal(
            [(HostKind.Web, "MyGame.web"), (HostKind.Tests, "MyGame.tests")],
            hosts.Select(h => (h.Kind, h.Folder)).ToArray());
    }

    [Fact]
    public void Defaults_HonorExclusions()
    {
        var hosts = HostSelection.Defaults([HostKind.Web], Project());
        Assert.Equal([HostKind.Desktop, HostKind.Tests], hosts.Select(h => h.Kind).ToArray());
    }

    // WinForms and WinUI (Windows-only) and Avalonia (heavy dependency set) are never
    // part of the default set - they only come from their flags or the interactive checkbox.
    [Fact]
    public void Defaults_NeverIncludeOptInHosts()
    {
        var kinds = HostSelection.Defaults([], Project()).Select(h => h.Kind).ToArray();
        Assert.DoesNotContain(HostKind.WinForms, kinds);
        Assert.DoesNotContain(HostKind.WinUi, kinds);
        Assert.DoesNotContain(HostKind.Avalonia, kinds);
    }

    [Fact]
    public void FromFlags_WinForms_AllocatesItsDefaultFolder()
    {
        var cmd = CommandLine.Parse(["add", "--winforms"]);
        var host = HostSelection.FromFlags(cmd, Project()).Single();
        Assert.Equal((HostKind.WinForms, "MyGame.winforms"), (host.Kind, host.Folder));
    }

    [Fact]
    public void FromFlags_WinUi_AllocatesItsDefaultFolder()
    {
        var cmd = CommandLine.Parse(["add", "--winui"]);
        var host = HostSelection.FromFlags(cmd, Project()).Single();
        Assert.Equal((HostKind.WinUi, "MyGame.winui"), (host.Kind, host.Folder));
    }

    [Fact]
    public void FromFlags_Avalonia_AllocatesItsDefaultFolder()
    {
        var cmd = CommandLine.Parse(["add", "--avalonia"]);
        var host = HostSelection.FromFlags(cmd, Project()).Single();
        Assert.Equal((HostKind.Avalonia, "MyGame.avalonia"), (host.Kind, host.Folder));
    }

    [Fact]
    public void FromFlags_AllocatesFreeFoldersForRepeatKinds()
    {
        var cmd = CommandLine.Parse(["add", "--desktop", "--desktop"]);
        var hosts = HostSelection.FromFlags(cmd, Project((HostKind.Desktop, "MyGame.2dog")));
        Assert.Equal(["MyGame.2dog2", "MyGame.2dog3"], hosts.Select(h => h.Folder).ToArray());
    }

    [Fact]
    public void FromFlags_RejectsAFolderThatIsTaken()
    {
        var cmd = CommandLine.Parse(["add", "--tests", "MyGame.tests"]);
        var ex = Assert.Throws<ToolException>(() => HostSelection.FromFlags(cmd, Project((HostKind.Tests, "MyGame.tests"))));
        Assert.Contains("already exists", ex.Message);
    }

    [Fact]
    public void FromFlags_SanitizesGivenFolderNames()
    {
        var cmd = CommandLine.Parse(["add", "--web", "My Game!"]);
        Assert.Equal("MyGame", HostSelection.FromFlags(cmd, Project()).Single().Folder);
    }

    // A directory that is not a 2dog host is still somebody's content: new
    // hosts go around it rather than into it.
    [Fact]
    public void Defaults_AvoidDirectoriesThatAreNotHosts()
    {
        var hosts = HostSelection.Defaults([], ProjectWithFolders("MyGame.tests", "assets"));
        Assert.Equal(
            [(HostKind.Desktop, "MyGame.2dog"), (HostKind.Web, "MyGame.web"), (HostKind.Tests, "MyGame.tests2")],
            hosts.Select(h => (h.Kind, h.Folder)).ToArray());
    }

    [Fact]
    public void FromFlags_AllocatesAroundDirectoriesThatAreNotHosts()
    {
        var cmd = CommandLine.Parse(["add", "--tests"]);
        Assert.Equal("MyGame.tests2", HostSelection.FromFlags(cmd, ProjectWithFolders("MyGame.tests")).Single().Folder);
    }

    [Fact]
    public void FromFlags_RejectsAFolderNameAnyDirectoryAlreadyUses()
    {
        var cmd = CommandLine.Parse(["add", "--tests", "docs"]);
        var ex = Assert.Throws<ToolException>(() => HostSelection.FromFlags(cmd, ProjectWithFolders("docs")));
        Assert.Contains("already exists", ex.Message);
    }
}

// End-to-end runs of the two shapes the conversion tests above do not cover:
// creating a project from scratch, and adding a second host of a kind that is
// already present.
public class ScaffoldEndToEndTests
{
    private static int Run(ScaffoldOptions options) => CliConsole.Capture(() =>
    {
        var project = ScaffoldCommand.Open(options);
        if (options.Hosts.Count == 0) options.Hosts = HostSelection.Defaults([], project);
        return ScaffoldCommand.Run(project, options);
    }).ExitCode;

    private static ScaffoldOptions NewProject(string dir) =>
        new() { ProjectPath = dir, NameOverride = "MyGame", CreateProject = true, Restore = false };

    [Fact]
    public void New_CreatesTheFullProjectLayout()
    {
        using var tmp = new TempProjectDir();
        var dir = System.IO.Path.Combine(tmp.Dir, "MyGame");

        Assert.Equal(0, Run(NewProject(dir)));

        foreach (var expected in new[]
                 {
                     "project.godot", "main.tscn", ".editorconfig", ".gitignore",
                     "MyGame.csproj", "MyGame.slnx", "Directory.Build.targets",
                     "export_presets.cfg", "global.json",
                     "MyGame.2dog/MyGame.2dog.csproj", "MyGame.web/MyGame.web.csproj",
                     "MyGame.web/TwoDogWebBoot.cs", "MyGame.tests/MyGame.tests.csproj",
                 })
            Assert.True(File.Exists(System.IO.Path.Combine(dir, expected)), $"missing {expected}");

        // The Godot editor resolves res://<assembly_name>.csproj, so the name
        // in project.godot and the csproj file name must agree.
        var projectGodot = File.ReadAllText(System.IO.Path.Combine(dir, "project.godot"));
        Assert.Contains("project/assembly_name=\"MyGame\"", projectGodot);
        Assert.Contains("config/name=\"MyGame\"", projectGodot);

        // The solution matches `dotnet new 2dog`: three build types, with the
        // web host mapped onto Debug for Editor and out of plain builds.
        var slnx = File.ReadAllText(System.IO.Path.Combine(dir, "MyGame.slnx"));
        Assert.Contains("<BuildType Name=\"Editor\" />", slnx);
        Assert.Contains("<BuildType Solution=\"Editor|*\" Project=\"Debug\" />", slnx);
        Assert.Contains("<Build Project=\"false\" />", slnx);

        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("Company.Product1", text);
            Assert.DoesNotContain("TPLRAWNAME", text);
        }
    }

    [Fact]
    public void New_RefusesAnExistingGodotProject()
    {
        using var tmp = new TempProjectDir();
        tmp.Write("project.godot", "config_version=5\n");
        Assert.Throws<ToolException>(() => Run(NewProject(tmp.Dir)));
    }

    [Fact]
    public void Add_SecondWebHost_KeepsASingleWebBoot()
    {
        using var tmp = new TempProjectDir();
        var dir = System.IO.Path.Combine(tmp.Dir, "MyGame");
        Assert.Equal(0, Run(NewProject(dir)));

        var options = new ScaffoldOptions { ProjectPath = dir, Restore = false };
        var project = ScaffoldCommand.Open(options);
        options.Hosts = HostSelection.FromFlags(CommandLine.Parse(["add", "--web"]), project);
        Assert.Equal(0, ScaffoldCommand.Run(project, options));

        // Exactly one bootstrap per project: two copies would collide (CS0101)
        // in the game assembly the root csproj compiles them into.
        Assert.True(File.Exists(System.IO.Path.Combine(dir, "MyGame.web2", "MyGame.web2.csproj")));
        Assert.Equal(
            [System.IO.Path.Combine(dir, "MyGame.web", "TwoDogWebBoot.cs")],
            Directory.EnumerateFiles(dir, "TwoDogWebBoot.cs", SearchOption.AllDirectories).ToArray());
        var csproj = File.ReadAllText(System.IO.Path.Combine(dir, "MyGame.csproj"));
        Assert.Contains("MyGame.web/TwoDogWebBoot.cs", csproj);
        Assert.DoesNotContain("MyGame.web2/TwoDogWebBoot.cs", csproj);
    }

    [Fact]
    public void Add_CustomWebFolderName_RewritesTheBootInclude()
    {
        using var tmp = new TempProjectDir();
        tmp.Write("project.godot", "config_version=5\n\n[application]\n\nconfig/name=\"My Game\"\n");

        var options = new ScaffoldOptions { ProjectPath = tmp.Dir, Restore = false };
        var project = ScaffoldCommand.Open(options);
        options.Hosts = HostSelection.FromFlags(CommandLine.Parse(["add", "--web", "Browser"]), project);
        Assert.Equal(0, ScaffoldCommand.Run(project, options));

        Assert.True(File.Exists(System.IO.Path.Combine(tmp.Dir, "Browser", "TwoDogWebBoot.cs")));
        var csproj = File.ReadAllText(System.IO.Path.Combine(tmp.Dir, "MyGame.csproj"));
        Assert.Contains("Compile Include=\"Browser/TwoDogWebBoot.cs\"", csproj);
        Assert.DoesNotContain("MyGame.web/TwoDogWebBoot.cs", csproj);
    }

    [Fact]
    public void Add_SecondHostOfTheSameKind_LandsBesideTheFirst()
    {
        using var tmp = new TempProjectDir();
        var dir = System.IO.Path.Combine(tmp.Dir, "MyGame");
        Assert.Equal(0, Run(NewProject(dir)));

        var options = new ScaffoldOptions { ProjectPath = dir, Restore = false };
        var project = ScaffoldCommand.Open(options);
        options.Hosts = HostSelection.FromFlags(CommandLine.Parse(["add", "--desktop"]), project);
        Assert.Equal(0, ScaffoldCommand.Run(project, options));

        var host = System.IO.Path.Combine(dir, "MyGame.2dog2");
        Assert.True(File.Exists(System.IO.Path.Combine(host, "MyGame.2dog2.csproj")));
        // It references the same game project and stays hidden from Godot ...
        Assert.True(File.Exists(System.IO.Path.Combine(host, ".gdignore")));
        Assert.Contains("../MyGame.csproj", File.ReadAllText(System.IO.Path.Combine(host, "MyGame.2dog2.csproj")));
        // ... and the game project excludes the new folder from its globs.
        Assert.Contains("MyGame.2dog2/**", File.ReadAllText(System.IO.Path.Combine(dir, "MyGame.csproj")));
        Assert.True(SolutionOps.ContainsProject(System.IO.Path.Combine(dir, "MyGame.slnx"), "MyGame.2dog2.csproj"));
    }
}

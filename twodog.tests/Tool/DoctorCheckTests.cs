using System.Text.Json;
using twodog.cli;

namespace twodog.tests.ToolTests;

// Individual doctor checks against deliberately damaged scaffolds: what a fresh project passes must be reported
// once an author disables, removes or mistypes it, and the fixes must follow a solution migration.
[Collection("doctor statics")] // DoctorCommand.Runner/Environment are process-wide seams: never swap them in parallel.
public class DoctorCheckTests : IDisposable
{
    private readonly TempProjectDir _tmp = new();
    private readonly string _cache;

    public DoctorCheckTests()
    {
        _cache = Path.Combine(_tmp.Dir, "cache");
        foreach (var (id, version) in new[]
                 {
                     ("2dog.engine", ToolVersions.TwoDogVersion), ("2dog.tools", ToolVersions.NativesVersion),
                     ("2dog.win-x64.editor", ToolVersions.NativesVersion), ("2dog.browser-wasm.release", ToolVersions.NativesVersion),
                 })
            Directory.CreateDirectory(Path.Combine(_cache, id, version));
    }

    public void Dispose() => _tmp.Dispose();

    private FakeProcessRunner Runner() => new(r =>
    {
        if (r.Args.Contains("--list-sdks"))
            return FakeProcessRunner.Result(r, 0, "10.0.303 [C:\\Program Files\\dotnet\\sdk]");
        if (r.Args.Contains("workload"))
            return FakeProcessRunner.Result(r, 0, "Installed Workload Id   Manifest Version   Installation Source",
                "------------------------------------------------------------", "wasm-tools              10.0.100/10.0.100  SDK 10.0.300", "");
        if (r.Args.Contains("locals"))
            return FakeProcessRunner.Result(r, 0, $"global-packages: {_cache}{Path.DirectorySeparatorChar}");
        return FakeProcessRunner.Result(r, 0);
    });

    private (int ExitCode, string Stdout, string Stderr) Doctor(string dir, params string[] extra)
    {
        var previousRunner = DoctorCommand.Runner;
        var previousEnv = DoctorCommand.Environment;
        DoctorCommand.Runner = Runner();
        DoctorCommand.Environment = new FakeEnvironment();
        try
        {
            return CliConsole.Run(["doctor", dir, "--offline", .. extra]);
        }
        finally
        {
            DoctorCommand.Runner = previousRunner;
            DoctorCommand.Environment = previousEnv;
        }
    }

    private string Scaffold(params string[] hostFlags)
    {
        var dir = Path.Combine(_tmp.Dir, "Game");
        Assert.Equal(0, CliConsole.Run(["new", "Game", dir, "--no-restore", .. hostFlags]).ExitCode);
        return dir;
    }

    private static List<JsonElement> Findings(string stdout, string id) =>
        JsonDocument.Parse(stdout).RootElement.GetProperty("doctor").GetProperty("findings").EnumerateArray()
            .Where(f => f.GetProperty("id").GetString() == id).ToList();

    /// <summary>The one warn/fail finding with that id; asserts its severity and whether it carries a fix.</summary>
    private static JsonElement Issue(string stdout, string id, string severity, bool fixable)
    {
        var issue = Assert.Single(Findings(stdout, id), f => f.GetProperty("severity").GetString() is "warn" or "fail");
        Assert.Equal(severity, issue.GetProperty("severity").GetString());
        Assert.Equal(fixable, issue.TryGetProperty("fix", out _));
        return issue;
    }

    private static void Edit(string dir, string relative, string from, string to)
    {
        var path = Path.Combine(dir, relative);
        var text = File.ReadAllText(path);
        Assert.Contains(from, text);
        File.WriteAllText(path, text.Replace(from, to));
    }

    [Fact]
    public void MissingPresetFile_FailsAndIsRecreated()
    {
        var dir = Scaffold("--desktop");
        File.Delete(Path.Combine(dir, "export_presets.cfg"));

        var run = Doctor(dir, "--json");
        Assert.Equal(ExitCodes.Findings, run.ExitCode);
        Issue(run.Stdout, "preset.file", "fail", fixable: true);

        Assert.Equal(0, Doctor(dir, "--fix").ExitCode);
        Assert.True(File.Exists(Path.Combine(dir, "export_presets.cfg")));
        Assert.All(Findings(Doctor(dir, "--json").Stdout, "preset.file"), f => Assert.Equal("pass", f.GetProperty("severity").GetString()));
    }

    [Fact]
    public void DisabledGameProperty_IsReportedWithoutAFix()
    {
        var dir = Scaffold("--desktop");
        Edit(dir, "Game.csproj", "<EnableDynamicLoading>true</EnableDynamicLoading>", "<EnableDynamicLoading>false</EnableDynamicLoading>");

        var issue = Issue(Doctor(dir, "--json").Stdout, "game.properties", "warn", fixable: false);
        Assert.Contains("EnableDynamicLoading", issue.GetProperty("title").GetString());
        Assert.Contains("set EnableDynamicLoading to true", issue.GetProperty("remedy").GetString());
    }

    [Fact]
    public void BlazorClientLiterals_CountForTheVersionChecks()
    {
        var dir = Scaffold("--blazor");
        Edit(dir, Hosts.BlazorClientProject("Game.blazor"), "Include=\"2dog.blazor\" Version=\"$(TwoDogVersion)\"", "Include=\"2dog.blazor\" Version=\"1.0.0\"");

        var run = Doctor(dir, "--json");
        Assert.Equal(ExitCodes.Findings, run.ExitCode);
        Assert.Contains("1 literal", Issue(run.Stdout, "ver.literal-versions", "warn", fixable: false).GetProperty("title").GetString());
        Assert.Contains("1.0.0", Issue(run.Stdout, "ver.twodog-consistent", "fail", fixable: false).GetProperty("title").GetString());
    }

    [Fact]
    public void MalformedPropsVersion_Fails()
    {
        var dir = Scaffold("--desktop");
        Edit(dir, "Directory.Build.props", $"<TwoDogVersion>{ToolVersions.TwoDogVersion}</TwoDogVersion>", "<TwoDogVersion>garbage</TwoDogVersion>");

        var run = Doctor(dir, "--json");
        Assert.Equal(ExitCodes.Findings, run.ExitCode);
        Assert.Contains("TwoDogVersion='garbage'", Issue(run.Stdout, "ver.props-invalid", "fail", fixable: false).GetProperty("title").GetString());
    }

    [Fact]
    public void MalformedCompanionVersion_Fails()
    {
        var dir = Scaffold("--desktop");
        Edit(dir, "Directory.Build.props", $"<TwoDogAvaloniaVersion>{ToolVersions.AvaloniaVersion}</TwoDogAvaloniaVersion>",
            "<TwoDogAvaloniaVersion>latest</TwoDogAvaloniaVersion>");

        var issue = Issue(Doctor(dir, "--json").Stdout, "ver.props-invalid", "fail", fixable: false);
        Assert.Contains("TwoDogAvaloniaVersion='latest'", issue.GetProperty("title").GetString());
    }

    [Fact]
    public void MalformedBlazorClient_IsALoadProblem()
    {
        var dir = Scaffold("--blazor");
        File.WriteAllText(Path.Combine(dir, Hosts.BlazorClientProject("Game.blazor")), "<Project>\n  <PropertyGroup>\n</Project>\n");

        var issue = Issue(Doctor(dir, "--json").Stdout, "layout.load-problems", "fail", fixable: false);
        Assert.Contains("Game.blazor.Client.csproj is not valid XML", issue.GetProperty("title").GetString());
    }

    [Fact]
    public void UnpinnedBrowserWasmProperty_Warns()
    {
        var dir = Scaffold("--web");
        Edit(dir, "Game.web/Game.web.csproj", "Version=\"[$(TwoDogNativesVersion)]\"", "Version=\"$(TwoDogNativesVersion)\"");

        Assert.Contains("not exact-pinned", Issue(Doctor(dir, "--json").Stdout, "ver.natives", "warn", fixable: false).GetProperty("title").GetString());
    }

    [Fact]
    public void AnalyzersSetToFalse_Warns()
    {
        var dir = Scaffold("--desktop");
        Edit(dir, "Game.2dog/Game.2dog.csproj", "<TwoDogRemoveDuplicateGodotAnalyzers>true</TwoDogRemoveDuplicateGodotAnalyzers>",
            "<TwoDogRemoveDuplicateGodotAnalyzers>false</TwoDogRemoveDuplicateGodotAnalyzers>");

        var issue = Issue(Doctor(dir, "--json").Stdout, "host.duplicate-analyzers", "warn", fixable: false);
        Assert.Contains("'false'", issue.GetProperty("title").GetString());
    }

    [Fact]
    public void AnalyzersFalseForOneConfiguration_Warns()
    {
        var dir = Scaffold("--desktop");
        Edit(dir, "Game.2dog/Game.2dog.csproj", "<TwoDogRemoveDuplicateGodotAnalyzers>true</TwoDogRemoveDuplicateGodotAnalyzers>",
            "<TwoDogRemoveDuplicateGodotAnalyzers>true</TwoDogRemoveDuplicateGodotAnalyzers>\n" +
            "    <TwoDogRemoveDuplicateGodotAnalyzers Condition=\"'$(Configuration)' == 'Release'\">false</TwoDogRemoveDuplicateGodotAnalyzers>");

        var issue = Issue(Doctor(dir, "--json").Stdout, "host.duplicate-analyzers", "warn", fixable: false);
        Assert.Contains("'false'", issue.GetProperty("title").GetString());
    }

    [Fact]
    public void NoTrimmerRoots_Warns()
    {
        var dir = Scaffold("--web");
        Edit(dir, "Game.web/Game.web.csproj", "<TrimmerRootAssembly Include=\"Game\"/>", "");
        Edit(dir, "Game.web/Game.web.csproj", "<TrimmerRootAssembly Include=\"$(TargetName)\"/>", "");

        var issue = Issue(Doctor(dir, "--json").Stdout, "host.trimmer-root", "warn", fixable: false);
        Assert.Contains("no TrimmerRootAssembly", issue.GetProperty("title").GetString());
    }

    [Fact]
    public void AssemblyNameWithoutAnyCsproj_Fails()
    {
        var dir = Path.Combine(_tmp.Dir, "ghost");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "project.godot"), "[application]\nconfig/name=\"Ghost\"\n\n[dotnet]\nproject/assembly_name=\"Ghost\"\n");

        var run = Doctor(dir, "--json");
        Assert.Equal(ExitCodes.Findings, run.ExitCode);
        Assert.Contains("no csproj at the root", Issue(run.Stdout, "layout.assembly-name", "fail", fixable: false).GetProperty("title").GetString());
    }

    [Fact]
    public void TargetsWithoutTheDeepCleanTarget_Warns()
    {
        var dir = Scaffold("--desktop");
        File.WriteAllText(Path.Combine(dir, "Directory.Build.targets"), "<Project>\n</Project>\n");

        var issue = Issue(Doctor(dir, "--json").Stdout, "layout.root-build-targets", "warn", fixable: false);
        Assert.Contains("TwoDogDeepClean", issue.GetProperty("title").GetString());
    }

    [Fact]
    public void TargetsOnlyMentioningTheDeepCleanTarget_Warns()
    {
        var dir = Scaffold("--desktop");
        File.WriteAllText(Path.Combine(dir, "Directory.Build.targets"), "<Project>\n  <!-- TwoDogDeepClean lives elsewhere -->\n</Project>\n");

        Issue(Doctor(dir, "--json").Stdout, "layout.root-build-targets", "warn", fixable: false);
    }

    [Fact]
    public void RootTargets_HideAParentsDeepClean_UnlessTheyImportIt()
    {
        var dir = Scaffold("--desktop");
        File.WriteAllText(Path.Combine(_tmp.Dir, "Directory.Build.targets"), TemplateAssets.RootBuildTargets());
        var targets = Path.Combine(dir, "Directory.Build.targets");

        File.WriteAllText(targets, "<Project>\n</Project>\n");
        Issue(Doctor(dir, "--json").Stdout, "layout.root-build-targets", "warn", fixable: false);

        File.WriteAllText(targets, "<Project>\n  <Import Project=\"$([MSBuild]::GetPathOfFileAbove('Directory.Build.targets', '$(MSBuildThisFileDirectory)../'))\" />\n</Project>\n");
        var findings = Findings(Doctor(dir, "--json").Stdout, "layout.root-build-targets");
        Assert.NotEmpty(findings);
        Assert.All(findings, f => Assert.Equal("pass", f.GetProperty("severity").GetString()));
    }

    [Fact]
    public void OldGlobalJsonPin_Warns()
    {
        var dir = Scaffold("--web");
        File.WriteAllText(Path.Combine(dir, "global.json"), "{ \"sdk\": { \"version\": \"8.0.100\", \"rollForward\": \"latestFeature\" } }");

        var issue = Issue(Doctor(dir, "--json").Stdout, "layout.root-global-json", "warn", fixable: false);
        Assert.Contains("8.0.100", issue.GetProperty("title").GetString());

        File.WriteAllText(Path.Combine(dir, "global.json"), "{ \"sdk\": { \"version\": \"8.0.100\", \"rollForward\": \"latestMajor\" } }");
        Assert.All(Findings(Doctor(dir, "--json").Stdout, "layout.root-global-json"), f => Assert.Equal("pass", f.GetProperty("severity").GetString()));
    }

    [Fact]
    public void FixAll_MigratesTheSln_ThenAddsProjectsToTheSlnx()
    {
        var dir = Scaffold("--desktop");
        File.Delete(Path.Combine(dir, "Game.slnx"));
        const string guid = "{11111111-1111-1111-1111-111111111111}";
        File.WriteAllText(Path.Combine(dir, "Game.sln"),
            "Microsoft Visual Studio Solution File, Format Version 12.00\n# Visual Studio Version 17\n" +
            $"Project(\"{{9A19103F-16F7-4668-BE54-9A1E7A4F7556}}\") = \"Game\", \"Game.csproj\", \"{guid}\"\nEndProject\n" +
            "Global\n\tGlobalSection(SolutionConfigurationPlatforms) = preSolution\n\t\tDebug|Any CPU = Debug|Any CPU\n" +
            "\t\tRelease|Any CPU = Release|Any CPU\n\tEndGlobalSection\n\tGlobalSection(ProjectConfigurationPlatforms) = postSolution\n" +
            $"\t\t{guid}.Debug|Any CPU.ActiveCfg = Debug|Any CPU\n\t\t{guid}.Debug|Any CPU.Build.0 = Debug|Any CPU\n" +
            $"\t\t{guid}.Release|Any CPU.ActiveCfg = Release|Any CPU\n\t\t{guid}.Release|Any CPU.Build.0 = Release|Any CPU\n" +
            "\tEndGlobalSection\nEndGlobal\n");

        var before = Doctor(dir, "--json");
        Assert.Equal("announced", Assert.Single(Findings(before.Stdout, "sln.legacy-format")).GetProperty("fix").GetProperty("class").GetString());
        Issue(before.Stdout, "sln.contains-hosts", "warn", fixable: true);

        var all = Doctor(dir, "--fix-all");
        Assert.Equal(0, all.ExitCode);
        Assert.DoesNotContain("fix failed", all.Stderr);
        Assert.False(File.Exists(Path.Combine(dir, "Game.sln")));
        Assert.Contains("Game.2dog.csproj", File.ReadAllText(Path.Combine(dir, "Game.slnx")));
    }
}

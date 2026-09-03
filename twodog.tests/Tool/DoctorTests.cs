using System.Text.Json;
using twodog.cli;

namespace twodog.tests.ToolTests;

// `2dog doctor`: a fresh scaffold is clean, every breaker is found by id, safe fixes repair and are idempotent,
// announced fixes wait for --fix-all, and the exit code says whether findings remain.
[Collection("doctor statics")] // DoctorCommand.Runner/Environment are process-wide seams: never swap them in parallel.
public class DoctorTests : IDisposable
{
    private readonly TempProjectDir _tmp = new();
    private readonly string _cache;

    public DoctorTests()
    {
        // A NuGet cache holding exactly what a restored project has, so env.packages-restored passes.
        _cache = Path.Combine(_tmp.Dir, "cache");
        foreach (var (id, version) in new[]
                 {
                     ("2dog.engine", ToolVersions.TwoDogVersion), ("2dog.tools", ToolVersions.NativesVersion),
                     ("2dog.win-x64.editor", ToolVersions.NativesVersion), ("2dog.browser-wasm.release", ToolVersions.NativesVersion),
                 })
            Directory.CreateDirectory(Path.Combine(_cache, id, version));
    }

    public void Dispose() => _tmp.Dispose();

    private FakeProcessRunner Runner(bool wasmTools = true, bool sdk = true) => new(r =>
    {
        if (r.Args.Contains("--list-sdks"))
            return FakeProcessRunner.Result(r, 0, sdk ? ["10.0.303 [C:\\Program Files\\dotnet\\sdk]"] : []);
        if (r.Args.Contains("workload"))
            return FakeProcessRunner.Result(r, 0, "Installed Workload Id   Manifest Version   Installation Source",
                "------------------------------------------------------------", wasmTools ? "wasm-tools              10.0.100/10.0.100  SDK 10.0.300" : "aspire  1.0", "");
        if (r.Args.Contains("locals"))
            return FakeProcessRunner.Result(r, 0, $"global-packages: {_cache}{Path.DirectorySeparatorChar}");
        if (r.Args.Contains("build"))
            return FakeProcessRunner.Result(r, 0, "Build succeeded.");
        return FakeProcessRunner.Result(r, 0);
    });

    private (int ExitCode, string Stdout, string Stderr) Doctor(string dir, FakeProcessRunner? runner = null, params string[] extra)
    {
        var previousRunner = DoctorCommand.Runner;
        var previousEnv = DoctorCommand.Environment;
        DoctorCommand.Runner = runner ?? Runner();
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

    private static JsonElement Findings(string stdout) =>
        JsonDocument.Parse(stdout).RootElement.GetProperty("doctor").GetProperty("findings");

    private static JsonElement? Finding(string stdout, string id) =>
        Findings(stdout).EnumerateArray().Where(f => f.GetProperty("id").GetString() == id).Select(f => (JsonElement?)f).FirstOrDefault();

    /// <summary>The finding when it is a warning or an error; passes and infos do not count.</summary>
    private static JsonElement? Issue(string stdout, string id) =>
        Findings(stdout).EnumerateArray()
            .Where(f => f.GetProperty("id").GetString() == id && f.GetProperty("severity").GetString() is "warn" or "fail")
            .Select(f => (JsonElement?)f).FirstOrDefault();

    private static Dictionary<string, string> Snapshot(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}cache{Path.DirectorySeparatorChar}"))
            .ToDictionary(f => Path.GetRelativePath(dir, f), File.ReadAllText);

    [Fact]
    public void FreshScaffold_IsClean()
    {
        var dir = Scaffold("--desktop", "--web", "--tests");

        var run = Doctor(dir);
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("No issues found", run.Stdout);
        Assert.Contains("ok environment", run.Stdout);

        var json = Doctor(dir, null, "--json");
        var summary = JsonDocument.Parse(json.Stdout).RootElement.GetProperty("doctor").GetProperty("summary");
        Assert.Equal(0, summary.GetProperty("fail").GetInt32());
        Assert.Equal(0, summary.GetProperty("warn").GetInt32());
        Assert.True(summary.GetProperty("pass").GetInt32() > 15);
    }

    [Fact]
    public void AllOptInHosts_AreCleanToo()
    {
        var dir = Scaffold("--desktop", "--webxr", "--avalonia", "--blazor", "--winforms", "--winui", "--tests");
        var run = Doctor(dir, null, "--json");
        var issues = Findings(run.Stdout).EnumerateArray()
            .Where(f => f.GetProperty("severity").GetString() is "warn" or "fail")
            .Select(f => f.GetProperty("id").GetString()).ToList();
        Assert.Empty(issues);
    }

    [Fact]
    public void Verbose_ListsEveryCheck()
    {
        var dir = Scaffold("--desktop", "--web", "--tests");
        var run = Doctor(dir, null, "-v");
        Assert.Contains("  ok", run.Stdout);
        var ids = Findings(Doctor(dir, null, "--json").Stdout).EnumerateArray().Select(f => f.GetProperty("id").GetString()).ToHashSet();
        foreach (var expected in new[] { "env.dotnet-sdk", "env.wasm-tools", "layout.game-csproj", "game.sdk", "host.gdignore", "sln.contains-hosts", "ver.twodog-outdated", "preset.file" })
            Assert.Contains(expected, ids);
    }

    public static IEnumerable<object[]> SafeBreakers() =>
    [
        ["host.gdignore", "fail", (Action<string>)(d => File.Delete(Path.Combine(d, "Game.web", ".gdignore")))],
        ["preset.desktop", "warn", (Action<string>)(d => Edit(d, "export_presets.cfg", "name=\"Linux\"", "name=\"Linux-old\""))],
        ["preset.web", "fail", (Action<string>)(d => Edit(d, "export_presets.cfg", "name=\"Web\"", "name=\"Web-old\""))],
        ["host.buildtype-deprecated", "warn", (Action<string>)(d => Edit(d, "Game.2dog/Game.2dog.csproj", "<GodotProjectDir>..</GodotProjectDir>", "<GodotProjectDir>..</GodotProjectDir><TwoDogBuildType>debug</TwoDogBuildType>"))],
        ["host.duplicate-analyzers", "warn", (Action<string>)(d => Edit(d, "Game.2dog/Game.2dog.csproj", "<TwoDogRemoveDuplicateGodotAnalyzers>true</TwoDogRemoveDuplicateGodotAnalyzers>", ""))],
        ["game.default-item-excludes", "warn", (Action<string>)(d => Edit(d, "Game.csproj", ";Game.tests/**", ""))],
        ["layout.root-build-targets", "warn", (Action<string>)(d => File.Delete(Path.Combine(d, "Directory.Build.targets")))],
        ["layout.root-global-json", "warn", (Action<string>)(d => File.Delete(Path.Combine(d, "global.json")))],
        ["sln.build-exclusions", "warn", (Action<string>)(d => Edit(d, "Game.slnx", "<Build Project=\"false\" />", ""))],
        ["godot.xr-shaders", "warn", (Action<string>)(d => Edit(d, "project.godot", "shaders/enabled.web=true", "shaders/enabled.web=false"))],
    ];

    private static void Edit(string dir, string relative, string from, string to)
    {
        var path = Path.Combine(dir, relative);
        var text = File.ReadAllText(path);
        Assert.Contains(from, text);
        File.WriteAllText(path, text.Replace(from, to));
    }

    [Theory]
    [MemberData(nameof(SafeBreakers))]
    public void SafeBreaker_IsFound_FixedAndIdempotent(string id, string severity, Action<string> breaker)
    {
        var dir = Scaffold("--desktop", "--web", "--webxr", "--tests");
        breaker(dir);

        var broken = Doctor(dir, null, "--json");
        var finding = Finding(broken.Stdout, id);
        Assert.NotNull(finding);
        Assert.Equal(severity, finding.Value.GetProperty("severity").GetString());
        Assert.Equal("safe", finding.Value.GetProperty("fix").GetProperty("class").GetString());
        Assert.Equal(severity == "fail" ? ExitCodes.Findings : ExitCodes.Ok, broken.ExitCode);

        var fixed_ = Doctor(dir, null, "--fix");
        Assert.Equal(0, fixed_.ExitCode);
        Assert.Contains("re-check", fixed_.Stdout);
        Assert.Null(Issue(Doctor(dir, null, "--json").Stdout, id));

        var before = Snapshot(dir);
        Assert.Equal(0, Doctor(dir, null, "--fix").ExitCode);
        Assert.Equal(before, Snapshot(dir));
    }

    [Fact]
    public void ManualFindings_HaveRemedies_AndNoFix()
    {
        var dir = Scaffold("--desktop", "--tests");
        Edit(dir, "Game.2dog/Game.2dog.csproj", "<GodotProjectDir>..</GodotProjectDir>", "<GodotProjectDir>..</GodotProjectDir><PublishAot>true</PublishAot>");
        Edit(dir, "Game.csproj", $"Godot.NET.Sdk/{ToolVersions.GodotSdkVersion}", "Godot.NET.Sdk/4.7.1");

        var run = Doctor(dir, null, "--json");
        Assert.Equal(ExitCodes.Findings, run.ExitCode);
        var aot = Finding(run.Stdout, "host.publish-aot")!.Value;
        Assert.Equal("fail", aot.GetProperty("severity").GetString());
        Assert.False(aot.TryGetProperty("fix", out _));
        Assert.Contains("remove PublishAot", aot.GetProperty("remedy").GetString());

        var mismatch = Finding(run.Stdout, "game.sdk-mismatch")!.Value;
        Assert.Equal("2dog update", mismatch.GetProperty("remedy").GetString());

        var text = Doctor(dir);
        Assert.Contains("run: 2dog update", text.Stdout);
        Assert.Contains("by hand", text.Stdout);
    }

    [Fact]
    public void AnnouncedFixes_WaitForFixAll()
    {
        var dir = Scaffold("--desktop", "--web");
        var boot = Path.Combine(dir, "Game.web", "TwoDogWebBoot.cs");
        File.WriteAllText(boot, "// stale\n");

        var run = Doctor(dir, null, "--json");
        Assert.Equal("announced", Finding(run.Stdout, "host.webboot-drift")!.Value.GetProperty("fix").GetProperty("class").GetString());

        Assert.Equal(0, Doctor(dir, null, "--fix").ExitCode);
        Assert.Equal("// stale\n", File.ReadAllText(boot));

        var all = Doctor(dir, null, "--fix-all");
        Assert.Equal(0, all.ExitCode);
        Assert.Contains("+ announced: refresh Game.web/TwoDogWebBoot.cs", all.Stdout);
        Assert.Equal(TemplateAssets.WebBootSource(), File.ReadAllText(boot));
    }

    [Fact]
    public void NonInteractive_PointsAtTheFixCommands()
    {
        var dir = Scaffold("--desktop");
        File.Delete(Path.Combine(dir, "Game.2dog", ".gdignore"));
        var run = Doctor(dir);
        Assert.Equal(ExitCodes.Findings, run.ExitCode);
        Assert.Contains("apply the safe fixes:", run.Stdout);
        Assert.Contains("2dog doctor --fix", run.Stdout);
        Assert.Contains("fix: create Game.2dog/.gdignore (safe)", run.Stdout);
    }

    [Fact]
    public void Ignore_DropsAFinding_AndStrict_CountsWarnings()
    {
        var dir = Scaffold("--desktop");
        File.Delete(Path.Combine(dir, "Game.2dog", ".gdignore"));
        Assert.Equal(0, Doctor(dir, null, "--ignore", "host.gdignore").ExitCode);

        var dir2 = Path.Combine(_tmp.Dir, "Other");
        Assert.Equal(0, CliConsole.Run("new", "Other", dir2, "--desktop", "--no-restore").ExitCode);
        File.Delete(Path.Combine(dir2, "Directory.Build.targets"));
        Assert.Equal(0, Doctor(dir2).ExitCode);
        Assert.Equal(ExitCodes.Findings, Doctor(dir2, null, "--strict").ExitCode);
    }

    [Fact]
    public void NotAProject_IsAToolError_AndGdScriptOnly_IsInfoOnly()
    {
        var missing = Path.Combine(_tmp.Dir, "nothing");
        Directory.CreateDirectory(missing);
        var run = Doctor(missing);
        Assert.Equal(ExitCodes.Error, run.ExitCode);
        Assert.Contains("no project.godot", run.Stderr);

        var gd = Path.Combine(_tmp.Dir, "gd");
        Directory.CreateDirectory(gd);
        File.WriteAllText(Path.Combine(gd, "project.godot"), "[application]\nconfig/name=\"Gd\"\n");
        var plain = Doctor(gd, null, "--json");
        Assert.Equal(0, plain.ExitCode);
        Assert.All(Findings(plain.Stdout).EnumerateArray(), f => Assert.Contains(f.GetProperty("severity").GetString(), new[] { "pass", "info" }));
        Assert.Equal("not a 2dog project yet (no hosts)", Finding(plain.Stdout, "layout.game-csproj")!.Value.GetProperty("title").GetString());
    }

    [Fact]
    public void EnvironmentProblems_AreReported()
    {
        var dir = Scaffold("--desktop", "--web");

        var noSdk = Doctor(dir, Runner(sdk: false), "--json");
        Assert.Equal("fail", Finding(noSdk.Stdout, "env.dotnet-sdk")!.Value.GetProperty("severity").GetString());

        var noWasm = Doctor(dir, Runner(wasmTools: false), "--json");
        var wasm = Finding(noWasm.Stdout, "env.wasm-tools")!.Value;
        Assert.Equal("fail", wasm.GetProperty("severity").GetString());
        Assert.Equal("dotnet workload install wasm-tools", wasm.GetProperty("remedy").GetString());

        Directory.Delete(Path.Combine(_cache, "2dog.tools"), true);
        var unrestored = Doctor(dir, null, "--json");
        Assert.Contains("2dog.tools", Finding(unrestored.Stdout, "env.packages-restored")!.Value.GetProperty("title").GetString());
    }

    [Fact]
    public void ListChecks_CoversTheCatalogueAndSignatures()
    {
        var run = CliConsole.Run("doctor", "--list-checks");
        Assert.Equal(0, run.ExitCode);
        foreach (var check in CheckCatalog.All) Assert.Contains(check.Id, run.Stdout);
        foreach (var signature in BuildSignatures.All) Assert.Contains(signature.Id, run.Stdout);
        Assert.True(CheckCatalog.All.Select(c => c.Id).Distinct().Count() == CheckCatalog.All.Count());

        var json = CliConsole.Run("doctor", "--list-checks", "--json");
        Assert.Equal(CheckCatalog.All.Count() + BuildSignatures.All.Length,
            JsonDocument.Parse(json.Stdout).RootElement.GetProperty("checks").GetArrayLength());
    }

    [Fact]
    public void Log_ExplainsAKnownFailure()
    {
        var log = _tmp.Write("build.log",
            "MSBuild version 17.14\n" +
            "  Game -> P:\\g\\bin\\Debug\\net10.0\\Game.dll\n" +
            "P:\\g\\Game.2dog\\Game.2dog.csproj : error : TwoDog: the Godot project references GodotSharp 4.7.1 (via Godot.NET.Sdk) but 2dog.engine 4.7.2.79 is built for Godot 4.7.2. Mixed versions crash at runtime.\n" +
            "P:\\g\\Game.2dog\\Program.cs(12,5): error CS0103: The name 'Foo' does not exist in the current context\n" +
            "P:\\g\\Game.2dog\\Program.cs(12,5): error CS0103: The name 'Foo' does not exist in the current context\n");

        var run = CliConsole.Run("doctor", "--log", log);
        Assert.Equal(ExitCodes.Findings, run.ExitCode);
        Assert.Contains("Godot.NET.Sdk and 2dog.engine are on different Godot lines", run.Stdout);
        Assert.Contains("run: 2dog update", run.Stdout);
        Assert.Contains("other errors", run.Stdout);
        Assert.Single(run.Stdout.Split('\n'), l => l.Contains("CS0103"));

        var clean = _tmp.Write("ok.log", "Build succeeded.\n    0 Warning(s)\n    0 Error(s)\n");
        var fine = CliConsole.Run("doctor", "--log", clean);
        Assert.Equal(0, fine.ExitCode);
        Assert.Contains("last lines", fine.Stdout);
    }

    [Fact]
    public void Build_RunsAndExplains()
    {
        var dir = Scaffold("--desktop");
        var failing = new FakeProcessRunner(r => r.Args.Contains("build")
            ? FakeProcessRunner.Result(r, 1, "error NETSDK1147: To build this project, the following workloads must be installed: wasm-tools")
            : Runner().Run(r));

        var run = Doctor(dir, failing, "--build");
        Assert.Equal(ExitCodes.Findings, run.ExitCode);
        Assert.Contains("Game.slnx (Debug): failed", run.Stdout);
        Assert.Contains("wasm-tools workload is not installed", run.Stdout);
        Assert.Contains("full log:", run.Stdout);
        Assert.Contains(failing.Requests, r => r.Args.Contains("build") && r.Args.Contains("-c") && r.Args.Contains("Debug"));

        var host = Doctor(dir, Runner(), "--build", "Game.2dog", "-c", "Release");
        Assert.Equal(0, host.ExitCode);
        Assert.Contains("Game.2dog.csproj (Release): succeeded", host.Stdout);
    }

    [Fact]
    public void HostCsproj_MissingBothProperties_IsOneFix()
    {
        var dir = Scaffold("--desktop");
        Edit(dir, "Game.2dog/Game.2dog.csproj", "<GodotProjectDir>..</GodotProjectDir>", "");
        Edit(dir, "Game.2dog/Game.2dog.csproj", "<TwoDogRemoveDuplicateGodotAnalyzers>true</TwoDogRemoveDuplicateGodotAnalyzers>", "");

        var broken = Doctor(dir, null, "--json");
        Assert.NotNull(Issue(broken.Stdout, "host.godot-project-dir"));
        Assert.NotNull(Issue(broken.Stdout, "host.duplicate-analyzers"));

        Assert.Equal(0, Doctor(dir, null, "--fix").ExitCode);
        var after = Doctor(dir, null, "--json");
        Assert.Null(Issue(after.Stdout, "host.godot-project-dir"));
        Assert.Null(Issue(after.Stdout, "host.duplicate-analyzers"));
        var csproj = File.ReadAllText(Path.Combine(dir, "Game.2dog", "Game.2dog.csproj"));
        Assert.Equal(2, csproj.Split("added by 2dog doctor").Length);
    }

    [Fact]
    public void FixAll_ComposesTheTargetFrameworkUpgrade_WithSafePatches()
    {
        var dir = Scaffold("--desktop", "--tests");
        Edit(dir, "Game.csproj", "<TargetFramework>net10.0</TargetFramework>", "<TargetFramework>net8.0</TargetFramework>");
        Edit(dir, "Game.csproj", ";Game.tests/**", "");

        var broken = Doctor(dir, null, "--json");
        Assert.Equal("announced", Finding(broken.Stdout, "game.target-framework")!.Value.GetProperty("fix").GetProperty("class").GetString());
        Assert.Equal("safe", Finding(broken.Stdout, "game.default-item-excludes")!.Value.GetProperty("fix").GetProperty("class").GetString());

        Assert.Equal(0, Doctor(dir, null, "--fix-all").ExitCode);
        var csproj = File.ReadAllText(Path.Combine(dir, "Game.csproj"));
        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", csproj);
        Assert.DoesNotContain("net8.0", csproj);
        Assert.Contains("Game.tests/**", csproj);
        var after = Doctor(dir, null, "--json");
        Assert.Null(Issue(after.Stdout, "game.target-framework"));
        Assert.Null(Issue(after.Stdout, "game.default-item-excludes"));
    }

    [Fact]
    public void LoadProblems_CoverTraversalAssemblyNames_AndUnreadableFiles()
    {
        var dir = Scaffold("--desktop");
        Edit(dir, "project.godot", "project/assembly_name=\"Game\"", "project/assembly_name=\"../Game\"");
        var model = ProjectModel.Load(dir);
        Assert.Contains(model.LoadProblems, p => p.Contains("assembly_name '../Game'"));
        Assert.Equal("Game", model.BaseName);
        Assert.Equal("fail", Finding(Doctor(dir, null, "--json").Stdout, "layout.load-problems")!.Value.GetProperty("severity").GetString());

        var game = Path.Combine(dir, "Game.csproj");
        void AssertUnreadable()
        {
            var locked = ProjectModel.Load(dir);
            Assert.Contains(locked.LoadProblems, p => p.StartsWith("Game.csproj:"));
            Assert.Null(locked.GameCsprojText);
        }

        if (OperatingSystem.IsWindows())
        {
            using var handle = File.Open(game, FileMode.Open, FileAccess.Read, FileShare.None);
            AssertUnreadable();
        }
        else
        {
            if (Environment.IsPrivilegedProcess) return;
            var mode = File.GetUnixFileMode(game);
            File.SetUnixFileMode(game, UnixFileMode.None);
            try { AssertUnreadable(); }
            finally { File.SetUnixFileMode(game, mode); }
        }
    }

    [Fact]
    public void AmbiguousSolutions_AreNotSilentlyPicked()
    {
        var dir = Scaffold("--desktop");
        File.Copy(Path.Combine(dir, "Game.slnx"), Path.Combine(dir, "Other.slnx"));

        var model = ProjectModel.Load(dir);
        Assert.Equal(2, model.GameSolutions.Count);
        Assert.Null(model.Solution);
        Assert.Equal("fail", Finding(Doctor(dir, null, "--json").Stdout, "sln.multiple")!.Value.GetProperty("severity").GetString());
    }

    [Fact]
    public void ASolutionNamingASimilarProject_IsNotTheGamesSolution()
    {
        var dir = Scaffold("--desktop");
        File.WriteAllText(Path.Combine(dir, "Other.slnx"), "<Solution>\n  <Project Path=\"OtherGame.csproj\" />\n</Solution>\n");

        var model = ProjectModel.Load(dir);
        Assert.Single(model.GameSolutions);
        Assert.Equal("Game.slnx", Path.GetFileName(model.Solution));
    }

    [Fact]
    public void Showcase_HasNoErrors()
    {
        var showcase = Path.Combine(HelperToolTestBed.RepoRoot, "demos", "showcase");
        var run = Doctor(showcase, null, "--json");
        var fails = Findings(run.Stdout).EnumerateArray().Where(f => f.GetProperty("severity").GetString() == "fail")
            .Select(f => f.GetProperty("id").GetString()).ToList();
        Assert.Empty(fails);
        Assert.NotNull(Finding(run.Stdout, "ver.managed-elsewhere"));
    }
}

public class DotnetInfoTests
{
    [Fact]
    public void ParseSdks_OrdersNewestFirst_AndKeepsPreviews()
    {
        var sdks = DotnetInfo.ParseSdks(["8.0.424 [C:\\Program Files\\dotnet\\sdk]", "10.0.303 [C:\\Program Files\\dotnet\\sdk]",
            "11.0.100-preview.5.26302.115 [C:\\Program Files\\dotnet\\sdk]", "garbage"]);
        Assert.Equal(["11.0.100-preview.5.26302.115", "10.0.303", "8.0.424"], sdks.Select(s => s.Raw));
        Assert.True(sdks[0].IsPreview);
        Assert.Equal(300, sdks[1].FeatureBand);
    }

    [Fact]
    public void ParseWorkloads_ReadsTheTable()
    {
        var ids = DotnetInfo.ParseWorkloads(["", "Installed Workload Id      Manifest Version      Installation Source",
            "---------------------------------------------------------------------", "wasm-tools                 10.0.100/10.0.100     SDK 10.0.300",
            "aspire                     8.2.2/8.0.100         VS", "", "Use `dotnet workload search` to find additional workloads to install."]);
        Assert.Equal(["wasm-tools", "aspire"], ids);
        Assert.Empty(DotnetInfo.ParseWorkloads(["nothing here"]));
    }

    [Fact]
    public void ParseGlobalPackages_FindsThePath()
    {
        Assert.Equal("C:\\Users\\me\\.nuget\\packages\\", DotnetInfo.ParseGlobalPackages(["global-packages: C:\\Users\\me\\.nuget\\packages\\"]));
        Assert.Null(DotnetInfo.ParseGlobalPackages(["http-cache: x"]));
    }

    [Theory]
    [InlineData("10.0.100", "latestFeature", "10.0.303", true)]
    [InlineData("10.0.100", "latestFeature", "11.0.100", false)]
    [InlineData("10.0.100", "latestPatch", "10.0.303", false)]
    [InlineData("10.0.100", "latestPatch", "10.0.111", true)]
    [InlineData("10.0.100", "disable", "10.0.111", false)]
    [InlineData("10.0.100", "latestMajor", "11.0.100", true)]
    [InlineData("10.0.400", "latestFeature", "10.0.303", false)]
    public void Satisfies_FollowsTheRollForwardPolicy(string pin, string roll, string installed, bool expected) =>
        Assert.Equal(expected, DotnetInfo.Satisfies(Version.Parse(pin), roll, [new DotnetInfo.Sdk(Version.Parse(installed), installed)]));

    [Fact]
    public void ParseGlobalJson_ToleratesCommentsAndMissingParts()
    {
        var (version, roll) = DotnetInfo.ParseGlobalJson("{ \"$comment\": \"x\", \"sdk\": { \"version\": \"10.0.100\", \"rollForward\": \"latestFeature\" } }");
        Assert.Equal(new Version(10, 0, 100), version);
        Assert.Equal("latestFeature", roll);
        Assert.Equal((null, "latestPatch"), DotnetInfo.ParseGlobalJson("{}"));
        Assert.Equal((null, "latestPatch"), DotnetInfo.ParseGlobalJson("not json"));
    }

    [Fact]
    public void ParseGlobalJson_ToleratesWrongShapes()
    {
        Assert.Equal((null, "latestPatch"), DotnetInfo.ParseGlobalJson("[]"));
        Assert.Equal((null, "latestPatch"), DotnetInfo.ParseGlobalJson("{ \"sdk\": \"10.0.100\" }"));
        Assert.Equal((null, "latestPatch"), DotnetInfo.ParseGlobalJson("{ \"sdk\": { \"version\": 10, \"rollForward\": false } }"));
    }
}

public class BuildLogAnalyzerTests
{
    [Fact]
    public void EverySignature_MatchesItsOwnSample()
    {
        var samples = new Dictionary<string, string>
        {
            ["build.variant-invalid"] = "error : TwoDog: invalid TwoDogVariant 'fast'. Allowed values: release, debug, editor.",
            ["build.publish-aot"] = "error : TwoDog: PublishAot (NativeAOT) is not supported for 2dog desktop hosts.",
            ["build.no-import-capability"] = "warning : TwoDog: Godot project 'x' needs a resource import, but no import capability was found (editor libgodot='', helper='', tools='').",
            ["build.import-required"] = "error : TwoDog: import required (TwoDogRequireImport=true) but no import capability is available (editor libgodot='', helper='', tools='').",
            ["build.web-payload-missing"] = "error : TwoDog: web payload (libgodot.a) not found. Searched the 2dog.browser-wasm.release package v4.7.2.3 and the source checkout.",
            ["build.native-missing"] = "warning : 2dog: could not locate libgodot-debug.dll for 2dog.win-x64. Searched NuGet: x - local: y.",
            ["build.nu1213"] = "error NU1213: The package 2dog 4.7.2.79 has a package type DotnetTool that is incompatible with this project.",
            ["build.wasm-tools-missing"] = "error NETSDK1147: To build this project, the following workloads must be installed: wasm-tools",
            ["build.webboot-duplicate"] = "TwoDogWebBoot.cs(5,18): error CS0101: The namespace 'Game.web' already contains a definition for 'TwoDogWebBoot'",
            ["build.godotplugins-missing"] = "Unhandled exception. System.IO.FileNotFoundException: TwoDog: GodotPlugins.dll not found (probed GODOTSHARP_DIR, ...)",
            ["build.variant-fallback"] = "TwoDog: TwoDogVariant is 'editor' but libgodot-editor.dll was not found; falling back to libgodot.dll, which may be a different variant.",
        };
        foreach (var (id, line) in samples)
        {
            var diagnosis = BuildLogAnalyzer.Analyze(line);
            Assert.Contains(diagnosis.Matches, m => m.Signature.Id == id);
        }
    }

    [Fact]
    public void RequiredImport_IsAFailure_WhereTheSkippedImportIsAWarning()
    {
        var skipped = BuildLogAnalyzer.Analyze("warning : TwoDog: Godot project 'x' needs a resource import, but no import capability was found (editor libgodot='', helper='', tools='').");
        Assert.Contains(skipped.Matches, m => m.Signature.Id == "build.no-import-capability" && m.Signature.Severity == Severity.Warn);
        Assert.False(skipped.HasProblems);

        var required = BuildLogAnalyzer.Analyze("error : TwoDog: import required (TwoDogRequireImport=true) but no import capability is available (editor libgodot='', helper='', tools='').");
        Assert.Contains(required.Matches, m => m.Signature.Id == "build.import-required" && m.Signature.Severity == Severity.Fail);
        Assert.True(required.HasProblems);
    }

    [Fact]
    public void UnmatchedErrors_AreDedupedAndCapped_TailOnlyWhenNothingElse()
    {
        var text = string.Join('\n', Enumerable.Range(0, 30).Select(i => $"a.cs({i},1): error CS9999: boom {i % 25}"));
        var diagnosis = BuildLogAnalyzer.Analyze(text);
        Assert.Empty(diagnosis.Matches);
        Assert.Equal(20, diagnosis.UnmatchedErrors.Count);
        Assert.Empty(diagnosis.Tail);

        var quiet = BuildLogAnalyzer.Analyze(string.Join('\n', Enumerable.Range(0, 40).Select(i => $"line {i}")));
        Assert.Equal(25, quiet.Tail.Count);
        Assert.False(quiet.HasProblems);
    }
}

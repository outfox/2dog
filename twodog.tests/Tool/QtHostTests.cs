using System.Runtime.InteropServices;
using twodog.cli;

namespace twodog.tests.ToolTests;

public class QtHostTests
{
    private static readonly string BridgeVersion = ToolVersions.QtBridgeVersion;

    [Fact]
    public void NewQtHost_ScaffoldsTheQmlHost_AndLeavesPlainSolutionBuildsAlone()
    {
        using var tmp = new TempProjectDir();
        Assert.Equal(0, CliConsole.Run("new", "Game", tmp.Dir, "--qt", "--no-restore").ExitCode);

        var host = Path.Combine(tmp.Dir, "Game.qt");
        foreach (var file in new[] { "Game.qt.csproj", ".gdignore", "Program.cs", "GameControls.cs", "Main.qml", "app.manifest" })
            Assert.True(File.Exists(Path.Combine(host, file)), $"missing {file}");
        var project = File.ReadAllText(Path.Combine(host, "Game.qt.csproj"));
        Assert.Contains("Include=\"2dog.qt\" Version=\"$(TwoDogVersion)\"", project);
        Assert.Contains("Include=\"QtGroup.Qt.Bridge.CSharp.$(QtBridgeRid)\" Version=\"$(TwoDogQtBridgeVersion)\"", project);
        Assert.Contains("<RootNamespace>Game</RootNamespace>", project);
        Assert.Contains("import TwoDog", File.ReadAllText(Path.Combine(host, "Main.qml")));
        Assert.Contains("new QtGodotSession(\"Game\"", File.ReadAllText(Path.Combine(host, "Program.cs")));

        Assert.Equal(BridgeVersion, PropsPatcher.Read(Path.Combine(tmp.Dir, "Directory.Build.props"))["TwoDogQtBridgeVersion"]);
        Assert.Contains("Game.qt/**", File.ReadAllText(Path.Combine(tmp.Dir, "Game.csproj")));
        // CMake, Ninja and a C++ toolchain are not a plain build's business; the host keeps its Editor configuration.
        var solution = Path.Combine(tmp.Dir, "Game.slnx");
        Assert.True(SolutionOps.IsExcludedFromSolutionBuild(solution, "Game.qt/Game.qt.csproj"));
        Assert.DoesNotContain("Project=\"Debug\"", File.ReadAllText(solution));
        Assert.Equal(HostKind.Qt, HostScan.Find(tmp.Dir).Single().Kind);
    }

    [Theory]
    [InlineData("<Project><PackageReference Include='QtGroup.Qt.Bridge.CSharp.linux-x64' Version='0.4.0-beta'/><GodotProjectDir>..</GodotProjectDir><OutputType>Exe</OutputType></Project>")]
    [InlineData("<Project><PackageReference Include='2dog.engine'/><PackageReference Include='2dog.qt'/><OutputType>WinExe</OutputType></Project>")]
    [InlineData("<Project><PackageReference Include='2dog.qt'>")]
    public void Detection_PrefersQtOverTheDesktopOutputType(string project) =>
        Assert.Equal(HostKind.Qt, HostScan.Classify(project, "custom.ui"));

    [Theory]
    [InlineData("0.4.0-beta", "0.4.0-beta", 0)]
    [InlineData("0.4.0", "0.4.0-beta", 1)]
    [InlineData("0.4.0.22-beta", "0.4.0-beta", 1)]
    [InlineData("0.4.0-beta.2", "0.4.0-beta.10", -1)]
    [InlineData("0.4.0-beta", "0.4.0-alpha", 1)]
    [InlineData("0.4.0-beta.1", "0.4.0-beta.rc", -1)]
    [InlineData("0.5.0-alpha", "0.4.1", 1)]
    [InlineData("1.0", "1.0.0.0", 0)]
    public void CompareNuGet_FollowsNuGetOrder(string a, string b, int expected) =>
        Assert.Equal(expected, Math.Sign(ToolVersions.CompareNuGet(a, b)!.Value));

    [Fact]
    public void CompareNuGet_RejectsUnreadableVersions()
    {
        Assert.Null(ToolVersions.CompareNuGet("$(TwoDogQtBridgeVersion)", BridgeVersion));
        Assert.Null(ToolVersions.CompareNuGet("10.0.*", "10.0.1"));
    }

    [Fact]
    public void BridgePackages_FollowTheirOwnVersionProperty()
    {
        foreach (var rid in new[] { "win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-arm64" })
            Assert.Equal("TwoDogQtBridgeVersion", VersionRewriter.PropertyFor(Hosts.QtBridgePackagePrefix + rid));
        Assert.Equal("TwoDogVersion", VersionRewriter.PropertyFor("2dog.qt"));
        Assert.True(VersionRewriter.IsTwoDogPackage("2dog.qt"));
        Assert.Empty(Workload.For([HostKind.Qt]));
    }

    [Fact]
    public void QtDir_IsOnlyNeededWhereQtBridgeBundlesNoQt()
    {
        using var tmp = new TempProjectDir();
        Assert.Equal(0, CliConsole.Run("new", "Game", tmp.Dir, "--qt", "--no-restore").ExitCode);

        Assert.Equal("pass", Severity(QtDirFindings(tmp.Dir, new FakeEnvironment())));
        Assert.Equal("warn", Severity(QtDirFindings(tmp.Dir, new FakeEnvironment(isWindows: false))));

        var qt = Path.Combine(tmp.Dir, "qt6");
        Directory.CreateDirectory(Path.Combine(qt, "lib", "cmake", "Qt6"));
        var linux = new FakeEnvironment(isWindows: false) { Vars = { ["QTDIR"] = qt } };
        // An explicit QtDir must be a Qt 6 prefix, even on win-x64 where it replaces the bundled Qt.
        Assert.Equal("warn", Severity(QtDirFindings(tmp.Dir, linux)));
        var windows = new FakeEnvironment { Vars = { ["QtDir"] = qt } };
        Assert.Equal("warn", Severity(QtDirFindings(tmp.Dir, windows)));

        File.WriteAllText(Path.Combine(qt, "lib", "cmake", "Qt6", "Qt6Config.cmake"), "");
        Assert.Equal("pass", Severity(QtDirFindings(tmp.Dir, linux)));

        // A QtDir property in the root Directory.Build.props counts like the environment variable.
        var props = Path.Combine(tmp.Dir, "Directory.Build.props");
        File.WriteAllText(props, File.ReadAllText(props).Replace("</Project>",
            $"<PropertyGroup><QtDir>{qt}</QtDir></PropertyGroup></Project>"));
        Assert.Equal("pass", Severity(QtDirFindings(tmp.Dir, new FakeEnvironment(isWindows: false))));
    }

    [Fact]
    public void Toolchain_IsReadFromPath_AndVisualStudioOnWindows()
    {
        using var tmp = new TempProjectDir();
        var bin = Path.Combine(tmp.Dir, "bin");
        Directory.CreateDirectory(bin);
        var runner = new FakeProcessRunner(r => FakeProcessRunner.Result(r, 0));
        var linux = new FakeEnvironment(isWindows: false) { Vars = { ["PATH"] = bin } };
        Assert.Equal(["a C++ compiler", "CMake", "Ninja"], EnvironmentChecks.MissingQtToolchain(linux, runner, tmp.Dir));
        foreach (var tool in new[] { "g++", "cmake", "ninja" })
            File.WriteAllText(Path.Combine(bin, tool), "");
        Assert.Empty(EnvironmentChecks.MissingQtToolchain(linux, runner, tmp.Dir));

        // Visual Studio's C++ workload, found through vswhere, brings MSVC plus its own CMake and Ninja.
        var programFiles = Path.Combine(tmp.Dir, "pf86");
        var vswhere = Path.Combine(programFiles, "Microsoft Visual Studio", "Installer", "vswhere.exe");
        var vs = Path.Combine(tmp.Dir, "vs");
        var cmakeTools = Path.Combine(vs, "Common7", "IDE", "CommonExtensions", "Microsoft", "CMake");
        Directory.CreateDirectory(Path.GetDirectoryName(vswhere)!);
        File.WriteAllText(vswhere, "");
        var windows = new FakeEnvironment { Vars = { ["ProgramFiles(x86)"] = programFiles } };
        Assert.Equal(["Visual Studio C++ tools", "CMake", "Ninja"], EnvironmentChecks.MissingQtToolchain(windows, runner, tmp.Dir));

        Directory.CreateDirectory(Path.Combine(cmakeTools, "CMake", "bin"));
        Directory.CreateDirectory(Path.Combine(cmakeTools, "Ninja"));
        File.WriteAllText(Path.Combine(cmakeTools, "CMake", "bin", "cmake.exe"), "");
        File.WriteAllText(Path.Combine(cmakeTools, "Ninja", "ninja.exe"), "");
        var withVs = new FakeProcessRunner(r => r.FileName == vswhere ? FakeProcessRunner.Result(r, 0, vs) : FakeProcessRunner.Result(r, 0));
        Assert.Empty(EnvironmentChecks.MissingQtToolchain(windows, withVs, tmp.Dir));
        Assert.Contains(withVs.Requests, r => r.Args.Contains("Microsoft.VisualStudio.ComponentGroup.NativeDesktop.Core"));
    }

    [Fact]
    public void PublishAot_IsRejected()
    {
        using var tmp = new TempProjectDir();
        Assert.Equal(0, CliConsole.Run("new", "Game", tmp.Dir, "--qt", "--no-restore").ExitCode);
        var csproj = Path.Combine(tmp.Dir, "Game.qt", "Game.qt.csproj");
        File.WriteAllText(csproj, File.ReadAllText(csproj).Replace("<IsPackable>false</IsPackable>",
            "<IsPackable>false</IsPackable><PublishAot>true</PublishAot>"));
        var finding = Assert.Single(HostChecks.Run(Context(tmp.Dir, new FakeEnvironment())), f => f.Id == "host.publish-aot");
        Assert.Equal(cli.Severity.Fail, finding.Severity);
    }

    private static DoctorContext Context(string dir, IEnvironment env) => new()
    {
        Project = ProjectModel.Load(dir),
        Env = env,
        Runner = new FakeProcessRunner(r => FakeProcessRunner.Result(r, 0)),
        Options = new DoctorOptions(),
    };

    private static List<Finding> QtDirFindings(string dir, IEnvironment env) =>
        HostChecks.Run(Context(dir, env)).Where(f => f.Id == "host.qt-dir").ToList();

    private static string Severity(List<Finding> findings) => Assert.Single(findings).Severity switch
    {
        cli.Severity.Pass => "pass",
        cli.Severity.Info => "info",
        cli.Severity.Warn => "warn",
        _ => "fail",
    };
}

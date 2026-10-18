using System.Xml.Linq;
using twodog.cli;

namespace twodog.tests.ToolTests;

public class CreationOptionsTests
{
    [Theory]
    [InlineData("new")]
    [InlineData("add")]
    public void BuildFlagsDistinguishUnsetEnabledAndDisabled(string verb)
    {
        var defaults = CommandLine.Parse([verb]).Options;
        Assert.Null(defaults.DoublePrecision);
        Assert.Null(defaults.WebThreads);
        var enabled = CommandLine.Parse([verb, "--double-precision", "--web-threads"]).Options;
        Assert.True(enabled.DoublePrecision);
        Assert.True(enabled.WebThreads);
        var disabled = CommandLine.Parse([verb, "--double-precision=false", "--web-threads=false"]).Options;
        Assert.False(disabled.DoublePrecision);
        Assert.False(disabled.WebThreads);
    }

    [Fact]
    public void GdUnitScaffoldsCustomFoldersAndCanCoexistWithOtherFrameworks()
    {
        using var tmp = new TempProjectDir();
        var dir = Path.Combine(tmp.Dir, "Game");
        var run = CliConsole.Run("new", "Game", dir, "--gdunit", "Game.scripts", "--xunit", "--nunit", "--no-restore");
        Assert.Equal(0, run.ExitCode);
        var project = File.ReadAllText(Path.Combine(dir, "Game.scripts", "Game.scripts.csproj"));
        Assert.Contains("Include=\"2dog.gdunit\"", project);
        Assert.Contains("../Game.csproj", project);
        Assert.Contains("<RootNamespace>Game.GdUnit</RootNamespace>", project);
        Assert.Contains("Game.scripts/tests", File.ReadAllText(Path.Combine(dir, "Game.scripts", "Program.cs")));
        Assert.Contains("Microsoft.Testing.Platform", File.ReadAllText(Path.Combine(dir, "Game.scripts", "global.json")));
        // .NET 10 runs one test runner per global.json, so `dotnet test` from the root needs every host on MTP.
        Assert.True(DotnetInfo.SelectsTestingPlatform(File.ReadAllText(Path.Combine(dir, "global.json"))));
        Assert.Contains("<UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>",
            File.ReadAllText(Path.Combine(dir, "Game.xunit", "Game.xunit.csproj")));
        Assert.Contains("<EnableNUnitRunner>true</EnableNUnitRunner>", File.ReadAllText(Path.Combine(dir, "Game.nunit", "Game.nunit.csproj")));
        Assert.Contains("dotnet test Game.scripts", run.Stdout);
        Assert.True(File.Exists(Path.Combine(dir, "Game.scripts", ".gdignore")));
        Assert.True(File.Exists(Path.Combine(dir, "Game.scripts", "tests", "example_test.gd")));
        Assert.Contains("Game.scripts/**", File.ReadAllText(Path.Combine(dir, "Game.csproj")));
        Assert.Contains("Game.scripts/Game.scripts.csproj", File.ReadAllText(Path.Combine(dir, "Game.slnx")));
        Assert.Equal([HostKind.Tests, HostKind.NUnit, HostKind.GdUnit], HostScan.Find(dir).Select(h => h.Kind).Order().ToArray());
        Assert.False(Hosts.InDefaultSet(HostKind.GdUnit));
        Assert.Equal(HostGroup.Testing, Hosts.Group(HostKind.GdUnit));
        Assert.Equal(HostKind.GdUnit, ProjectModel.Load(dir).Hosts.Single(h => h.Folder == "Game.scripts").Kind);
        Assert.Equal("$(TwoDogVersion)", VersionRewriter.Reference("2dog.gdunit.double"));
    }

    [Fact]
    public void AddingHostsPreservesDoublePrecisionAndCanSwitchBackAcrossTheGraph()
    {
        using var tmp = new TempProjectDir();
        var dir = Path.Combine(tmp.Dir, "Game");
        Assert.Equal(0, CliConsole.Run("new", "Game", dir, "--generic", "--double-precision", "--no-restore").ExitCode);
        Assert.True(BuildSettings.IsDouble(dir));
        Assert.Equal(0, CliConsole.Run("add", dir, "--gdunit", "--nunit", "--no-restore").ExitCode);
        Assert.Equal(3, HostScan.Find(dir).Count);
        AssertPrecision(dir, true);
        Assert.Equal(0, CliConsole.Run("add", dir, "--no-web", "--double-precision=false", "--no-restore").ExitCode);
        Assert.False(BuildSettings.IsDouble(dir));
        AssertPrecision(dir, false);
    }

    private static void AssertPrecision(string dir, bool useDouble)
    {
        foreach (var path in Directory.EnumerateFiles(dir, "*.csproj", SearchOption.AllDirectories))
        foreach (var package in VersionRewriter.References(File.ReadAllText(path)))
        {
            var baseId = package.Id.EndsWith(".double") ? package.Id[..^7] : package.Id;
            if (BuildSettings.PrecisionPackages.Contains(baseId))
                Assert.Equal(baseId + (useDouble ? ".double" : ""), package.Id);
        }
    }

    [Fact]
    public void WebThreadsCoverEveryBrowserClientAndArePreservedWhenAddingHosts()
    {
        using var tmp = new TempProjectDir();
        var dir = Path.Combine(tmp.Dir, "Game");
        Assert.Equal(0, CliConsole.Run("new", "Game", dir, "--web", "--webxr", "--blazor", "--web-threads", "--no-restore").ExitCode);
        Assert.True(BuildSettings.HasWebThreads(dir));
        foreach (var relative in new[] { "Game.web/Game.web.csproj", "Game.webxr/Game.webxr.csproj", Hosts.BlazorClientProject("Game.blazor") })
        {
            var doc = XDocument.Load(Path.Combine(dir, relative));
            Assert.Equal("$(TwoDogWebThreads)", doc.Descendants("WasmEnableThreads").Single().Value);
        }
        Assert.Equal(0, CliConsole.Run("add", dir, "--generic", "--no-restore").ExitCode);
        Assert.True(BuildSettings.HasWebThreads(dir));
        Assert.Equal(0, CliConsole.Run("add", dir, "--web", "--web-threads=false", "--no-restore").ExitCode);
        Assert.False(BuildSettings.HasWebThreads(dir));
    }

    [Fact]
    public void CreationOptionsDryRunDoesNotChangeExistingFiles()
    {
        using var tmp = new TempProjectDir();
        var dir = Path.Combine(tmp.Dir, "Game");
        Assert.Equal(0, CliConsole.Run("new", "Game", dir, "--generic", "--no-restore").ExitCode);
        var before = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllText);
        var run = CliConsole.Run("add", dir, "--gdunit", "--double-precision", "--web-threads", "--dry-run");
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("TwoDogPrecision=double", run.Stdout);
        Assert.Contains("TwoDogWebThreads=true", run.Stdout);
        Assert.Equal(before.Keys.Order(), Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Order());
        foreach (var (path, content) in before) Assert.Equal(content, File.ReadAllText(path));
    }
}

using twodog.cli;

namespace twodog.tests.ToolTests;

public class NUnitHostTests
{
    [Fact]
    public void TestFrameworksAreOptInAndCanCoexist()
    {
        Assert.False(Hosts.InDefaultSet(HostKind.Tests));
        Assert.False(Hosts.InDefaultSet(HostKind.NUnit));
        var command = CommandLine.Parse(["new", "Game", "--nunit", "Game.integration", "--xunit"]);
        var hosts = HostSelection.FromFlags(command, new ProjectContext { Dir = ".", BaseName = "Game" });
        Assert.Equal([(HostKind.Tests, "Game.xunit"), (HostKind.NUnit, "Game.integration")],
            hosts.Select(h => (h.Kind, h.Folder)).OrderBy(h => h.Kind).ToArray());
    }

    [Theory]
    [InlineData("--xunit")]
    [InlineData("--tests")]
    [InlineData("--test")]
    public void XunitFlagAndLegacyAliasesSelectTheSameHost(string flag)
    {
        var command = CommandLine.Parse(["add", flag, "Game.integration", flag]);
        Assert.Equal([(HostKind.Tests, "Game.integration"), (HostKind.Tests, (string?)null)],
            command.Requested.Select(h => (h.Kind, h.Folder)).ToArray());
        var help = Usage.Render(Verb.Add);
        Assert.Contains("--xunit", help);
        Assert.Contains("--nunit", help);
        Assert.DoesNotContain("--tests", help);
        Assert.DoesNotContain("--no-tests", help);
    }

    [Theory]
    [InlineData("new")]
    [InlineData("add")]
    public void UnattendedDefaultsDoNotCreateTestHosts(string verb)
    {
        using var tmp = new TempProjectDir();
        var dir = Path.Combine(tmp.Dir, "Game");
        string[] args;
        if (verb == "new") args = ["new", "Game", dir, "--yes", "--no-restore"];
        else
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "project.godot"), "[application]\nconfig/name=\"Game\"\n");
            args = ["add", dir, "--yes", "--no-restore"];
        }

        Assert.Equal(0, CliConsole.Run(args).ExitCode);
        Assert.Equal([HostKind.Desktop, HostKind.Web], HostScan.Find(dir).Select(h => h.Kind).Order().ToArray());
        Assert.False(Directory.Exists(Path.Combine(dir, "Game.xunit")));
        Assert.False(Directory.Exists(Path.Combine(dir, "Game.nunit")));
    }

    [Fact]
    public void NewNUnitHostScaffoldsRunnableTestsAndDoctorRecognizesIt()
    {
        using var tmp = new TempProjectDir();
        var dir = Path.Combine(tmp.Dir, "Game");
        Assert.Equal(0, CliConsole.Run("new", "Game", dir, "--nunit", "--no-restore").ExitCode);
        Assert.Equal(HostKind.NUnit, Assert.Single(HostScan.Find(dir)).Kind);
        var project = File.ReadAllText(Path.Combine(dir, "Game.nunit", "Game.nunit.csproj"));
        Assert.Contains("Include=\"2dog.nunit\"", project);
        Assert.Contains("Include=\"NUnit3TestAdapter\"", project);
        Assert.Contains("../Game.csproj", project);
        Assert.True(File.Exists(Path.Combine(dir, "Game.nunit", ".gdignore")));
        Assert.Contains("Game.nunit.csproj", File.ReadAllText(Path.Combine(dir, "Game.slnx")));
        Assert.Contains("Game.nunit/**", File.ReadAllText(Path.Combine(dir, "Game.csproj")));
        var tests = File.ReadAllText(Path.Combine(dir, "Game.nunit", "BasicTests.cs"));
        Assert.Contains("namespace Game.Tests;", tests);
        Assert.Contains(": GodotTestFixture", tests);
        Assert.Contains("GodotAssert.ExpectSignal", tests);
        Assert.DoesNotContain("Xunit", tests);
        var model = ProjectModel.Load(dir);
        var host = Assert.Single(model.Hosts);
        Assert.Equal(HostKind.NUnit, host.Kind);
        Assert.True(host.HasGdIgnore);
        Assert.Contains(host.Packages, package => package.Id == "2dog.nunit");
        Assert.True(VersionRewriter.IsTwoDogPackage("2dog.nunit"));
        Assert.Equal("$(TwoDogVersion)", VersionRewriter.Reference("2dog.nunit"));
    }
}

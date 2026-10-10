using twodog.cli;

namespace twodog.tests.ToolTests;

public class ReplHostTests
{
    [Fact]
    public void ReplIsOptInSupportsCustomFoldersAndIsRecognizedByItsPackage()
    {
        Assert.False(Hosts.InDefaultSet(HostKind.Repl));
        Assert.False(Hosts.ExcludedFromSolutionBuild(HostKind.Repl));
        Assert.Equal(HostGroup.Desktop, Hosts.Group(HostKind.Repl));
        var command = CommandLine.Parse(["new", "Game", "--repl", "Game.console"]);
        var hosts = HostSelection.FromFlags(command, new ProjectContext { Dir = ".", BaseName = "Game" });
        Assert.Equal(new HostSpec(HostKind.Repl, "Game.console"), Assert.Single(hosts));
        Assert.Equal(HostKind.Repl, HostScan.Classify("<Project><PackageReference Include='2dog.repl'/><OutputType>Exe</OutputType></Project>", "custom"));
        Assert.Equal(HostKind.Repl, HostScan.ClassifyText("<PackageReference Include='2dog.repl'>", "custom"));
        Assert.True(VersionRewriter.IsTwoDogPackage("2dog.repl"));
        Assert.Equal("$(TwoDogVersion)", VersionRewriter.Reference("2dog.repl"));
    }

    [Fact]
    public void ScaffoldCreatesConsoleHostWithJitSettingsAndGameAssemblyPreload()
    {
        using var tmp = new TempProjectDir();
        var dir = Path.Combine(tmp.Dir, "Game");
        var result = CliConsole.Run("new", "Game", dir, "--repl", "--no-restore");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("dotnet run --project Game.repl", result.Stdout);
        Assert.Equal(HostKind.Repl, Assert.Single(HostScan.Find(dir)).Kind);
        var project = File.ReadAllText(Path.Combine(dir, "Game.repl", "Game.repl.csproj"));
        Assert.Contains("Include=\"2dog.repl\"", project);
        Assert.Contains("<OutputType>Exe</OutputType>", project);
        Assert.DoesNotContain("WinExe", project);
        Assert.Contains("<PublishAot>false</PublishAot>", project);
        Assert.Contains("<PublishTrimmed>false</PublishTrimmed>", project);
        Assert.Contains("<PublishSingleFile>false</PublishSingleFile>", project);
        Assert.True(File.Exists(Path.Combine(dir, "Game.repl", ".gdignore")));
        Assert.Contains("Game.repl.csproj", File.ReadAllText(Path.Combine(dir, "Game.slnx")));
        Assert.Contains("Game.repl/**", File.ReadAllText(Path.Combine(dir, "Game.csproj")));
        Assert.Contains("Assembly.Load(\"Game\")", File.ReadAllText(Path.Combine(dir, "Game.repl", "Program.cs")));
        Assert.Equal(HostKind.Repl, Assert.Single(ProjectModel.Load(dir).Hosts).Kind);
    }
}

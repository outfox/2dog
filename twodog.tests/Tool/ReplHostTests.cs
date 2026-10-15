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
        // Agents get the guide with the host.
        Assert.Equal(SkillCommand.Skill, File.ReadAllText(Path.Combine(dir, SkillCommand.SkillFile)));
        Assert.Contains("2dog-repl/SKILL.md", CliConsole.Run("add", dir, "--repl", "Game.second", "--no-restore").Stdout);
    }

    [Fact]
    public void SkillWritesTheGuideForClaudeAndCodexAndLeavesTheRestOfAgentsMdAlone()
    {
        using var tmp = new TempProjectDir();
        // The tool and a running host's ':help agents' show the same guide.
        Assert.Equal(twodog.Repl.ReplOutput.AgentGuide, SkillCommand.Guide);
        Assert.StartsWith("---\nname: 2dog-repl\n", SkillCommand.Skill);

        Assert.Contains("would: create .claude/skills/2dog-repl/SKILL.md", CliConsole.Run("skill", tmp.Dir, "--dry-run").Stdout);
        Assert.False(Directory.Exists(Path.Combine(tmp.Dir, ".claude")));
        Assert.Equal(0, CliConsole.Run("skill", tmp.Dir).ExitCode);
        Assert.Contains("is up to date", CliConsole.Run("skill", tmp.Dir, "--claude").Stdout);

        var agents = Path.Combine(tmp.Dir, "AGENTS.md");
        File.WriteAllText(agents, "# Project rules\r\n\r\nKeep it tidy.\r\n");
        var json = CliConsole.Run("skill", tmp.Dir, "--codex", "--json");
        Assert.Contains("\"status\": \"updated\"", json.Stdout);
        var text = File.ReadAllText(agents);
        Assert.StartsWith("# Project rules\n\nKeep it tidy.\n\n" + SkillCommand.Begin, text);
        // A later run replaces only the section.
        File.WriteAllText(agents, text.Replace("Keep it tidy.", "Keep it tidier.").Replace("## Connect", "## Old"));
        CliConsole.Run("skill", tmp.Dir, "--codex");
        var replaced = File.ReadAllText(agents);
        Assert.Contains("Keep it tidier.", replaced);
        Assert.Contains("## Connect", replaced);
        Assert.Single(replaced.Split(SkillCommand.Begin), part => part.Length > 0 && !part.StartsWith("# Project"));
        Assert.Equal(SkillCommand.Section(null), SkillCommand.Section(""));
        Assert.Equal(2, CliConsole.Run("skill", Path.Combine(tmp.Dir, "missing")).ExitCode);
    }
}

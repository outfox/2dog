using twodog.cli;

namespace twodog.tests.ToolTests;

public class ProgramTests
{
    [Fact]
    public void Main_HandlesHelpVersionAndUserErrors()
    {
        Assert.Equal(0, Program.Main([]));
        Assert.Equal(0, Program.Main(["help"]));
        Assert.Equal(0, Program.Main(["--version"]));
        Assert.Equal(0, Program.Main(["version"]));
        Assert.Equal(2, Program.Main(["--pin", "1.2.3"]));
        Assert.Equal(1, Program.Main(["--unknown"]));
        Assert.Equal(1, Program.Main(["new", "--desktop"]));

        var missing = Path.Combine(Path.GetTempPath(), "2dog-missing-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(2, Program.Main(["add", missing, "--desktop"]));
    }

    [Fact]
    public void Main_NonInteractiveDryRun_ExercisesFullDispatchWithoutWriting()
    {
        using var tmp = new TempProjectDir();
        tmp.Write("project.godot", "[application]\nconfig/name=\"My Game\"\n");

        Assert.Equal(0, Program.Main(["add", tmp.Dir, "--desktop", "--dry-run", "--no-restore"]));

        Assert.False(File.Exists(Path.Combine(tmp.Dir, "MyGame.csproj")));
    }

    // Bare `2dog` shows version info + usage; anything more without a verb is
    // an error (the old create-vs-add auto-detection is gone).
    [Fact]
    public void VerblessArguments_AreUsageErrors()
    {
        Assert.Equal(Verb.None, CommandLine.Parse([]).Verb);
        Assert.Throws<UsageException>(() => CommandLine.Parse(["some/path"]));
        Assert.Throws<UsageException>(() => CommandLine.Parse(["--desktop"]));
        Assert.Throws<UsageException>(() => CommandLine.Parse(["--yes"]));
    }

    [Fact]
    public void PrepareNewProject_MapsNameAndOutputIntoScaffoldOptions()
    {
        var cmd = CommandLine.Parse(["new", "My Game!", "games/mine", "--desktop"]);

        Program.PrepareNewProject(cmd, interactive: false);

        Assert.Equal("My Game!", cmd.Options.NameOverride);
        Assert.Equal("games/mine", cmd.Options.ProjectPath);
        Assert.True(cmd.Options.CreateProject);
    }

    [Fact]
    public void IsEmpty_IgnoresDotfilesButNotRegularEntries()
    {
        using var tmp = new TempProjectDir();
        var missing = Path.Combine(tmp.Dir, "missing");
        Assert.True(Program.IsEmpty(missing));

        tmp.Write(".gitignore", "bin/");
        Assert.True(Program.IsEmpty(tmp.Dir));

        tmp.Write("README.md", "content");
        Assert.False(Program.IsEmpty(tmp.Dir));
    }
}

public class CommandLineCoverageTests
{
    [Fact]
    public void Parse_RecognizesAliasesAndAllScalarOptions()
    {
        var cmd = CommandLine.Parse(
            ["convert", "-n", "Game", "--2dog", "Desk", "--browser", "Web", "--test", "Tests",
             "--winforms", "Forms", "--dry-run", "--force", "--verbose", "--no-input"]);

        Assert.Equal(Verb.Add, cmd.Verb);
        Assert.Equal("Game", cmd.Options.NameOverride);
        Assert.True(cmd.Options.DryRun);
        Assert.True(cmd.Options.Force);
        Assert.True(cmd.Options.Verbose);
        Assert.True(cmd.NoInteractive);
        Assert.Equal(
            [(HostKind.Desktop, "Desk"), (HostKind.Web, "Web"), (HostKind.Tests, "Tests"),
             (HostKind.WinForms, "Forms")],
            cmd.Requested.Select(r => (r.Kind, r.Folder!)).ToArray());
    }

    [Fact]
    public void Parse_OutputFlagWinsOverNewProjectPositionals()
    {
        var cmd = CommandLine.Parse(["new", "PositionalName", "positional-dir", "-n", "FlagName", "-o", "flag-dir"]);

        Assert.Equal("FlagName", cmd.Options.NameOverride);
        Assert.Equal("flag-dir", cmd.OutputDir);
    }

    [Theory]
    [InlineData("new", "one", "two", "three")]
    [InlineData("new", "--output")]
    [InlineData("add", "--name", "--force")]
    public void Parse_RejectsAdditionalMalformedForms(params string[] args) =>
        Assert.Throws<UsageException>(() => CommandLine.Parse(args));

    [Theory]
    [InlineData(".")]
    [InlineData("folder/child")]
    [InlineData("folder\\child")]
    public void HostFlag_LeavesPathLikeTokensAsTheProjectPath(string path)
    {
        var cmd = CommandLine.Parse(["add", "--desktop", path]);

        Assert.Null(cmd.Requested.Single().Folder);
        Assert.Equal(path, cmd.Options.ProjectPath);
    }
}

public class HostsCoverageTests
{
    [Theory]
    [InlineData("2dog", "desktop", "your own Main(), runs the game on desktop", "--desktop")]
    [InlineData("web", "browser", "WebAssembly host, published as a static bundle", "--web")]
    [InlineData("tests", "tests", "xUnit project driving a headless engine", "--tests")]
    [InlineData("winforms", "winforms", "game embedded in a WinForms window (Windows-only)", "--winforms")]
    public void HostMetadata_IsComplete(string suffix, string label, string blurb, string flag)
    {
        var kind = HostKinds.Of(suffix);
        Assert.Equal(label, Hosts.Label(kind));
        Assert.Equal(blurb, Hosts.Blurb(kind));
        Assert.Equal(flag, Hosts.Flag(kind));
    }

    [Fact]
    public void HostMetadata_RejectsUnknownKinds()
    {
        var unknown = (HostKind)999;
        Assert.Throws<ArgumentOutOfRangeException>(() => Hosts.Suffix(unknown));
        Assert.Throws<ArgumentOutOfRangeException>(() => Hosts.Label(unknown));
        Assert.Throws<ArgumentOutOfRangeException>(() => Hosts.Blurb(unknown));
        Assert.Throws<ArgumentOutOfRangeException>(() => Hosts.Flag(unknown));
    }

    [Fact]
    public void Classify_UsesSuffixForOtherwiseUnrecognizedTwoDogHosts()
    {
        const string wired = "<Project><GodotProjectDir>..</GodotProjectDir></Project>";
        Assert.Equal(HostKind.Web, HostScan.Classify(wired, "custom.web"));
        Assert.Equal(HostKind.Desktop, HostScan.Classify(wired, "custom"));
        Assert.Empty(HostScan.Find(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
    }

    [Fact]
    public void FromFlags_RejectsFolderNamesWithoutLettersOrDigits()
    {
        var cmd = CommandLine.Parse(["add", "--desktop", "!!!"]);
        var project = new ProjectContext { Dir = ".", BaseName = "Game" };

        Assert.Throws<ToolException>(() => HostSelection.FromFlags(cmd, project));
    }
}

public class SolutionOpsCoverageTests
{
    [Fact]
    public void CreateSolution_RequiresSlnxAndDeclaresAllBuildTypes()
    {
        using var tmp = new TempProjectDir();
        Assert.Throws<ArgumentException>(() => SolutionOps.CreateSolution(Path.Combine(tmp.Dir, "Game.sln")));

        var slnx = Path.Combine(tmp.Dir, "Game.slnx");
        SolutionOps.CreateSolution(slnx);
        var text = File.ReadAllText(slnx);
        Assert.Contains("Debug", text);
        Assert.Contains("Editor", text);
        Assert.Contains("Release", text);
    }

    [Fact]
    public void MigrateToSlnx_RejectsWrongExtensionsAndConflictingTargets()
    {
        using var tmp = new TempProjectDir();
        Assert.Throws<ArgumentException>(() => SolutionOps.MigrateToSlnx(Path.Combine(tmp.Dir, "Game.slnx")));

        var sln = tmp.Write("Game.sln", "classic");
        tmp.Write("Game.slnx", "modern");
        Assert.Throws<ToolException>(() => SolutionOps.MigrateToSlnx(sln));
    }

    [Fact]
    public void ExcludeFromSolutionBuild_CoversSlnxNoOpAndEditorMappingBranches()
    {
        using var tmp = new TempProjectDir();
        var missing = Path.Combine(tmp.Dir, "missing.slnx");
        Assert.False(SolutionOps.ExcludeFromSolutionBuild(missing, "Game.web/Game.web.csproj"));

        var slnx = tmp.Write("Game.slnx",
            "<Solution>\n  <Configurations><BuildType Name=\"Editor\" /></Configurations>\n" +
            "  <Project Path=\"Game.web/Game.web.csproj\" />\n</Solution>\n");
        Assert.True(SolutionOps.ExcludeFromSolutionBuild(slnx, "Game.web\\Game.web.csproj"));
        var updated = File.ReadAllText(slnx);
        Assert.Contains("<BuildType Solution=\"Editor|*\" Project=\"Debug\" />", updated);
        Assert.Contains("<Build Project=\"false\" />", updated);

        Assert.True(SolutionOps.ExcludeFromSolutionBuild(slnx, "Game.web/Game.web.csproj"));
        Assert.False(SolutionOps.ExcludeFromSolutionBuild(slnx, "Other/Other.csproj"));
        Assert.False(SolutionOps.ExcludeFromSolutionBuild(tmp.Write("notes.txt", "text"), "anything"));
    }

    [Fact]
    public void Restore_ReportsSuccessForAnEmptySolution()
    {
        using var tmp = new TempProjectDir();
        var slnx = Path.Combine(tmp.Dir, "Empty.slnx");
        SolutionOps.CreateSolution(slnx);

        SolutionOps.Restore(slnx, out var succeeded);

        Assert.True(succeeded);
    }
}

public class ScaffoldCoverageTests
{
    [Fact]
    public void Open_RejectsMissingProjectsAndInvalidNewProjectNames()
    {
        using var tmp = new TempProjectDir();
        var missing = Path.Combine(tmp.Dir, "missing");
        Assert.Throws<ToolException>(() => ScaffoldCommand.Open(new ScaffoldOptions { ProjectPath = missing }));
        Assert.Throws<ToolException>(() => ScaffoldCommand.Open(new ScaffoldOptions
        {
            ProjectPath = missing,
            CreateProject = true,
            NameOverride = "!!!",
        }));
    }

    [Fact]
    public void Run_CancelledPlanDoesNotWriteAnything()
    {
        using var tmp = new TempProjectDir();
        var projectGodot = tmp.Write("project.godot", "[application]\nconfig/name=\"Game\"\n");
        var options = new ScaffoldOptions
        {
            ProjectPath = tmp.Dir,
            Restore = false,
            Hosts = [new HostSpec(HostKind.Desktop, "Game.desktop")],
        };
        var project = ScaffoldCommand.Open(options);
        IReadOnlyList<string>? descriptions = null;

        Assert.Equal(0, ScaffoldCommand.Run(project, options, plan =>
        {
            descriptions = plan;
            return false;
        }));

        Assert.NotNull(descriptions);
        Assert.Contains(descriptions, d => d.Contains("Game.desktop"));
        Assert.Equal("[application]\nconfig/name=\"Game\"\n", File.ReadAllText(projectGodot));
        Assert.False(File.Exists(Path.Combine(tmp.Dir, "Game.csproj")));
    }
}

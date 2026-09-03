using System.Text.RegularExpressions;
using twodog.cli;

namespace twodog.tests.ToolTests;

// Output modes: how the environment, the console and the output flags decide colour, prompts, spinners, glyphs
// and which lines are printed where.
public class OutputModeTests
{
    private static readonly ConsoleFacts Tty = new(false, false, false, true);

    private static OutputMode Resolve(string[] args, params (string Name, string Value)[] env) =>
        OutputMode.Resolve(args, name => env.FirstOrDefault(e => e.Name == name).Value, Tty);

    [Fact]
    public void Terminal_WithoutFlags_AllowsEverything()
    {
        var mode = Resolve([]);
        Assert.True(mode.CanPrompt);
        Assert.True(mode.Animate);
        Assert.True(mode.Unicode);
        Assert.False(mode.Plain);
        Assert.False(mode.NoColor);
    }

    [Theory]
    [InlineData("CI")]
    [InlineData("GITHUB_ACTIONS")]
    [InlineData("TF_BUILD")]
    [InlineData("GITLAB_CI")]
    [InlineData("TEAMCITY_VERSION")]
    [InlineData("BUILD_NUMBER")]
    public void CiVariables_TurnPromptsAndAnimationOff(string variable)
    {
        var mode = Resolve([], (variable, "true"));
        Assert.True(mode.Ci);
        Assert.False(mode.CanPrompt);
        Assert.False(mode.Animate);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("")]
    public void CiVariable_SetToNothing_DoesNotCount(string value) =>
        Assert.False(Resolve([], ("CI", value)).Ci);

    [Fact]
    public void NoColor_KeepsCursorControlButDropsColour()
    {
        var mode = Resolve([], ("NO_COLOR", "1"));
        Assert.True(mode.NoColor);
        Assert.False(mode.Plain);
        Assert.True(mode.Animate);
        Assert.True(Resolve(["--no-color"]).NoColor);
    }

    [Fact]
    public void DumbTerminal_MeansPlainAndAccessible()
    {
        var mode = Resolve([], ("TERM", "dumb"));
        Assert.True(mode.Plain);
        Assert.True(mode.Accessible);
        Assert.False(mode.Unicode);
        Assert.False(mode.Animate);
        Assert.True(mode.CanPrompt);
        Assert.False(mode.ForceColor);
    }

    [Fact]
    public void ForceColor_IsHonouredUnlessPlain()
    {
        Assert.True(Resolve([], ("CLICOLOR_FORCE", "1")).ForceColor);
        Assert.True(Resolve([], ("FORCE_COLOR", "3")).ForceColor);
        Assert.False(Resolve(["--plain"], ("FORCE_COLOR", "1")).ForceColor);
    }

    [Fact]
    public void AccessibleMode_ComesFromFlagOrEnvironment()
    {
        Assert.True(Resolve(["--accessible"]).Accessible);
        Assert.True(Resolve([], ("TWODOG_ACCESSIBLE", "1")).Accessible);
        Assert.False(Resolve([], ("TWODOG_ACCESSIBLE", "0")).Accessible);
        Assert.False(Resolve(["--accessible"]).Animate);
        Assert.True(Resolve(["--accessible"]).CanPrompt);
    }

    [Fact]
    public void Json_ImpliesNoPromptsAndNoAnimation()
    {
        var mode = Resolve(["add", "--json"]);
        Assert.True(mode.Json);
        Assert.False(mode.CanPrompt);
        Assert.False(mode.Animate);
    }

    [Fact]
    public void Flags_AfterDoubleDash_AreNotModeFlags()
    {
        Assert.False(Resolve(["add", "--", "--json"]).Json);
        Assert.True(Resolve(["add", "--quiet=x"]).Quiet);
        Assert.True(Resolve(["add", "-q"]).Quiet);
        Assert.True(Resolve(["add", "-v"]).Verbose);
    }

    [Fact]
    public void RedirectedOutput_MeansAsciiAndNoPrompts()
    {
        var mode = OutputMode.Resolve([], _ => null, ConsoleFacts.Redirected);
        Assert.False(mode.Unicode);
        Assert.False(mode.CanPrompt);
        Assert.False(mode.Animate);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("help", "add")]
    [InlineData("add", "--dekstop")]
    public void CapturedOutput_HasNoEscapeSequences(params string[] args)
    {
        var run = CliConsole.Run(args);
        Assert.DoesNotMatch("\x1b", run.Stdout);
        Assert.DoesNotMatch("\x1b", run.Stderr);
    }

    /// <summary>Spectre's built-in CI enrichers force ANSI on under GITHUB_ACTIONS; the gateway must not let them.</summary>
    [Fact]
    public void CiEnvironment_DoesNotForceEscapeSequences()
    {
        // Pinned, not set process-wide, and under the capture lock: other collections run in parallel and Out is global.
        var run = CliConsole.Capture(() =>
        {
            Out.PinConsoleFacts(ConsoleFacts.Redirected, new Dictionary<string, string> { ["GITHUB_ACTIONS"] = "true" });
            Assert.True(Out.Mode.Ci);
            return Program.Main(["--help"]);
        });
        Assert.DoesNotMatch("\x1b", run.Stdout);
        Assert.DoesNotMatch("\x1b", run.Stderr);
    }

    [Fact]
    public void Quiet_KeepsResultsAndProblems_DropsNarration()
    {
        using var tmp = new TempProjectDir();
        tmp.Write("project.godot", "[application]\nconfig/name=\"Game\"\n");
        tmp.Write("global.json", "{}");

        var loud = CliConsole.Run("add", tmp.Dir, "--web", "--dry-run", "--no-restore");
        Assert.Equal(0, loud.ExitCode);
        Assert.Contains("would:", loud.Stdout);
        Assert.Contains("Dry run:", loud.Stdout);
        Assert.Contains("warning: global.json already exists", loud.Stderr);
        Assert.DoesNotContain("warning:", loud.Stdout);

        var quiet = CliConsole.Run("add", tmp.Dir, "--web", "--dry-run", "--no-restore", "--quiet");
        Assert.Equal(0, quiet.ExitCode);
        Assert.Contains("would:", quiet.Stdout);
        Assert.DoesNotContain("Dry run:", quiet.Stdout);
        Assert.DoesNotContain("plan", quiet.Stdout);
        Assert.Contains("warning: global.json already exists", quiet.Stderr);
    }

    [Fact]
    public void NoTerminal_AppliesDefaultsWithANote()
    {
        using var tmp = new TempProjectDir();
        tmp.Write("project.godot", "[application]\nconfig/name=\"Game\"\n");

        var run = CliConsole.Run("add", tmp.Dir, "--dry-run", "--no-restore");
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("note: no terminal to ask on", run.Stderr);
        Assert.Contains("Game.2dog", run.Stdout);
        Assert.Contains("Game.web", run.Stdout);

        var explicitRun = CliConsole.Run("add", tmp.Dir, "--dry-run", "--no-restore", "--yes");
        Assert.DoesNotContain("note: no terminal", explicitRun.Stderr);
    }

    [Fact]
    public void NoOpExclusion_IsReportedAsANote()
    {
        using var tmp = new TempProjectDir();
        tmp.Write("project.godot", "[application]\nconfig/name=\"Game\"\n");

        var run = CliConsole.Run("add", tmp.Dir, "--no-winui", "--dry-run", "--no-restore", "--yes");
        Assert.Contains("note: --no-winui changes nothing", run.Stderr);
    }

    [Fact]
    public void Glyphs_FallBackToAscii_WhenRedirected()
    {
        var run = CliConsole.Capture(() =>
        {
            Out.VersionTable([("tool", "1.0", VersionMark.UpToDate, "a"), ("natives", "1.0", VersionMark.Outdated, "b")]);
            Out.Rule("done");
            Assert.Equal("ascii", Out.Glyph("utf8", "ascii"));
            return 0;
        });
        Assert.Contains("ok", run.Stdout);
        Assert.Contains("new", run.Stdout);
        Assert.Contains("-- done --", run.Stdout);
        Assert.DoesNotContain("✅", run.Stdout);
    }
}

// Every Out helper must be safe against markup in user text and must keep the greppable prefixes.
public class OutGuardTests
{
    private const string Hostile = "[bold]x[/] ]] [[ [red";

    [Fact]
    public void Helpers_PrintUserTextLiterally()
    {
        var run = CliConsole.Capture(() =>
        {
            Out.Note(Hostile);
            Out.Warning(Hostile);
            Out.Skip(Hostile);
            Out.Would(Hostile);
            Out.Action(Hostile);
            Out.ErrorLine(Hostile);
            Out.Hint(Hostile);
            Out.Plan([new ActionReport(Hostile, ActionKind.CreateFile, ActionStatus.Planned)]);
            Out.NextSteps($"cd {Hostile}", [(Hostile, Hostile)]);
            Out.VersionTable([(Hostile, Hostile, null, Hostile)]);
            return 0;
        });

        Assert.Equal(10, Regex.Matches(run.Stdout, Regex.Escape(Hostile)).Count);
        Assert.Equal(4, Regex.Matches(run.Stderr, Regex.Escape(Hostile)).Count);
        foreach (var prefix in new[] { "note:", "warning:", "error:", "hint:" })
            Assert.Contains(prefix, run.Stderr);
        foreach (var prefix in new[] { "skip:", "would:", "+ " })
            Assert.Contains(prefix, run.Stdout);
    }

    [Fact]
    public void Diagnostics_AreCollectedForTheEnvelope()
    {
        CliConsole.Capture(() =>
        {
            // Contains, not Equal: the collectors are flow-local, but this flow prints its own header first.
            Out.Note("n");
            Out.Warning("w");
            Out.ErrorLine("e");
            Out.Hint("h");
            Assert.Contains("n", Out.Notes);
            Assert.Contains("w", Out.Warnings);
            Assert.Contains("e", Out.Errors);
            Assert.Contains("h", Out.Hints);
            return 0;
        });
    }

    [Fact]
    public void TerminalDirty_IsClearedByARestore_AndByANewRun()
    {
        // Under the capture lock: a parallel run's mode switch (--json) must not change what a restore does.
        CliConsole.Capture(() =>
        {
            Out.TerminalDirty = true;
            Out.RestoreTerminal();
            Assert.False(Out.TerminalDirty);

            Out.TerminalDirty = true;
            Program.Main(["--help"]);
            Assert.False(Out.TerminalDirty);
            return 0;
        });
    }

    [Fact]
    public void Verbose_IsSilentUnlessAsked()
    {
        var silent = CliConsole.Capture(() =>
        {
            Out.Verbose("hidden");
            return 0;
        });
        Assert.Equal("", silent.Stderr);

        var loud = CliConsole.Capture(() =>
        {
            Out.Configure(OutputMode.Resolve(["-v"], _ => null, ConsoleFacts.Redirected));
            Out.Verbose("shown");
            return 0;
        });
        Assert.Contains("verbose: shown", loud.Stderr);
    }

    // The gateway is the only place that talks to the console, otherwise capture and mode handling silently leak.
    [Fact]
    public void OnlyTheGateway_TalksToTheConsole()
    {
        var toolDir = Path.Combine(HelperToolTestBed.RepoRoot, "twodog");
        Assert.SkipWhen(!Directory.Exists(toolDir), "tool sources not available (packaged run)");

        string[] allowed = ["Out.cs", "Tui.cs", "ProcessRunner.cs", "Diagnostics.cs"];
        var offenders = Directory.EnumerateFiles(toolDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => !allowed.Contains(Path.GetFileName(f)))
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"\bAnsiConsole\.|\bConsole\.(Write|Out|Error)\b"))
            .Select(Path.GetFileName)
            .ToList();
        Assert.Empty(offenders);
    }
}

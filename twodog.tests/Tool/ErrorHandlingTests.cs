using System.Xml;
using twodog.cli;

namespace twodog.tests.ToolTests;

// Failures end in an `error:` line and a stable exit code, never a stack trace (unless --verbose asks for one),
// and a plan that breaks half-way says what stands and what never ran.
[Collection("doctor statics")] // DoctorCommand.Runner/Environment are process-wide seams: never swap them in parallel.
public class ErrorHandlingTests
{
    [Fact]
    public void MalformedCsproj_IsAFriendlyError_WithLineInformation()
    {
        using var tmp = new TempProjectDir();
        tmp.Write("project.godot", "[application]\nconfig/name=\"Game\"\n");
        tmp.Write("Game.csproj", "<Project Sdk=\"Godot.NET.Sdk/4.7.2\">\n  <PropertyGroup>\n</Project>\n");

        var run = CliConsole.Run("add", tmp.Dir, "--desktop", "--dry-run", "--no-restore");
        Assert.Equal(ExitCodes.Error, run.ExitCode);
        Assert.Contains("error: Game.csproj is not valid XML (line 3", run.Stderr);
        Assert.DoesNotContain("   at ", run.Stderr);

        var verbose = CliConsole.Run("add", tmp.Dir, "--desktop", "--dry-run", "--no-restore", "--verbose");
        Assert.Equal(ExitCodes.Error, verbose.ExitCode);
        Assert.Contains("verbose: System.Xml.XmlException", verbose.Stderr);
    }

    [Fact]
    public void PartialApply_ReportsWhatStandsAndWhatNeverRan()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() && Environment.UserName == "root", "root ignores read-only bits");
        using var tmp = new TempProjectDir();
        var godot = tmp.Write("project.godot", "[application]\nconfig/name=\"Game\"\n");
        File.SetAttributes(godot, FileAttributes.ReadOnly);
        try
        {
            var run = CliConsole.Run("add", tmp.Dir, "--desktop", "--no-restore", "--yes");

            Assert.Equal(ExitCodes.Error, run.ExitCode);
            Assert.Contains("error: step 2/", run.Stderr);
            Assert.Contains("append [dotnet] assembly_name", run.Stderr);
            Assert.Contains("note: 1 earlier step(s) stand: create Game.csproj", run.Stderr);
            Assert.Contains("later step(s) did not run", run.Stderr);
            Assert.Contains("hint: fix the cause and re-run", run.Stderr);
            Assert.True(File.Exists(Path.Combine(tmp.Dir, "Game.csproj")));
            Assert.False(Directory.Exists(Path.Combine(tmp.Dir, "Game.2dog")));
        }
        finally
        {
            File.SetAttributes(godot, FileAttributes.Normal);
        }
    }

    [Fact]
    public void FriendlyError_MapsTheUsualSuspects()
    {
        var (message, hint) = FriendlyError.Describe(new UnauthorizedAccessException("denied"));
        Assert.Equal("denied", message);
        Assert.Contains("read-only", hint);

        (message, hint) = FriendlyError.Describe(new XmlException("bad", null, 4, 7));
        Assert.Contains("is not valid XML (line 4, column 7): bad", message);
        Assert.Null(hint);

        (message, hint) = FriendlyError.Describe(new InvalidOperationException("boom"));
        Assert.Equal("unexpected InvalidOperationException: boom", message);
        Assert.Contains("--verbose", hint);
        Assert.Contains("github.com/outfox/2dog/issues", hint);

        (_, hint) = FriendlyError.Describe(new ToolException("plain"));
        Assert.Null(hint);
    }

    [Fact]
    public void ToolVersions_AreAllBakedInAsRealVersions()
    {
        foreach (var value in new[]
                 {
                     ToolVersions.TwoDogVersion, ToolVersions.NativesVersion, ToolVersions.GodotSdkVersion,
                     ToolVersions.AvaloniaVersion, ToolVersions.WindowsAppSdkVersion, ToolVersions.AspNetCoreVersion,
                 })
            Assert.True(Version.TryParse(value, out _), $"'{value}' is not a version");
    }

    // A cancelled subprocess (Ctrl+C during `doctor --build`) ends the run with the cancelled code, not with 3 or 2.
    [Fact]
    public void CancelledSubprocess_EndsInTheCancelledExitCode()
    {
        using var tmp = new TempProjectDir();
        tmp.Write("project.godot", "[application]\nconfig/name=\"Game\"\n\n[dotnet]\nproject/assembly_name=\"Game\"\n");
        tmp.Write("Game.csproj", "<Project Sdk=\"Godot.NET.Sdk/4.7.2\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n");

        var previousRunner = DoctorCommand.Runner;
        var previousEnv = DoctorCommand.Environment;
        DoctorCommand.Runner = new FakeProcessRunner(r => r.Args.Contains("build")
            ? throw new OperationCanceledException()
            : FakeProcessRunner.Result(r, 0));
        DoctorCommand.Environment = new FakeEnvironment();
        try
        {
            var run = CliConsole.Run("doctor", tmp.Dir, "--offline", "--build");
            Assert.Equal(ExitCodes.Cancelled, run.ExitCode);
            Assert.Contains("error: cancelled", run.Stderr);
        }
        finally
        {
            DoctorCommand.Runner = previousRunner;
            DoctorCommand.Environment = previousEnv;
        }
    }

    [Fact]
    public void ExitCodes_AreStable()
    {
        Assert.Equal(0, ExitCodes.Ok);
        Assert.Equal(1, ExitCodes.Usage);
        Assert.Equal(2, ExitCodes.Error);
        Assert.Equal(3, ExitCodes.Findings);
        Assert.Equal(130, ExitCodes.Cancelled);
    }
}

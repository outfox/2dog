using twodog.cli;

namespace twodog.tests.ToolTests;

public class ScaffoldRestoreTests
{
    [Theory]
    [InlineData("android", "error NETSDK1147: The following workloads must be installed: android", "dotnet workload install android", "wasm-tools")]
    [InlineData("web", "error NETSDK1147: The following workloads must be installed: wasm-tools", "dotnet workload install wasm-tools", "install android")]
    [InlineData("2dog", "error NU1301: Unable to load the service index for source https://example.invalid/index.json", "Unable to load the service index", "workload install")]
    public void FailedRestore_ExplainsTheActualCauseAndPreservesTheScaffold(string kind, string error, string expected, string unrelated)
    {
        using var tmp = new TempProjectDir();
        var dir = Path.Combine(tmp.Dir, "Game");
        var options = new ScaffoldOptions
        {
            ProjectPath = dir, NameOverride = "Game", CreateProject = true, Restore = true,
            Hosts = [new HostSpec(HostKinds.Of(kind), $"Game.{kind}")],
        };
        var runner = new FakeProcessRunner(request =>
        {
            Assert.Equal("restore", request.Args[0]);
            return FakeProcessRunner.Result(request, 1, error);
        });
        ScaffoldResult? result = null;

        var run = CliConsole.Capture(() =>
        {
            result = ScaffoldCommand.Run(ScaffoldCommand.Open(options), options, workloadRunner: runner);
            return result.ExitCode;
        });

        Assert.Equal(ExitCodes.Error, run.ExitCode);
        Assert.Contains(expected, run.Stdout);
        Assert.Contains(error, run.Stderr);
        Assert.DoesNotContain(unrelated, run.Stdout + run.Stderr);
        Assert.Contains(result!.Actions, action => action.Kind == ActionKind.Restore && action.Status == ActionStatus.Failed);
        Assert.True(File.Exists(Path.Combine(dir, "project.godot")));
        Assert.True(File.Exists(Path.Combine(dir, $"Game.{kind}", $"Game.{kind}.csproj")));
        Assert.Empty(result.NextSteps);
    }
}

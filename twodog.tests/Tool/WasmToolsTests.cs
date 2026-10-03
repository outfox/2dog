using twodog.cli;

namespace twodog.tests.ToolTests;

public class WasmToolsTests
{
    private static FakeProcessRunner Runner(bool installed, int installExit = 0) => new(request =>
        request.Args.SequenceEqual(["workload", "list"])
            ? FakeProcessRunner.Result(request, 0, "Installed Workload Id   Manifest Version   Installation Source",
                "------------------------------------------------------------", installed ? "wasm-tools  10.0.100  SDK" : "", "")
            : FakeProcessRunner.Result(request, installExit, installExit == 0 ? "Success" : "installation denied"));

    [Fact]
    public void Installed_DoesNotOfferOrInstall()
    {
        var runner = Runner(installed: true);
        WasmTools.EnsureInstalled(".", false, () => throw new Exception("must not prompt"), runner);
        Assert.Single(runner.Requests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Missing_InstallsOnlyAfterConsent(bool consent)
    {
        var runner = Runner(installed: false);
        var offers = 0;
        WasmTools.EnsureInstalled(".", false, () => { offers++; return consent; }, runner);
        Assert.Equal(1, offers);
        Assert.Equal(consent ? 2 : 1, runner.Requests.Count);
        if (consent) Assert.Equal(["workload", "install", "wasm-tools"], runner.Requests[1].Args);
    }

    [Fact]
    public void Missing_WithoutATerminal_DoesNotInstall()
    {
        var runner = Runner(installed: false);
        WasmTools.EnsureInstalled(".", false, null, runner);
        Assert.Single(runner.Requests);
    }

    [Fact]
    public void ExplicitInstall_DoesNotPrompt()
    {
        var runner = Runner(installed: false);
        WasmTools.EnsureInstalled(".", true, () => throw new Exception("must not prompt"), runner);
        Assert.Equal(["workload", "install", "wasm-tools"], runner.Requests[1].Args);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Update_InstalledWorkload_UsesASeparateOffer(bool consent)
    {
        var runner = Runner(installed: true);
        WasmTools.EnsureInstalled(".", false, () => throw new Exception("must not offer installation"), runner,
            confirmUpdate: () => consent);
        Assert.Equal(consent ? 2 : 1, runner.Requests.Count);
        if (consent) Assert.Equal(["workload", "update"], runner.Requests[1].Args);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExplicitUpdate_InstallsMissingOrUpdatesInstalled(bool installed)
    {
        var runner = Runner(installed);
        WasmTools.EnsureInstalled(".", false, () => throw new Exception("must not prompt"), runner,
            update: true, confirmUpdate: () => throw new Exception("must not prompt"));
        Assert.Equal(installed ? ["workload", "update"] : ["workload", "install", "wasm-tools"], runner.Requests[1].Args);
    }

    [Fact]
    public void FailedProbe_DoesNotOfferOrInstall()
    {
        var runner = new FakeProcessRunner(request => FakeProcessRunner.Result(request, 1, "SDK not found"));
        WasmTools.EnsureInstalled(".", false, () => throw new Exception("must not prompt"), runner);
        Assert.Single(runner.Requests);
        Assert.Throws<ToolException>(() => WasmTools.EnsureInstalled(".", true, null, runner));
    }

    [Fact]
    public void FailedInstallation_ReportsOutputAndFails()
    {
        var runner = Runner(installed: false, installExit: 1);
        var run = CliConsole.Capture(() =>
        {
            Assert.Throws<ToolException>(() => WasmTools.EnsureInstalled(".", true, null, runner));
            return 0;
        });
        Assert.Contains("installation denied", run.Stderr);
        Assert.Contains(WasmTools.InstallCommand, run.Stderr);
    }

    [Fact]
    public void Cancellation_IsPropagated()
    {
        var runner = new FakeProcessRunner(_ => throw new OperationCanceledException());
        Assert.Throws<OperationCanceledException>(() => WasmTools.EnsureInstalled(".", true, null, runner));
    }

    [Theory]
    [InlineData("new")]
    [InlineData("add")]
    [InlineData("update")]
    public void InstallFlag_ParsesForScaffoldingAndUpdate(string verb) =>
        Assert.True(CommandLine.Parse([verb, "--install-wasm-tools"]).Options.InstallWasmTools);

    [Fact]
    public void WorkloadFlags_ParseForDoctorAndUpdate()
    {
        Assert.True(CommandLine.Parse(["doctor", "--install-wasm-tools"]).Doctor!.InstallWasmTools);
        Assert.True(CommandLine.Parse(["update", "--update-workloads"]).Options.UpdateWorkloads);
    }

    [Theory]
    [InlineData("web")]
    [InlineData("webxr")]
    [InlineData("blazor")]
    public void Scaffold_InstallsForTheGeneratedProjectSdk(string suffix)
    {
        using var tmp = new TempProjectDir();
        var dir = Path.Combine(tmp.Dir, "Game");
        var runner = new FakeProcessRunner(request =>
        {
            Assert.Equal(dir, request.WorkingDir);
            Assert.True(File.Exists(Path.Combine(dir, "global.json")));
            return request.Args.Contains("list")
                ? FakeProcessRunner.Result(request, 0, "Installed Workload Id", "--------------------", "")
                : FakeProcessRunner.Result(request, 0);
        });
        var options = new ScaffoldOptions
        {
            ProjectPath = dir, NameOverride = "Game", CreateProject = true,
            Restore = false, InstallWasmTools = true, Hosts = [new HostSpec(HostKinds.Of(suffix), $"Game.{suffix}")],
        };
        var run = CliConsole.Capture(() => ScaffoldCommand.Run(ScaffoldCommand.Open(options), options,
            workloadRunner: runner).ExitCode);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(2, runner.Requests.Count);
    }

    [Theory]
    [InlineData("web", false)]
    [InlineData("2dog", true)]
    public void Scaffold_NoRestoreOrNoBrowserHost_DoesNotOffer(string kind, bool explicitInstall)
    {
        using var tmp = new TempProjectDir();
        var runner = new FakeProcessRunner(_ => throw new Exception("must not run a workload command"));
        var options = new ScaffoldOptions
        {
            ProjectPath = Path.Combine(tmp.Dir, "Game"), NameOverride = "Game", CreateProject = true,
            Restore = false, InstallWasmTools = explicitInstall,
            ConfirmWasmToolsInstall = () => throw new Exception("must not prompt"),
            Hosts = [new HostSpec(HostKinds.Of(kind), "Game.host")],
        };
        var run = CliConsole.Capture(() => ScaffoldCommand.Run(ScaffoldCommand.Open(options), options,
            workloadRunner: runner).ExitCode);
        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Empty(runner.Requests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DryRunOrCancelledPlan_NeverChecksOrInstalls(bool dryRun)
    {
        using var tmp = new TempProjectDir();
        var dir = Path.Combine(tmp.Dir, "Game");
        var runner = Runner(installed: false);
        var options = new ScaffoldOptions
        {
            ProjectPath = dir, NameOverride = "Game", CreateProject = true, DryRun = dryRun,
            Restore = false, InstallWasmTools = true, Hosts = [new HostSpec(HostKind.Web, "Game.web")],
        };
        var run = CliConsole.Capture(() => ScaffoldCommand.Run(ScaffoldCommand.Open(options), options,
            _ => false, runner).ExitCode);
        Assert.Equal(0, run.ExitCode);
        Assert.Empty(runner.Requests);
        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public void FailedInstallation_PreventsRestore()
    {
        using var tmp = new TempProjectDir();
        var options = new ScaffoldOptions
        {
            ProjectPath = Path.Combine(tmp.Dir, "Game"), NameOverride = "Game", CreateProject = true,
            InstallWasmTools = true, Hosts = [new HostSpec(HostKind.Web, "Game.web")],
        };
        var runner = Runner(installed: false, installExit: 1);
        ScaffoldResult? result = null;
        var run = CliConsole.Capture(() => (result = ScaffoldCommand.Run(ScaffoldCommand.Open(options), options,
            workloadRunner: runner)).ExitCode);
        Assert.Equal(ExitCodes.Error, run.ExitCode);
        Assert.Contains(result!.Actions, action => action.Kind == ActionKind.Workload && action.Status == ActionStatus.Failed);
        Assert.Contains(result.Actions, action => action.Kind == ActionKind.Restore && action.Status == ActionStatus.NotRun);
    }
}

using twodog.cli;

namespace twodog.tests.ToolTests;

public class WorkloadsTests
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
        Workloads.EnsureInstalled(Workload.WasmTools, ".", false, _ => throw new Exception("must not prompt"), runner);
        Assert.Single(runner.Requests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Missing_InstallsOnlyAfterConsent(bool consent)
    {
        var runner = Runner(installed: false);
        var offers = 0;
        Workloads.EnsureInstalled(Workload.WasmTools, ".", false, _ => { offers++; return consent; }, runner);
        Assert.Equal(1, offers);
        Assert.Equal(consent ? 2 : 1, runner.Requests.Count);
        if (consent) Assert.Equal(["workload", "install", "wasm-tools"], runner.Requests[1].Args);
    }

    [Fact]
    public void Missing_WithoutATerminal_DoesNotInstall()
    {
        var runner = Runner(installed: false);
        Workloads.EnsureInstalled(Workload.WasmTools, ".", false, null, runner);
        Assert.Single(runner.Requests);
    }

    [Fact]
    public void ExplicitInstall_DoesNotPrompt()
    {
        var runner = Runner(installed: false);
        Workloads.EnsureInstalled(Workload.WasmTools, ".", true, _ => throw new Exception("must not prompt"), runner);
        Assert.Equal(["workload", "install", "wasm-tools"], runner.Requests[1].Args);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UpdateOffer_RunsOnlyAfterConsent(bool consent)
    {
        var runner = Runner(installed: true);
        Workloads.Update(".", runner, () => consent);
        Assert.Equal(consent ? 2 : 1, runner.Requests.Count);
        if (consent) Assert.Equal(["workload", "update"], runner.Requests[1].Args);
    }

    [Fact]
    public void UpdateOffer_WithoutInstalledWorkloads_DoesNotPrompt()
    {
        var runner = new FakeProcessRunner(request => FakeProcessRunner.Result(request, 0, "Installed Workload Id",
            "--------------------", ""));
        Workloads.Update(".", runner, () => throw new Exception("must not prompt"));
        Assert.Single(runner.Requests);
    }

    [Fact]
    public void UpdateOffer_FailedProbe_DoesNotPrompt()
    {
        var runner = new FakeProcessRunner(request => FakeProcessRunner.Result(request, 1, "listing failed"));
        CliConsole.Capture(() =>
        {
            Workloads.Update(".", runner, () => throw new Exception("must not prompt"));
            return ExitCodes.Ok;
        });
        Assert.Single(runner.Requests);
    }

    [Fact]
    public void ExplicitUpdate_RunsWithoutProbing()
    {
        var runner = Runner(installed: false);
        Workloads.Update(".", runner);
        Assert.Equal(["workload", "update"], Assert.Single(runner.Requests).Args);
    }

    [Fact]
    public void FailedProbe_DoesNotOfferOrInstall()
    {
        var runner = new FakeProcessRunner(request => FakeProcessRunner.Result(request, 1, "SDK not found"));
        Workloads.EnsureInstalled(Workload.WasmTools, ".", false, _ => throw new Exception("must not prompt"), runner);
        Assert.Single(runner.Requests);
    }

    [Theory]
    [InlineData("exit")]
    [InlineData("timeout")]
    [InlineData("exception")]
    public void ExplicitRequest_FailedProbeStillAttemptsInstallation(string failure)
    {
        var runner = new FakeProcessRunner(request =>
        {
            if (!request.Args.SequenceEqual(["workload", "list"])) return FakeProcessRunner.Result(request, 0);
            if (failure == "exception") throw new ToolException("listing could not start");
            return FakeProcessRunner.Result(request, 1, "listing failed") with { TimedOut = failure == "timeout" };
        });
        var run = CliConsole.Capture(() =>
        {
            Workloads.EnsureInstalled(Workload.WasmTools, ".", true, _ => throw new Exception("must not prompt"), runner);
            return ExitCodes.Ok;
        });

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Contains("could not list installed workloads", run.Stderr);
        Assert.DoesNotContain("error:", run.Stderr);
        Assert.Equal(["workload", "install", "wasm-tools"], runner.Requests[1].Args);
        Assert.Equal(2, runner.Requests.Count);
    }

    [Fact]
    public void FailedProbeAndInstallation_ReportsTheInstallationFailure()
    {
        var runner = new FakeProcessRunner(request => request.Args.Contains("list")
            ? throw new ToolException("listing could not start")
            : FakeProcessRunner.Result(request, 1, "installation denied"));
        var run = CliConsole.Capture(() =>
        {
            var error = Assert.Throws<ToolException>(() =>
                Workloads.EnsureInstalled(Workload.WasmTools, ".", true, null, runner));
            Assert.Contains("installing wasm-tools failed", error.Message);
            return 0;
        });
        Assert.Contains("installation denied", run.Stderr);
        Assert.Equal(["workload", "install", "wasm-tools"], runner.Requests[1].Args);
    }

    [Fact]
    public void FailedInstallation_ReportsOutputAndFails()
    {
        var runner = Runner(installed: false, installExit: 1);
        var run = CliConsole.Capture(() =>
        {
            Assert.Throws<ToolException>(() =>
                Workloads.EnsureInstalled(Workload.WasmTools, ".", true, null, runner));
            return 0;
        });
        Assert.Contains("installation denied", run.Stderr);
        Assert.Contains(Workload.WasmTools.InstallCommand, run.Stderr);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cancellation_IsPropagated(bool duringInstallation)
    {
        var runner = new FakeProcessRunner(request => duringInstallation && request.Args.Contains("list")
            ? FakeProcessRunner.Result(request, 0, "Installed Workload Id", "--------------------", "")
            : throw new OperationCanceledException());
        Assert.Throws<OperationCanceledException>(() =>
            Workloads.EnsureInstalled(Workload.WasmTools, ".", true, null, runner));
    }

    [Fact]
    public void Android_MissingInstallsTheAndroidWorkload()
    {
        var runner = Runner(installed: true);
        Workload? offered = null;
        Workloads.EnsureInstalled(Workload.Android, ".", false, workload => { offered = workload; return true; }, runner);
        Assert.Equal(Workload.Android, offered);
        Assert.Equal(["workload", "install", "android"], runner.Requests[1].Args);
    }

    [Fact]
    public void Android_Installed_DoesNotOffer()
    {
        var runner = new FakeProcessRunner(request => FakeProcessRunner.Result(request, 0, "Installed Workload Id",
            "--------------------", "android  36.1.2/10.0.100  SDK 10.0.100", ""));
        Workloads.EnsureInstalled(Workload.Android, ".", false, _ => throw new Exception("must not prompt"), runner);
        Assert.Single(runner.Requests);
    }

    [Fact]
    public void For_ListsEachNeededWorkloadOnce()
    {
        Assert.Empty(Workload.For([HostKind.Desktop, HostKind.Avalonia]));
        Assert.Equal([Workload.WasmTools], Workload.For([HostKind.Web, HostKind.Blazor]));
        Assert.Equal([Workload.Android], Workload.For([HostKind.Android]));
        Assert.Equal([Workload.WasmTools, Workload.Android], Workload.For([HostKind.Android, HostKind.WebXr]));
    }

    [Theory]
    [InlineData("new")]
    [InlineData("add")]
    [InlineData("update")]
    public void InstallFlags_ParseForScaffoldingAndUpdate(string verb)
    {
        Assert.True(CommandLine.Parse([verb, "--install-wasm-tools"]).Options.InstallWasmTools);
        Assert.True(CommandLine.Parse([verb, "--install-android-workload"]).Options.InstallAndroidWorkload);
    }

    [Fact]
    public void WorkloadFlags_ParseForDoctorAndUpdate()
    {
        Assert.True(CommandLine.Parse(["doctor", "--install-wasm-tools"]).Doctor!.InstallWasmTools);
        Assert.True(CommandLine.Parse(["doctor", "--install-android-workload"]).Doctor!.InstallAndroidWorkload);
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

    [Fact]
    public void Scaffold_AndroidHost_InstallsOnlyTheAndroidWorkload()
    {
        using var tmp = new TempProjectDir();
        var dir = Path.Combine(tmp.Dir, "Game");
        var runner = Runner(installed: false);
        var options = new ScaffoldOptions
        {
            ProjectPath = dir, NameOverride = "Game", CreateProject = true, Restore = false,
            InstallWasmTools = true, InstallAndroidWorkload = true,
            Hosts = [new HostSpec(HostKind.Android, "Game.android")],
        };
        var run = CliConsole.Capture(() => ScaffoldCommand.Run(ScaffoldCommand.Open(options), options,
            workloadRunner: runner).ExitCode);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(["workload", "install", "android"], runner.Requests[^1].Args);
        Assert.DoesNotContain(runner.Requests, request => request.Args.Contains("wasm-tools"));
    }

    [Fact]
    public void Scaffold_PlansACheckForEachWorkloadItsHostsNeed()
    {
        using var tmp = new TempProjectDir();
        var runner = new FakeProcessRunner(_ => throw new Exception("a cancelled plan must not run workload commands"));
        var options = new ScaffoldOptions
        {
            ProjectPath = Path.Combine(tmp.Dir, "Game"), NameOverride = "Game", CreateProject = true, Restore = true,
            ConfirmWorkloadInstall = _ => throw new Exception("a cancelled plan must not prompt"),
            Hosts = [new HostSpec(HostKind.Web, "Game.web"), new HostSpec(HostKind.Android, "Game.android")],
        };
        string[] checks = [];
        var run = CliConsole.Capture(() => ScaffoldCommand.Run(ScaffoldCommand.Open(options), options, actions =>
        {
            checks = actions.Where(a => a.Kind == ActionKind.Workload).Select(a => a.Description).ToArray();
            return false;
        }, runner).ExitCode);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(["check wasm-tools and install if requested", "check android and install if requested"], checks);
    }

    [Fact]
    public void AddingDesktopHost_DoesNotOfferWorkloadsForExistingOptionalHosts()
    {
        using var tmp = new TempProjectDir();
        var dir = Path.Combine(tmp.Dir, "Game");
        Assert.Equal(0, CliConsole.Run("new", "Game", dir, "--web", "--android", "--no-restore").ExitCode);
        var options = new ScaffoldOptions
        {
            ProjectPath = dir, Restore = true,
            ConfirmWorkloadInstall = _ => throw new Exception("no optional host was selected"),
            Hosts = [new HostSpec(HostKind.Desktop, "Game.2dog")],
        };
        var runner = new FakeProcessRunner(_ => throw new Exception("must not probe optional workloads"));
        var run = CliConsole.Capture(() => ScaffoldCommand.Run(ScaffoldCommand.Open(options), options, actions =>
        {
            Assert.DoesNotContain(actions, a => a.Kind == ActionKind.Workload);
            return false;
        }, runner).ExitCode);
        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Empty(runner.Requests);
        Assert.DoesNotContain("Could not find an Android SDK", run.Stderr);
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
            ConfirmWorkloadInstall = _ => throw new Exception("must not prompt"),
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

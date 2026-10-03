namespace twodog.cli;

/// <summary>Checks the project's selected SDK and installs its WebAssembly workload when requested.</summary>
internal static class WasmTools
{
    public const string InstallCommand = "dotnet workload install wasm-tools";
    public const string UpdateCommand = "dotnet workload update";

    public static void EnsureInstalled(string projectDir, bool install, Func<bool>? confirm, IProcessRunner runner,
        bool update = false, Func<bool>? confirmUpdate = null)
    {
        var workloads = TryListWorkloads(projectDir, runner);
        if (workloads is null)
        {
            if (install || update)
            {
                // Listing is only an optimization: an explicit request still gets an installation attempt.
                Install(projectDir, runner);
                if (update) Run(projectDir, runner, ["workload", "update"], "updating installed workloads");
            }
            else
                Out.Hint($"run '{InstallCommand}' before publishing");
            return;
        }

        if (workloads.Contains("wasm-tools"))
        {
            if (update || confirmUpdate?.Invoke() == true)
                Run(projectDir, runner, ["workload", "update"], "updating installed workloads");
            return;
        }

        if (!install && !update && !(confirm?.Invoke() ?? false))
        {
            Out.Hint($"browser hosts need wasm-tools; run '{InstallCommand}' before publishing");
            return;
        }

        Install(projectDir, runner);
    }

    public static void Install(string projectDir, IProcessRunner runner) =>
        Run(projectDir, runner, ["workload", "install", "wasm-tools"], "installing wasm-tools");

    private static List<string>? TryListWorkloads(string projectDir, IProcessRunner runner)
    {
        try
        {
            var result = runner.Run(ProcessRunner.Dotnet(projectDir, null, TimeSpan.FromMinutes(2),
                "workload", "list"), Cancellation.Token);
            if (result.Ok) return DotnetInfo.ParseWorkloads(result.Output);
            Out.Warning($"could not list installed workloads ({result.Outcome})");
        }
        catch (ToolException ex)
        {
            Out.Warning($"could not list installed workloads: {ex.Message}");
        }
        return null;
    }

    private static void Run(string projectDir, IProcessRunner runner, string[] args, string label)
    {
        var result = runner.Run(ProcessRunner.Dotnet(projectDir, label, TimeSpan.FromMinutes(30), args), Cancellation.Token);
        if (!result.Ok)
        {
            ProcessRunner.ReportFailure(result);
            throw new ToolException($"{label} failed ({result.Outcome}); run '{result.CommandLine}' and try again");
        }

        Out.Info(args.Contains("update") ? "[green]Installed workloads updated[/]" : "[green]wasm-tools installed[/]");
    }
}

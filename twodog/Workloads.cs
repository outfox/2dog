namespace twodog.cli;

/// <summary>A .NET SDK workload that a kind of host needs before it can build or publish.</summary>
internal sealed record Workload(string Id, string NeededBy)
{
    public static readonly Workload WasmTools = new("wasm-tools", "browser hosts");
    public static readonly Workload Android = new("android", "Android hosts");

    public string InstallCommand => $"dotnet workload install {Id}";

    /// <summary>The workloads these hosts need, browser before Android.</summary>
    public static IEnumerable<Workload> For(IEnumerable<HostKind> kinds)
    {
        var list = kinds.ToList();
        if (list.Any(Hosts.IsWebLike)) yield return WasmTools;
        if (list.Contains(HostKind.Android)) yield return Android;
    }
}

/// <summary>Checks the project's selected SDK and installs a workload its hosts need when requested.</summary>
internal static class Workloads
{
    public const string UpdateCommand = "dotnet workload update";

    public static void EnsureInstalled(Workload workload, string projectDir, bool install, Func<Workload, bool>? confirm,
        IProcessRunner runner, bool update = false, Func<bool>? confirmUpdate = null)
    {
        var workloads = TryListWorkloads(projectDir, runner);
        if (workloads is null)
        {
            if (install || update)
            {
                // Listing is only an optimization: an explicit request still gets an installation attempt.
                Install(workload, projectDir, runner);
                if (update) Run(projectDir, runner, ["workload", "update"], "updating installed workloads");
            }
            else
                Out.Hint($"run '{workload.InstallCommand}' before publishing");
            return;
        }

        if (workloads.Contains(workload.Id))
        {
            if (update || confirmUpdate?.Invoke() == true)
                Run(projectDir, runner, ["workload", "update"], "updating installed workloads");
            return;
        }

        if (!install && !update && !(confirm?.Invoke(workload) ?? false))
        {
            Out.Hint($"{workload.NeededBy} need {workload.Id}; run '{workload.InstallCommand}' before publishing");
            return;
        }

        Install(workload, projectDir, runner);
    }

    public static void Install(Workload workload, string projectDir, IProcessRunner runner) =>
        Run(projectDir, runner, ["workload", "install", workload.Id], $"installing {workload.Id}");

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

        Out.Info(args.Contains("update") ? "[green]Installed workloads updated[/]" : $"[green]{args[^1]} installed[/]");
    }
}

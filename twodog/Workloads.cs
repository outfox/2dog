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

/// <summary>
/// Checks the project's selected SDK, installs a workload its hosts need when requested, and updates the installed
/// workloads.
/// </summary>
internal static class Workloads
{
    public const string UpdateCommand = "dotnet workload update";

    public static void EnsureInstalled(Workload workload, string projectDir, bool install, Func<Workload, bool>? confirm,
        IProcessRunner runner)
    {
        var workloads = TryListWorkloads(projectDir, runner);
        if (workloads is null)
        {
            // Listing is only an optimization: an explicit request still gets an installation attempt.
            if (install) Install(workload, projectDir, runner);
            else Out.Hint($"run '{workload.InstallCommand}' before publishing");
            return;
        }

        if (workloads.Contains(workload.Id)) return;

        if (!install && !(confirm?.Invoke(workload) ?? false))
        {
            Out.Hint($"{workload.NeededBy} need {workload.Id}; run '{workload.InstallCommand}' before publishing");
            return;
        }

        Install(workload, projectDir, runner);
    }

    public static void Install(Workload workload, string projectDir, IProcessRunner runner) =>
        Run(projectDir, runner, ["workload", "install", workload.Id], $"installing {workload.Id}");

    /// <summary>
    /// `dotnet workload update` for the project's SDK. Without <paramref name="confirm"/> it always runs (an explicit
    /// request); an offer is only made when there is an installed workload to update.
    /// </summary>
    public static void Update(string projectDir, IProcessRunner runner, Func<bool>? confirm = null)
    {
        if (confirm != null && (TryListWorkloads(projectDir, runner) is not { Count: > 0 } || !confirm())) return;
        Run(projectDir, runner, ["workload", "update"], "updating installed workloads");
    }

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

namespace twodog.cli;

/// <summary>
/// `2dog update`: brings a project's 2dog packages to the versions of the running tool. Literal versions left in
/// host csprojs move to the root Directory.Build.props first, then the props block and the game project's
/// Godot.NET.Sdk are set in lockstep; the tool never downgrades and never runs a git write.
/// </summary>
internal static class UpdateCommand
{
    /// <summary>Tests inject a fake runner for the git probe and the restore.</summary>
    internal static IProcessRunner Runner { get; set; } = ProcessRunner.Default;

    public static int Run(ParsedCommand cmd, Report report)
    {
        var interactive = !cmd.NoInteractive && Tui.CanPrompt;
        var options = new ScaffoldOptions { ProjectPath = cmd.Options.ProjectPath };
        var project = ScaffoldCommand.Open(options);
        if (interactive)
        {
            Tui.Header();
            Tui.ShowProject(project);
        }

        if (!cmd.AllowDirty) RefuseDirtyTree(project.Dir);

        var model = ProjectModel.Load(project.Dir);
        var current = ProjectVersions.Current(model);
        RefuseDowngrade(current);

        var plan = new List<PlannedAction>();
        var warnings = new List<string>();
        PlanMigrations(plan, model);
        ScaffoldCommand.PlanRootBuildProps(plan, project.Dir);
        PlanPropsValues(plan, model, current);
        PlanGodotSdk(plan, model, warnings);
        PlanWebBootRefresh(plan, model);

        if (plan.Count > 0 && cmd.Options.Restore)
            plan.Add(new PlannedAction("dotnet restore", ActionKind.Restore, () => Restore(project)));

        var result = ScaffoldCommand.Apply(plan, warnings, [], cmd.Options.DryRun,
            interactive ? Tui.ConfirmPlan : null,
            () => [("2dog doctor", "check the project after the update")],
            () => Out.Info("[green]Updated.[/] Run [bold]2dog doctor[/] to check the project."));
        JsonReport.Describe(report, project, [], result);
        return result.ExitCode;
    }

    /// <summary>cargo-fix style: an update rewrites tracked files, so it wants a clean tree to diff against.</summary>
    private static void RefuseDirtyTree(string projectDir)
    {
        ProcessResult status;
        try
        {
            status = Runner.Run(new ProcessRequest("git", ["status", "--porcelain", "--untracked-files=no"], projectDir,
                TimeSpan.FromSeconds(30)), Cancellation.Token);
        }
        catch (ToolException)
        {
            Out.Verbose("git not found - skipping the working tree check");
            return;
        }

        if (!status.Ok)
        {
            Out.Verbose("not a git repository - skipping the working tree check");
            return;
        }

        if (status.Output.Any(l => l.Trim().Length > 0))
            throw new ToolException("the git working tree has uncommitted changes - commit or stash them so the " +
                                    "update is easy to review, or pass --allow-dirty");
    }

    /// <summary>The engine, natives and Godot line follow the tool; a newer companion is kept instead (see Targets).</summary>
    private static void RefuseDowngrade(Dictionary<string, Version> current)
    {
        foreach (var (name, value) in PropsPatcher.ToolValues.Take(3))
        {
            if (!current.TryGetValue(name, out var have) || !Version.TryParse(value, out var tool) || have <= tool) continue;
            throw new ToolException($"the project already uses {name} {have}, newer than this tool's {tool} - " +
                                    "run the newest tool instead: dnx 2dog update");
        }
    }

    private static void PlanMigrations(List<PlannedAction> plan, ProjectModel model)
    {
        foreach (var csproj in model.HostCsprojs)
        {
            var (newText, changes) = VersionRewriter.Migrate(csproj);
            if (newText == null) continue;
            var relative = Path.GetRelativePath(model.Dir, csproj).Replace('\\', '/');
            plan.Add(new PlannedAction($"switch {relative} to the shared version properties ({changes.Count} reference(s))",
                ActionKind.Patch, () => MsBuildXml.Write(csproj, newText)));
        }
    }

    /// <summary>
    /// The values the block should hold: the tool's, except that a companion (Avalonia, Windows App SDK, ASP.NET
    /// Core) the project already has at a newer version keeps it - a migrated literal must never go backwards.
    /// </summary>
    internal static List<(string Name, string Value)> Targets(Dictionary<string, Version> current) =>
        PropsPatcher.ToolValues
            .Select(v => current.TryGetValue(v.Name, out var have) && Version.TryParse(v.Value, out var tool) && have > tool
                ? (v.Name, have.ToString())
                : v)
            .ToList();

    /// <summary>Shared with `2dog add`: an existing block follows the tool there too, so new hosts resolve.</summary>
    internal static void PlanPropsValues(List<PlannedAction> plan, ProjectModel model, Dictionary<string, Version> current)
    {
        var path = Path.Combine(model.Dir, PropsPatcher.FileName);
        var values = Targets(current);
        // Without a block yet, the plan lists the move away from the literal versions the hosts carry today.
        var block = model.PropsValues.Count > 0
            ? model.PropsValues
            : current.ToDictionary(kv => kv.Key, kv => kv.Value.ToString());
        var changes = values
            .Where(v => !block.TryGetValue(v.Name, out var have) || have != v.Value)
            .Select(v => block.TryGetValue(v.Name, out var have) ? $"{v.Name} {have} -> {v.Value}" : $"{v.Name} {v.Value}")
            .ToList();
        if (changes.Count == 0) return;

        // The file may be created by an earlier step of this same plan, so the patch runs at apply time.
        plan.Add(new PlannedAction($"set {PropsPatcher.FileName}: {string.Join(", ", changes)}", ActionKind.Patch, () =>
        {
            if (PropsPatcher.SetValues(path, values) is { } text) MsBuildXml.Write(path, text);
        }));
    }

    private static void PlanGodotSdk(List<PlannedAction> plan, ProjectModel model, List<string> warnings)
    {
        if (model.GameCsprojPath is not { } game || model.GameCsprojText is not { } text) return;
        var current = VersionRewriter.GodotSdkVersion(text);
        var newText = VersionRewriter.SetGodotSdkVersion(text, ToolVersions.GodotSdkVersion);
        if (newText == null) return;

        plan.Add(new PlannedAction($"set {model.GameCsprojName} Sdk to Godot.NET.Sdk/{ToolVersions.GodotSdkVersion} (was {current})",
            ActionKind.Patch, () => MsBuildXml.Write(game, newText)));

        if (VersionRewriter.GodotLineChangeWarning(current, ToolVersions.GodotSdkVersion) is { } warning)
            warnings.Add(warning);
    }

    /// <summary>TwoDogWebBoot.cs is tool-owned: a newer tool may need a newer bootstrap.</summary>
    private static void PlanWebBootRefresh(List<PlannedAction> plan, ProjectModel model)
    {
        var template = TemplateAssets.WebBootSource();
        foreach (var host in model.Hosts.Where(h => h.IsWebLike))
        {
            var path = Path.Combine(model.Dir, host.Folder, "TwoDogWebBoot.cs");
            if (!File.Exists(path) || File.ReadAllText(path) == template) continue;
            plan.Add(new PlannedAction($"refresh {host.Folder}/TwoDogWebBoot.cs (tool-owned web bootstrap)",
                ActionKind.CreateFile, () => File.WriteAllText(path, template)));
        }
    }

    private static void Restore(ProjectContext project)
    {
        var (solution, exists) = SolutionOps.Locate(project.Dir, project.BaseName);
        var target = exists ? Path.GetFileName(solution) : project.BaseName + ".csproj";
        var result = Runner.Run(ProcessRunner.Dotnet(project.Dir, $"restoring {target}", TimeSpan.FromMinutes(10),
            "restore", target), Cancellation.Token);
        if (result.Ok) return;
        ProcessRunner.ReportFailure(result);
        // Thrown, not warned: the new versions do not resolve, so the update failed even though the files are written.
        throw new ToolException("dotnet restore failed - the version files are updated; fix the restore and run 'dotnet restore' again");
    }
}

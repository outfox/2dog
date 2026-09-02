namespace twodog.cli;

/// <summary>
/// The 2dog command. Bare `2dog` prints version info and usage; the scaffolding verbs (new, add) prompt when no
/// host flags are given and run unattended when they are. Both paths end in the same scaffolder.
/// </summary>
internal static class Program
{
    internal static int Main(string[] args)
    {
        // Windows consoles default to a legacy codepage that mangles the UTF-8 version marks.
        if (OperatingSystem.IsWindows())
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { /* no console attached */ }

        // Resolved from the raw arguments so that even a usage error renders in the requested mode.
        Out.Configure(OutputMode.Resolve(args, Environment.GetEnvironmentVariable, Out.Facts));
        Cancellation.Install();

        var report = new Report();
        var exitCode = Run(args, report);
        Out.RestoreTerminal();
        if (Out.Mode.Json) JsonReport.Print(report, exitCode);
        return exitCode;
    }

    /// <summary>Dispatches the verb and maps every failure onto its exit code and an `error:` line.</summary>
    private static int Run(string[] args, Report report)
    {
        try
        {
            var cmd = CommandLine.Parse(args);
            report.Command = cmd.Verb == Verb.None ? null : cmd.Verb.ToString().ToLowerInvariant();
            switch (cmd.Verb)
            {
                case Verb.None:
                    PrintVersion(checkLatest: false, report);
                    Out.Blank();
                    Usage.Print(null, withHeader: false);
                    return ExitCodes.Ok;
                case Verb.Help:
                    Usage.Print(cmd.HelpVerb);
                    return ExitCodes.Ok;
                case Verb.Version:
                    PrintVersion(checkLatest: true, report);
                    return ExitCodes.Ok;
                case Verb.Doctor:
                    return DoctorCommand.Run(cmd, report);
                case Verb.Update:
                    return UpdateCommand.Run(cmd, report);
                case Verb.Pack:
                    return PackCommand.Run(cmd, report);
                default:
                    return Execute(cmd, report);
            }
        }
        catch (OperationCanceledException)
        {
            Out.ErrorLine("cancelled");
            return ExitCodes.Cancelled;
        }
        catch (UsageException ex)
        {
            Out.ErrorLine(ex.Message);
            Out.Hint(Usage.Hint(ex.Verb));
            return ExitCodes.Usage;
        }
        catch (ToolException ex)
        {
            Out.ErrorLine(ex.Message);
            Out.Verbose(ex.ToString());
            return ExitCodes.Error;
        }
        catch (Exception ex)
        {
            var (message, hint) = FriendlyError.Describe(ex);
            Out.ErrorLine(message);
            if (hint != null) Out.Hint(hint);
            Out.Verbose(ex.ToString());
            return ExitCodes.Error;
        }
    }

    private static int Execute(ParsedCommand cmd, Report report)
    {
        foreach (var note in cmd.Notes) Out.Note(note);

        var wantsPrompts = WantsPrompts(cmd);
        var interactive = wantsPrompts && Tui.CanPrompt;
        // Prompts wanted but impossible (piped, CI): the defaults apply, said out loud.
        if (wantsPrompts && !interactive && (cmd.Verb == Verb.Add || cmd.Options.NameOverride != null))
            Out.Note("no terminal to ask on - applying the default host set (pass --yes or name hosts to make this explicit)");

        if (interactive) Tui.Header();

        if (cmd.Verb == Verb.New) PrepareNewProject(cmd, interactive);

        ProjectContext project;
        try
        {
            project = ScaffoldCommand.Open(cmd.Options);
        }
        catch (SpacedNameException ex) when (interactive && ex.CanOfferRename)
        {
            // Declining falls through to the checklist error.
            if (Tui.OfferRename(ex) is not { } newName) throw;
            cmd.Options.RenameTo = newName;
            project = ScaffoldCommand.Open(cmd.Options);
        }

        // A new project into a directory that already has files: fine, but not silently.
        if (cmd.Verb == Verb.New && Directory.Exists(project.Dir) && !IsEmpty(project.Dir))
        {
            if (interactive && !Tui.ConfirmNonEmptyDirectory(project.Dir))
            {
                Out.Info("[yellow]Cancelled[/] - nothing changed.");
                return ExitCodes.Ok;
            }

            if (!interactive)
                Out.Warning($"{project.Dir} is not empty - the project is created alongside its files " +
                            "(existing files are skipped, --force overwrites)");
        }

        if (interactive) Tui.ShowProject(project);

        cmd.Options.Hosts = interactive
            ? Tui.SelectHosts(project, HostSelection.Defaults(cmd.Excluded, project))
            : HostSelection.FromFlags(cmd, project);

        var result = ScaffoldCommand.Run(project, cmd.Options, interactive ? Tui.ConfirmPlan : null);
        JsonReport.Describe(report, project, cmd.Options.Hosts, result);
        return result.ExitCode;
    }

    /// <summary>
    /// Prompting is off once the command line answers the questions itself (any host flag, or --yes), so scripted
    /// runs never block, not even at the final confirmation. --dry-run still asks, then prints the plan.
    /// </summary>
    internal static bool WantsPrompts(ParsedCommand cmd) =>
        cmd is { NoInteractive: false, HostFlagsSeen: false } && !Out.Mode.Json;

    internal static void PrepareNewProject(ParsedCommand cmd, bool interactive)
    {
        var outputDir = cmd.OutputDir;
        var name = cmd.Options.NameOverride;

        if (name == null)
        {
            if (!interactive) throw new UsageException("2dog new needs a project name", Verb.New);
            var here = Path.GetFullPath(outputDir ?? ".");
            name = Tui.AskProjectName(Path.GetFileName(here.TrimEnd(Path.DirectorySeparatorChar)));
            outputDir ??= IsEmpty(here) ? "." : name;
            outputDir = Tui.AskDirectory(outputDir);
            Out.Blank();
        }

        cmd.Options.NameOverride = name;
        // Default directory: the sanitized name, so `2dog new "My Game"` does not pair MyGame.csproj with a
        // "My Game" folder. An explicit -o stays as given.
        cmd.Options.ProjectPath = outputDir ?? Hosts.SanitizeName(name) ?? name;
        cmd.Options.CreateProject = true;
    }

    /// <summary>A directory with nothing in it but dotfiles counts as empty.</summary>
    internal static bool IsEmpty(string dir) =>
        !Directory.Exists(dir) || Directory.EnumerateFileSystemEntries(dir).All(e => Path.GetFileName(e).StartsWith('.'));

    /// <summary>
    /// The tool version, then every package the scaffolded projects reference. checkLatest asks nuget.org (best
    /// effort) and marks one package per publish group: latest stable, newer stable available, or unknown.
    /// </summary>
    private static void PrintVersion(bool checkLatest, Report report)
    {
        var rows = new (string Label, string Version, string Probe, string Packages)[]
        {
            ("tool + packages", ToolVersions.TwoDogVersion, "2dog", "2dog, 2dog.engine, 2dog.avalonia, 2dog.blazor, 2dog.xunit"),
            ("native binaries", ToolVersions.NativesVersion, "2dog.win-x64", "2dog.win-x64, 2dog.linux-x64, 2dog.osx-arm64, 2dog.browser-wasm, 2dog.tools"),
            ("Godot SDK", ToolVersions.GodotSdkVersion, "Godot.NET.Sdk", "Godot.NET.Sdk, GodotSharp"),
        };
        var latest = checkLatest ? NuGetLatest.Query(rows.Select(r => r.Probe)) : null;
        var marked = rows
            .Select(r => (r.Label, r.Version, Latest: latest?[r.Probe], r.Packages))
            .Select(r => (r.Label, r.Version, r.Latest, Mark: NuGetLatest.Mark(r.Version, r.Latest), r.Packages))
            .ToList();

        report.Versions = marked
            .Select(r => new ReportVersion(r.Label, r.Version, r.Packages, r.Latest,
                r.Mark switch { VersionMark.UpToDate => true, VersionMark.Outdated => false, _ => null }))
            .ToList();

        Out.Header();
        Out.VersionTable(marked.Select(r => (r.Label, r.Version, r.Mark, r.Packages)).ToList());
    }
}

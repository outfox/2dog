namespace twodog.cli;

/// <summary>
/// The 2dog command. Bare `2dog` prints version info and usage; the scaffolding
/// verbs (new, add) prompt when no host flags are given and run unattended when
/// they are. Both paths end in the same scaffolder.
/// </summary>
internal static class Program
{
    internal static int Main(string[] args)
    {
        // Windows consoles default to a legacy codepage that mangles the ✅/🔄 version marks.
        if (OperatingSystem.IsWindows())
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { /* no console attached */ }

        try
        {
            var cmd = CommandLine.Parse(args);
            switch (cmd.Verb)
            {
                case Verb.None:
                    PrintVersion(checkLatest: false);
                    Console.WriteLine();
                    PrintUsage(withHeader: false);
                    return 0;
                case Verb.Help:
                    PrintUsage();
                    return 0;
                case Verb.Version:
                    PrintVersion(checkLatest: true);
                    return 0;
                case Verb.Pack:
                    return PackCommand.Run(cmd);
                default:
                    return Execute(cmd);
            }
        }
        catch (UsageException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            PrintUsage();
            return 1;
        }
        catch (ToolException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 2;
        }
    }

    private static int Execute(ParsedCommand cmd)
    {
        var interactive = WantsPrompts(cmd) && Tui.CanPrompt;

        if (interactive) Tui.Header();

        if (cmd.Verb == Verb.New) PrepareNewProject(cmd, interactive);
        var project = ScaffoldCommand.Open(cmd.Options);

        if (interactive) Tui.ShowProject(project);

        cmd.Options.Hosts = interactive
            ? Tui.SelectHosts(project, HostSelection.Defaults(cmd.Excluded, project))
            : HostSelection.FromFlags(cmd, project);

        return ScaffoldCommand.Run(project, cmd.Options, interactive ? Tui.ConfirmPlan : null);
    }

    /// <summary>
    /// Prompting is off as soon as the command line answers the questions
    /// itself - any host flag, or --yes - so scripted runs never block on a
    /// terminal, not even at the final confirmation. --dry-run still asks:
    /// it gathers the same choices and then prints the plan instead of
    /// applying it.
    /// </summary>
    // internal for unit tests
    internal static bool WantsPrompts(ParsedCommand cmd) => cmd is {NoInteractive: false, HostFlagsSeen: false};

    internal static void PrepareNewProject(ParsedCommand cmd, bool interactive)
    {
        var outputDir = cmd.OutputDir;
        var name = cmd.Options.NameOverride;

        if (name == null)
        {
            if (!interactive) throw new UsageException("2dog new needs a project name");
            var here = Path.GetFullPath(outputDir ?? ".");
            name = Tui.AskProjectName(Path.GetFileName(here.TrimEnd(Path.DirectorySeparatorChar)));
            outputDir ??= IsEmpty(here) ? "." : name;
            outputDir = Tui.AskDirectory(outputDir);
            Console.WriteLine();
        }

        cmd.Options.NameOverride = name;
        cmd.Options.ProjectPath = outputDir ?? name;
        cmd.Options.CreateProject = true;
    }

    /// <summary>A directory with nothing in it but dotfiles counts as empty.</summary>
    internal static bool IsEmpty(string dir) =>
        !Directory.Exists(dir) || Directory.EnumerateFileSystemEntries(dir).All(e => Path.GetFileName(e).StartsWith('.'));

    /// <summary>
    /// The tool version, then every package the scaffolded projects reference.
    /// checkLatest asks nuget.org (best effort) and marks each row via one package
    /// per publish group: ✅ latest stable, 🔄 newer stable available, blank unknown.
    /// </summary>
    private static void PrintVersion(bool checkLatest)
    {
        var rows = new (string Label, string Version, string Probe, string Packages)[]
        {
            ("tool + packages", ToolVersions.TwoDogVersion, "2dog", "2dog, 2dog.engine, 2dog.avalonia, 2dog.xunit"),
            ("native binaries", ToolVersions.NativesVersion, "2dog.win-x64", "2dog.win-x64, 2dog.linux-x64, 2dog.osx-arm64, 2dog.browser-wasm, 2dog.tools"),
            ("Godot SDK", ToolVersions.GodotSdkVersion, "Godot.NET.Sdk", "Godot.NET.Sdk, GodotSharp"),
        };
        var latest = checkLatest ? NuGetLatest.Query(rows.Select(r => r.Probe)) : null;

        Console.WriteLine($"2dog {ToolVersions.TwoDogVersion} - https://2dog.dev");
        Console.WriteLine();
        foreach (var (label, version, probe, packages) in rows)
        {
            // The mark and its blank stand-in are both two terminal cells wide, so columns never move.
            var mark = latest == null ? null : NuGetLatest.Mark(version, latest[probe]);
            Console.WriteLine($"{label,-15}  {version,-10} {mark ?? "  "}  {packages}");
        }
    }

    private static void PrintUsage(bool withHeader = true)
    {
        if (withHeader)
            Console.WriteLine($"2dog {ToolVersions.TwoDogVersion} - https://2dog.dev{Environment.NewLine}");
        Console.WriteLine(
            $"""
             usage: 2dog <verb> [path] [options]

             Scaffolds .NET host projects for a Godot project: the Godot project
             directory is the solution root, hosts are nested subfolders the Godot
             editor ignores (.gdignore). No existing file is ever moved, renamed or
             deleted.

             verbs
               new [Name] [dir]  Create a new Godot project with 2dog hosts
               add [path]        Add hosts to an existing Godot project
               convert [path]    Alias of add, for projects that have no hosts yet
               pack list <pck>   List a .pck's contents by size (no engine needed)
               version           Print tool and package versions

             Without any host option, new and add ask interactively; run add again
             to add more hosts, including a second host of the same kind.

             hosts
               --desktop [folder]  Desktop host (your own Main entry point)
               --web [folder]      Browser (WebAssembly) host
               --webxr [folder]    Browser host with the WebXR Layers polyfill
                                   wired into its page (opt-in)
               --tests [folder]    xUnit test project
               --winforms [folder] WinForms host embedding the game window
                                   (Windows-only; never part of the default set)
               --winui [folder]    WinUI 3 host embedding the game window
                                   (Windows-only, like --winforms; builds only
                                   on Windows)
               --avalonia [folder] Avalonia host embedding the game in a
                                   cross-platform GUI (opt-in, like --winforms)
               --no-desktop, --no-web, --no-tests
                                   Leave a host out of the default set

             options
               -n, --name <name>   Project name (new) or base name override
               -o, --output <dir>  Directory for a new project
               -y, --yes           Do not prompt; take the flags and defaults
               --non-interactive   Same as --yes
               --dry-run           Print planned actions without changing anything
               --force             Overwrite files that already exist (never deletes)
               --no-restore        Skip the final 'dotnet restore'
               --verbose           Extra output
               --version           Print tool and package versions; under dnx use
                                   '2dog version' (dnx consumes --version itself,
                                   to pin the tool version: 'dnx 2dog@<version>')

             examples
               2dog add                      # interactive, here
               2dog new MyGame               # interactive host choice, new project
               2dog new MyGame --desktop --tests
               2dog add --desktop MyGame.editor
               2dog add path/to/project --no-web
               2dog pack list MyGame.web/AppBundle/godot.pck
             """);
    }
}

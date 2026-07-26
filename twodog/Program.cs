namespace twodog.cli;

/// <summary>
/// The 2dog command. With no host flags it prompts; with them it runs
/// unattended. Both paths end in the same scaffolder.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            var cmd = CommandLine.Parse(args);
            switch (cmd.Verb)
            {
                case Verb.Help:
                    PrintUsage();
                    return 0;
                case Verb.Version:
                    Console.WriteLine(ToolVersions.TwoDogVersion);
                    return 0;
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
        var verb = ResolveVerb(cmd, interactive);

        if (interactive) Tui.Header();

        if (verb == Verb.New) PrepareNewProject(cmd, interactive);
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
    internal static bool WantsPrompts(ParsedCommand cmd) => !cmd.NoInteractive && !cmd.HostFlagsSeen;

    /// <summary>
    /// Bare `2dog` reads the directory: a Godot project there means "add
    /// hosts", anything else means "create a project here".
    /// </summary>
    private static Verb ResolveVerb(ParsedCommand cmd, bool interactive)
    {
        if (cmd.Verb != Verb.Auto) return cmd.Verb;

        var dir = Path.GetFullPath(cmd.Options.ProjectPath ?? ".");
        if (File.Exists(Path.Combine(dir, "project.godot"))) return Verb.Add;

        if (!interactive)
            throw new ToolException($"no project.godot in {dir} - run '2dog new <Name>' to create a project, " +
                                    "or point 2dog at a Godot project directory");

        // The bare-command new-project case: the positional (if any) named the
        // directory to work in, not the project.
        cmd.OutputDir ??= cmd.Options.ProjectPath;
        return Verb.New;
    }

    private static void PrepareNewProject(ParsedCommand cmd, bool interactive)
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
    private static bool IsEmpty(string dir) =>
        !Directory.Exists(dir) ||
        !Directory.EnumerateFileSystemEntries(dir).Any(e => !Path.GetFileName(e).StartsWith('.'));

    private static void PrintUsage()
    {
        Console.WriteLine(
            $"""
             2dog {ToolVersions.TwoDogVersion} - https://2dog.dev

             usage: 2dog [verb] [path] [options]

             Scaffolds .NET host projects for a Godot project: the Godot project
             directory is the solution root, hosts are nested subfolders the Godot
             editor ignores (.gdignore). No existing file is ever moved, renamed or
             deleted.

             verbs
               (none)            Add hosts to the Godot project here, or create a
                                 project if there is none
               new [Name] [dir]  Create a new Godot project with 2dog hosts
               add [path]        Add hosts to an existing Godot project
               convert [path]    Alias of add, for projects that have no hosts yet

             Without any host option the tool asks interactively; run it again to
             add more hosts, including a second host of the same kind.

             hosts
               --desktop [folder]  Desktop host (your own Main entry point)
               --web [folder]      Browser (WebAssembly) host
               --tests [folder]    xUnit test project
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
               --version           Print the tool version

             examples
               2dog                          # interactive, here
               2dog new MyGame               # interactive host choice, new project
               2dog new MyGame --desktop --tests
               2dog add --desktop MyGame.editor
               2dog convert path/to/project --no-web
             """);
    }
}

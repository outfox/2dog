namespace twodog.cli;

internal enum Verb
{
    /// <summary>No verb given: create or add, depending on what is here.</summary>
    Auto,
    New,
    Add,
    Help,
    Version,
}

/// <summary>A host asked for on the command line; a null folder means "pick one".</summary>
internal sealed record HostRequest(HostKind Kind, string? Folder);

/// <summary>The parsed command line.</summary>
internal sealed class ParsedCommand
{
    public Verb Verb = Verb.Auto;
    public ScaffoldOptions Options = new();
    public List<HostRequest> Requested = [];
    public HashSet<HostKind> Excluded = [];
    public string? OutputDir;

    /// <summary>Never prompt: take the flags and the defaults as given.</summary>
    public bool NoInteractive;

    /// <summary>Whether the command line already decided which hosts to create.</summary>
    public bool HostFlagsSeen => Requested.Count > 0 || Excluded.Count > 0;
}

/// <summary>A malformed command line (exit code 1, prints usage).</summary>
internal sealed class UsageException(string message) : Exception(message);

/// <summary>
/// Manual argument parsing, in the style of twodog.import: a couple of verbs,
/// a handful of flags, no parser dependency. Every interactive choice has a
/// flag here, and giving any host flag is what turns the prompts off.
/// </summary>
internal static class CommandLine
{
    public static ParsedCommand Parse(string[] args)
    {
        var cmd = new ParsedCommand();
        var queue = new Queue<string>(args);

        if (queue.Count > 0)
        {
            switch (queue.Peek())
            {
                case "new":
                    cmd.Verb = Verb.New;
                    queue.Dequeue();
                    break;
                case "add" or "convert":
                    cmd.Verb = Verb.Add;
                    queue.Dequeue();
                    break;
                case "help":
                    return new ParsedCommand { Verb = Verb.Help };
            }
        }

        var positionals = new List<string>();
        while (queue.Count > 0)
        {
            var arg = queue.Dequeue();
            switch (arg)
            {
                case "--help" or "-h":
                    return new ParsedCommand { Verb = Verb.Help };
                case "--version":
                    return new ParsedCommand { Verb = Verb.Version };

                case "--name" or "-n":
                    cmd.Options.NameOverride = Value(queue, arg);
                    break;
                case "--output" or "-o":
                    cmd.OutputDir = Value(queue, arg);
                    break;

                case "--desktop" or "--2dog":
                    cmd.Requested.Add(new HostRequest(HostKind.Desktop, OptionalFolder(queue)));
                    break;
                case "--web" or "--browser":
                    cmd.Requested.Add(new HostRequest(HostKind.Web, OptionalFolder(queue)));
                    break;
                case "--tests" or "--test":
                    cmd.Requested.Add(new HostRequest(HostKind.Tests, OptionalFolder(queue)));
                    break;

                case "--no-desktop":
                    cmd.Excluded.Add(HostKind.Desktop);
                    break;
                case "--no-web":
                    cmd.Excluded.Add(HostKind.Web);
                    break;
                case "--no-tests":
                    cmd.Excluded.Add(HostKind.Tests);
                    break;

                case "--dry-run":
                    cmd.Options.DryRun = true;
                    break;
                case "--force":
                    cmd.Options.Force = true;
                    break;
                case "--no-restore":
                    cmd.Options.Restore = false;
                    break;
                case "--verbose":
                    cmd.Options.Verbose = true;
                    break;
                // "yes to everything" and "don't ask" are the same thing here:
                // the prompts only ever gather what the flags already carry.
                case "--yes" or "-y" or "--non-interactive" or "--no-input":
                    cmd.NoInteractive = true;
                    break;

                default:
                    if (arg.StartsWith('-')) throw new UsageException($"unknown option '{arg}'");
                    positionals.Add(arg);
                    break;
            }
        }

        AssignPositionals(cmd, positionals);
        return cmd;
    }

    /// <summary>
    /// `2dog new [Name] [directory]`; every other form takes a project
    /// directory.
    /// </summary>
    private static void AssignPositionals(ParsedCommand cmd, List<string> positionals)
    {
        if (cmd.Verb == Verb.New)
        {
            if (positionals.Count > 2) throw new UsageException("2dog new takes at most a name and a directory");
            if (positionals.Count > 0) cmd.Options.NameOverride ??= positionals[0];
            if (positionals.Count > 1) cmd.OutputDir ??= positionals[1];
            return;
        }

        if (positionals.Count > 1) throw new UsageException("more than one project path given");
        if (positionals.Count == 1) cmd.Options.ProjectPath = positionals[0];
    }

    private static string Value(Queue<string> queue, string flag) =>
        queue.Count > 0 && !queue.Peek().StartsWith('-')
            ? queue.Dequeue()
            : throw new UsageException($"{flag} requires a value");

    /// <summary>
    /// The optional folder name after a host flag. Path-like tokens are left
    /// alone so `2dog add --web ./MyGame` still reads as a project path.
    /// </summary>
    private static string? OptionalFolder(Queue<string> queue)
    {
        if (queue.Count == 0) return null;
        var next = queue.Peek();
        if (next.StartsWith('-') || next.Contains('/') || next.Contains('\\') || next == ".") return null;
        return queue.Dequeue();
    }
}

/// <summary>Turns flags (or their absence) into the concrete list of hosts to create.</summary>
internal static class HostSelection
{
    /// <summary>The hosts named by host flags, with folder names resolved.</summary>
    public static List<HostSpec> FromFlags(ParsedCommand cmd, ProjectContext project)
    {
        if (cmd.Requested.Count == 0) return Defaults(cmd.Excluded, project);

        var taken = new List<string>(project.TakenFolders);
        var hosts = new List<HostSpec>();
        foreach (var request in cmd.Requested)
        {
            string folder;
            if (request.Folder is { } given)
            {
                folder = Hosts.SanitizeName(given)
                         ?? throw new ToolException($"'{given}' is not a usable folder name - it needs a letter or a digit");
                if (taken.Contains(folder, StringComparer.OrdinalIgnoreCase))
                    throw new ToolException($"'{folder}' already exists in the project - pick another folder name");
            }
            else
            {
                folder = Hosts.AllocateFolder(request.Kind, project.BaseName, taken);
            }

            taken.Add(folder);
            hosts.Add(new HostSpec(request.Kind, folder));
        }

        return hosts;
    }

    /// <summary>
    /// What a run creates when no host was named: every kind the project does
    /// not have yet, minus the excluded ones. Which kinds are missing follows
    /// from the recognized hosts; the folder names avoid every directory.
    /// </summary>
    public static List<HostSpec> Defaults(ICollection<HostKind> excluded, ProjectContext project)
    {
        var taken = new List<string>(project.TakenFolders);
        var hosts = new List<HostSpec>();
        foreach (var kind in Hosts.All)
        {
            if (excluded.Contains(kind) || project.ExistingHosts.Any(h => h.Kind == kind)) continue;
            var folder = Hosts.AllocateFolder(kind, project.BaseName, taken);
            taken.Add(folder);
            hosts.Add(new HostSpec(kind, folder));
        }

        return hosts;
    }
}

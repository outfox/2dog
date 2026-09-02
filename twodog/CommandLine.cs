using System.CommandLine;
using System.CommandLine.Parsing;

namespace twodog.cli;

internal enum Verb
{
    /// <summary>No arguments at all: print version info and usage.</summary>
    None,
    New,
    Add,
    Doctor,
    Update,
    Pack,
    Help,
    Version,
}

/// <summary>A host asked for on the command line; a null folder means "pick one".</summary>
internal sealed record HostRequest(HostKind Kind, string? Folder);

/// <summary>The parsed command line.</summary>
internal sealed class ParsedCommand
{
    public Verb Verb = Verb.None;
    public ScaffoldOptions Options = new();
    public List<HostRequest> Requested = [];
    public HashSet<HostKind> Excluded = [];
    public string? OutputDir;

    /// <summary>The .pck file a pack operation works on.</summary>
    public string? PackFile;

    /// <summary>For <see cref="Verb.Help"/>: the verb to show help for, null for the general help.</summary>
    public Verb? HelpVerb;

    /// <summary>Remarks about the command line worth a note, e.g. a --no-&lt;host&gt; that cannot change anything.</summary>
    public List<string> Notes = [];

    /// <summary>update: proceed although the git working tree is dirty.</summary>
    public bool AllowDirty;

    /// <summary>doctor: what to check, fix, build.</summary>
    public DoctorOptions? Doctor;

    /// <summary>Never prompt: take the flags and the defaults as given.</summary>
    public bool NoInteractive;

    /// <summary>
    /// Whether the command line already decided which hosts to create: a host flag, or a --no-&lt;host&gt; removing
    /// something from the default set (excluding an opt-in kind changes nothing, so it does not count).
    /// </summary>
    public bool HostFlagsSeen => Requested.Count > 0 || Excluded.Any(Hosts.InDefaultSet);
}

/// <summary>A malformed command line (exit code 1). Carries the verb so the hint can point at its help.</summary>
internal sealed class UsageException(string message, Verb? verb = null) : Exception(message)
{
    public Verb? Verb { get; } = verb;
}

/// <summary>
/// Parses the command line against <see cref="CliTree"/>. Every interactive choice has a flag here, and giving any
/// host flag is what turns the prompts off.
/// </summary>
internal static class CommandLine
{
    public static ParsedCommand Parse(string[] args)
    {
        if (args.Length == 0) return new ParsedCommand();

        var (path, consumed) = CliTree.Resolve(args);
        var command = path[^1];
        var verb = CliTree.VerbOf(command);
        RejectUnknownOptions(args, consumed, path, verb);

        var result = CliTree.Root.Parse(OptionalValueTokens.Normalize(args), CliTree.Configuration);
        if (Present(result, CliTree.HelpOption)) return new ParsedCommand { Verb = Verb.Help, HelpVerb = verb };
        if (Present(result, CliTree.VersionOption)) return new ParsedCommand { Verb = Verb.Version };
        // Before the error translation: at the root, --pin has no verb to complain about.
        if (result.GetResult(CliTree.Pin) is { Implicit: false } pin) throw PinError(pin.Tokens.FirstOrDefault()?.Value);
        if (result.GetResult(CliTree.To) is { Implicit: false })
            throw new UsageException("2dog update always targets the versions of the running tool - to update to " +
                                     "another version, run that tool: 'dnx 2dog@<version> update'", Verb.Update);
        if (result.Errors.Count > 0) throw Translate(result, verb);

        var cmd = new ParsedCommand { Verb = verb ?? Verb.None, NoInteractive = result.GetValue(CliTree.Yes) };
        switch (verb)
        {
            case Verb.New:
                cmd.Options.NameOverride = result.GetValue(CliTree.Name) ?? result.GetValue(CliTree.NewNameArg);
                cmd.OutputDir = result.GetValue(CliTree.Output) ?? result.GetValue(CliTree.NewDirArg);
                ReadScaffoldOptions(result, cmd);
                break;
            case Verb.Add:
                cmd.Options.NameOverride = result.GetValue(CliTree.Name);
                cmd.Options.RenameTo = result.GetValue(CliTree.Rename);
                cmd.Options.ProjectPath = result.GetValue(CliTree.ProjectPathArg);
                ReadScaffoldOptions(result, cmd);
                break;
            case Verb.Doctor:
                cmd.Options.ProjectPath = result.GetValue(CliTree.ProjectPathArg);
                cmd.Doctor = ReadDoctorOptions(result);
                break;
            case Verb.Update:
                cmd.Options.ProjectPath = result.GetValue(CliTree.ProjectPathArg);
                cmd.Options.DryRun = result.GetValue(CliTree.DryRun);
                cmd.Options.Restore = !result.GetValue(CliTree.NoRestore);
                cmd.AllowDirty = result.GetValue(CliTree.AllowDirty);
                break;
            case Verb.Pack:
                cmd.PackFile = result.GetValue(CliTree.PackFileArg);
                break;
            case Verb.Help:
                if (result.GetValue(CliTree.HelpVerbArg) is { } name)
                    cmd.HelpVerb = CliTree.VerbNamed(name) ?? throw new UsageException(UnknownVerb(name), Verb.Help);
                break;
        }

        return cmd;
    }

    /// <summary>Whether the option was given on the command line (not just defaulted).</summary>
    private static bool Present(ParseResult result, Option option) =>
        result.GetResult(option) is { Implicit: false };

    /// <summary>
    /// Unknown --options must be rejected up front: System.CommandLine would otherwise treat them as positional
    /// arguments (a project path) and never complain.
    /// </summary>
    private static void RejectUnknownOptions(string[] args, int from, List<Command> path, Verb? verb)
    {
        var options = CliTree.OptionsOf(path).ToList();
        var valid = options.SelectMany(CliTree.NamesOf).ToHashSet(StringComparer.Ordinal);
        for (var i = from; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--") return;
            // A lone "-" is a value (stdin), not an option.
            if (arg.Length < 2 || !arg.StartsWith('-')) continue;
            var name = arg.Split(['=', ':'], 2)[0];
            if (!valid.Contains(name))
                throw new UsageException(UnknownOption(name, path, options), verb);

            // System.CommandLine would accept the next flag as the value of a value option; refuse that up front.
            var option = options.First(o => CliTree.NamesOf(o).Contains(name));
            var takesValue = option.Arity.MinimumNumberOfValues > 0 && !CliTree.OptionalValueOptions.Contains(option);
            if (takesValue && name == arg && (i + 1 >= args.Length || args[i + 1] is ['-', _, ..]))
                throw new UsageException($"{name} requires a value", verb);
        }
    }

    private static string UnknownOption(string name, List<Command> path, IEnumerable<Option> options)
    {
        if (path.Count == 1)
            return $"unknown option '{name}' here - a verb comes first: {VerbList}";

        var owners = CliTree.Commands
            .Where(c => c.Options.Any(o => CliTree.NamesOf(o).Contains(name)))
            .SelectMany(c => c.Aliases.Prepend(c.Name))
            .Distinct()
            .ToList();
        if (owners.Count > 0)
        {
            var message = $"'{name}' is not an option of '2dog {path[^1].Name}' - it applies to " +
                          $"{string.Join("/", owners)} only";
            if (name == "--rename") message += " (2dog new already picks a clean name)";
            return message;
        }

        var suggestion = Suggest.Closest(name, options.Where(o => !o.Hidden).Select(o => o.Name));
        return $"unknown option '{name}'" + (suggestion is null ? "" : $" (did you mean {suggestion}?)");
    }

    private static UsageException Translate(ParseResult result, Verb? verb)
    {
        foreach (var error in result.Errors)
        {
            switch (error.SymbolResult)
            {
                case OptionResult option:
                    return new UsageException(
                        $"{option.IdentifierToken?.Value ?? option.Option.Name} requires a value", verb);
                case ArgumentResult { Argument: var argument } when argument == CliTree.PackFileArg:
                    return new UsageException("2dog pack list needs a .pck file", verb);
                case CommandResult { Command: var at }:
                    if (result.UnmatchedTokens.Count > 0) return Unmatched(at, result.UnmatchedTokens[0], verb);
                    if (at == CliTree.Root) return new UsageException($"a verb is required: {VerbList}");
                    if (at == CliTree.Pack) return new UsageException($"2dog pack needs an operation: {PackOperations}", verb);
                    break;
            }
        }

        return new UsageException(result.Errors[0].Message, verb);
    }

    private static UsageException Unmatched(Command at, string token, Verb? verb)
    {
        if (at == CliTree.Root) return new UsageException(UnknownVerb(token));
        if (at == CliTree.New) return new UsageException("2dog new takes at most a name and a directory", verb);
        if (at == CliTree.Add) return new UsageException("more than one project path given", verb);
        if (at == CliTree.Pack) return new UsageException($"unknown pack operation '{token}' (supported: {PackOperations})", verb);
        if (at == CliTree.PackList) return new UsageException("2dog pack list takes one .pck file", verb);
        if (at == CliTree.HelpCommand) return new UsageException("2dog help takes at most one verb", verb);
        return new UsageException($"unexpected argument '{token}'", verb);
    }

    private static string UnknownVerb(string token)
    {
        var suggestion = Suggest.Closest(token, CliTree.VerbNames);
        return suggestion is null
            ? $"'{token}' is not a verb - a verb is required: {VerbList}"
            : $"unknown verb '{token}' (did you mean {suggestion}?)";
    }

    private static string VerbList => string.Join(", ", CliTree.VerbNames);

    private static string PackOperations => string.Join(", ", CliTree.Pack.Subcommands.Select(s => s.Name));

    /// <summary>Which tool version runs is decided by the launcher before 2dog starts.</summary>
    private static UsageException PinError(string? version)
    {
        if (string.IsNullOrEmpty(version)) version = "<version>";
        return new UsageException(
            "2dog cannot pin its own version - by the time it runs, the version is already chosen. " +
            $"Pin it where the tool is launched: 'dnx 2dog@{version}' " +
            $"or 'dotnet tool install -g 2dog --version {version}'.");
    }

    private static DoctorOptions ReadDoctorOptions(ParseResult result)
    {
        var options = new DoctorOptions
        {
            Fix = result.GetValue(CliTree.Fix),
            FixAll = result.GetValue(CliTree.FixAll),
            Strict = result.GetValue(CliTree.Strict),
            Offline = result.GetValue(CliTree.Offline),
            ListChecks = result.GetValue(CliTree.ListChecks),
            LogFile = result.GetValue(CliTree.Log),
        };
        if (result.GetValue(CliTree.BuildConfiguration) is { } configuration) options.Configuration = configuration;
        foreach (var id in result.GetValue(CliTree.Ignore) ?? []) options.Ignore.Add(id);
        if (result.GetValue(CliTree.Build) is { Length: > 0 } build)
            options.BuildTarget = CliTree.OptionalValue(build[^1]) ?? "";
        return options;
    }

    private static void ReadScaffoldOptions(ParseResult result, ParsedCommand cmd)
    {
        cmd.Options.DryRun = result.GetValue(CliTree.DryRun);
        cmd.Options.Force = result.GetValue(CliTree.Force);
        cmd.Options.Restore = !result.GetValue(CliTree.NoRestore);

        // Host requests in command-line order (hosts are created in that order): walk the tokens rather than the
        // per-option values, which would group them by kind.
        var tokens = result.Tokens;
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Type != TokenType.Option || CliTree.HostKindOfFlag(tokens[i].Value) is not { } kind) continue;
            // The pre-pass guarantees the argument token.
            cmd.Requested.Add(new HostRequest(kind, CliTree.OptionalValue(tokens[i + 1].Value)));
        }

        foreach (var (kind, option) in CliTree.NoHostOptions)
        {
            if (!result.GetValue(option)) continue;
            cmd.Excluded.Add(kind);
            if (!Hosts.InDefaultSet(kind))
                cmd.Notes.Add($"{option.Name} changes nothing: {Hosts.Label(kind)} hosts are only created when asked for");
        }
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
    /// What a run creates when no host was named: every default-set kind the project does not have yet, minus the
    /// excluded ones. Missing kinds follow from the recognized hosts; folder names avoid every directory.
    /// </summary>
    public static List<HostSpec> Defaults(ICollection<HostKind> excluded, ProjectContext project)
    {
        var taken = new List<string>(project.TakenFolders);
        var hosts = new List<HostSpec>();
        foreach (var kind in Hosts.All)
        {
            if (!Hosts.InDefaultSet(kind)) continue;
            if (excluded.Contains(kind) || project.ExistingHosts.Any(h => h.Kind == kind)) continue;
            var folder = Hosts.AllocateFolder(kind, project.BaseName, taken);
            taken.Add(folder);
            hosts.Add(new HostSpec(kind, folder));
        }

        return hosts;
    }
}

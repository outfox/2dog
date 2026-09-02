using System.CommandLine;

namespace twodog.cli;

/// <summary>
/// The command tree, the single source of truth for verbs, options and their help text: CommandLine parses with it,
/// Usage renders from it, and the docs drift test locks the documentation against it.
/// </summary>
internal static class CliTree
{
    public static readonly ParserConfiguration Configuration = new()
    {
        // No @file expansion (an argument starting with @ stays literal) and no -yv style flag bundling.
        ResponseFileTokenReplacer = null,
        EnablePosixBundling = false,
    };

    /// <summary>
    /// The host folder value standing for "pick a free name". The command-line pre-pass gives a bare host flag this
    /// value: System.CommandLine drops empty option values and would swallow the next token instead.
    /// </summary>
    public const string AnyFolder = "*";

    // Global options, valid under every verb.
    public static readonly Option<bool> HelpOption = new("--help", "-h")
        { Recursive = true, Description = "Show help; after a verb, the help for that verb" };

    public static readonly Option<bool> VersionOption = new("--version")
    {
        Recursive = true,
        Description = "Print tool and package versions; under dnx use '2dog version' (dnx consumes --version " +
                      "itself; to pin the tool version: 'dnx 2dog@<version>')",
    };

    public static readonly Option<string?> Pin = new("--pin")
        { Recursive = true, Hidden = true, Arity = ArgumentArity.ZeroOrOne };

    public static readonly Option<bool> Yes = new("--yes", "-y", "--non-interactive", "--no-input")
        { Recursive = true, Description = "Do not prompt; take the flags and defaults (also --non-interactive, --no-input)" };

    public static readonly Option<bool> Verbose = new("--verbose", "-v")
    {
        Recursive = true,
        Description = "Extra detail on stderr: subprocess command lines and their output, stack traces",
    };

    // Output options: how things are printed, never what is done.
    public static readonly Option<bool> Json = new("--json")
        { Recursive = true, Description = "Machine-readable output: one JSON document on stdout, nothing else; implies --yes" };

    public static readonly Option<bool> Quiet = new("--quiet", "-q")
        { Recursive = true, Description = "Results and problems only: no header, plan, progress or next steps" };

    public static readonly Option<bool> PlainOption = new("--plain")
        { Recursive = true, Description = "No colour, no cursor movement, ASCII markers (also TERM=dumb)" };

    public static readonly Option<bool> NoColor = new("--no-color")
        { Recursive = true, Description = "No colour (also the NO_COLOR environment variable)" };

    public static readonly Option<bool> Accessible = new("--accessible")
    {
        Recursive = true,
        Description = "Screen-reader friendly: numbered yes/no questions instead of lists, no spinners (also " +
                      "TWODOG_ACCESSIBLE=1)",
    };

    /// <summary>The output options, in help order.</summary>
    public static readonly Option[] OutputOptions = [Json, Quiet, PlainOption, NoColor, Accessible, Verbose];

    // Scaffolding options.
    public static readonly Option<string> Name = new("--name", "-n")
    {
        HelpName = "name",
        Description = "Project name (new) or base name override (names are reduced to letters, digits, '.', '_' " +
                      "and '-'; adjustments are announced)",
    };

    public static readonly Option<string> Rename = new("--rename")
    {
        HelpName = "NewName",
        Description = "Fix a project whose .NET name contains spaces (breaks publish): renames the csproj, sets " +
                      "assembly_name, repoints the solution. add/convert only, before any hosts exist",
    };

    public static readonly Option<string> Output = new("--output", "-o")
        { HelpName = "dir", Description = "Directory for a new project" };

    public static readonly Option<bool> DryRun = new("--dry-run")
        { Description = "Print planned actions without changing anything" };

    public static readonly Option<bool> Force = new("--force")
        { Description = "Overwrite files that already exist (never deletes)" };

    public static readonly Option<bool> NoRestore = new("--no-restore")
        { Description = "Skip the final 'dotnet restore'" };

    public static readonly Option<bool> AllowDirty = new("--allow-dirty")
        { Description = "Update although the git working tree has uncommitted changes" };

    /// <summary>Rejected with an explanation: the running tool's versions are the only target.</summary>
    public static readonly Option<string?> To = new("--to") { Hidden = true, Arity = ArgumentArity.ZeroOrOne };

    // Doctor options.
    public static readonly Option<bool> Fix = new("--fix")
        { Description = "Apply the safe fixes, then check again" };

    public static readonly Option<bool> FixAll = new("--fix-all")
        { Description = "Also apply the announced fixes (solution migration, target framework, bootstrap refresh)" };

    /// <summary>Optional value: the pre-pass gives a bare --build the AnyFolder sentinel, meaning the default target.</summary>
    public static readonly Option<string[]> Build = new("--build")
    {
        Arity = ArgumentArity.OneOrMore,
        AllowMultipleArgumentsPerToken = false,
        HelpName = "target",
        Description = "Build the solution, or a host folder or project, and explain known failures",
    };

    public static readonly Option<string> BuildConfiguration = new("--configuration", "-c")
        { HelpName = "Cfg", Description = "Configuration for --build (default Debug)" };

    public static readonly Option<string> Log = new("--log")
        { HelpName = "file", Description = "Only explain an existing build, restore or runtime log ('-' reads stdin)" };

    public static readonly Option<bool> Offline = new("--offline")
        { Description = "Skip the nuget.org check" };

    public static readonly Option<string[]> Ignore = new("--ignore")
    {
        Arity = ArgumentArity.OneOrMore,
        AllowMultipleArgumentsPerToken = false,
        HelpName = "id",
        Description = "Drop a finding by id (repeatable; --list-checks shows the ids)",
    };

    public static readonly Option<bool> Strict = new("--strict")
        { Description = "Warnings count as findings for the exit code" };

    public static readonly Option<bool> ListChecks = new("--list-checks")
        { Description = "List every check and build-log signature" };

    /// <summary>Flags whose value is optional; the pre-pass attaches or invents one.</summary>
    public static IEnumerable<Option> OptionalValueOptions => HostOptions.Values.Cast<Option>().Append(Build);

    /// <summary>The value of an optional-value flag as the user meant it: null when the pre-pass invented it.</summary>
    public static string? OptionalValue(string value) => value == AnyFolder ? null : value;

    /// <summary>One repeatable option per host kind; each value is a folder name or <see cref="AnyFolder"/>.</summary>
    public static readonly IReadOnlyDictionary<HostKind, Option<string[]>> HostOptions =
        Hosts.All.ToDictionary(k => k, HostOption);

    /// <summary>--no-&lt;host&gt; per kind; only the default-set kinds are advertised, the rest parse but are no-ops.</summary>
    public static readonly IReadOnlyDictionary<HostKind, Option<bool>> NoHostOptions =
        Hosts.All.ToDictionary(k => k, NoHostOption);

    // Arguments.
    public static readonly Argument<string?> NewNameArg = new("Name") { Arity = ArgumentArity.ZeroOrOne };
    public static readonly Argument<string?> NewDirArg = new("dir") { Arity = ArgumentArity.ZeroOrOne };
    public static readonly Argument<string?> ProjectPathArg = new("path") { Arity = ArgumentArity.ZeroOrOne };
    public static readonly Argument<string> PackFileArg = new("pck");
    public static readonly Argument<string?> HelpVerbArg = new("verb") { Arity = ArgumentArity.ZeroOrOne };

    // Verbs.
    public static readonly Command New = new("new", "Create a new Godot project with 2dog hosts");
    public static readonly Command Add = new("add", "Add hosts to an existing Godot project");
    public static readonly Command Update = new("update", "Update a project's 2dog packages to this tool's versions");
    public static readonly Command Doctor = new("doctor", "Check the project and this machine, fix what can be fixed");
    public static readonly Command Pack = new("pack", "Inspect exported .pck files (no engine needed)");
    public static readonly Command PackList = new("list", "List a .pck's contents by size (no engine needed)");
    public static readonly Command VersionCommand = new("version", "Print tool and package versions");
    public static readonly Command HelpCommand = new("help", "Show help, for one verb when named");
    public static readonly RootCommand Root = new("2dog");

    /// <summary>Verb aliases get their own row in the verb table, with their own wording.</summary>
    public static readonly IReadOnlyDictionary<string, string> AliasDescriptions = new Dictionary<string, string>
    {
        ["convert"] = "Alias of add, for projects that have no hosts yet",
    };

    static CliTree()
    {
        // The built-in --help/--version actions print System.CommandLine's own help; ours render through Usage.
        foreach (var option in Root.Options.ToList()) Root.Options.Remove(option);
        foreach (var option in new Option[] { Yes, VersionOption, HelpOption, Pin }.Concat(OutputOptions)) Root.Options.Add(option);

        New.Arguments.Add(NewNameArg);
        New.Arguments.Add(NewDirArg);
        New.Options.Add(Name);
        New.Options.Add(Output);

        Add.Aliases.Add("convert");
        Add.Arguments.Add(ProjectPathArg);
        Add.Options.Add(Name);
        Add.Options.Add(Rename);

        foreach (var command in new[] { New, Add })
        {
            foreach (var kind in Hosts.All) command.Options.Add(HostOptions[kind]);
            foreach (var kind in Hosts.All) command.Options.Add(NoHostOptions[kind]);
            foreach (var option in new Option[] { DryRun, Force, NoRestore }) command.Options.Add(option);
        }

        Update.Arguments.Add(ProjectPathArg);
        foreach (var option in new Option[] { DryRun, NoRestore, AllowDirty, To }) Update.Options.Add(option);

        Doctor.Arguments.Add(ProjectPathArg);
        foreach (var option in new Option[] { Fix, FixAll, Build, BuildConfiguration, Log, Offline, Ignore, Strict, ListChecks })
            Doctor.Options.Add(option);

        PackList.Arguments.Add(PackFileArg);
        Pack.Subcommands.Add(PackList);
        HelpCommand.Arguments.Add(HelpVerbArg);

        foreach (var command in new[] { New, Add, Doctor, Update, Pack, VersionCommand, HelpCommand }) Root.Subcommands.Add(command);
    }

    private static Option<string[]> HostOption(HostKind kind)
    {
        var option = new Option<string[]>(Hosts.Flag(kind))
        {
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = false,
            HelpName = "folder",
            Description = Hosts.HelpText(kind),
        };
        foreach (var alias in Hosts.FlagAliases(kind)) option.Aliases.Add(alias);
        return option;
    }

    private static Option<bool> NoHostOption(HostKind kind) => new(Hosts.NoFlag(kind))
    {
        Hidden = !Hosts.InDefaultSet(kind),
        Description = "Leave a host out of the default set",
    };

    public static Verb? VerbOf(Command command)
    {
        if (command == New) return Verb.New;
        if (command == Add) return Verb.Add;
        if (command == Doctor) return Verb.Doctor;
        if (command == Update) return Verb.Update;
        if (command == Pack || command == PackList) return Verb.Pack;
        if (command == VersionCommand) return Verb.Version;
        if (command == HelpCommand) return Verb.Help;
        return null;
    }

    public static Command? CommandOf(Verb verb) => verb switch
    {
        Verb.New => New,
        Verb.Add => Add,
        Verb.Doctor => Doctor,
        Verb.Update => Update,
        Verb.Pack => Pack,
        Verb.Version => VersionCommand,
        Verb.Help => HelpCommand,
        _ => null,
    };

    /// <summary>The verb a user-typed name or alias means, or null.</summary>
    public static Verb? VerbNamed(string name) =>
        Root.Subcommands.FirstOrDefault(c => c.Name == name || c.Aliases.Contains(name)) is { } command
            ? VerbOf(command)
            : null;

    public static IEnumerable<string> VerbNames =>
        Root.Subcommands.SelectMany(c => c.Aliases.Prepend(c.Name));

    /// <summary>
    /// The verb path the leading tokens name (root first) and the index of the first token past it. Options valid at
    /// each level are stepped over on the way (`2dog --json add`), with their detached value when they take one.
    /// </summary>
    public static (List<Command> Path, int Consumed) Resolve(IReadOnlyList<string> args)
    {
        var path = new List<Command> { Root };
        var i = 0;
        for (; i < args.Count; i++)
        {
            var arg = args[i];
            if (SubcommandOf(path[^1], arg) is { } sub)
            {
                path.Add(sub);
                continue;
            }

            if (arg == "--" || arg.Length < 2 || !arg.StartsWith('-')) break;
            var name = arg.Split(['=', ':'], 2)[0];
            var option = OptionsOf(path).FirstOrDefault(o => NamesOf(o).Contains(name, StringComparer.Ordinal));
            if (option is null) break;
            // Same rule as the parser: a following token is the option's value unless it is a flag or a verb.
            if (name == arg && option.Arity.MaximumNumberOfValues > 0 && i + 1 < args.Count
                && !args[i + 1].StartsWith('-') && SubcommandOf(path[^1], args[i + 1]) is null)
                i++;
        }

        return (path, i);
    }

    private static Command? SubcommandOf(Command command, string token) =>
        command.Subcommands.FirstOrDefault(c => c.Name == token || c.Aliases.Contains(token));

    /// <summary>The options a command accepts: its own plus the recursive ones of its ancestors.</summary>
    public static IEnumerable<Option> OptionsOf(IReadOnlyList<Command> path) =>
        path[^1].Options.Concat(path.Take(path.Count - 1).SelectMany(c => c.Options.Where(o => o.Recursive)));

    public static IEnumerable<Option> AllOptions => Commands.SelectMany(c => c.Options).Distinct();

    public static IEnumerable<Command> Commands => Walk(Root);

    private static IEnumerable<Command> Walk(Command command)
    {
        yield return command;
        foreach (var sub in command.Subcommands)
        foreach (var nested in Walk(sub))
            yield return nested;
    }

    /// <summary>Every spelling of an option: its name and its aliases.</summary>
    public static IEnumerable<string> NamesOf(Option option) => option.Aliases.Prepend(option.Name);

    public static HostKind? HostKindOf(Option option)
    {
        foreach (var (kind, candidate) in HostOptions)
            if (ReferenceEquals(candidate, option))
                return kind;
        return null;
    }

    /// <summary>The host kind a flag spelling (name or alias) selects, or null.</summary>
    public static HostKind? HostKindOfFlag(string flag)
    {
        foreach (var (kind, option) in HostOptions)
            if (NamesOf(option).Contains(flag, StringComparer.Ordinal))
                return kind;
        return null;
    }

    public static bool IsHostOption(Option option) =>
        HostKindOf(option) != null || NoHostOptions.Values.Any(o => ReferenceEquals(o, option));

    /// <summary>The label a verb table row shows: the verb and its positional arguments.</summary>
    public static string Signature(Command command, string? name = null)
    {
        var parts = new List<string> { name ?? command.Name };
        parts.AddRange(command.Arguments.Select(a =>
            a.Arity.MinimumNumberOfValues == 0 ? $"[{a.Name}]" : $"<{a.Name}>"));
        return string.Join(' ', parts);
    }
}

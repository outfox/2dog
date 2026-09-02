using System.CommandLine;
using System.Text;
using Spectre.Console;

namespace twodog.cli;

/// <summary>Renders help from <see cref="CliTree"/> in the tool's own style: sections, aligned rows, examples.</summary>
internal static class Usage
{
    private const int Width = 80;
    private const int MaxLabel = 22;
    private const int MaxExampleLabel = 44;

    private const string Preamble =
        """
        Scaffolds .NET host projects for a Godot project: the Godot project
        directory is the solution root, hosts are nested subfolders the Godot
        editor ignores (.gdignore). It creates files and edits *.csproj,
        project.godot, the solution and Directory.Build.props in place; nothing
        is moved, renamed or deleted, except for two announced opt-ins: the
        .sln-to-.slnx migration, and the --rename fix for names with spaces.
        """;

    private const string Interactive =
        """
        Without any host option, new and add ask interactively; run add again
        to add more hosts, including a second host of the same kind.
        """;

    private enum Kind { Plain, Section, Usage }

    private sealed record Line(string Text, Kind Kind = Kind.Plain);

    private sealed record Row(string Label, string Description);

    /// <summary>The options the general help lists, in display order (output options have their own section).</summary>
    private static readonly Option[] GeneralOptions =
    [
        CliTree.Name, CliTree.Rename, CliTree.Output, CliTree.Yes, CliTree.DryRun, CliTree.Force, CliTree.NoRestore,
        CliTree.VersionOption, CliTree.HelpOption,
    ];

    public static void Print(Verb? verb, bool withHeader = true)
    {
        if (withHeader) Out.Header();
        foreach (var line in Lines(verb))
        {
            switch (line.Kind)
            {
                case Kind.Section:
                    Out.Line($"[bold]{line.Text}[/]");
                    break;
                case Kind.Usage:
                    Out.Line($"[bold]usage:[/]{Markup.Escape(line.Text["usage:".Length..])}");
                    break;
                default:
                    Out.Plain(line.Text);
                    break;
            }
        }
    }

    /// <summary>The help text as plain lines (tests and the docs drift guard read this).</summary>
    internal static string Render(Verb? verb) => string.Join('\n', Lines(verb).Select(l => l.Text));

    /// <summary>The one-line pointer printed after a usage error.</summary>
    public static string Hint(Verb? verb) =>
        verb is { } v && CliTree.CommandOf(v) is { } command ? $"see '2dog {command.Name} --help'" : "see '2dog --help'";

    private static IEnumerable<Line> Lines(Verb? verb) =>
        verb is { } v && CliTree.CommandOf(v) is { } command ? VerbLines(v, command) : GeneralLines();

    private static IEnumerable<Line> GeneralLines()
    {
        yield return new Line("usage: 2dog <verb> [path] [options]", Kind.Usage);
        yield return new Line("");
        foreach (var line in Paragraph(Preamble)) yield return line;
        yield return new Line("");
        yield return new Line("verbs", Kind.Section);
        foreach (var line in Rows(VerbRows())) yield return line;
        yield return new Line("");
        foreach (var line in Paragraph(Interactive)) yield return line;
        yield return new Line("");
        yield return new Line("hosts", Kind.Section);
        foreach (var line in Rows(HostRows())) yield return line;
        yield return new Line("");
        yield return new Line("options", Kind.Section);
        foreach (var line in Rows(GeneralOptions.Select(OptionRow))) yield return line;
        yield return new Line("");
        yield return new Line("doctor, update", Kind.Section);
        foreach (var line in Rows(VerbOnlyOptions.Select(OptionRow))) yield return line;
        yield return new Line("");
        yield return new Line("output", Kind.Section);
        foreach (var line in Rows(CliTree.OutputOptions.Select(OptionRow))) yield return line;
        yield return new Line("");
        yield return new Line("examples", Kind.Section);
        foreach (var line in Rows(Examples(null), MaxExampleLabel)) yield return line;
    }

    /// <summary>Options of the doctor and update verbs, for the general help's own section.</summary>
    private static IEnumerable<Option> VerbOnlyOptions =>
        CliTree.Doctor.Options.Concat(CliTree.Update.Options)
            .Where(o => !o.Hidden && !GeneralOptions.Contains(o) && !CliTree.OutputOptions.Contains(o))
            .Distinct();

    private static IEnumerable<Line> VerbLines(Verb verb, Command command)
    {
        var leaf = command.Subcommands.Count == 1 ? command.Subcommands[0] : command;
        var signature = command.Subcommands.Count == 1
            ? $"{command.Name} {CliTree.Signature(leaf)}"
            : CliTree.Signature(command);
        var hosts = leaf.Options.Where(CliTree.IsHostOption).Any();
        var usage = $"usage: 2dog {signature}" + (hosts ? " [hosts]" : "") + (leaf.Options.Count > 0 || hosts ? " [options]" : "");
        yield return new Line(usage, Kind.Usage);
        yield return new Line("");
        foreach (var line in Wrap($"{leaf.Description}. {Prose(verb)}".TrimEnd(), Width))
            yield return new Line(line);

        if (hosts)
        {
            yield return new Line("");
            yield return new Line("hosts", Kind.Section);
            foreach (var line in Rows(HostRows())) yield return line;
        }

        var options = leaf.Options.Where(o => !o.Hidden && !CliTree.IsHostOption(o))
            .Concat(CliTree.Root.Options.Where(o => !o.Hidden && !CliTree.OutputOptions.Contains(o)))
            .ToList();
        if (options.Count > 0)
        {
            yield return new Line("");
            yield return new Line("options", Kind.Section);
            foreach (var line in Rows(options.Select(OptionRow))) yield return line;
        }

        yield return new Line("");
        yield return new Line("output", Kind.Section);
        foreach (var line in Rows(CliTree.OutputOptions.Select(OptionRow))) yield return line;

        var examples = Examples(verb).ToList();
        if (examples.Count > 0)
        {
            yield return new Line("");
            yield return new Line("examples", Kind.Section);
            foreach (var line in Rows(examples, MaxExampleLabel)) yield return line;
        }
    }

    private static string Prose(Verb verb) => verb switch
    {
        Verb.New => "Without host options it asks which hosts to create; any host flag or -y runs unattended. " +
                    "The directory defaults to the (sanitized) project name.",
        Verb.Add => "The path defaults to the current directory. Run it again to add more hosts, including a " +
                    "second host of the same kind. Without host options it asks interactively; any host flag or " +
                    "-y runs unattended. Existing files are never overwritten without --force.",
        Verb.Doctor => "Static checks by default (fast, works offline): the machine, the layout, every csproj, the " +
                       "solution, versions, export presets, project.godot. Safe fixes apply under --fix, announced " +
                       "ones under --fix-all; interactively it asks. Exit code 3 while findings remain. --build " +
                       "runs dotnet build and explains failures it knows; --log explains a log you already have.",
        Verb.Update => "Targets the versions of the running tool (dnx 2dog fetches the newest). Literal versions " +
                       "still in host csprojs move to the root Directory.Build.props first; the game project's " +
                       "Godot.NET.Sdk follows. Never downgrades; refuses a dirty git tree without --allow-dirty.",
        Verb.Pack => "Reads the pack directly, so it works on any exported .pck without a project or engine.",
        Verb.Version => "Asks nuget.org (best effort, 2.5 s) whether newer stable packages exist.",
        _ => "",
    };

    private static IEnumerable<Row> VerbRows()
    {
        foreach (var command in CliTree.Root.Subcommands.Where(c => !c.Hidden))
        {
            if (command.Subcommands.Count > 0)
            {
                foreach (var sub in command.Subcommands.Where(s => !s.Hidden))
                    yield return new Row($"{command.Name} {CliTree.Signature(sub)}", sub.Description ?? "");
                continue;
            }

            yield return new Row(CliTree.Signature(command), command.Description ?? "");
            foreach (var alias in command.Aliases)
                yield return new Row(CliTree.Signature(command, alias),
                    CliTree.AliasDescriptions.GetValueOrDefault(alias, $"Alias of {command.Name}"));
        }
    }

    private static IEnumerable<Row> HostRows()
    {
        foreach (var kind in Hosts.All)
            yield return new Row($"{Hosts.Flag(kind)} [folder]", Hosts.HelpText(kind));

        var advertised = Hosts.All.Where(k => !CliTree.NoHostOptions[k].Hidden).Select(Hosts.NoFlag);
        yield return new Row(string.Join(", ", advertised),
            "Leave a host out of the default set (every kind has a --no-<host> form; the opt-in kinds are never " +
            "in that set, so theirs change nothing)");
    }

    private static Row OptionRow(Option option)
    {
        var label = new StringBuilder();
        if (option.Aliases.FirstOrDefault(a => a.Length == 2 && a[0] == '-' && a[1] != '-') is { } shortAlias)
            label.Append(shortAlias).Append(", ");
        label.Append(option.Name);
        if (option.ValueType != typeof(bool) && option.HelpName is { } value)
            label.Append(" <").Append(value).Append('>');
        return new Row(label.ToString(), option.Description ?? "");
    }

    private static IEnumerable<Row> Examples(Verb? verb)
    {
        (string Command, string Comment)[] rows = verb switch
        {
            null =>
            [
                ("2dog add", "interactive, here"),
                ("2dog new MyGame", "interactive host choice, new project"),
                ("2dog new MyGame --desktop --tests", ""),
                ("2dog add --desktop MyGame.editor", ""),
                ("2dog add path/to/project --no-web", ""),
                ("2dog doctor", "check the project; offers fixes"),
                ("2dog update", "packages to this tool's versions"),
                ("2dog pack list MyGame.web/AppBundle/godot.pck", ""),
            ],
            Verb.New =>
            [
                ("2dog new MyGame", "interactive host choice"),
                ("2dog new MyGame --desktop --tests", "unattended"),
                ("2dog new \"My Game\" -o games/mine --no-web", "name adjusted to MyGame"),
            ],
            Verb.Add =>
            [
                ("2dog add", "interactive, here"),
                ("2dog add --desktop MyGame.editor", "a second desktop host, named"),
                ("2dog add path/to/project --no-web", ""),
                ("2dog add --rename MyGame", "fix a spaced .NET name first"),
            ],
            Verb.Doctor =>
            [
                ("2dog doctor", "check, then ask which fixes to apply"),
                ("2dog doctor --fix", "apply the safe fixes unattended"),
                ("2dog doctor --build MyGame.web -c Release", "build one host and explain failures"),
                ("2dog doctor --log build.log", "explain an existing log"),
                ("2dog doctor --json --strict", "for CI: exit 3 on any warning"),
            ],
            Verb.Update =>
            [
                ("2dog update", "here, after committing"),
                ("2dog update path/to/project --dry-run", "show what would change"),
                ("dnx 2dog@4.7.2.80 update", "a specific tool version"),
            ],
            Verb.Pack => [("2dog pack list MyGame.web/AppBundle/godot.pck", "")],
            Verb.Version => [("2dog version", "")],
            Verb.Help => [("2dog help new", "")],
            _ => [],
        };
        return rows.Select(r => new Row(r.Command, r.Comment.Length == 0 ? "" : $"# {r.Comment}"));
    }

    /// <summary>Two-column rows: labels padded to the widest (capped), descriptions wrapped and aligned.</summary>
    private static IEnumerable<Line> Rows(IEnumerable<Row> rows, int maxLabel = MaxLabel)
    {
        var list = rows.ToList();
        var labelWidth = Math.Min(maxLabel, list.Max(r => r.Label.Length));
        var indent = new string(' ', labelWidth + 4);
        foreach (var row in list)
        {
            var wrapped = Wrap(row.Description, Width - labelWidth - 4).ToList();
            if (row.Label.Length > labelWidth)
            {
                yield return new Line($"  {row.Label}");
                foreach (var line in wrapped.Where(l => l.Length > 0)) yield return new Line(indent + line);
                continue;
            }

            yield return new Line($"  {row.Label.PadRight(labelWidth)}  {wrapped[0]}".TrimEnd());
            foreach (var line in wrapped.Skip(1)) yield return new Line(indent + line);
        }
    }

    private static IEnumerable<Line> Paragraph(string text) =>
        text.Split('\n').Select(l => new Line(l.TrimEnd('\r')));

    /// <summary>Greedy word wrap; explicit newlines start a new paragraph.</summary>
    internal static IEnumerable<string> Wrap(string text, int width)
    {
        foreach (var paragraph in text.Split('\n'))
        {
            var line = new StringBuilder();
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Length > 0 && line.Length + 1 + word.Length > width)
                {
                    yield return line.ToString();
                    line.Clear();
                }

                if (line.Length > 0) line.Append(' ');
                line.Append(word);
            }

            yield return line.ToString();
        }
    }
}

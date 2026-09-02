using Spectre.Console;

namespace twodog.cli;

/// <summary>
/// The one Spectre gateway for non-interactive output (prompts live in Tui). stdout carries the report, stderr the
/// diagnostics (errors, warnings, notes, verbose lines, spinners). Writes go through a forwarder resolving the
/// *current* Console.Out/Error per write, since tests swap them per test; redirected stdio degrades to plain text.
/// </summary>
internal static class Out
{
    private static ConsoleFacts? _pinnedFacts;
    private static IAnsiConsole _stdout = null!;
    private static IAnsiConsole _stderr = null!;
    private static readonly TextWriter StdoutWriter = new ForwardingWriter(stdErr: false);

    /// <summary>The active mode: Main configures it from the command line, tests pin a redirected one.</summary>
    public static OutputMode Mode { get; private set; } = null!;

    static Out() => Configure(OutputMode.Resolve([], Environment.GetEnvironmentVariable, Facts));

    /// <summary>Console facts as captured at startup, or as pinned by tests.</summary>
    public static ConsoleFacts Facts => _pinnedFacts ?? ConsoleFacts.Capture();

    /// <summary>The environment the mode is resolved from; tests pin an empty one so a runner's colour settings cannot leak in.</summary>
    public static Func<string, string?> Env { get; private set; } = Environment.GetEnvironmentVariable;

    public static IAnsiConsole Console => _stdout;
    public static IAnsiConsole Error => _stderr;

    /// <summary>A plain stdout writer for output produced outside Spectre (the pack listing).</summary>
    public static TextWriter Writer => StdoutWriter;

    /// <summary>
    /// Everything a run reported, for the --json envelope. Flow-local: tests run several commands in parallel in one
    /// process, and one run's Configure must not clear what another is still collecting.
    /// </summary>
    public static List<string> Warnings => Collected.Warnings;
    public static List<string> Notes => Collected.Notes;
    public static List<string> Errors => Collected.Errors;
    public static List<string> Hints => Collected.Hints;

    private sealed class Collectors
    {
        public readonly List<string> Warnings = [], Notes = [], Errors = [], Hints = [];
    }

    private static readonly AsyncLocal<Collectors?> CollectedSlot = new();
    private static Collectors Collected => CollectedSlot.Value ??= new Collectors();

    public static void Configure(OutputMode mode)
    {
        Mode = mode;
        _stdout = Create(stdErr: false);
        _stderr = Create(stdErr: true);
        CollectedSlot.Value = new Collectors();
        TerminalDirty = false;
    }

    /// <summary>
    /// Tests pin the console facts: a test host may have an attached terminal (ANSI, narrow width) while Console.Out
    /// is a StringWriter, which is undetectable from here.
    /// </summary>
    internal static void PinConsoleFacts(ConsoleFacts facts)
    {
        _pinnedFacts = facts;
        Env = _ => null;
        Configure(OutputMode.Resolve([], Env, facts));
    }

    private static IAnsiConsole Create(bool stdErr)
    {
        var redirected = stdErr ? Facts.ErrorRedirected : Facts.OutputRedirected;
        return AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = Mode.Plain || (redirected && !Mode.ForceColor) ? AnsiSupport.No
                : Mode.ForceColor ? AnsiSupport.Yes
                : AnsiSupport.Detect,
            ColorSystem = Mode.Plain || Mode.NoColor ? ColorSystemSupport.NoColors
                : Mode.ForceColor ? ColorSystemSupport.EightBit
                : ColorSystemSupport.Detect,
            Interactive = Mode.CanPrompt ? InteractionSupport.Detect : InteractionSupport.No,
            Out = new ForwardingOutput(stdErr),
            // Spectre's CI enrichers (GITHUB_ACTIONS, TF_BUILD, ...) would force ANSI back on; OutputMode owns that.
            Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false },
        });
    }

    /// <summary>
    /// Spectre output over the process's current Console.Out/Error. Terminal facts come from the real console (or
    /// the pinned facts), not from whichever writer happens to be installed.
    /// </summary>
    private sealed class ForwardingOutput(bool stdErr) : IAnsiConsoleOutput
    {
        public TextWriter Writer { get; } = new ForwardingWriter(stdErr);

        // The snapshot the mode was resolved with: this runs on every write, so no re-capture.
        public bool IsTerminal => !Mode.Plain && !(stdErr ? Mode.Console.ErrorRedirected : Mode.Console.OutputRedirected);

        // Redirected output never wraps (Console.WriteLine parity: piped and
        // captured lines stay grep- and assert-able in one piece).
        public int Width => IsTerminal ? Safe(() => System.Console.BufferWidth, 80) : 4096;
        public int Height => IsTerminal ? Safe(() => System.Console.BufferHeight, 24) : 4096;

        public void SetEncoding(System.Text.Encoding encoding) { /* UTF-8 is forced in Main */ }

        private static int Safe(Func<int> get, int fallback)
        {
            try
            {
                var value = get();
                return value > 0 ? value : fallback;
            }
            catch (IOException)
            {
                return fallback;
            }
        }
    }

    private sealed class ForwardingWriter(bool stdErr) : TextWriter
    {
        private TextWriter Target => stdErr ? System.Console.Error : System.Console.Out;

        public override System.Text.Encoding Encoding => Target.Encoding;
        public override void Write(char value) => Target.Write(value);
        public override void Write(string? value) => Target.Write(value);
        public override void WriteLine(string? value) => Target.WriteLine(value);
        public override void Flush() => Target.Flush();
    }

    /// <summary>Narration (headers, plans, progress, summaries) is dropped under --quiet and --json.</summary>
    private static bool Narrating => !Mode.Quiet && !Mode.Json;

    /// <summary>A result line on stdout: printed in every mode but --json. Caller escapes user text.</summary>
    public static void Line(string markup)
    {
        if (!Mode.Json) Console.MarkupLine(markup);
    }

    /// <summary>Text printed verbatim on stdout - never parsed as markup.</summary>
    public static void Plain(string text)
    {
        if (!Mode.Json) Console.WriteLine(text);
    }

    /// <summary>A narration line on stdout. Caller escapes user text.</summary>
    public static void Info(string markup)
    {
        if (Narrating) Console.MarkupLine(markup);
    }

    public static void Blank()
    {
        if (Narrating) Console.WriteLine();
    }

    public static void Header()
    {
        Info($"[bold]2dog[/] [grey]{ToolVersions.TwoDogVersion}[/]  [grey]https://2dog.dev[/]");
        Blank();
    }

    /// <summary>The project line and its hosts, as new/add/doctor introduce a run.</summary>
    public static void ProjectSummary(string what, string name, string dir, IEnumerable<(string Folder, HostKind Kind)> hosts)
    {
        Info($"[grey]{what}[/]  [bold]{Markup.Escape(name)}[/] [grey]({Markup.Escape(dir)})[/]");
        var list = hosts.Select(h => $"{Markup.Escape(h.Folder)} [grey]({Hosts.Label(h.Kind)})[/]").ToList();
        if (list.Count > 0) Info($"[grey]hosts[/]    {string.Join("[grey],[/] ", list)}");
        Blank();
    }

    /// <summary>A left-titled grey divider between output sections. On a terminal it spans the window; redirected
    /// it stays a short fixed line (the redirected width is huge so lines never wrap).</summary>
    public static void Rule(string title)
    {
        if (!Narrating) return;
        if (Console.Profile.Out.IsTerminal)
            Console.Write(new Rule($"[grey]{Markup.Escape(title)}[/]") { Style = "grey", Justification = Justify.Left });
        else
            Console.MarkupLine($"[grey]{Glyph("──", "--")} {Markup.Escape(title)} {Glyph("──", "--")}[/]");
    }

    public static void Note(string text)
    {
        Notes.Add(text);
        if (Narrating) Error.MarkupLine($"[grey]note:[/] {Markup.Escape(text)}");
    }

    public static void Warning(string text)
    {
        Warnings.Add(text);
        if (!Mode.Json) Error.MarkupLine($"[yellow]warning:[/] {Markup.Escape(text)}");
    }

    public static void Skip(string text) => Line($"[grey]skip:[/] {Markup.Escape(text)}");
    public static void Would(string text) => Line($"[grey]would:[/] {Markup.Escape(text)}");
    public static void Action(string description) => Info($"[green]+[/] {Markup.Escape(description)}");

    public static void ErrorLine(string message)
    {
        Errors.Add(message);
        if (!Mode.Json) Error.MarkupLine($"[red]error:[/] {Markup.Escape(message)}");
    }

    /// <summary>A pointer after an error, on stderr next to it.</summary>
    public static void Hint(string text)
    {
        Hints.Add(text);
        if (!Mode.Json) Error.MarkupLine($"[grey]hint:[/] {Markup.Escape(text)}");
    }

    /// <summary>Extra detail on stderr, only under --verbose.</summary>
    public static void Verbose(string text)
    {
        if (Mode.Verbose && !Mode.Json) Error.MarkupLine($"[grey]verbose:[/] {Markup.Escape(text)}");
    }

    /// <summary>A subprocess output line, streamed on stderr under --verbose.</summary>
    public static void Echo(string line)
    {
        if (Mode.Verbose && !Mode.Json) Error.MarkupLine($"[grey]  | {Markup.Escape(line)}[/]");
    }

    /// <summary>An indented detail line under an error, on stderr.</summary>
    public static void Detail(string line)
    {
        if (!Mode.Json) Error.MarkupLine($"[grey]  {Markup.Escape(line)}[/]");
    }

    /// <summary>The UTF-8 glyph on a capable terminal, the ASCII stand-in everywhere else.</summary>
    public static string Glyph(string utf8, string ascii) => Mode.Unicode ? utf8 : ascii;

    /// <summary>
    /// Runs work behind a spinner on stderr when the mode allows animation; otherwise the label is a verbose line.
    /// </summary>
    public static T Status<T>(string label, Func<T> work)
    {
        if (Mode.Animate)
        {
            TerminalDirty = true;
            return Error.Status().Start(Markup.Escape(label), _ => work());
        }

        Verbose($"{label}...");
        return work();
    }

    /// <summary>Set once a prompt or spinner has drawn: only then is there a cursor state worth restoring.</summary>
    internal static bool TerminalDirty { get; set; }

    /// <summary>
    /// Shows the cursor and resets styling, in case a prompt or spinner was interrupted mid-way. Never under --json:
    /// stdout must stay one JSON document (nothing prompts or animates there anyway).
    /// </summary>
    public static void RestoreTerminal()
    {
        if (!TerminalDirty) return;
        // Cleared first, whatever the mode: a later in-process run that draws nothing has nothing to undo.
        TerminalDirty = false;
        if (Mode.Json) return;
        if (Console.Profile.Capabilities.Ansi) Console.Profile.Out.Writer.Write("\e[?25h\e[0m");
        if (Error.Profile.Capabilities.Ansi) Error.Profile.Out.Writer.Write("\e[?25h\e[0m");
    }

    public static void Plan(IReadOnlyList<ActionReport> plan)
    {
        Rule("plan");
        Info($"[bold]{plan.Count}[/] action(s): {Markup.Escape(PlanSummary(plan))}");
        foreach (var action in plan)
            Info($"  [grey]-[/] {Markup.Escape(action.Description)}");
        Blank();
    }

    /// <summary>"9 files, 1 patch, 1 solution, 1 restore" - the plan by kind, in a fixed order.</summary>
    internal static string PlanSummary(IReadOnlyList<ActionReport> plan)
    {
        var parts = new List<string>();
        void Count(string singular, string plural, params ActionKind[] kinds)
        {
            var n = plan.Count(a => kinds.Contains(a.Kind));
            if (n > 0) parts.Add($"{n} {(n == 1 ? singular : plural)}");
        }

        Count("file", "files", ActionKind.CreateDir, ActionKind.CreateFile);
        Count("patch", "patches", ActionKind.Patch);
        Count("project.godot edit", "project.godot edits", ActionKind.GodotConfig);
        Count("rename", "renames", ActionKind.Rename);
        Count("solution step", "solution steps", ActionKind.Solution);
        Count("restore", "restores", ActionKind.Restore);
        return string.Join(", ", parts);
    }

    /// <summary>The "Done" block: an optional cd line, then command/comment rows.</summary>
    public static void NextSteps(string? cdLine, IReadOnlyList<(string Command, string Comment)> rows)
    {
        if (!Narrating) return;
        Blank();
        Rule("done");
        Info("[green]Done.[/] Next steps:");

        // The cd line stays outside the grid: a long path would stretch the
        // command column and push every comment to the far right.
        if (cdLine != null) Info($"  [bold]{Markup.Escape(cdLine)}[/]");

        var grid = new Grid().AddColumn(new GridColumn().PadLeft(2)).AddColumn(new GridColumn().PadLeft(2));
        foreach (var (command, comment) in rows)
            grid.AddRow($"[bold]{Markup.Escape(command)}[/]", $"[grey]# {Markup.Escape(comment)}[/]");
        Console.Write(grid);

        Blank();
        Info("[grey]Docs:[/] https://2dog.dev");
    }

    public static void VersionTable(IReadOnlyList<(string Label, string Version, VersionMark? Mark, string Packages)> rows)
    {
        if (Mode.Json) return;
        var grid = new Grid().AddColumn().AddColumn(new GridColumn().PadLeft(2))
            .AddColumn(new GridColumn().PadLeft(1)).AddColumn(new GridColumn().PadLeft(2));
        foreach (var (label, version, mark, packages) in rows)
        {
            var glyph = mark switch
            {
                VersionMark.UpToDate => Glyph("✅", "[green]ok[/]"),
                VersionMark.Outdated => Glyph("🔄", "[yellow]new[/]"),
                _ => "",
            };
            grid.AddRow($"[grey]{Markup.Escape(label)}[/]", $"[bold]{Markup.Escape(version)}[/]",
                glyph, $"[grey]{Markup.Escape(packages)}[/]");
        }

        Console.Write(grid);
    }
}

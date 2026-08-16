using Spectre.Console;

namespace twodog.cli;

/// <summary>
/// The one Spectre gateway for non-interactive output (prompts live in Tui).
/// Spectre's static AnsiConsole binds to the writer it sees first, but the
/// tests swap Console.Out/Error for StringWriters per test - so these
/// consoles write through a forwarder that resolves the *current*
/// Console.Out/Error at every write, exactly like Console.WriteLine does.
/// Under a test host stdio is redirected, so markup degrades to plain text
/// there, keeping the asserted `error:`/`warning:` prefixes byte-identical.
/// </summary>
internal static class Out
{
    private static readonly Lazy<IAnsiConsole> Stdout = new(() => Create(stdErr: false));
    private static readonly Lazy<IAnsiConsole> Stderr = new(() => Create(stdErr: true));

    /// <summary>
    /// Renders everything as unwrapped plain text. The test assembly sets
    /// this in a module initializer: a test host can have an attached
    /// terminal (whose ANSI support and narrow width would leak into the
    /// captured strings) while Console.Out points at a StringWriter, and
    /// that swap is undetectable from here.
    /// </summary>
    internal static bool ForcePlain;

    public static IAnsiConsole Console => Stdout.Value;
    public static IAnsiConsole Error => Stderr.Value;

    private static IAnsiConsole Create(bool stdErr) =>
        AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = ForcePlain ? AnsiSupport.No : AnsiSupport.Detect,
            ColorSystem = ForcePlain ? ColorSystemSupport.NoColors : ColorSystemSupport.Detect,
            Out = new ForwardingOutput(stdErr),
        });

    /// <summary>
    /// Spectre output over the process's current Console.Out/Error. Terminal
    /// facts come from the real console, not from whichever writer happens to
    /// be installed.
    /// </summary>
    private sealed class ForwardingOutput(bool stdErr) : IAnsiConsoleOutput
    {
        public TextWriter Writer { get; } = new ForwardingWriter(stdErr);

        public bool IsTerminal =>
            !ForcePlain && !(stdErr ? System.Console.IsErrorRedirected : System.Console.IsOutputRedirected);

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

    public static void Line(string markup) => Console.MarkupLine(markup);

    /// <summary>Text printed verbatim - never parsed as markup.</summary>
    public static void Plain(string text) => Console.WriteLine(text);

    public static void Blank() => Console.WriteLine();

    public static void Header()
    {
        Line($"[bold]2dog[/] [grey]{ToolVersions.TwoDogVersion}[/]  [grey]https://2dog.dev[/]");
        Blank();
    }

    /// <summary>A left-titled grey divider between output sections. On a
    /// terminal it spans the window; redirected it stays a short fixed line
    /// (the redirected width is huge so that lines never wrap).</summary>
    public static void Rule(string title)
    {
        if (Console.Profile.Out.IsTerminal)
            Console.Write(new Rule($"[grey]{Markup.Escape(title)}[/]") { Style = "grey", Justification = Justify.Left });
        else
            Line($"[grey]── {Markup.Escape(title)} ──[/]");
    }

    public static void Note(string text) => Line($"[grey]note:[/] {Markup.Escape(text)}");
    public static void Warning(string text) => Line($"[yellow]warning:[/] {Markup.Escape(text)}");
    public static void Skip(string text) => Line($"[grey]skip:[/] {Markup.Escape(text)}");
    public static void Would(string text) => Line($"[grey]would:[/] {Markup.Escape(text)}");
    public static void Action(string description) => Line($"[green]+[/] {Markup.Escape(description)}");

    public static void ErrorLine(string message) =>
        Error.MarkupLine($"[red]error:[/] {Markup.Escape(message)}");

    public static void Plan(IReadOnlyList<string> descriptions)
    {
        Rule("plan");
        foreach (var description in descriptions)
            Line($"  [grey]-[/] {Markup.Escape(description)}");
        Blank();
    }

    /// <summary>The "Done" block: an optional cd line, then command/comment rows.</summary>
    public static void NextSteps(string? cdLine, IReadOnlyList<(string Command, string Comment)> rows)
    {
        Blank();
        Rule("done");
        Line("[green]Done.[/] Next steps:");

        // The cd line stays outside the grid: a long path would stretch the
        // command column and push every comment to the far right.
        if (cdLine != null) Line($"  [bold]{Markup.Escape(cdLine)}[/]");

        var grid = new Grid().AddColumn(new GridColumn().PadLeft(2)).AddColumn(new GridColumn().PadLeft(2));
        foreach (var (command, comment) in rows)
            grid.AddRow($"[bold]{Markup.Escape(command)}[/]", $"[grey]# {Markup.Escape(comment)}[/]");
        Console.Write(grid);

        Blank();
        Line("[grey]Docs:[/] https://2dog.dev");
    }

    public static void VersionTable(IReadOnlyList<(string Label, string Version, string? Mark, string Packages)> rows)
    {
        var grid = new Grid().AddColumn().AddColumn(new GridColumn().PadLeft(2))
            .AddColumn(new GridColumn().PadLeft(1)).AddColumn(new GridColumn().PadLeft(2));
        foreach (var (label, version, mark, packages) in rows)
            // The mark and its blank stand-in are both two terminal cells wide, so columns never move.
            grid.AddRow($"[grey]{Markup.Escape(label)}[/]", $"[bold]{Markup.Escape(version)}[/]",
                mark ?? "  ", $"[grey]{Markup.Escape(packages)}[/]");
        Console.Write(grid);
    }
}

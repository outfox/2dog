using System.Reflection;
using PrettyPrompt;
using PrettyPrompt.Highlighting;
using twodog.Repl;
using twodog.tests.EngineTests;

namespace twodog.tests.ReplTests;

[Collection(nameof(EngineRestartCollection))]
public class HostLifecycleTests
{
    private static readonly ConsoleKeyInfo RunKey = new('\r', ConsoleKey.Enter, false, false, true);
    private static IEnumerable<ConsoleKeyInfo> Type(string text) => text.Select(c => new ConsoleKeyInfo(c,
        char.IsLetterOrDigit(c) ? (ConsoleKey)char.ToUpperInvariant(c) : ConsoleKey.NoName, false, false, false));
    private static IEnumerable<ConsoleKeyInfo> Command(string text) => Type(text).Append(new('\r', ConsoleKey.Enter, false, false, false));
    private static IEnumerable<ConsoleKeyInfo> Code(string text) => Type(text).Append(RunKey);

    private static (int Exit, Terminal Terminal) Run(string? input, IEnumerable<ConsoleKeyInfo>? keys = null, bool wait = false)
    {
        var game = typeof(showcase.CSharpTicker).Assembly;
        using var engine = new Engine("repl-host-lifetime", Engine.ResolveProjectDir(), "--headless") { CaptureErrors = true };
        Terminal? terminal = null;
        var exit = ReplHost.Run(engine, token => terminal = new Terminal(input, keys ?? [], wait ? token : null), game);
        Assert.True(terminal!.Restored);
        Assert.Empty(engine.Errors.Drain());
        return (exit, terminal);
    }

    [Fact]
    public void InteractiveHostRunsCommandsChangesScopeResetsCSharpAndRestoresTerminal()
    {
        var keys = Command(":help").Concat(Command(":multiline")).Concat(Command(":help"))
            .Concat(Command(":multiline")).Concat(Command(":clear"))
            .Concat(Code("")).Append(new('\x03', ConsoleKey.C, false, false, true))
            .Concat(Code("int retained = 42;")).Concat(Code("retained"))
            .Concat(Code("cd $Control")).Concat(Command("pwd")).Concat(Command("ls"))
            .Concat(Code("throw new OperationCanceledException();"))
            .Concat(Command(":reset")).Concat(Code("retained")).Concat(Command(":quit"));
        var (exit, terminal) = Run(null, keys);
        Assert.Equal(0, exit);
        Assert.Equal(1, terminal.Prompt.Clears);
        Assert.Contains("C# REPL", terminal.Stdout.ToString());
        Assert.Contains("42", terminal.Stdout.ToString());
        Assert.Contains("/root/Control", terminal.Stdout.ToString());
        Assert.Contains("CenterContainer", terminal.Stdout.ToString());
        Assert.Contains("Submission cancelled.", terminal.Stdout.ToString());
        Assert.Contains("C# session reset", terminal.Stdout.ToString());
        Assert.Contains("CS0103", terminal.Stderr.ToString());
        Assert.False(File.Exists(terminal.HistoryFile) && new FileInfo(terminal.HistoryFile).Length == 0);
        terminal.Dispose();
    }

    [Theory]
    [InlineData(":help\n:clear\n:multiline\n:reset\n40+2\n:exit\n", 0)]
    [InlineData("\n40+2\nls\nexit\n", 0)]
    [InlineData("var broken = ;\n40+2\n", 1)]
    [InlineData("if (true) {", 1)]
    [InlineData("", 0)]
    [InlineData("throw new OperationCanceledException();\n", 0)]
    public void RedirectedHostHandlesEofMultilineCommandsAndExitStatus(string input, int expected)
    {
        var (exit, terminal) = Run(input);
        using (terminal)
        {
            Assert.Equal(expected, exit);
            Assert.Equal(0, terminal.Prompt.Clears);
            Assert.DoesNotContain("Preparing C# completion...", terminal.Stdout.ToString());
            if (expected != 0) Assert.NotEmpty(terminal.Stderr.ToString());
            if (input.Contains("40+2")) Assert.Contains("42", terminal.Stdout.ToString());
        }
    }

    [Fact]
    public void EngineQuitCancelsBlockedTerminalInputAndJoinsCleanup()
    {
        var (exit, terminal) = Run(null, Code("tree.Quit();"), wait: true);
        using (terminal) Assert.Equal(0, exit);
    }

    [Fact]
    public void PublicEntryPointUsesTheRedirectedSystemTerminal()
    {
        var original = Console.In;
        try
        {
            Console.SetIn(new StringReader(":quit\n"));
            var game = typeof(showcase.CSharpTicker).Assembly;
            using var engine = new Engine("repl-public-entry", Engine.ResolveProjectDir(), "--headless");
            Assert.Equal(0, ReplHost.Run(engine, game));
        }
        finally { Console.SetIn(original); }
    }

    private sealed class Terminal : IReplTerminal, IDisposable
    {
        public PromptConsole Prompt { get; }
        public PrettyPrompt.Consoles.IConsole Editor => Prompt;
        public bool Interactive { get; }
        public TextReader Input { get; }
        public StringWriter Stdout { get; } = new();
        public StringWriter Stderr { get; } = new();
        public ReplOutput Output { get; }
        public string HistoryFile { get; } = Path.Combine(Path.GetTempPath(), "2dog-repl-" + Guid.NewGuid(), "history");
        public bool Restored { get; private set; }
        public Terminal(string? input, IEnumerable<ConsoleKeyInfo> keys, CancellationToken? token)
        {
            Interactive = input is null;
            Input = new StringReader(input ?? "");
            Prompt = new PromptConsole(keys, waitForInput: token);
            Output = new ReplOutput(Stdout, Stderr, color: false, errorColor: false);
        }
        public string? TakePaste() => null;
        public void Restore() => Restored = true;
        public void Dispose()
        {
            Input.Dispose();
            Stdout.Dispose();
            Stderr.Dispose();
            var directory = Path.GetDirectoryName(HistoryFile)!;
            // The test creates only this unique history file and directory.
            if (File.Exists(HistoryFile)) File.Delete(HistoryFile);
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }
    }
}

public class TerminalOutputTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void OutputKeepsContentAndSeparatesOutputAndErrorColor(bool color, bool errorColor)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var output = new ReplOutput(stdout, stderr, color, errorColor);
        output.Banner("res://main.tscn");
        output.Result("42");
        output.Result("/root/Control", NodeColors.Path("/root/Control"));
        output.Tree("tree\n");
        output.Tree("tree\n", new FormattedString("tree\n"));
        output.InputMode(false);
        output.InputMode(true);
        output.Help(false);
        output.Help(true);
        output.Message("session message");
        output.Error("test error");
        Assert.Contains("Game scene", stdout.ToString());
        Assert.Contains("res://main.tscn", stdout.ToString());
        Assert.Contains("cp $Source $Parent", stdout.ToString());
        Assert.Contains("mv(...)", stdout.ToString());
        Assert.Equal(color, stdout.ToString().Contains('\x1b'));
        Assert.Equal(errorColor, stderr.ToString().Contains('\x1b'));
        Assert.Contains("test error", stderr.ToString());
        Assert.EndsWith(Environment.NewLine + Environment.NewLine, stderr.ToString());
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("40+2", "40+2")]
    [InlineData("if (true) {\n40+2;\n}", "if (true) {\n40+2;\n}")]
    [InlineData("if (true) {", "if (true) {")]
    [InlineData(":help\n42", ":help")]
    public async Task RedirectedSubmissionUsesSyntaxAndPreservesIncompleteEof(string input, string? expected)
    {
        using var reader = new StringReader(input);
        var result = await ReplHost.ReadSubmissionAsync(reader, TestContext.Current.CancellationToken);
        Assert.Equal(expected, result?.TrimEnd().Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task DispatcherPropagatesFailuresAndCallerCancellationWithoutExecutingCancelledActions()
    {
        using var dispatcher = new EngineDispatcher();
        var failure = dispatcher.InvokeAsync<int>(() => throw new InvalidOperationException("owner failure"), TestContext.Current.CancellationToken);
        var cancelled = dispatcher.InvokeAsync<int>(() => throw new OperationCanceledException(), TestContext.Current.CancellationToken);
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        var called = false;
        var skipped = dispatcher.InvokeAsync(() => { called = true; return Task.FromResult(1); }, stop.Token);
        dispatcher.Pump();
        Assert.Equal("owner failure", (await Assert.ThrowsAsync<InvalidOperationException>(() => failure)).Message);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => skipped);
        Assert.False(called);
    }
}

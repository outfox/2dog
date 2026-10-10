using PrettyPrompt;
using PrettyPrompt.Consoles;
using twodog.Repl;

namespace twodog.tests.ReplTests;

public class ReplInputTests
{
    private static readonly ConsoleKeyInfo Enter = new('\r', ConsoleKey.Enter, false, false, false);
    private static readonly ConsoleKeyInfo Escape = new('\x1b', ConsoleKey.Escape, false, false, false);
    private static IEnumerable<ConsoleKeyInfo> Text(string text) => text.Select(c => new ConsoleKeyInfo(c, ConsoleKey.NoName, false, false, false));

    private static async Task<string> Read(IEnumerable<ConsoleKeyInfo> keys, bool windows = true, PromptCallbacks? callbacks = null)
    {
        var console = new PromptConsole(keys, buffered: true, windows);
        await using var prompt = new Prompt(console: console, callbacks: callbacks, configuration: ReplHost.CreatePromptConfiguration());
        return (await prompt.ReadLineAsync()).Text;
    }

    [Theory]
    [InlineData("ls")]
    [InlineData("lss")]
    [InlineData("40+2")]
    [InlineData("world.Open(\"res://tool_test.tscn\")")]
    public async Task InitialBufferedTypingPreservesTextAndSubmit(string text)
        => Assert.Equal(text, await Read(Text(text).Append(Enter)));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EscapeDoesNotDiscardFollowingTyping(bool windows)
        => Assert.Equal("abcdef", await Read(Text("abc").Append(Escape).Concat(Text("def")).Append(Enter), windows));

    [Fact]
    public async Task BufferedBackspaceEditsInsteadOfBeingPasted()
        => Assert.Equal("ls", await Read(Text("lsX").Append(new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false)).Append(Enter)));

    [Fact]
    public async Task BufferedTabCommitsCompletionInsteadOfBeingPasted()
        => Assert.Equal("alpha", await Read(Text("alph").Concat([
            new ConsoleKeyInfo(' ', ConsoleKey.Spacebar, false, false, true),
            new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false), Enter]), callbacks: new CompletionCallbacks()));

    [Fact]
    public async Task QueuedTextRemainsAvailableForTheNextPrompt()
    {
        var console = new PromptConsole(Text("ls").Append(Enter).Concat(Text("40+2")).Append(Enter), buffered: true);
        await using var prompt = new Prompt(console: console, configuration: ReplHost.CreatePromptConfiguration());
        Assert.Equal("ls", (await prompt.ReadLineAsync()).Text);
        Assert.Equal("40+2", (await prompt.ReadLineAsync()).Text);
    }

    [Fact]
    public void SubmissionDoesNotPrefetchKeysBelongingToTheShell()
    {
        var keys = new Queue<ConsoleKeyInfo>(Text("ls").Append(Enter).Concat(Text("shell")));
        var buffer = new ReplInputBuffer(() => keys.Count > 0, () => keys.Dequeue(), windows: true);
        Assert.Equal('l', buffer.ReadKey().KeyChar);
        Assert.True(buffer.KeyAvailable);
        Assert.Equal('s', buffer.ReadKey().KeyChar);
        Assert.False(buffer.KeyAvailable);
        Assert.Equal(Enter, buffer.ReadKey());
        Assert.False(buffer.KeyAvailable);
        Assert.Equal('s', keys.Peek().KeyChar);
    }

    [Fact]
    public async Task BufferedCancellationDoesNotBecomeLiteralText()
    {
        var console = new PromptConsole(Text("abandon").Append(new ConsoleKeyInfo('\x03', ConsoleKey.C, false, false, true))
            .Concat(Text("ls")).Append(Enter), buffered: true);
        await using var prompt = new Prompt(console: console, configuration: ReplHost.CreatePromptConfiguration());
        Assert.False((await prompt.ReadLineAsync()).IsSuccess);
        Assert.Equal("ls", (await prompt.ReadLineAsync()).Text);
    }

    [Fact]
    public async Task LargePastesStayBatchedWhileEnterStillSubmits()
    {
        var text = new string('x', 10_000);
        var callbacks = new CountingCallbacks();
        Assert.Equal(text, await Read(Text(text).Append(Enter), callbacks: callbacks));
        Assert.Equal(1, callbacks.Pastes);
        Assert.Equal(2, callbacks.Keys);
    }

    [Fact]
    public async Task UnixModifiedEnterSequenceDoesNotSwallowTheFollowingSubmit()
        => Assert.Equal(Environment.NewLine, await Read(new[] { Escape }.Concat(Text("[27;2;13~")).Append(Enter), windows: false));

    [Theory]
    [InlineData("[A", false)]
    [InlineData("[B", true)]
    [InlineData("[C", false)]
    [InlineData("[D", true)]
    [InlineData("[H", false)]
    [InlineData("[F", true)]
    [InlineData("[Z", false)]
    [InlineData("[1;5D", true)]
    [InlineData("[27;2;13~", false)]
    public void UnixCsiFinalBytesPreserveFollowingEditingKeyBoundaries(string sequence, bool tab)
    {
        var boundary = tab ? new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false) : Enter;
        var keys = new Queue<ConsoleKeyInfo>(new[] { Escape }.Concat(Text(sequence)).Append(boundary).Concat(Text("shell")));
        var buffer = new ReplInputBuffer(() => keys.Count > 0, () => keys.Dequeue(), windows: false);
        Assert.Equal(Escape, buffer.ReadKey());
        foreach (var character in sequence)
        {
            Assert.True(buffer.KeyAvailable);
            Assert.Equal(character, buffer.ReadKey().KeyChar);
        }
        Assert.False(buffer.KeyAvailable);
        Assert.Equal(boundary, buffer.ReadKey());
        Assert.False(buffer.KeyAvailable);
        Assert.Equal('s', keys.Peek().KeyChar);
    }

    [Theory]
    [InlineData("[200~", "[201~")]
    [InlineData("200~", "201~")]
    public void BracketedPastePreservesLiteralNewlinesAndTabs(string start, string end)
    {
        const string text = "if (true)\r\n{\r\n\t42;\r\n}";
        var keys = new Queue<ConsoleKeyInfo>(new[] { Escape }.Concat(Text(start + text)).Append(Escape).Concat(Text(end)).Append(Enter));
        var buffer = new ReplInputBuffer(() => keys.Count > 0, () => keys.Dequeue(), windows: true);
        var paste = buffer.ReadKey();
        Assert.Equal(ConsoleKey.Insert, paste.Key);
        Assert.Equal(ConsoleModifiers.Shift, paste.Modifiers);
        Assert.Equal(text, buffer.TakePaste());
        Assert.Null(buffer.TakePaste());
        Assert.False(buffer.KeyAvailable);
        Assert.Equal(Enter, buffer.ReadKey());
    }

    [Fact]
    public async Task UnframedMultilinePasteWaitsForExplicitSubmitInMultilineMode()
    {
        const string code = "int pastedAnswer =\r    40 + 2;\rpastedAnswer";
        var keys = code.Select(c => c == '\r' ? Enter : new ConsoleKeyInfo(c, ConsoleKey.NoName, false, false, false))
            .Append(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, true));
        var console = new PromptConsole(keys, buffered: true);
        await using var prompt = new Prompt(console: console, configuration: ReplHost.CreatePromptConfiguration(multiline: true));
        Assert.Equal(code.Replace("\r", Environment.NewLine), (await prompt.ReadLineAsync()).Text);
    }

    private sealed class CountingCallbacks : PromptCallbacks
    {
        public int Keys { get; private set; }
        public int Pastes { get; private set; }
        protected override Task<KeyPress> TransformKeyPressAsync(string text, int caret, KeyPress keyPress, CancellationToken cancellationToken)
        {
            Keys++;
            if (keyPress.PastedText is not null) Pastes++;
            return Task.FromResult(keyPress);
        }
    }

    private sealed class CompletionCallbacks : PromptCallbacks
    {
        protected override Task<IReadOnlyList<PrettyPrompt.Completion.CompletionItem>> GetCompletionItemsAsync(string text, int caret,
            PrettyPrompt.Documents.TextSpan spanToBeReplaced, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<PrettyPrompt.Completion.CompletionItem>>([new("alpha")]);
    }
}

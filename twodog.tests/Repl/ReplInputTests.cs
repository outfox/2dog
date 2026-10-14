using twodog.Repl;

namespace twodog.tests.ReplTests;

public class ReplInputTests
{
    private static IEnumerable<ConsoleKeyInfo> Text(string text) => text.Select(c => new ConsoleKeyInfo(c, ConsoleKey.NoName, false, false, false));

    private static ReplKeyReader Reader(IEnumerable<ConsoleKeyInfo> keys, bool windows, out Queue<ConsoleKeyInfo> queue)
    {
        var pending = queue = new Queue<ConsoleKeyInfo>(keys);
        return new ReplKeyReader(() => pending.Count > 0, pending.Dequeue, windows);
    }

    [Theory]
    [InlineData("ls")]
    [InlineData("lss")]
    [InlineData("40+2")]
    [InlineData("world.Open(\"res://tool_test.tscn\")")]
    public async Task QueuedTypingPreservesTextAndSubmit(string text)
        => Assert.Equal(text, await EditorDriver.ReadQueuedAsync(PlainLanguage.Instance, Text(text).Append(Keys.Enter)));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EscapeDoesNotDiscardFollowingTyping(bool windows)
        => Assert.Equal("abcdef", await EditorDriver.ReadQueuedAsync(PlainLanguage.Instance, Text("abc").Append(Keys.Escape).Concat(Text("def")).Append(Keys.Enter), windows));

    [Fact]
    public async Task QueuedBackspaceEditsInsteadOfBeingPasted()
        => Assert.Equal("ls", await EditorDriver.ReadQueuedAsync(PlainLanguage.Instance, Text("lsX").Append(Keys.Backspace).Append(Keys.Enter)));

    [Fact]
    public async Task QueuedTabCommitsCompletionInsteadOfBeingPasted()
        => Assert.Equal("alpha", await EditorDriver.ReadQueuedAsync(new Words("alpha"), Text("alph").Concat([Keys.ControlSpace, Keys.Tab, Keys.Enter])));

    [Fact]
    public void SubmissionDoesNotPrefetchKeysBelongingToTheShell()
    {
        var reader = Reader(Text("ls").Append(Keys.Enter).Concat(Text("shell")), windows: true, out var queue);
        Assert.Equal('l', Assert.IsType<KeyInput>(reader.Read(idle: false)).Key.KeyChar);
        Assert.Equal('s', Assert.IsType<KeyInput>(reader.Read(idle: false)).Key.KeyChar);
        Assert.Equal(Keys.Enter, Assert.IsType<KeyInput>(reader.Read(idle: false)).Key);
        Assert.Equal("shell", new string(queue.Select(k => k.KeyChar).ToArray()));
    }

    [Fact]
    public void AnIdleBurstIsAPasteWhoseNewlinesAndTabsAreLiteral()
    {
        var reader = Reader(Text("if (x)").Append(Keys.Enter).Append(Keys.Tab).Concat(Text("y();")).Append(Keys.Enter), windows: true, out _);
        Assert.Equal("if (x)\n\ty();\n", Assert.IsType<TextInput>(reader.Read(idle: true)).Text);
        var single = Reader([Keys.Enter], windows: true, out _);
        Assert.Equal(Keys.Enter, Assert.IsType<KeyInput>(single.Read(idle: true)).Key);
        var control = Reader(Text("ab").Append(Keys.ControlC), windows: true, out _);
        Assert.Equal("ab", Assert.IsType<TextInput>(control.Read(idle: true)).Text);
        Assert.Equal(Keys.ControlC, Assert.IsType<KeyInput>(control.Read(idle: false)).Key);
        // A single pasted line with its line break runs, like a command typed into a shell.
        var line = Reader(Text("40+2").Append(Keys.Enter).Append(Keys.Enter), windows: true, out _);
        Assert.Equal("40+2", Assert.IsType<TextInput>(line.Read(idle: true)).Text);
        Assert.Equal(ConsoleKey.Enter, Assert.IsType<KeyInput>(line.Read(idle: false)).Key.Key);
    }

    [Fact]
    public void UnixBurstsStayKeysBecauseTerminalsMarkTheirPastes()
    {
        var reader = Reader(Text("ls").Append(Keys.Enter), windows: false, out _);
        Assert.Equal('l', Assert.IsType<KeyInput>(reader.Read(idle: true)).Key.KeyChar);
        Assert.Equal('s', Assert.IsType<KeyInput>(reader.Read(idle: false)).Key.KeyChar);
        Assert.Equal(Keys.Enter, Assert.IsType<KeyInput>(reader.Read(idle: true)).Key);
    }

    [Fact]
    public async Task QueuedCancellationDoesNotBecomeLiteralText()
    {
        var queue = new Queue<ConsoleKeyInfo>(Text("abandon").Append(Keys.ControlC).Concat(Text("ls")).Append(Keys.Enter));
        var reader = new ReplKeyReader(() => queue.Count > 0, queue.Dequeue, windows: true);
        var editor = new ReplEditor(PlainLanguage.Instance);
        Task<TerminalInput> Read(CancellationToken _) => Task.FromResult(reader.Read(idle: false));
        var cancelled = await editor.ReadLineAsync(Read, CancellationToken.None);
        Assert.Equal((EditorAction.Cancel, "abandon"), (cancelled.Action, cancelled.Text));
        Assert.Equal("ls", (await editor.ReadLineAsync(Read, CancellationToken.None)).Text);
    }

    [Fact]
    public void LongQueuedTextIsInsertedAtOnceWhileShortTypingStaysKeys()
    {
        var text = new string('x', 10_000);
        var reader = Reader(Text(text).Append(Keys.Enter), windows: true, out _);
        Assert.Equal(text, Assert.IsType<TextInput>(reader.Read(idle: false)).Text);
        Assert.Equal(Keys.Enter, Assert.IsType<KeyInput>(reader.Read(idle: false)).Key);
        var shortRun = Reader(Text(new string('y', ReplKeyReader.LongRun - 1)), windows: true, out _);
        for (var i = 0; i < ReplKeyReader.LongRun - 1; i++) Assert.Equal('y', Assert.IsType<KeyInput>(shortRun.Read(idle: false)).Key.KeyChar);
    }

    [Fact]
    public async Task UnixModifiedEnterSequenceDoesNotSwallowTheFollowingSubmit()
        => Assert.Equal(Environment.NewLine, await EditorDriver.ReadQueuedAsync(PlainLanguage.Instance,
            new[] { Keys.Escape }.Concat(Text("[27;2;13~")).Append(Keys.Enter), windows: false));

    [Theory]
    [InlineData("[27;2;13~", ConsoleKey.Enter, ConsoleModifiers.Shift)]
    [InlineData("27;5;13~", ConsoleKey.Enter, ConsoleModifiers.Control)]
    [InlineData("[13;2u", ConsoleKey.Enter, ConsoleModifiers.Shift)]
    [InlineData("[9;2u", ConsoleKey.Tab, ConsoleModifiers.Shift)]
    [InlineData("[27u", ConsoleKey.Escape, (ConsoleModifiers)0)]
    [InlineData("[127;5u", ConsoleKey.Backspace, ConsoleModifiers.Control)]
    [InlineData("[32;5u", ConsoleKey.Spacebar, ConsoleModifiers.Control)]
    [InlineData("[99;5u", ConsoleKey.C, ConsoleModifiers.Control)]
    [InlineData("[97;4u", ConsoleKey.A, ConsoleModifiers.Shift | ConsoleModifiers.Alt)]
    [InlineData("[49;3u", ConsoleKey.D1, ConsoleModifiers.Alt)]
    [InlineData("[46u", ConsoleKey.NoName, (ConsoleModifiers)0)]
    public void UnixModifiedKeysDecodeAndKeepTheFollowingEnter(string sequence, ConsoleKey key, ConsoleModifiers modifiers)
    {
        var reader = Reader(new[] { Keys.Escape }.Concat(Text(sequence)).Append(Keys.Enter), windows: false, out _);
        var decoded = Assert.IsType<KeyInput>(reader.Read(idle: false)).Key;
        Assert.Equal((key, modifiers), (decoded.Key, decoded.Modifiers));
        Assert.Equal(Keys.Enter, Assert.IsType<KeyInput>(reader.Read(idle: false)).Key);
    }

    [Theory]
    [InlineData("[A")]
    [InlineData("[1;5D")]
    [InlineData("OP")]
    [InlineData("[27;2~")]
    [InlineData("[55296u")]
    [InlineData("[57399u")]
    public void UnusedUnixSequencesAreDroppedRatherThanTyped(string sequence)
    {
        var reader = Reader(new[] { Keys.Escape }.Concat(Text(sequence)).Append(Keys.Tab), windows: false, out _);
        Assert.Equal(Keys.Tab, Assert.IsType<KeyInput>(reader.Read(idle: false)).Key);
    }

    [Theory]
    [InlineData("27;a;13~")]
    [InlineData("27;5~")]
    [InlineData("x;1u")]
    [InlineData("99;xu")]
    [InlineData("1;5D")]
    public void MalformedSequenceBodiesDoNotDecode(string body) => Assert.Null(ReplKeyReader.Decode(body));

    [Fact]
    public void UnixEscapeAltAndControlSpaceKeys()
    {
        var escape = Reader([Keys.Escape], windows: false, out _);
        Assert.Equal(ConsoleKey.Escape, Assert.IsType<KeyInput>(escape.Read(idle: false)).Key.Key);
        var doubled = Reader([Keys.Escape, Keys.Escape], windows: false, out _);
        Assert.Equal(ConsoleKey.Escape, Assert.IsType<KeyInput>(doubled.Read(idle: false)).Key.Key);
        Assert.Equal(ConsoleKey.Escape, Assert.IsType<KeyInput>(doubled.Read(idle: false)).Key.Key);
        // Escape followed by typing keeps the typed character; Alt+Enter and Alt+Backspace keep their modifier.
        var typed = Reader(new[] { Keys.Escape }.Concat(Text("b")), windows: false, out _);
        Assert.Equal(ConsoleKey.Escape, Assert.IsType<KeyInput>(typed.Read(idle: false)).Key.Key);
        Assert.Equal('b', Assert.IsType<KeyInput>(typed.Read(idle: false)).Key.KeyChar);
        var altEnter = Reader([Keys.Escape, Keys.Enter, Keys.Escape, Keys.Backspace], windows: false, out _);
        Assert.Equal((ConsoleKey.Enter, ConsoleModifiers.Alt), Assert.IsType<KeyInput>(altEnter.Read(idle: false)).Key is var e ? (e.Key, e.Modifiers) : default);
        Assert.Equal((ConsoleKey.Backspace, ConsoleModifiers.Alt), Assert.IsType<KeyInput>(altEnter.Read(idle: false)).Key is var b ? (b.Key, b.Modifiers) : default);
        // Digits right after Escape that do not form a sequence are typed text.
        var digits = Reader(new[] { Keys.Escape }.Concat(Text("5ab")), windows: false, out _);
        Assert.Equal(ConsoleKey.Escape, Assert.IsType<KeyInput>(digits.Read(idle: false)).Key.Key);
        Assert.Equal('5', Assert.IsType<KeyInput>(digits.Read(idle: false)).Key.KeyChar);
        var partial = Reader(new[] { Keys.Escape }.Concat(Text("[")), windows: false, out var queue);
        queue.Enqueue(Keys.Enter);
        Assert.Equal(Keys.Enter, Assert.IsType<KeyInput>(partial.Read(idle: false)).Key);
        var space = Reader([new ConsoleKeyInfo('\0', 0, false, false, false)], windows: false, out _);
        Assert.Equal((ConsoleKey.Spacebar, ConsoleModifiers.Control), Assert.IsType<KeyInput>(space.Read(idle: false)).Key is var k ? (k.Key, k.Modifiers) : default);
        var windowsEscape = Reader([Keys.Escape], windows: true, out _);
        Assert.Equal(Keys.Escape, Assert.IsType<KeyInput>(windowsEscape.Read(idle: false)).Key);
    }

    [Theory]
    [InlineData("[200~", "[201~")]
    [InlineData("200~", "201~")]
    public void BracketedPastePreservesLiteralNewlinesTabsAndLookalikeMarkers(string start, string end)
    {
        const string text = "if (true)\r\n{\r\n\t42; ~ \x1b[202~\r\n}";
        var reader = Reader(new[] { Keys.Escape }.Concat(Text(start + text)).Append(Keys.Escape).Concat(Text(end)).Append(Keys.Enter), windows: false, out _);
        Assert.Equal(text, Assert.IsType<TextInput>(reader.Read(idle: false)).Text);
        Assert.Equal(Keys.Enter, Assert.IsType<KeyInput>(reader.Read(idle: false)).Key);
    }

    [Fact]
    public async Task PastedMultilineCodeWaitsForAnExplicitSubmit()
    {
        var editor = new ReplEditor(PlainLanguage.Instance);
        Assert.Null(await editor.HandleAsync(new TextInput("int pastedAnswer =\r\n    40 + 2;\rpastedAnswer\n\n"), CancellationToken.None));
        Assert.Equal("int pastedAnswer =\n    40 + 2;\npastedAnswer", editor.Text);
        Assert.Null(await editor.HandleAsync(new TextInput("\x01\n"), CancellationToken.None));
        var submitted = await editor.HandleAsync(new KeyInput(Keys.Enter), CancellationToken.None);
        Assert.Equal("int pastedAnswer =\n    40 + 2;\npastedAnswer".ReplaceLineEndings(), submitted!.Text);
    }

    // A language with fixed words, for editing behavior that needs completion.
    private sealed class Words(params string[] words) : PlainLanguage
    {
        public override Task<IReadOnlyList<ReplCompletionItem>> CompletionsAsync(string text, int caret, TextRange range, CancellationToken token)
            => Task.FromResult<IReadOnlyList<ReplCompletionItem>>(words.Select(w => new ReplCompletionItem(w)).ToArray());
    }
}

using System.Text;
using twodog.Repl;

namespace twodog.tests.ReplTests;

public class EditorTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class FakeLanguage : PlainLanguage
    {
        public List<ReplCompletionItem> Items { get; } = [];
        public IReadOnlyList<SignatureItem> Signatures { get; set; } = [];
        public int Argument { get; set; }
        public Func<string, IReadOnlyList<StyledSpan>> Highlight { get; set; } = _ => [];
        /// <summary>While set, highlighting waits for it: the editor keeps showing the previous result.</summary>
        public TaskCompletionSource? HighlightGate { get; set; }
        public TabAction Tab { get; set; } = TabAction.Open;
        public HashSet<string> Commands { get; } = [];
        public bool Accepts { get; set; } = true;
        public override Task<IReadOnlyList<ReplCompletionItem>> CompletionsAsync(string text, int caret, TextRange range, CancellationToken token)
            => Task.FromResult<IReadOnlyList<ReplCompletionItem>>(Items);
        public override bool AcceptsCommit(ReplCompletionItem item, string text, int caret, char key) => Accepts;
        public override Task<TabAction> TabAsync(string text, int caret, CancellationToken token) => Task.FromResult(Tab);
        public override async Task<IReadOnlyList<StyledSpan>> HighlightAsync(string text, CancellationToken token)
        {
            if (HighlightGate is { } gate) await gate.Task;
            return Highlight(text);
        }
        public override Task<(IReadOnlyList<SignatureItem> Items, int Argument)> SignaturesAsync(string text, int caret, CancellationToken token)
            => Task.FromResult((Signatures, Argument));
        public override bool IsBareCommand(string text) => Commands.Contains(text);
    }

    private static ConsoleKeyInfo Key(ConsoleKey key, bool shift = false, bool alt = false, bool control = false, char character = '\0')
        => new(character, key, shift, alt, control);

    private static async Task Press(ReplEditor editor, params ConsoleKeyInfo[] keys)
    {
        foreach (var key in keys) Assert.Null(await editor.HandleAsync(new KeyInput(key), Token));
    }

    private static Task Type(ReplEditor editor, string text) => Press(editor, [.. Keys.Type(text)]);

    [Fact]
    public async Task ClipboardUndoRedoAndKillKeysEditTheDraft()
    {
        var editor = new ReplEditor(PlainLanguage.Instance);
        var copied = new List<string>();
        editor.Copied += copied.Add;
        await Type(editor, "alpha beta");
        await Press(editor, Key(ConsoleKey.LeftArrow, shift: true, control: true), Keys.Control(ConsoleKey.C));
        Assert.Equal(["beta"], copied);
        Assert.Equal(new TextRange(6, 4), editor.View.Selection);
        await Press(editor, Keys.Control(ConsoleKey.X));
        Assert.Equal("alpha ", editor.Text);
        await Press(editor, Keys.Control(ConsoleKey.V), Keys.Control(ConsoleKey.V));
        Assert.Equal("alpha betabeta", editor.Text);
        await Press(editor, Keys.Control(ConsoleKey.Z), Keys.Control(ConsoleKey.Z));
        Assert.Equal("alpha ", editor.Text);
        await Press(editor, Keys.Control(ConsoleKey.Y));
        Assert.Equal("alpha beta", editor.Text);
        await Press(editor, Keys.Control(ConsoleKey.Z, shift: true));
        Assert.Equal("alpha betabeta", editor.Text);
        await Press(editor, Keys.Control(ConsoleKey.Z), Keys.Control(ConsoleKey.Z), Keys.Control(ConsoleKey.Z));
        Assert.Equal("alpha beta", editor.Text);
        // Consecutive typing is one undo step.
        await Press(editor, Keys.Control(ConsoleKey.Z));
        Assert.Equal("", editor.Text);
        await Press(editor, Keys.Control(ConsoleKey.Z), Keys.Control(ConsoleKey.Y));
        Assert.Equal("alpha beta", editor.Text);
        await Press(editor, Key(ConsoleKey.Home), Key(ConsoleKey.RightArrow), Keys.Control(ConsoleKey.K));
        Assert.Equal("a", editor.Text);
        await Type(editor, "bc");
        await Press(editor, Keys.Left, Keys.Control(ConsoleKey.U));
        Assert.Equal(("c", 0), (editor.Text, editor.Caret));
        await Press(editor, Keys.Control(ConsoleKey.D));
        Assert.Equal("", editor.Text);
        Assert.Equal(EditorAction.Submit, (await editor.HandleAsync(new KeyInput(Keys.ControlD), Token))!.Action);
        Assert.Equal(EditorAction.Clear, (await editor.HandleAsync(new KeyInput(Keys.Control(ConsoleKey.L)), Token))!.Action);
        await Press(editor, Keys.Control(ConsoleKey.A), Keys.Control(ConsoleKey.X), Keys.Control(ConsoleKey.K), Keys.Control(ConsoleKey.X));
        Assert.Equal("", editor.Text);
    }

    [Fact]
    public async Task SelectionWordMotionAndDeletionFollowWordBreaks()
    {
        var editor = new ReplEditor(PlainLanguage.Instance);
        await Type(editor, "world.Scene  next");
        await Press(editor, Key(ConsoleKey.LeftArrow, control: true));
        Assert.Equal(13, editor.Caret);
        await Press(editor, Key(ConsoleKey.LeftArrow, control: true), Key(ConsoleKey.LeftArrow, control: true));
        Assert.Equal(5, editor.Caret);
        await Press(editor, Key(ConsoleKey.RightArrow, control: true), Key(ConsoleKey.RightArrow, control: true));
        Assert.Equal(13, editor.Caret);
        await Press(editor, Key(ConsoleKey.Delete, control: true));
        Assert.Equal("world.Scene  ", editor.Text);
        await Press(editor, Key(ConsoleKey.Backspace, control: true));
        Assert.Equal("world.", editor.Text);
        await Press(editor, Key(ConsoleKey.Backspace, alt: true));
        Assert.Equal("world", editor.Text);
        await Press(editor, Key(ConsoleKey.Home, shift: true));
        Assert.Equal(new TextRange(0, 5), editor.View.Selection);
        await Press(editor, Keys.Right);
        Assert.Equal((5, null), (editor.Caret, editor.View.Selection));
        await Press(editor, Key(ConsoleKey.LeftArrow, shift: true), Key(ConsoleKey.LeftArrow, shift: true), Keys.Left);
        Assert.Equal(3, editor.Caret);
        await Press(editor, Key(ConsoleKey.RightArrow, shift: true), Keys.Backspace);
        Assert.Equal("word", editor.Text);
        await Press(editor, Key(ConsoleKey.Home, shift: true, control: true), Key(ConsoleKey.Delete));
        Assert.Equal("d", editor.Text);
        await Press(editor, Key(ConsoleKey.End, control: true), Key(ConsoleKey.LeftArrow, shift: true));
        await Type(editor, "X");
        Assert.Equal("X", editor.Text);
        await Press(editor, Key(ConsoleKey.Delete), Key(ConsoleKey.Home), Key(ConsoleKey.Backspace), Key(ConsoleKey.Delete, control: true));
        Assert.Equal("", editor.Text);
        await editor.HandleAsync(new TextInput("a🦊b"), Token);
        await Press(editor, Keys.Left, Keys.Left);
        Assert.Equal(1, editor.Caret);
        await Press(editor, Keys.Right);
        Assert.Equal(3, editor.Caret);
        await Press(editor, Keys.Backspace);
        Assert.Equal("ab", editor.Text);
        // AltGr arrives as Control+Alt and still types; Control or Alt alone do not.
        await Press(editor, Key(ConsoleKey.Q, control: true, alt: true, character: '@'), Key(ConsoleKey.Q, alt: true, character: 'q'), Key(ConsoleKey.F5));
        Assert.Equal("a@b", editor.Text);
    }

    [Fact]
    public async Task MultilineDraftsMoveBetweenLinesAndHistoryKeepsTheUnsentDraft()
    {
        var path = Path.Combine(Path.GetTempPath(), "2dog-history-" + Guid.NewGuid() + ".txt");
        try
        {
            var history = new ReplHistory(path);
            foreach (var entry in new[] { "first", "find me", "second\nline", "find me", "  " }) history.Add(entry);
            Assert.Equal(4, history.Count);
            var editor = new ReplEditor(PlainLanguage.Instance, new ReplHistory(path));
            await Press(editor, Keys.Down);
            Assert.Equal("", editor.Text);
            await Press(editor, Keys.Up);
            Assert.Equal("find me", editor.Text);
            await Press(editor, Keys.Up);
            Assert.Equal("second\nline", editor.Text);
            // Up within a multiline entry moves between its lines before browsing further.
            await Press(editor, Keys.Up);
            Assert.Equal(("second\nline", 4), (editor.Text, editor.Caret));
            await Press(editor, Keys.Down, Key(ConsoleKey.UpArrow, shift: true));
            Assert.Equal(new TextRange(4, 7), editor.View.Selection);
            await Press(editor, Key(ConsoleKey.DownArrow, shift: true), Key(ConsoleKey.DownArrow, shift: true), Key(ConsoleKey.UpArrow, shift: true), Key(ConsoleKey.UpArrow, shift: true));
            await Press(editor, Key(ConsoleKey.Escape), Keys.Control(ConsoleKey.A), Keys.Backspace);
            await Type(editor, "fi");
            await Press(editor, Keys.Up);
            Assert.Equal("find me", editor.Text);
            await Press(editor, Keys.Up);
            Assert.Equal("first", editor.Text);
            await Press(editor, Keys.Up);
            Assert.Equal("first", editor.Text);
            await Press(editor, Keys.Down);
            Assert.Equal("find me", editor.Text);
            await Press(editor, Keys.Down);
            Assert.Equal("fi", editor.Text);
            var submitted = await editor.HandleAsync(new KeyInput(Keys.Enter), Token);
            Assert.Equal("fi", submitted!.Text);
            Assert.Equal(["first", "find me", "second\nline", "find me", "fi"],
                File.ReadAllLines(path).Select(line => Encoding.UTF8.GetString(Convert.FromBase64String(line))));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void HistoryKeepsTheNewestEntriesSkipsDamagedLinesAndSurvivesWriteFailures()
    {
        var path = Path.Combine(Path.GetTempPath(), "2dog-history-" + Guid.NewGuid() + ".txt");
        try
        {
            File.WriteAllLines(path, Enumerable.Range(0, ReplHistory.Capacity + 5).Select(i => Convert.ToBase64String(Encoding.UTF8.GetBytes("entry " + i))).Append("not base64!"));
            var history = new ReplHistory(path);
            Assert.Equal(ReplHistory.Capacity - 1, history.Count);
            Assert.Equal((history.Count - 1, "entry 504"), history.Find(history.Count, -1, "", "")!.Value);
            for (var i = 0; i < 3; i++) history.Add("more " + i);
            Assert.Equal(ReplHistory.Capacity, history.Count);
        }
        finally { File.Delete(path); }
        // A directory where the file belongs makes saving fail; the entry stays in memory.
        var directory = Directory.CreateTempSubdirectory("2dog-history-");
        try
        {
            var blocked = new ReplHistory(directory.FullName);
            blocked.Add("kept");
            Assert.Equal(1, blocked.Count);
            Assert.Equal(0, new ReplHistory(null).Count);
        }
        finally { directory.Delete(); }
    }

    [Fact]
    public async Task MenuScrollsDescribesItemsAndClosesWhenTheCaretLeavesTheWord()
    {
        var language = new FakeLanguage();
        language.Items.AddRange(Enumerable.Range(0, 12).Select(i => new ReplCompletionItem("item" + i.ToString("00"),
            description: i == 1 ? _ => throw new InvalidOperationException("no docs") : _ => Task.FromResult<StyledText>("doc " + i))));
        var editor = new ReplEditor(language);
        var changed = 0;
        editor.Changed += () => Interlocked.Increment(ref changed);
        await Press(editor, Keys.ControlSpace);
        Assert.Equal((0, 0, 12), (editor.View.Menu!.Selected, editor.View.Menu.First, editor.View.Menu.Total));
        await WaitFor(() => editor.View.Menu!.Description.Text == "doc 0");
        await Press(editor, Keys.Up, Keys.Down);
        Assert.Equal(1, editor.View.Menu!.Selected);
        for (var i = 0; i < 10; i++) await Press(editor, Keys.Down);
        Assert.Equal((11, 4), (editor.View.Menu!.First + editor.View.Menu.Selected!.Value, editor.View.Menu.First));
        for (var i = 0; i < 10; i++) await Press(editor, Keys.Up);
        Assert.Equal((1, 0), (editor.View.Menu!.First, editor.View.Menu.Selected));
        Assert.True(changed > 0);
        await Type(editor, "item0");
        Assert.Equal(10, editor.View.Menu!.Total);
        // With no match nothing is drawn and arrows keep their usual meaning; Backspace brings the matches back.
        await Type(editor, "x");
        Assert.Null(editor.View.Menu);
        await Press(editor, Keys.Down, Keys.Backspace, Keys.Left);
        Assert.NotNull(editor.View.Menu);
        await Press(editor, Key(ConsoleKey.Home));
        Assert.Null(editor.View.Menu);
        await Press(editor, Keys.ControlSpace, Key(ConsoleKey.LeftArrow, control: true));
        Assert.Null(editor.View.Menu);
        // Tab with nothing selected closes the menu; Enter then submits the typed text.
        await Press(editor, Key(ConsoleKey.End));
        await Type(editor, "zz");
        await Press(editor, Keys.ControlSpace);
        Assert.Null(editor.View.Menu);
        await Press(editor, Keys.Tab);
        Assert.Null(editor.View.Menu);
        await Press(editor, Keys.ControlSpace);
        Assert.Equal("item0zz", (await editor.HandleAsync(new KeyInput(Keys.Enter), Token))!.Text);
        language.Items.Clear();
        await Press(editor, Keys.ControlSpace);
        Assert.Null(editor.View.Menu);
    }

    [Fact]
    public async Task CommitCharactersSignaturesAndBackgroundHighlighting()
    {
        var language = new FakeLanguage
        {
            Highlight = text => text.Contains("boom") ? throw new InvalidOperationException("highlighter") : [new StyledSpan(0, Math.Min(3, text.Length), TermColor.Cyan)],
        };
        language.Items.Add(new ReplCompletionItem("Call", commitCharacters: "!"));
        var editor = new ReplEditor(language);
        await Type(editor, "Ca");
        await WaitFor(() => editor.View.Highlights.Count == 1);
        // A commit character accepts the selection and is still typed; a rejected one is typed only.
        await Type(editor, "!");
        Assert.Equal("Call!", editor.Text);
        await Press(editor, Keys.Control(ConsoleKey.A), Keys.Backspace);
        language.Accepts = false;
        await Type(editor, "Ca.");
        Assert.Equal("Ca.", editor.Text);
        // While highlighting catches up, only spans within the unchanged prefix remain.
        await WaitFor(() => editor.View.Highlights.Count == 1 && editor.View.Highlights[0].Length == 3);
        language.HighlightGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Press(editor, Keys.Left, Keys.Backspace);
        Assert.Empty(editor.View.Highlights);
        language.HighlightGate.SetResult();
        language.HighlightGate = null;
        await WaitFor(() => editor.View.Highlights.Count == 1);
        await Type(editor, "boom");
        await Press(editor, Keys.Control(ConsoleKey.A), Keys.Backspace);

        language.Signatures =
        [
            new SignatureItem("void Call()", "Calls.", []),
            new SignatureItem(new StyledText("void Call(int first, List<int> second)", TermColor.Yellow), "Calls with values.",
                [new SignatureParameter("first", "The first."), new SignatureParameter("second", "")]),
        ];
        await Type(editor, "Call(");
        var signature = editor.View.Signature!;
        Assert.StartsWith("void Call(int first, List<int> second)  (+1 overload)\nfirst: The first.", signature.Text, StringComparison.Ordinal);
        Assert.Contains(signature.Spans, span => span.Style.Underline && signature.Text.Substring(span.Start, span.Length) == "int first");
        language.Argument = 1;
        await Type(editor, "1,");
        Assert.EndsWith("\nCalls with values.", editor.View.Signature!.Text, StringComparison.Ordinal);
        Assert.Contains(editor.View.Signature!.Spans, span => span.Style.Underline && editor.View.Signature.Text.Substring(span.Start, span.Length) == "List<int> second");
        language.Argument = 5;
        await Press(editor, Key(ConsoleKey.Spacebar, shift: true, control: true));
        Assert.DoesNotContain(editor.View.Signature!.Spans, span => span.Style.Underline);
        language.Signatures = [new SignatureItem("Indexer", "", []), new SignatureItem("Other", "", []), new SignatureItem("Third", "", [])];
        await Press(editor, Keys.Left);
        Assert.Equal("Indexer  (+2 overloads)", editor.View.Signature!.Text);
        await Press(editor, Keys.Escape);
        Assert.Null(editor.View.Signature);
        language.Signatures = [];
        await Press(editor, Key(ConsoleKey.Spacebar, shift: true, control: true));
        Assert.Null(editor.View.Signature);
    }

    [Fact]
    public async Task TabContinuesCyclesAndRefinesWithLanguageEdits()
    {
        var language = new FakeLanguage { Tab = new TabAction(new CompletionEdit(new TextRange(0, 0), "."), false) };
        language.Items.AddRange([new ReplCompletionItem("alpha"), new ReplCompletionItem("alphabet")]);
        var editor = new ReplEditor(language);
        // Shift+Tab without a menu or cycle, and Tab with a selection, do nothing.
        await Press(editor, Keys.ShiftTab);
        await Type(editor, "x");
        await Press(editor, Key(ConsoleKey.LeftArrow, shift: true), Keys.Tab, Keys.Escape, Key(ConsoleKey.End));
        Assert.Equal("x", editor.Text);
        await Press(editor, Keys.Tab);
        Assert.Equal(".x", editor.Text);
        // Like typing '.', a continuation can open suggestions for what follows.
        Assert.NotNull(editor.View.Menu);
        language.Tab = TabAction.Ignore;
        await Press(editor, Keys.Escape, Keys.Tab);
        Assert.Equal(".x", editor.Text);
        await Press(editor, Keys.Control(ConsoleKey.A), Keys.Backspace);
        await Type(editor, "al");
        await Press(editor, Keys.Tab);
        Assert.Equal("alpha", editor.Text);
        // Refining with letters that match no item keeps the cycle and the text.
        await Type(editor, "q");
        await Press(editor, Keys.Tab);
        Assert.Equal("alphaq", editor.Text);
        await Press(editor, Keys.Backspace);
        await Press(editor, Keys.ShiftTab);
        Assert.Null(editor.View.Menu);
    }

    [Fact]
    public async Task EdgeCasesOfCopyingPastingSignaturesAndClearing()
    {
        var language = new FakeLanguage();
        var editor = new ReplEditor(language);
        // Copying without a clipboard listener, word motion from punctuation, and literal pasted tabs.
        await Type(editor, "a.b");
        await Press(editor, Keys.Control(ConsoleKey.A), Keys.Control(ConsoleKey.C), Key(ConsoleKey.Home), Keys.Right, Key(ConsoleKey.RightArrow, control: true));
        Assert.Equal(2, editor.Caret);
        await editor.HandleAsync(new TextInput("\tx"), Token);
        Assert.Equal("a.\txb", editor.Text);
        Assert.Empty(new EditorResult(EditorAction.Clear).Highlights);
        // Unclosed and nested argument lists.
        language.Signatures = [new SignatureItem("Call(Dictionary<string, int[]> map, (int, int) pair", "", [new("map", ""), new("pair", "")])];
        language.Argument = 1;
        await Press(editor, Key(ConsoleKey.Spacebar, shift: true, control: true));
        var signature = editor.View.Signature!;
        Assert.Contains(signature.Spans, span => span.Style.Underline && signature.Text.Substring(span.Start, span.Length) == "(int, int) pair");
        language.Argument = 3;
        await Press(editor, Key(ConsoleKey.Spacebar, shift: true, control: true));
        Assert.DoesNotContain(editor.View.Signature!.Spans, span => span.Style.Underline);
        language.Signatures = [new SignatureItem("Call( , x)", "", [new("first", ""), new("second", "")])];
        language.Argument = 0;
        await Press(editor, Key(ConsoleKey.Spacebar, shift: true, control: true));
        Assert.DoesNotContain(editor.View.Signature!.Spans, span => span.Style.Underline);
        // Ctrl+L without a handler only redraws; the next submission still arrives.
        var keys = new Queue<TerminalInput>([new KeyInput(Keys.Control(ConsoleKey.L)), new KeyInput(Keys.ControlEnter)]);
        Assert.Equal(EditorAction.Submit, (await editor.ReadLineAsync(_ => Task.FromResult(keys.Dequeue()), Token)).Action);
    }

    [Fact]
    public async Task BareCommandsSubmitAndCancelReturnsTheDraft()
    {
        var language = new FakeLanguage();
        language.Commands.Add("ls");
        language.Items.Add(new ReplCompletionItem("lsx"));
        var editor = new ReplEditor(language);
        await Type(editor, "ls");
        Assert.NotNull(editor.View.Menu);
        Assert.Equal("ls", (await editor.HandleAsync(new KeyInput(Keys.Enter), Token))!.Text);
        await Type(editor, "draft");
        var cancelled = (await editor.HandleAsync(new KeyInput(Keys.ControlC), Token))!;
        Assert.Equal((EditorAction.Cancel, "draft"), (cancelled.Action, cancelled.Text));
        Assert.Empty(cancelled.Highlights);
        Assert.Equal("", editor.Text);
        Assert.Null(await editor.HandleAsync(new TextInput("\x01\x02"), Token));
        var cleared = 0;
        var keys = new Queue<TerminalInput>([new KeyInput(Keys.Control(ConsoleKey.L)), new KeyInput(Keys.ControlEnter)]);
        var result = await editor.ReadLineAsync(_ => Task.FromResult(keys.Dequeue()), Token, () => cleared++);
        Assert.Equal((1, EditorAction.Submit), (cleared, result.Action));
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Condition not met.");
            await Task.Delay(5, Token);
        }
    }
}

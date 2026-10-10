using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using Godot;
using Microsoft.CodeAnalysis.Text;
using PrettyPrompt;
using PrettyPrompt.Completion;
using PrettyPrompt.Consoles;
using PrettyPrompt.Highlighting;
using twodog.Repl;
using twodog.tests.EngineTests;

namespace twodog.tests.ReplTests;

public class NodeMetadataContractTests
{
    public class @event : Node;
    private class HiddenNode : Node;
    public class GenericNode<T> : Node;

    [Fact]
    public void GeneratedGetNodeTypesUseVisibleNonGenericNamesAndEscapeKeywords()
    {
        Assert.EndsWith(".@event", NodePathInput.ReferenceableType(typeof(@event)));
        Assert.Equal("global::Godot.Node", NodePathInput.ReferenceableType(typeof(HiddenNode)));
        Assert.Equal("global::Godot.Node", NodePathInput.ReferenceableType(typeof(GenericNode<int>)));
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("ReplTransient" + Guid.NewGuid()), AssemblyBuilderAccess.Run);
        var contract = assembly.DefineDynamicModule("runtime").DefineType("RuntimeNode", TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract).CreateType()!;
        Assert.Equal("global::Godot.Node", NodePathInput.ReferenceableType(contract));
        Assert.Equal("global::System.Object", NodePathInput.ReferenceableType(typeof(GenericNode<>).GetGenericArguments()[0]));
    }

    [Theory]
    [InlineData("$[]", false)]
    [InlineData("$[42]", false)]
    [InlineData("$[ \"Name\" ]", true)]
    [InlineData("$[\"Name\"", false)]
    [InlineData("$[\"Name", false)]
    public void IncompleteQuotedAliasesStayEditableWithoutIndexErrors(string text, bool complete)
    {
        var input = new NodePathInput(text, new Dictionary<string, NodePathTarget>());
        Assert.Equal(complete, Assert.Single(input.References).Complete);
        Assert.Null(input.At(0));
        Assert.Null(input.CompletionAt(0));
        Assert.Null(NodePathInput.CompletionAt("ordinary", 3));
        Assert.All(Enumerable.Range(0, text.Length + 1), position => Assert.InRange(input.ToGenerated(position), 0, input.Code.Length));
    }

    [Fact]
    public void GeneratedDiagnosticsMapInteriorPositionsAndBothEndsToTheAlias()
    {
        const string text = "$Control.Size";
        var input = new NodePathInput(text, new Dictionary<string, NodePathTarget>());
        var end = input.Code.IndexOf(".Size", StringComparison.Ordinal);
        Assert.Equal(0, input.ToOriginal(10));
        Assert.Equal(8, input.ToOriginal(10, end: true));
        Assert.Equal(new TextSpan(0, 8), input.ToOriginal(TextSpan.FromBounds(1, end)));
        Assert.True(input.IsGenerated(new TextSpan(5, 2)));
        Assert.False(input.IsGenerated(new TextSpan(end + 1, 2)));
    }

    [Theory]
    [InlineData("&")]
    [InlineData("|")]
    [InlineData("^")]
    [InlineData("!")]
    [InlineData("<")]
    [InlineData(">")]
    [InlineData("-")]
    [InlineData("*")]
    [InlineData("%")]
    public void CSharpOperatorsCannotBeCapturedAsTransferArguments(string operation)
        => Assert.False(NavigationCommand.TryParse("mv " + operation + " value", out _));

    [Theory]
    [InlineData("cp ?Source root")]
    [InlineData(":mv ?Source root")]
    public void FirstOperandSearchMustBeResolvedBeforeExecution(string text)
    {
        Assert.True(NavigationCommand.TryParse(text, out var command));
        Assert.StartsWith("Press Tab", command.Error);
    }

    [Theory]
    [InlineData(":cd #if DEBUG\nroot\n#endif")]
    [InlineData(":cp root #if DEBUG\nroot\n#endif")]
    public void ScriptDirectivesCannotEscapeCommandOperands(string text)
    {
        Assert.True(NavigationCommand.TryParse(text, out var command));
        Assert.StartsWith("Usage:", command.Error);
    }

    [Fact]
    public void TruncatedPromptAndIncompleteQuotedPathKeepTheirContentAndGreySeparators()
    {
        foreach (var path in new[] { "/root/AVeryLongUnbrokenNodeName", "/root/a/b/c/d/e/f/g", "/root/Short" })
        {
            var prompt = ReplHost.CreatePromptText(path, 60, new Dictionary<string, NodeFamily> { [path] = NodeFamily.Control }, color: true);
            Assert.Contains("godot [", prompt.Text!);
            Assert.EndsWith("]> ", prompt.Text!);
            Assert.All(prompt.FormatSpans.ToArray(), span => Assert.InRange(span.End, 0, prompt.Length));
            Assert.Equal(NodeColors.Color(NodeFamily.Control), prompt.FormatSpans[0].Formatting.Foreground);
            Assert.Empty(ReplHost.CreatePromptText(path, 60, null, color: false).FormatSpans.ToArray());
        }
        foreach (var path in new string?[] { null, "/root", "", "/root/Short" })
            Assert.NotEmpty(ReplHost.CreatePromptText(path, null, null, color: true).FormatSpans.ToArray());
        Assert.NotEmpty(ReplHost.CreatePromptText(null, 160, new Dictionary<string, NodeFamily>(), color: true).FormatSpans.ToArray());
        Assert.NotEmpty(ReplHost.CreatePromptText("/root/Unknown", 160, new Dictionary<string, NodeFamily>(), color: true).FormatSpans.ToArray());
        foreach (var path in new[] { "$[", "$[\"Node", "$[ \"Node\"]", "?Name", "" })
            Assert.Equal(path, NodeColors.Path(path).Text);
        Assert.Equal("", NodeColors.Ansi(default, color: false));
        Assert.Equal("plain\x1b[0m", NodeColors.Ansi(new FormattedString("plain", new FormatSpan(0, 5, new ConsoleFormat())), color: true));
        var cycle = new CompletionCycle("obj.pref suffix", 8, [new CompletionItem("prefix"), new CompletionItem("preferred")], -1);
        var edit = new CompletionEdit(new PrettyPrompt.Documents.TextSpan(4, 4), "prefix");
        Assert.Equal("obj.prefix suffix", cycle.Apply(edit).Text);
        Assert.True(cycle.Matches(cycle.Text, cycle.Caret));
        Assert.False(cycle.Matches(cycle.Text, cycle.Caret - 1));
        Assert.False(cycle.Matches("other", cycle.Caret));
        cycle.Refine("refined", 7);
        Assert.False(cycle.Applied);
    }


    [Theory]
    [InlineData("var n = 1 % ?Timer")]
    [InlineData("var n = () => ?Timer")]
    [InlineData("return ?Timer")]
    [InlineData("new[] { ?Timer }")]
    [InlineData("x[?Timer]")]
    public void SearchDecoratesCSharpExpressionStarts(string source)
        => Assert.Equal("Timer", Assert.Single(NodePathInput.Find(source)).Path);

    [Theory]
    [InlineData("?.Name")]
    [InlineData("?[0]")]
    [InlineData("??other")]
    [InlineData("ls ?Timer : other")]
    public void ConditionalOperatorsAreNotSearchAliases(string source)
        => Assert.Empty(NodePathInput.Find(source));

    [Fact]
    public void APartialAnsiSequenceDoesNotWaitForeverOrConsumeTheNextEnter()
    {
        var keys = new Queue<ConsoleKeyInfo>(new[] {
            new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false),
            new ConsoleKeyInfo('[', ConsoleKey.NoName, false, false, false) });
        var buffer = new ReplInputBuffer(() => keys.Count > 0, () => keys.Dequeue(), windows: false);
        Assert.Equal(ConsoleKey.Escape, buffer.ReadKey().Key);
        Assert.True(buffer.KeyAvailable);
        Assert.Equal('[', buffer.ReadKey().KeyChar);
        Assert.False(buffer.KeyAvailable);
        keys.Enqueue(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        Assert.Equal(ConsoleKey.Enter, buffer.ReadKey().Key);
    }

    [Fact]
    public void UnixModifiedKeyWithoutBracketKeepsItsDigitPrefixAndFollowingEnter()
    {
        const string sequence = "\u001b27;2;13~";
        var keys = new Queue<ConsoleKeyInfo>(sequence.Select(c => new ConsoleKeyInfo(c,
            c == '\x1b' ? ConsoleKey.Escape : ConsoleKey.NoName, false, false, false))
            .Append(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)));
        var buffer = new ReplInputBuffer(() => keys.Count > 0, () => keys.Dequeue(), windows: false);
        Assert.Equal(ConsoleKey.Escape, buffer.ReadKey().Key);
        foreach (var character in sequence[1..])
        {
            Assert.True(buffer.KeyAvailable);
            Assert.Equal(character, buffer.ReadKey().KeyChar);
        }
        Assert.False(buffer.KeyAvailable);
        Assert.Equal(ConsoleKey.Enter, buffer.ReadKey().Key);
    }

    [Fact]
    public void PasteEndingInATildeOrLookalikeMarkerIsPreservedUntilTheExactEndMarker()
    {
        const string content = "~ text \x1b[202~ and more ~";
        var sequence = "\x1b[200~" + content + "\x1b[201~";
        var keys = new Queue<ConsoleKeyInfo>(sequence.Select(c => new ConsoleKeyInfo(c,
            c == '\x1b' ? ConsoleKey.Escape : ConsoleKey.NoName, false, false, false)));
        var buffer = new ReplInputBuffer(() => keys.Count > 0, () => keys.Dequeue(), windows: false);
        Assert.Equal(ConsoleKey.Insert, buffer.ReadKey().Key);
        Assert.Equal(content, buffer.TakePaste());
        Assert.Null(buffer.TakePaste());
    }

    [Theory]
    [InlineData('P')]
    [InlineData('Q')]
    [InlineData('R')]
    [InlineData('S')]
    [InlineData('~')]
    public void UnixFunctionSequencesStopBeforeTheFollowingEnter(char terminator)
    {
        var keys = new Queue<ConsoleKeyInfo>(new[] {
            new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false),
            new ConsoleKeyInfo('[', ConsoleKey.NoName, false, false, false),
            new ConsoleKeyInfo(terminator, ConsoleKey.NoName, false, false, false),
            new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false) });
        var buffer = new ReplInputBuffer(() => keys.Count > 0, () => keys.Dequeue(), windows: false);
        Assert.Equal(ConsoleKey.Escape, buffer.ReadKey().Key);
        Assert.True(buffer.KeyAvailable);
        Assert.Equal('[', buffer.ReadKey().KeyChar);
        Assert.True(buffer.KeyAvailable);
        Assert.Equal(terminator, buffer.ReadKey().KeyChar);
        Assert.False(buffer.KeyAvailable);
        Assert.Equal(ConsoleKey.Enter, buffer.ReadKey().Key);
    }
}

[Collection(nameof(EngineRestartCollection))]
public class EditorContractIntegrationTests
{
    [Fact]
    public void MetadataCompletionAndHighlightingRemainCSharpAwareAcrossEditingContexts()
    {
        using var bed = new Bed();
        bed.Success("Func<Node, Node> pick = node => node; Node[] nodes = [root];");
        foreach (var (text, expected) in new (string, char?)[] {
            ("", null), ("notDeclared", null), ("System", '.'), ("Console", '.'), ("pick", '('),
            ("pick(root)", '.'), ("nodes[0]", '.'), ("(root)", '.'), ("root.QueueFree()", null),
            ("root.GetNode<Node>", '('), ("root.GetTree()", '.'), ("new Node", '('),
            ("$[\"Control\"]", '.'), ("ls $[\"Control\"]", null), ("cd $[\"Control\"]", null),
            ("$/root/Control", '/'), ("$Control/EnterVrButton", '.'), ("$Control/EnterVrButton/", null) })
        {
            var prepared = bed.Prepare(text);
            var actual = bed.Pump(ReplCompletion.ContinuationAsync(prepared, text.Length, bed.Token));
            Assert.True(expected == actual, $"{text}: expected {expected}, got {actual}");
        }
        const string source = "interface IThing {} struct Point { public int X; } enum Mode { One } delegate int Transform(int arg); " +
            "class Example : IThing { public int Value { get; set; } public int Field; " +
            "public int Map(int parameter) { var local = \"regular\"; var verbatim = @\"literal\"; int number = 42; " +
            "/* comment */ if (true) return parameter + number; return 0; } }";
        var highlighting = bed.Pump(bed.Callbacks.HighlightCallbackAsync(source, bed.Token));
        Assert.Contains(highlighting, span => span.Formatting.Foreground == AnsiColor.Yellow);
        Assert.Contains(highlighting, span => span.Formatting.Foreground == AnsiColor.BrightMagenta);
        Assert.Contains(highlighting, span => span.Formatting.Foreground == AnsiColor.Green);
        Assert.Contains(highlighting, span => span.Formatting.Foreground == AnsiColor.BrightCyan);
        Assert.Contains(highlighting, span => span.Formatting.Foreground == AnsiColor.BrightYellow);
        Assert.Empty(bed.Pump(bed.Callbacks.HighlightCallbackAsync(" ", bed.Token)));
        Assert.Equal(AnsiColor.BrightMagenta, Assert.Single(bed.Pump(bed.Callbacks.HighlightCallbackAsync(":help", bed.Token))).Formatting.Foreground);
        Assert.Equal(AnsiColor.BrightMagenta, Assert.Single(bed.Pump(bed.Callbacks.HighlightCallbackAsync("cd ", bed.Token))).Formatting.Foreground);
        foreach (var prefix in new[] { "", "c", "r", "p", "cp", "mv" })
        {
            var commands = bed.Items(prefix).Where(item => item.DisplayText is "cd" or "rm" or "pwd" or "cp" or "mv").ToArray();
            Assert.NotEmpty(commands);
            foreach (var command in commands)
                Assert.NotEmpty(bed.Pump(command.GetExtendedDescriptionAsync(bed.Token)).Text!);
        }
        var awaited = bed.Items("ls ").Single(item => item.DisplayText == "$Control");
        Assert.Equal("$Control", bed.Pump(awaited.GetComplexTextEditAsync("ls ", 3, bed.Token)).NewText);
        Assert.Equal("Control", NodePathInput.Capture(bed.Root, bed.Token)["Control"].Path);
        var missingCall = bed.Pump(ReplCompletion.CallEditAsync(bed.Prepare(""), new TextSpan(0, 0), "Unknown", bed.Token));
        Assert.Equal("Unknown()", missingCall.NewText);
        Assert.Equal(8, missingCall.NewCaret);
        var existingCall = bed.Pump(ReplCompletion.CallEditAsync(bed.Prepare("root.GetTree ()"), new TextSpan(5, 7), "GetTree", bed.Token));
        Assert.Equal("GetTree", existingCall.NewText);
        Assert.Equal(14, existingCall.NewCaret);
        Assert.False(bed.Pump(bed.Callbacks.ShouldOpenCompletionWindowAsync("?Timer", 0,
            new KeyPress(new ConsoleKeyInfo('?', ConsoleKey.Oem2, true, false, false)), bed.Token)));
        var cdHighlight = bed.Pump(bed.Callbacks.HighlightCallbackAsync("cd root", bed.Token));
        Assert.Contains(cdHighlight, span => span.Start == 0 && span.Length == 2 && span.Formatting.Foreground == AnsiColor.BrightMagenta);
        var ordinaryNode = bed.Items("$Cont").Single(item => item.DisplayText == "$Control");
        Assert.Equal("$[\"Control", bed.Pump(ordinaryNode.GetComplexTextEditAsync("$[\"Cont\"]", 7, bed.Token)).NewText);
        Assert.False(bed.Pump(bed.Callbacks.ConfirmCompletionCommit("$[\"Control\"]", 5,
            new KeyPress(new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, true)), bed.Token)));
        var oldSize = bed.Items("root.Si").Single(item => item.DisplayText == "Size");
        var changedContext = bed.Pump(oldSize.GetComplexTextEditAsync("world.Si", 8, bed.Token));
        Assert.Equal("Size", changedContext.NewText);
        var method = bed.Items("world.Op").Single(item => item.DisplayText == "Open");
        Assert.True(bed.Pump(bed.Callbacks.ConfirmCompletionCommit("world.Op", 8,
            new KeyPress(new ConsoleKeyInfo('.', ConsoleKey.OemPeriod, false, false, false)), bed.Token)));
        Assert.Equal("Open", bed.Pump(method.GetComplexTextEditAsync("world.Op", 8, bed.Token)).NewText);
        Assert.Equal(":exit", bed.Read("", new ConsoleKeyInfo('\x04', ConsoleKey.D, false, false, true)));
        Assert.Equal("x", bed.Read("x", new('\x04', ConsoleKey.D, false, false, true), Bed.Submit));
        Assert.Equal("$Controlzz", bed.Read("$Cont", Bed.Tab, new('z', ConsoleKey.Z, false, false, false),
            new('z', ConsoleKey.Z, false, false, false), Bed.Tab, Bed.Submit));
        var close = new KeyPress(new ConsoleKeyInfo(')', ConsoleKey.D0, true, false, false));
        Assert.Equal(ConsoleKey.RightArrow, bed.Pump(bed.Callbacks.TransformKeyPressAsync("root.GetTree()", 13, close, bed.Token)).ConsoleKeyInfo.Key);
        Assert.Equal(')', bed.Pump(bed.Callbacks.TransformKeyPressAsync("\")\"", 1, close, bed.Token)).ConsoleKeyInfo.KeyChar);
        var quoted = bed.Items("$[\"Cont\"]").Single(item => item.DisplayText == "$[\"Control\"]");
        var quotedEdit = bed.Pump(quoted.GetComplexTextEditAsync("$[\"Cont\"]", 8, bed.Token));
        Assert.Equal("$[\"Control\"", quotedEdit.NewText);
        var extensionColors = bed.Pump(bed.Callbacks.HighlightCallbackAsync("Enumerable.Range(0, 1).Select(item => item)", bed.Token));
        Assert.Contains(extensionColors, span => span.Formatting.Foreground == AnsiColor.BrightYellow);
        var queued = new Node { Name = "ExcludedFromCompletion" };
        bed.Root.AddChild(queued);
        queued.QueueFree();
        Assert.DoesNotContain("ExcludedFromCompletion", NodePathInput.Capture(bed.Root, bed.Token).Keys);
        Assert.Equal("$Control/EnterVrButton.", bed.Read("$Control/EnterVrButton", new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false), Bed.Tab, Bed.Submit));
        var tab = new KeyPress(Bed.Tab);
        Assert.Equal(ConsoleKey.Spacebar, bed.Pump(bed.Callbacks.TransformKeyPressAsync("root.Size", 4, tab, bed.Token)).ConsoleKeyInfo.Key);
        Assert.Equal(ConsoleKey.Spacebar, bed.Pump(bed.Callbacks.TransformKeyPressAsync("root.GetTree ()", 12, tab, bed.Token)).ConsoleKeyInfo.Key);
        var pastedCallbacks = new ReplPromptCallbacks(bed.Session, () => "pasted");
        Assert.Equal("pasted", bed.Read("", [new('\0', ConsoleKey.Insert, true, false, false), Bed.Submit], pastedCallbacks));
        var noPaste = bed.Pump(bed.Callbacks.TransformKeyPressAsync("", 0,
            new KeyPress(new ConsoleKeyInfo('\0', ConsoleKey.Insert, true, false, false)), bed.Token));
        Assert.Null(noPaste.PastedText);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => bed.Pump(bed.Callbacks.HighlightCallbackAsync("$Control", cancelled.Token)));
        Assert.NotEmpty(bed.Pump(bed.Callbacks.HighlightCallbackAsync("$Control", bed.Token)));
    }

    [Fact]
    public void SignaturesAndFormattingHandleDocsConstructorsNullCollectionsAndLoadedErrors()
    {
        using var bed = new Bed();
        bed.Success("class Docs {\n /// <summary>Construct a sample.</summary>\n public Docs(int value) {}\n " +
            "/// <summary>Describe a call.</summary><param name=\"count\">How many.</param>\n public string Call(int count) => count.ToString(); }");
        foreach (var text in new[] { "new Docs(", "new Docs(1)", "new MissingType(", "", "Console.WriteLine", "typeof(", "Docs(" })
        {
            var prepared = bed.Prepare(text);
            var (items, index) = bed.Pump(ReplCompletion.OverloadsAsync(prepared, text.Length, bed.Token));
            if (text == "new Docs(") Assert.NotEmpty(items);
            Assert.True(index >= 0);
        }
        var call = bed.Prepare("new Docs(1).Call(");
        var (overloads, _) = bed.Pump(ReplCompletion.OverloadsAsync(call, call.Input.Original.Length, bed.Token));
        Assert.NotEmpty(overloads);
        Assert.Equal("Describe a call.", overloads[0].Summary.Text);
        Assert.Equal("How many.", overloads[0].Parameters[0].Description.Text);
        Assert.Equal("null", bed.Success("(object)null").Value);
        Assert.Equal("7", bed.Success("return 7;").Value);
        Assert.Equal("[]", bed.Success("Array.Empty<object>()").Value);
        Assert.Contains("null", bed.Success("new object[] { \"text\", null, Array.Empty<object>() }").Value!);
        Assert.EndsWith(", ...]", bed.Success("Enumerable.Range(0, 25)").Value!);
        bed.Success("class NullDisplay { public override string ToString() => null; }");
        Assert.Equal("null", bed.Success("new NullDisplay()").Value);
        Assert.Contains("Incomplete node path", bed.Eval("$").Error!);
        Assert.Contains("Incomplete node path", bed.Eval("$[]").Error!);
        Assert.Contains("No PackedScene", bed.Eval("world.Open(\"res://does-not-exist.tscn\")").Error!);
        Assert.Equal("true", bed.Success("engine == engine").Value);
        Assert.True(bed.Eval(":cd ((Func<Node>)(() => throw new OperationCanceledException()))()").Cancelled);
        var path = Path.Combine(Path.GetTempPath(), "repl-load-" + Guid.NewGuid() + ".csx");
        try
        {
            File.WriteAllText(path, "var broken = ;");
            Assert.Contains(Path.GetFileName(path), bed.Eval("#load \"" + path.Replace('\\', '/') + "\"").Error!);
        }
        finally { File.Delete(path); }
    }

    private sealed class Bed : IDisposable
    {
        private readonly Engine engine;
        private readonly EngineDispatcher dispatcher = new();
        private readonly ReplGlobals globals;
        public Window Root => globals.root;
        public ReplSession Session { get; }
        public IPromptCallbacks Callbacks { get; }
        public CancellationToken Token => TestContext.Current.CancellationToken;
        public static ConsoleKeyInfo Submit => new('\r', ConsoleKey.Enter, false, false, true);
        public static ConsoleKeyInfo Tab => new('\t', ConsoleKey.Tab, false, false, false);
        public Bed()
        {
            var game = typeof(showcase.CSharpTicker).Assembly;
            engine = new Engine("repl-editor-contract", Engine.ResolveProjectDir(), "--headless") { CaptureErrors = true };
            engine.Start();
            Assert.False(engine.Iteration());
            globals = new ReplGlobals(engine);
            var dynamicAssembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Transient" + Guid.NewGuid()), AssemblyBuilderAccess.Run);
            Session = new ReplSession(globals, [game, dynamicAssembly], dispatcher);
            Callbacks = new ReplPromptCallbacks(Session);
        }
        public T Pump<T>(Task<T> task)
        {
            var clock = Stopwatch.StartNew();
            while (!task.IsCompleted)
            {
                Token.ThrowIfCancellationRequested();
                Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30));
                Assert.False(engine.Iteration());
                dispatcher.Pump();
                Thread.Sleep(1);
            }
            return task.GetAwaiter().GetResult();
        }
        public ReplResult Eval(string text) => Pump(Task.Run(() => Session.EvaluateAsync(text, Token), Token));
        public ReplResult Success(string text)
        {
            var result = Eval(text);
            Assert.True(result.Error is null, text + "\n" + result.Error);
            return result;
        }
        public PreparedInput Prepare(string text) => Pump(Session.PrepareAsync(text, Token));
        public IReadOnlyList<CompletionItem> Items(string text)
        {
            var span = Pump(Callbacks.GetSpanToReplaceByCompletionAsync(text, text.Length, Token));
            return Pump(Callbacks.GetCompletionItemsAsync(text, text.Length, span, Token));
        }
        public string Read(string text, params ConsoleKeyInfo[] keys) => Read(text, keys, new ReplPromptCallbacks(Session));
        public string Read(string text, ConsoleKeyInfo[] keys, ReplPromptCallbacks callbacks)
        {
            var typed = text.Select(c => new ConsoleKeyInfo(c, char.IsLetterOrDigit(c) ? (ConsoleKey)char.ToUpperInvariant(c) : ConsoleKey.NoName, false, false, false));
            var console = new PromptConsole(typed.Concat(keys));
            return Pump(Task.Run(async () =>
            {
                await using var prompt = new Prompt(callbacks: callbacks, console: console, configuration: ReplHost.CreatePromptConfiguration());
                return (await prompt.ReadLineAsync()).Text;
            }, Token));
        }
        public void Dispose()
        {
            Session.Dispose();
            globals.world.Dispose();
            dispatcher.Dispose();
            Assert.Empty(engine.Errors.Drain());
            engine.Dispose();
        }
    }
}

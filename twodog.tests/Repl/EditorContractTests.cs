using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using Godot;
using Microsoft.CodeAnalysis.Text;
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
            Assert.All(prompt.Spans.ToArray(), span => Assert.InRange(span.End, 0, prompt.Length));
            Assert.Equal(NodeColors.Color(NodeFamily.Control), prompt.Spans[0].Style.Foreground);
            Assert.Empty(ReplHost.CreatePromptText(path, 60, null, color: false).Spans.ToArray());
        }
        foreach (var path in new string?[] { null, "/root", "", "/root/Short" })
            Assert.NotEmpty(ReplHost.CreatePromptText(path, null, null, color: true).Spans.ToArray());
        Assert.NotEmpty(ReplHost.CreatePromptText(null, 160, new Dictionary<string, NodeFamily>(), color: true).Spans.ToArray());
        Assert.NotEmpty(ReplHost.CreatePromptText("/root/Unknown", 160, new Dictionary<string, NodeFamily>(), color: true).Spans.ToArray());
        foreach (var path in new[] { "$[", "$[\"Node", "$[ \"Node\"]", "?Name", "" })
            Assert.Equal(path, NodeColors.Path(path).Text);
        Assert.Equal("", StyledText.Empty.ToAnsi(false));
        Assert.Equal("plain", new StyledText("plain", [new StyledSpan(0, 5, new TextStyle())]).ToAnsi(true));
        var cycle = new CompletionCycle("obj.pref suffix", 8, [new ReplCompletionItem("prefix"), new ReplCompletionItem("preferred")], -1);
        var edit = new CompletionEdit(new TextRange(4, 4), "prefix");
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

    [Theory]
    [InlineData('P')]
    [InlineData('Q')]
    [InlineData('R')]
    [InlineData('S')]
    [InlineData('~')]
    public void UnixFunctionSequencesStopBeforeTheFollowingEnter(char terminator)
    {
        var keys = new Queue<ConsoleKeyInfo>([Keys.Escape, new('[', ConsoleKey.NoName, false, false, false),
            new(terminator, ConsoleKey.NoName, false, false, false), Keys.Enter]);
        var reader = new ReplKeyReader(() => keys.Count > 0, keys.Dequeue, windows: false);
        Assert.Equal(Keys.Enter, Assert.IsType<KeyInput>(reader.Read(idle: false)).Key);
        Assert.Empty(keys);
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
        var highlighting = bed.Pump(bed.Language.HighlightAsync(source, bed.Token));
        Assert.Contains(highlighting, span => span.Style.Foreground == TermColor.Yellow);
        Assert.Contains(highlighting, span => span.Style.Foreground == TermColor.BrightMagenta);
        Assert.Contains(highlighting, span => span.Style.Foreground == TermColor.Green);
        Assert.Contains(highlighting, span => span.Style.Foreground == TermColor.BrightCyan);
        Assert.Contains(highlighting, span => span.Style.Foreground == TermColor.BrightYellow);
        Assert.Empty(bed.Pump(bed.Language.HighlightAsync(" ", bed.Token)));
        Assert.Equal(TermColor.BrightMagenta, Assert.Single(bed.Pump(bed.Language.HighlightAsync(":help", bed.Token))).Style.Foreground);
        Assert.Equal(TermColor.BrightMagenta, Assert.Single(bed.Pump(bed.Language.HighlightAsync("cd ", bed.Token))).Style.Foreground);
        foreach (var prefix in new[] { "", "c", "r", "p", "cp", "mv" })
        {
            var commands = bed.Items(prefix).Where(item => item.DisplayText is "cd" or "rm" or "pwd" or "cp" or "mv").ToArray();
            Assert.NotEmpty(commands);
            foreach (var command in commands)
                Assert.NotEmpty(bed.Pump(command.DescriptionAsync(bed.Token)).Text!);
        }
        var awaited = bed.Items("ls ").Single(item => item.DisplayText == "$Control");
        Assert.Equal("$Control", bed.Edit(awaited, "ls ", 3).NewText);
        Assert.Equal("Control", NodePathInput.Capture(bed.Root, bed.Token)["Control"].Path);
        var missingCall = bed.Pump(ReplCompletion.CallEditAsync(bed.Prepare(""), new TextSpan(0, 0), "Unknown", bed.Token));
        Assert.Equal("Unknown()", missingCall.NewText);
        Assert.Equal(8, missingCall.NewCaret);
        var existingCall = bed.Pump(ReplCompletion.CallEditAsync(bed.Prepare("root.GetTree ()"), new TextSpan(5, 7), "GetTree", bed.Token));
        Assert.Equal("GetTree", existingCall.NewText);
        Assert.Equal(14, existingCall.NewCaret);
        Assert.False(bed.Language.ShouldOpenCompletion("?Timer", 0, '?'));
        var cdHighlight = bed.Pump(bed.Language.HighlightAsync("cd root", bed.Token));
        Assert.Contains(cdHighlight, span => span.Start == 0 && span.Length == 2 && span.Style.Foreground == TermColor.BrightMagenta);
        var ordinaryNode = bed.Items("$Cont").Single(item => item.DisplayText == "$Control");
        Assert.Equal("$[\"Control", bed.Edit(ordinaryNode, "$[\"Cont\"]", 7).NewText);
        Assert.False(bed.Language.AcceptsCommit(ordinaryNode, "$[\"Control\"]", 5, '.'));
        var oldSize = bed.Items("root.Si").Single(item => item.DisplayText == "Size");
        var changedContext = bed.Edit(oldSize, "world.Si", 8);
        Assert.Equal("Size", changedContext.NewText);
        var method = bed.Items("world.Op").Single(item => item.DisplayText == "Open");
        Assert.True(bed.Language.AcceptsCommit(method, "world.Op", 8, '.'));
        Assert.Equal("Open", bed.Pump(bed.Language.CommitAsync(method, "world.Op", 8, CommitKey.Character, bed.Token)).NewText);
        Assert.Equal("Open()", bed.Edit(method, "world.Op", 8).NewText);
        Assert.Equal(":exit", bed.Read("", new ConsoleKeyInfo('\x04', ConsoleKey.D, false, false, true)));
        Assert.Equal("x", bed.Read("x", new('\x04', ConsoleKey.D, false, false, true), Bed.Submit));
        Assert.Equal("$Controlzz", bed.Read("$Cont", Bed.Tab, new('z', ConsoleKey.Z, false, false, false),
            new('z', ConsoleKey.Z, false, false, false), Bed.Tab, Bed.Submit));
        Assert.True(bed.Pump(bed.Language.SkipsCloseParenAsync("root.GetTree()", 13, bed.Token)));
        Assert.False(bed.Pump(bed.Language.SkipsCloseParenAsync("\")\"", 1, bed.Token)));
        Assert.False(bed.Pump(bed.Language.SkipsCloseParenAsync("root", 4, bed.Token)));
        var quoted = bed.Items("$[\"Cont\"]").Single(item => item.DisplayText == "$[\"Control\"]");
        var quotedEdit = bed.Edit(quoted, "$[\"Cont\"]", 8);
        Assert.Equal("$[\"Control\"", quotedEdit.NewText);
        var extensionColors = bed.Pump(bed.Language.HighlightAsync("Enumerable.Range(0, 1).Select(item => item)", bed.Token));
        Assert.Contains(extensionColors, span => span.Style.Foreground == TermColor.BrightYellow);
        var queued = new Node { Name = "ExcludedFromCompletion" };
        bed.Root.AddChild(queued);
        queued.QueueFree();
        Assert.DoesNotContain("ExcludedFromCompletion", NodePathInput.Capture(bed.Root, bed.Token).Keys);
        Assert.Equal("$Control/EnterVrButton.", bed.Read("$Control/EnterVrButton", new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false), Bed.Tab, Bed.Submit));
        Assert.Same(TabAction.Open, bed.Pump(bed.Language.TabAsync("root.Size", 4, bed.Token)));
        Assert.Same(TabAction.Open, bed.Pump(bed.Language.TabAsync("root.GetTree ()", 12, bed.Token)));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => bed.Pump(bed.Language.HighlightAsync("$Control", cancelled.Token)));
        Assert.NotEmpty(bed.Pump(bed.Language.HighlightAsync("$Control", bed.Token)));
    }

    [Fact]
    public void QualifiedConstructorsContinueWithCallParentheses()
    {
        using var bed = new Bed();
        foreach (var name in new[] { "Node", "Godot.Node", "global::Godot.Node", "System.Collections.Generic.List<Node>" })
        {
            var text = "new " + name;
            Assert.Equal('(', bed.Pump(ReplCompletion.ContinuationAsync(bed.Prepare(text), text.Length, bed.Token)));
            Assert.Equal(text + "()", bed.Read(text, new('\x1b', ConsoleKey.Escape, false, false, false), Bed.Tab, Bed.Submit));
        }
        const string argument = "new Godot.Node(root";
        Assert.Equal('.', bed.Pump(ReplCompletion.ContinuationAsync(bed.Prepare(argument), argument.Length, bed.Token)));
    }

    [Fact]
    public void MethodCompletionPreservesExistingGenericTypeArgumentsAndCalls()
    {
        using var bed = new Bed();
        bed.Success("class GenericCalls { public static int Empty<T>() => 1; }");
        foreach (var (source, name, expected, caret) in new (string, string, string, int)[] {
            ("root.GetNo<Node>", "GetNode", "root.GetNode<Node>()", "root.GetNode<Node>(".Length),
            ("$Control.GetNo <Node>", "GetNode", "$Control.GetNode <Node>()", "$Control.GetNode <Node>(".Length),
            ("root.GetNo<Node> (\"Control\")", "GetNode", "root.GetNode<Node> (\"Control\")", "root.GetNode<Node> (".Length),
            ("root.GetNo<Dictionary<string, Node>>", "GetNode", "root.GetNode<Dictionary<string, Node>>()", "root.GetNode<Dictionary<string, Node>>(".Length),
            ("root.GetNo<Node", "GetNode", "root.GetNode<Node", "root.GetNode".Length),
            ("GenericCalls.Emp <Node", "Empty", "GenericCalls.Empty <Node", "GenericCalls.Empty".Length),
            ("GenericCalls.Emp<", "Empty", "GenericCalls.Empty<", "GenericCalls.Empty".Length),
            ("root.GetT < 1", "GetTree", "root.GetTree() < 1", "root.GetTree()".Length),
            ("GenericCalls.Emp < 1", "Empty", "GenericCalls.Empty() < 1", "GenericCalls.Empty()".Length),
            ("GenericCalls.Emp<Node>", "Empty", "GenericCalls.Empty<Node>()", "GenericCalls.Empty<Node>()".Length) })
        {
            var start = source.IndexOf('.') + 1;
            var length = source[start..source.IndexOf('<')].TrimEnd().Length;
            var edit = bed.Pump(ReplCompletion.CallEditAsync(bed.Prepare(source), new TextSpan(start, length), name, bed.Token));
            var result = source.Remove(edit.Range.Start, edit.Range.Length).Insert(edit.Range.Start, edit.NewText);
            Assert.Equal(expected, result);
            Assert.Equal(caret, edit.NewCaret);
        }
        var completion = bed.Items("root.GetNo").Single(item => item.DisplayText == "GetNode");
        const string text = "root.GetNo<Node>";
        var committed = bed.Edit(completion, text, "root.GetNo".Length);
        Assert.Equal("root.GetNode<Node>()", committed.Apply(text).Text);
    }

    [Fact]
    public void AliasSignatureHelpSelectsTheUsersInvocationAndExactArgumentIndex()
    {
        using var bed = new Bed();
        const string text = "root.AddChild($Control";
        for (var caret = text.IndexOf('$') + 1; caret <= text.Length; caret++)
        {
            var (items, index) = bed.Pump(ReplCompletion.OverloadsAsync(bed.Prepare(text), caret, bed.Token));
            Assert.NotEmpty(items);
            Assert.All(items, item => Assert.Contains("AddChild(", item.Signature.Text!));
            Assert.Equal(0, index);
        }
        const string multiArgument = "root.AddChild($Control, false, ";
        var (overloads, argumentIndex) = bed.Pump(ReplCompletion.OverloadsAsync(bed.Prepare(multiArgument), multiArgument.Length, bed.Token));
        Assert.NotEmpty(overloads);
        Assert.All(overloads, item => Assert.Contains("AddChild(", item.Signature.Text!));
        Assert.Equal(2, argumentIndex);
        Assert.Contains(overloads, item => item.Parameters.Count >= 3);
        const string comparison = "root == ?EnterVrB";
        var completion = bed.Items(comparison).Single(item => item.DisplayText == "$Control/EnterVrButton");
        Assert.Equal("$Control/EnterVrButton", bed.Edit(completion, comparison, comparison.Length).NewText);
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
            Assert.Equal(0, index);
        }
        var call = bed.Prepare("new Docs(1).Call(");
        var (overloads, _) = bed.Pump(ReplCompletion.OverloadsAsync(call, call.Input.Original.Length, bed.Token));
        Assert.NotEmpty(overloads);
        Assert.Equal("Describe a call.", overloads[0].Summary);
        Assert.Equal("How many.", overloads[0].Parameters[0].Documentation);
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

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    public void OutputWhileTypingPreservesVariableCompletion(int interruptRead)
    {
        using var bed = new Bed();
        bed.Success("var colorAnswer = 42;");
        var keys = Keys.Type("colorAns").Append(Bed.Tab).Append(Bed.Tab).Append(Bed.Submit).ToArray();
        var terminal = new VirtualTerminal(80, 12);
        using var screen = new ReplScreen(terminal, color: true);
        var editor = new ReplEditor(bed.Language);
        editor.Changed += screen.Invalidate;
        screen.SetRegion((width, height) => RegionLayout.Build("godot> ", editor.View, null, width, height, true));
        var reads = 0;
        var result = bed.Pump(Task.Run(() => editor.ReadLineAsync(_ =>
        {
            if (reads == interruptRead) screen.Write("background result\n");
            screen.Flush();
            return Task.FromResult<TerminalInput>(new KeyInput(keys[reads++]));
        }, bed.Token), bed.Token));
        Assert.Equal("colorAnswer", result.Text);
        Assert.Contains("background result", terminal.Text);
        Assert.Equal("42", bed.Success(result.Text).Value);
    }

    private sealed class Bed : IDisposable
    {
        private readonly Engine engine;
        private readonly EngineDispatcher dispatcher = new();
        private readonly ReplGlobals globals;
        public Window Root => globals.root;
        public ReplSession Session { get; }
        public ReplLanguage Language { get; }
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
            Language = new ReplLanguage(Session);
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
        public IReadOnlyList<ReplCompletionItem> Items(string text)
        {
            var span = Pump(Language.CompletionRangeAsync(text, text.Length, Token));
            return Pump(Language.CompletionsAsync(text, text.Length, span, Token));
        }
        /// <summary>The edit for accepting an item with Tab, which also completes calls with parentheses.</summary>
        public CompletionEdit Edit(ReplCompletionItem item, string text, int caret) => Pump(Language.CommitAsync(item, text, caret, CommitKey.Tab, Token));
        public string Read(string text, params ConsoleKeyInfo[] keys)
        {
            var typed = text.Select(c => new ConsoleKeyInfo(c, char.IsLetterOrDigit(c) ? (ConsoleKey)char.ToUpperInvariant(c) : ConsoleKey.NoName, false, false, false));
            return Pump(Task.Run(() => EditorDriver.ReadAsync(new ReplLanguage(Session), typed.Concat(keys), token: Token), Token));
        }
        public void Dispose()
        {
            Session.Dispose();
            globals.world.Dispose();
            dispatcher.Dispose();
            engine.Dispose();
            Assert.Empty(engine.Errors.Drain());
        }
    }
}

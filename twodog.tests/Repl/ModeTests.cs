using System.Diagnostics;
using Godot;
using PrettyPrompt;
using PrettyPrompt.Completion;
using twodog.Repl;
using twodog.tests.EngineTests;

namespace twodog.tests.ReplTests;

[Collection(nameof(EngineRestartCollection))]
public class ModeIntegrationTests
{
    [Fact]
    public void ModesPreserveVariablesAndScopeWhileResolvingCommandCollisions()
    {
        using var bed = new Bed();
        Assert.Equal(ReplMode.Auto, bed.Session.Mode);
        bed.Ok("var ls = 41; var cd = 42; var pwd = 43; var rm = 44; var cp = 45; var mv = 46; var exit = 47;");
        Assert.Equal("46", bed.Ok("mv").Value);
        Assert.Equal(bed.Globals.root.GetTreeStringPretty(), bed.Ok(":ls").Tree);
        Assert.Equal(":ls", bed.Read(":ls", Bed.Enter));
        bed.Ok("cd $Control");
        var selected = bed.Globals.here;
        bed.Ok(":cs");
        foreach (var (name, value) in new[] { ("ls", "41"), ("cd", "42"), ("pwd", "43"), ("rm", "44"), ("cp", "45"), ("mv", "46"), ("exit", "47") })
            Assert.Equal(value, bed.Ok(name).Value);
        Assert.Contains("CS", bed.Eval("cd $CenterContainer").Error!);
        Assert.Contains("no shell commands", bed.Eval(":cd /").Error!);
        Assert.Contains("no shell commands", bed.Eval(":ls").Error!);
        Assert.Same(selected, bed.Globals.here);
        Assert.Equal("\"Control\"", bed.Ok("here.Name.ToString()").Value);
        Assert.Contains(bed.Items("$CenterContainer."), i => i.DisplayText == "GetChild");
        Assert.Contains(bed.Items("?Cent"), i => i.ReplacementText.Contains("CenterContainer"));
        Assert.Single(bed.Items("r"), i => i.DisplayText == "rm");
        Assert.DoesNotContain(bed.Items(":"), i => i.DisplayText == ":mv");
        Assert.Contains(bed.Items(":"), i => i.DisplayText == ":sh");
        var csharp = bed.Prepare("mv");
        Assert.Equal('.', bed.Pump(ReplCompletion.ContinuationAsync(csharp, 2, bed.Token)));
        Assert.Equal("mv.ToString()", bed.Read("mv.ToStr", Bed.Tab, Bed.Run));

        bed.Ok(":sh");
        Assert.Contains("CenterContainer", bed.Ok("ls").Tree!);
        Assert.Contains("TargetLabel", bed.Ok(":ls $CenterContainer").Tree!);
        Assert.Contains(bed.Items(":ls "), item => item.DisplayText.StartsWith("$CenterContainer", StringComparison.Ordinal));
        Assert.Equal(":ls $CenterContainer", bed.Read(":ls $Cent", Bed.Tab, Bed.Run));
        Assert.Contains(bed.Items(":"), item => item.DisplayText == ":ls");
        Assert.Equal("/root/Control", bed.Ok("pwd").Value);
        Assert.Contains("Usage:", bed.Eval("mv").Error!);
        Assert.Contains("Usage:", bed.Eval("cp").Error!);
        Assert.Contains("Usage:", bed.Eval("rm").Error!);
        bed.Ok("cd (here.GetNode(\"CenterContainer\"))");
        Assert.Equal("/root/Control/CenterContainer", bed.Ok("pwd").Value);
        Assert.Equal("\"CenterContainer\"", bed.Ok("here.Name.ToString()").Value);
        bed.Ok("here.SetMeta(\"mode_test\", 9)");
        Assert.Equal("9", bed.Ok("(int)here.GetMeta(\"mode_test\")").Value);
        Assert.Contains("Use :cs", bed.Eval("var shouldNotExist = 7;").Error!);
        Assert.Contains("Use :cs", bed.Eval("here.QueueFree(); here.QueueFree();").Error!);
        Assert.True(GodotObject.IsInstanceValid(bed.Globals.here));
        Assert.Contains("Usage:", bed.Eval("pwd here").Error!);
        bed.Ok("cd /root/Control");
        Assert.Equal("cd $/root/Control", bed.Read("cd $/root/Cont", Bed.Tab, Bed.Run));
        Assert.Null(bed.Pump(ReplCompletion.ContinuationAsync(bed.Prepare("mv"), 2, bed.Token)));
        Assert.Equal("ls", bed.Read("ls", Bed.Enter));
        Assert.Contains(bed.Items(":"), i => i.DisplayText == ":mv");
        bed.Ok("cd");
        Assert.Same(bed.Globals.root, bed.Globals.here);

        bed.Ok(":ai");
        foreach (var input in new[] { "hello", "ls", "root.QueueFree();", ":unknown", "exit", "if (true) {" })
        {
            var result = bed.Ok(input);
            Assert.Equal(ReplModes.AgentReply, result.Value);
            Assert.False(result.Committed);
        }
        Assert.Empty(bed.Items("root."));
        IPromptCallbacks agentCallbacks = new ReplPromptCallbacks(bed.Session);
        Assert.Empty(bed.Pump(agentCallbacks.GetOverloadsAsync("root.GetNode(", 13, bed.Token)).Item1);
        Assert.Contains(bed.Pump(agentCallbacks.HighlightCallbackAsync(":cs", bed.Token)), span => span.Start == 0);
        bed.Pump(Task.Run(async () => { await new ReplPromptCallbacks(bed.Session).WarmUpAsync(bed.Token); return true; }));
        Assert.Empty(bed.Pump(((IPromptCallbacks)new ReplPromptCallbacks(bed.Session)).HighlightCallbackAsync("hello", bed.Token)));
        Assert.Equal("hello", bed.Read("hello", Bed.Tab, Bed.Enter));
        Assert.Equal(":cs", bed.Read(":cs", Bed.Enter));
        Assert.DoesNotContain(bed.Items(":"), i => i.DisplayText == ":rm");
        bed.Ok(":auto");
        Assert.Equal("46", bed.Ok("mv").Value);
        Assert.Contains("CS0103", bed.Eval("shouldNotExist").Error!);
        bed.Ok("cd $Control");
        Assert.Contains("CenterContainer", bed.Ok("ls").Tree!);
    }

    [Theory]
    [InlineData(":auto")]
    [InlineData(":sh")]
    public void MoveCanRenameWithoutChangingIdentityParentOrTransform(string mode)
    {
        using var bed = new Bed();
        bed.Ok("var holder = new Node2D { Name = \"RenameTest\" }; root.AddChild(holder); " +
            "var original = new Node2D { Name = \"Old\", Position = new Vector2(10, 20) }; holder.AddChild(original); " +
            "var sibling = new Node { Name = \"Taken\" }; holder.AddChild(sibling); " +
            "var detached = new Node(); var freed = new Node(); freed.Free(); " +
            "var queued = new Node { Name = \"Queued\" }; holder.AddChild(queued); queued.QueueFree(); " +
            "int renameCalls = 0; Node Pick() { renameCalls++; return original; }");
        bed.Ok(mode);
        bed.Ok("cd original");
        var node = bed.Globals.here;
        var position = ((Node2D)node).GlobalPosition;
        var result = bed.Ok("mv Pick() \"New Name\"");
        Assert.Contains("Renamed:", result.Value!);
        Assert.False(result.Committed);
        Assert.Same(node, bed.Globals.here);
        Assert.Equal("/root/RenameTest/New Name", bed.Pump(bed.Session.ScopePathAsync(bed.Token)));
        Assert.Equal(position, ((Node2D)node).GlobalPosition);
        Assert.Equal("1", bed.Ok("renameCalls").Value);
        Assert.Equal("true", bed.Ok("original.GetParent() == holder").Value);
        bed.Ok("mv original \"New Name\"");
        Assert.Contains("already has a child", bed.Eval("mv original \"Taken\"").Error!);
        foreach (var name in new[] { "\"\"", "\" \"", "\"Bad/Path\"", "\"Bad:Name\"", "\"Bad\\0Name\"" })
            Assert.Contains("valid Godot node name", bed.Eval("mv original " + name).Error!);
        Assert.Equal("New Name", node.Name.ToString());
        Assert.Contains("root Window", bed.Eval("mv root \"RenamedRoot\"").Error!);
        foreach (var target in new[] { "detached", "freed", "queued" })
            Assert.Contains("live node", bed.Eval("mv " + target + " \"Nope\"").Error!);
        Assert.Contains("Godot.Node", bed.Eval("mv 42 \"Nope\"").Error!);
        Assert.Contains("Godot.Node", bed.Eval("cp original \"Nope\"").Error!);
        bed.Ok("cd ..");
        Assert.Contains(bed.Items("$"), i => i.ReplacementText == "$[\"New Name\"]");
        bed.Ok("mv $[\"New Name\"] \"Final\"");
        Assert.Contains(bed.Items("$Fi"), i => i.ReplacementText == "$Final");
        Assert.DoesNotContain(bed.Items("$"), i => i.ReplacementText == "$[\"New Name\"]");
        bed.Ok("mv $Final root");
        Assert.Same(bed.Globals.root, node.GetParent());
        bed.Ok("mv $/root/Final \"FinalRenamed\"");
        Assert.Same(node, bed.Globals.root.GetNode("FinalRenamed"));
        bed.Ok("cd /");
        bed.Ok("rm holder");
        bed.Ok("rm original");
        bed.Ok("detached.Free()");
    }

    [Theory]
    [InlineData(":auto")]
    [InlineData(":sh")]
    [InlineData(":cs")]
    public void DollarCompletionRanksPathsBeforeGlobalNamesAndKeepsEditingAtTheCursor(string mode)
    {
        using var bed = new Bed();
        bed.Ok("var scope = new Node { Name = \"ReplDollarScope\" }; root.AddChild(scope); " +
            "scope.AddChild(new Node { Name = \"DogfoodPath\" }); " +
            "scope.AddChild(new Node { Name = \"dogfoodLower\" }); " +
            "var group = new Node { Name = \"Group\" }; scope.AddChild(group); " +
            "var branch = new Node { Name = \"DogfoodName\" }; group.AddChild(branch); " +
            "branch.AddChild(new Node { Name = \"Leaf\" }); " +
            "group.AddChild(new Node { Name = \"dogfoodCase\" }); " +
            "group.AddChild(new Node { Name = \"Dogfood With Space\" }); " +
            "var outside = new Node { Name = \"DogfoodOutside\" }; root.AddChild(outside);");
        bed.Ok("cd scope");
        bed.Ok(mode);
        Assert.Contains(bed.Items(":"), item => item.DisplayText == ":exit");
        Assert.DoesNotContain(bed.Items(":"), item => item.DisplayText == ":quit");
        Assert.Equal(":exit", bed.Read(":exit", Bed.Enter));

        const string prefix = "$Dogfood";
        var span = new PrettyPrompt.Documents.TextSpan(0, prefix.Length);
        var items = bed.Items(prefix);
        var ranked = items.OrderByDescending(i => i.GetCompletionItemPriority(prefix, prefix.Length, span))
            .Where(i => i.GetCompletionItemPriority(prefix, prefix.Length, span) >= 0).Select(i => i.ReplacementText).ToArray();
        Assert.Equal(new[] { "$DogfoodPath", "$dogfoodLower", "$[\"Group/Dogfood With Space\"]",
            "$Group/DogfoodName/", "$/root/DogfoodOutside", "$Group/dogfoodCase" }, ranked);
        Assert.Equal("$DogfoodPath", bed.Read(prefix, Bed.Tab, Bed.Run));
        Assert.Equal("$dogfoodLower", bed.Read(prefix, Bed.Tab, Bed.Tab, Bed.Run));
        Assert.Equal("$[\"Group/Dogfood With Space\"]", bed.Read(prefix, Bed.Tab, Bed.Tab, Bed.Tab, Bed.Run));
        var reverse = new ConsoleKeyInfo('\t', ConsoleKey.Tab, true, false, false);
        Assert.Equal("$Group/dogfoodCase", bed.Read(prefix, Bed.Tab, reverse, Bed.Run));
        Assert.Equal(prefix, bed.Read(prefix, Bed.Tab, Bed.Tab, new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false), Bed.Run));
        Assert.Equal("$Group/DogfoodName/", bed.Read("$DogfoodNa", Bed.Enter, Bed.Run));
        Assert.Equal("$Group/DogfoodName/Leaf", bed.Read("$DogfoodNa", Bed.Tab,
            new ConsoleKeyInfo('L', ConsoleKey.L, true, false, false), Bed.Tab, Bed.Run));
        Assert.Equal("$/root/DogfoodOutside", bed.Read("$DogfoodOut", Bed.Tab, Bed.Run));
        Assert.Equal("$/root/ReplDollarScope/", bed.Read("$ReplDollarSc", Bed.Tab, Bed.Run));
        Assert.Equal("$[\"Group/Dogfood With Space\"]", bed.Read("$[\"Dogfood With\"]", Bed.Tab, Bed.Run));
        Assert.Equal("$/root/DogfoodOutside", bed.Read("$/root/DogfoodOut", Bed.Tab, Bed.Run));
        Assert.Contains("not found", bed.Eval("$DogfoodName").Error!);
        Assert.Equal("true", bed.Ok("$Group/DogfoodName/ is Node").Value);

        // The menu retains the items created for an empty prefix as typing continues.
        var cached = bed.Items("$");
        Assert.DoesNotContain(cached, i => i.ReplacementText.StartsWith("$/", StringComparison.Ordinal) &&
            i.GetCompletionItemPriority("$", 1, new PrettyPrompt.Documents.TextSpan(0, 1)) >= 0);
        var branchItem = Assert.Single(cached, i => i.ReplacementText == "$Group/DogfoodName");
        var edit = bed.Pump(branchItem.GetComplexTextEditAsync("$DogfoodNa", 10, bed.Token));
        Assert.Equal("$Group/DogfoodName/", edit.NewText);
        var narrowed = "$Group/DogfoodNa";
        var narrowedSpan = new PrettyPrompt.Documents.TextSpan(0, narrowed.Length);
        Assert.True(branchItem.GetCompletionItemPriority(narrowed, narrowed.Length, narrowedSpan) >= 0);
        edit = bed.Pump(branchItem.GetComplexTextEditAsync(narrowed, narrowed.Length, bed.Token));
        Assert.Equal("$Group/DogfoodName", edit.NewText);
        var left = new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, false, false, false);
        Assert.Equal("$Group/DogfoodName/Flair", bed.Read("$DogfoodNaFlair", [.. Enumerable.Repeat(left, 5), Bed.Tab, Bed.Run]));
        Assert.Equal("$[\"Group/Dogfood With SpaceFlair\"]", bed.Read("$DogfoodFlair", [.. Enumerable.Repeat(left, 5), Bed.Tab, Bed.Tab, Bed.Tab, Bed.Run]));
        Assert.Equal("$[\"Group/Dogfood With SpaceFlair\"]", bed.Read("$[\"Dogfood WithFlair\"]", [.. Enumerable.Repeat(left, 7), Bed.Tab, Bed.Run]));
        if (mode != ":cs")
        {
            Assert.Equal("ls $Group/DogfoodName/", bed.Read("ls $DogfoodNa", Bed.Tab, Bed.Run));
            Assert.Equal("cd $/root/DogfoodOutside", bed.Read("cd $DogfoodOut", Bed.Tab, Bed.Run));
            Assert.Equal("cp $DogfoodPath $Group/DogfoodName/", bed.Read("cp $DogfoodPath $DogfoodNa", Bed.Tab, Bed.Run));
            Assert.Equal("mv $DogfoodPath $Group/DogfoodName/", bed.Read("mv $DogfoodPath $DogfoodNa", Bed.Tab, Bed.Run));
        }
        bed.Ok(":auto");
        bed.Ok("cd /");
        bed.Ok("rm scope");
        bed.Ok("rm outside");
    }

    private sealed class Bed : IDisposable
    {
        private readonly Engine engine;
        private readonly EngineDispatcher dispatcher = new();
        public ReplGlobals Globals { get; }
        public ReplSession Session { get; }
        public CancellationToken Token => TestContext.Current.CancellationToken;
        public static ConsoleKeyInfo Run => new('\r', ConsoleKey.Enter, false, false, true);
        public static ConsoleKeyInfo Enter => new('\r', ConsoleKey.Enter, false, false, false);
        public static ConsoleKeyInfo Tab => new('\t', ConsoleKey.Tab, false, false, false);
        public Bed()
        {
            var game = typeof(showcase.CSharpTicker).Assembly;
            engine = new Engine("repl-modes", Engine.ResolveProjectDir(), "--headless") { CaptureErrors = true };
            engine.Start();
            Assert.False(engine.Iteration());
            Globals = new ReplGlobals(engine);
            Session = new ReplSession(Globals, [game], dispatcher);
        }
        public T Pump<T>(Task<T> task)
        {
            var watch = Stopwatch.StartNew();
            while (!task.IsCompleted)
            {
                Token.ThrowIfCancellationRequested();
                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), "Mode test stopped making progress.");
                Assert.False(engine.Iteration());
                dispatcher.Pump();
                Thread.Sleep(1);
            }
            return task.GetAwaiter().GetResult();
        }
        public ReplResult Eval(string text) => Pump(Task.Run(() => Session.EvaluateAsync(text, Token)));
        public ReplResult Ok(string text)
        {
            var result = Eval(text);
            Assert.True(result.Error is null, text + "\n" + result.Error);
            return result;
        }
        public PreparedInput Prepare(string text) => Pump(Session.PrepareAsync(text, Token));
        public IReadOnlyList<CompletionItem> Items(string text)
        {
            IPromptCallbacks callbacks = new ReplPromptCallbacks(Session);
            var span = Pump(callbacks.GetSpanToReplaceByCompletionAsync(text, text.Length, Token));
            return Pump(callbacks.GetCompletionItemsAsync(text, text.Length, span, Token));
        }
        public string Read(string text, params ConsoleKeyInfo[] keys)
        {
            var typed = text.Select(c => new ConsoleKeyInfo(c, char.IsLetterOrDigit(c) ? (ConsoleKey)char.ToUpperInvariant(c) : ConsoleKey.NoName, false, false, false));
            return Pump(Task.Run(async () =>
            {
                await using var prompt = new Prompt(callbacks: new ReplPromptCallbacks(Session), console: new PromptConsole(typed.Concat(keys)),
                    configuration: ReplHost.CreatePromptConfiguration(mode: Session.Mode));
                return (await prompt.ReadLineAsync()).Text;
            }));
        }
        public void Dispose()
        {
            Session.Dispose();
            Globals.world.Dispose();
            dispatcher.Dispose();
            engine.Dispose();
            Assert.Empty(engine.Errors.Drain());
        }
    }
}

public class ModeInputTests
{
    [Theory]
    [InlineData("auto", "godot>")]
    [InlineData("sh", "godot:sh>")]
    [InlineData("cs", "godot:cs>")]
    [InlineData("ai", "godot:ai>")]
    public void PromptShowsModeAndKeepsPathColorSpansInBounds(string command, string expected)
    {
        Assert.True(ReplModes.TryParse(":" + command, out var mode));
        Assert.Equal(expected + " ", ReplHost.CreatePromptConfiguration(mode: mode).Prompt.Text);
        foreach (var path in new[] { "/root", "/root/Control", "/root/LongName/LongerName/VeryLongName" })
        {
            var prompt = ReplHost.CreatePromptText(path, 60, null, color: true, mode);
            Assert.All(prompt.FormatSpans.ToArray(), span => Assert.InRange(span.End, 0, prompt.Length));
            Assert.Empty(ReplHost.CreatePromptText(path, 60, null, color: false, mode).FormatSpans.ToArray());
        }
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("cs")]
    public async Task RedirectedCSharpKeepsIncompleteSubmissionsTogether(string command)
    {
        Assert.True(ReplModes.TryParse(":" + command, out var mode));
        using var input = new StringReader("if (true) {\nroot.PrintTreePretty();\n}\n42\n");
        var text = await ReplHost.ReadSubmissionAsync(input, TestContext.Current.CancellationToken, mode);
        Assert.Contains("root.PrintTreePretty();", text);
        Assert.EndsWith("}", text!.TrimEnd());
        Assert.Equal("42", (await ReplHost.ReadSubmissionAsync(input, TestContext.Current.CancellationToken, mode))!.Trim());
    }

    [Theory]
    [InlineData("sh")]
    [InlineData("ai")]
    public async Task RedirectedShellAndAgentInputIsLineOriented(string command)
    {
        Assert.True(ReplModes.TryParse(":" + command, out var mode));
        using var input = new StringReader("if (true) {\nsecond line\n");
        Assert.Equal("if (true) {", (await ReplHost.ReadSubmissionAsync(input, TestContext.Current.CancellationToken, mode))!.Trim());
        Assert.Equal("second line", (await ReplHost.ReadSubmissionAsync(input, TestContext.Current.CancellationToken, mode))!.Trim());
    }
}

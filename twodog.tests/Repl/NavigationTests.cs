using System.Diagnostics;
using PrettyPrompt;
using PrettyPrompt.Highlighting;
using twodog.Repl;
using twodog.tests.EngineTests;

namespace twodog.tests.ReplTests;

[Collection(nameof(EngineRestartCollection))]
public class NavigationIntegrationTests
{
    [Fact]
    public void NavigationScopesEvaluationListingAndTerminalCompletionWithoutLosingCSharpState()
    {
        var game = typeof(showcase.CSharpTicker).Assembly;
        using var engine = new Engine("repl-navigation", Engine.ResolveProjectDir(), "--headless") { CaptureErrors = true };
        engine.Start();
        Assert.False(engine.Iteration());
        using var dispatcher = new EngineDispatcher();
        var globals = new ReplGlobals(engine);
        using var scratch = globals.world;
        using var session = new ReplSession(globals, [game], dispatcher);
        var token = TestContext.Current.CancellationToken;
        T Pump<T>(Task<T> task)
        {
            var watch = Stopwatch.StartNew();
            while (!task.IsCompleted)
            {
                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), "Navigation stopped making progress");
                Assert.False(engine.Iteration());
                dispatcher.Pump();
                Thread.Sleep(1);
            }
            return task.GetAwaiter().GetResult();
        }
        ReplResult Eval(string text) => Pump(Task.Run(() => session.EvaluateAsync(text, token)));
        ReplResult Success(string text)
        {
            var result = Eval(text);
            Assert.Null(result.Error);
            return result;
        }
        string Scope() => Pump(Task.Run(() => session.ScopePathAsync(token)));
        var scopeFromOwner = "/root";
        string Complete(string text, params ConsoleKeyInfo[] keys) => ReadPrompt(text, false, keys);
        string ReadPrompt(string text, bool buffered, params ConsoleKeyInfo[] keys)
        {
            var console = new PromptConsole(text.Select(c => new ConsoleKeyInfo(c,
                char.IsLetterOrDigit(c) ? (ConsoleKey)char.ToUpperInvariant(c) : ConsoleKey.NoName,
                false, false, false)).Concat(keys), buffered: buffered);
            return Pump(Task.Run(async () =>
            {
                await using var prompt = new Prompt(callbacks: new ReplPromptCallbacks(session), console: console,
                    configuration: ReplHost.CreatePromptConfiguration(scopePath: scopeFromOwner));
                return (await prompt.ReadLineAsync()).Text;
            }));
        }
        var tab = new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false);
        var run = new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, true);
        Assert.Equal("cd $Control", Complete("cd $Cont", tab, run));
        Assert.Equal(":cd $Control", Complete(":cd $Cont", tab, run));
        Assert.Equal("cd $Control", Complete("cd ", tab, tab, run));

        var left = new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, false, false, false);
        var leftFive = Enumerable.Repeat(left, 5).ToArray();
        Assert.Equal("ls $Control/Flair", ReadPrompt("ls $ControlFlair", true, [.. leftFive, tab, run]));
        Assert.Equal("ls $Control/CenterContainerFlair", ReadPrompt("ls $ContFlair", true, [.. leftFive, tab, tab, tab, run]));

        Success("int retainedAnswer = 42;");
        Success("var x = $Control/CenterContainer; var selectedContainer = x;");
        var variableScope = Success("cd x");
        Assert.False(variableScope.Committed);
        Assert.Equal("/root/Control/CenterContainer", variableScope.Value);
        Assert.Equal("42", Success("retainedAnswer").Value);
        Assert.Equal("cd selectedContainer", Complete("cd selectedCont", tab, run));
        Assert.Equal(":cd selectedContainer", Complete(":cd selectedCont", tab, run));
        Assert.Equal("cd x.GetParent()", Complete("cd x.GetPa", tab, run));
        Assert.Equal(":cd x.GetParent()", Complete(":cd x.GetPa", tab, run));
        Assert.Equal("cd x.", ReadPrompt("cd x", true, tab, run));
        Assert.Equal("/root/Control", Success("cd x.GetParent()").Value);
        Assert.Equal("/root/Control/CenterContainer", Success(":cd x;").Value);
        Assert.Equal("/root/Control", Success("cd scene").Value);
        Assert.Equal("/root", Success("cd $CenterContainer.GetParent().GetParent()").Value);
        Assert.Equal("/root/Control", Success(":cd (scene ?? root)").Value);
        Assert.Equal("/root", Success("cd root").Value);

        Success("int scopeCalls = 0; int scopeThread = System.Environment.CurrentManagedThreadId; " +
            "Node PickScope() { scopeCalls++; if (System.Environment.CurrentManagedThreadId != scopeThread) throw new Exception(\"Wrong thread\"); return x; } " +
            "async Task<Node> AwaitScope() { await Task.Delay(10, ct); return PickScope(); }");
        Assert.Equal("/root/Control/CenterContainer", Success("cd PickScope()").Value);
        Assert.Equal("1", Success("scopeCalls").Value);
        Assert.Equal("/root/Control/CenterContainer", Success("cd await AwaitScope()").Value);
        Assert.Equal("2", Success("scopeCalls").Value);
        Assert.Contains("Usage:", Eval("cd PickScope(); throw new Exception(\"must not run\");").Error!);
        Assert.Equal("2", Success("scopeCalls").Value);
        Success("Node FailScope() { scopeCalls++; throw new InvalidOperationException(\"navigation failure\"); } " +
            "Node QueueScope() { var node = new Node(); root.AddChild(node); node.QueueFree(); return node; }");
        var beforeFailure = Scope();
        Assert.Contains("navigation failure", Eval("cd FailScope()").Error!);
        Assert.Equal("3", Success("scopeCalls").Value);
        Assert.Contains("live node", Eval("cd QueueScope()").Error!);
        Assert.Contains("Godot.Node", Eval("cd 42").Error!);
        Assert.Contains("returned null", Eval("cd null").Error!);
        Assert.Contains("(1,4): CS0103", Eval("cd missingScopeVariable").Error!);
        Assert.Equal(beforeFailure, Scope());
        Success("var detachedScope = new Node();");
        Assert.Contains("live node", Eval("cd detachedScope").Error!);
        Success("detachedScope.Free();");
        Assert.Contains("live node", Eval("cd detachedScope").Error!);
        Assert.Equal(beforeFailure, Scope());
        Success("cd root");
        var changed = Success("cd $Control/Signals/Table/");
        Assert.False(changed.Committed);
        scopeFromOwner = Scope();
        Assert.Equal("/root/Control/Signals/Table", scopeFromOwner);
        Assert.Equal(scopeFromOwner, changed.Value);
        Assert.Same(globals.root.GetNode("Control/Signals/Table"), globals.here);
        Assert.Equal(globals.here.GetTreeStringPretty(), Success("ls").Tree);
        Assert.DoesNotContain("CenterContainer", Success("ls").Tree!);
        Assert.Equal("42", Success("retainedAnswer").Value);
        Assert.Equal("true", Success("$EngineTimerName is Label").Value);
        Assert.Equal("true", Success("$/root/Control/Signals/Sources/CSharpTicker is showcase.CSharpTicker").Value);
        Assert.Equal("cd $EngineTimerName", Complete("cd $EngineTimerNa", tab, run));
        Assert.Equal("ls $EngineTimerNameFlair", Complete("ls $EngineTimerNaFlair", [.. leftFive, tab, run]));
        Assert.Equal("ls $/root/ControlFlair", Complete("ls $/root/ContFlair", [.. leftFive, tab, run]));
        Assert.Equal(":cd $EngineTimerName", Complete(":cd $EngineTimerNa", tab, run));
        Assert.Equal("$/root/Control/CenterContainer/GDScriptLinkerProbe", Complete("?GDScriptL", tab, run));
        Assert.Equal("ls $EngineTimerName", Complete("ls $EngineTimerNa", tab, run));
        Assert.Equal("$EngineTimerName", ReadPrompt("$EngineTimerNa", true, tab, tab, run));
        Assert.Equal("cd $/root/Control/CenterContainer/GDScriptLinkerProbe", Complete("cd ?GDScriptL", tab, run));
        Assert.Equal("$/root/Control/Signals/Table/", Complete("?Table", tab, run));
        Assert.Equal("$/root/Control/Signals/Table/EngineTimerName", Complete("$/root/Control/Signals/Table/EngineTimerNa", tab, run));
        Assert.Equal("$/root/Control/Signals/Table/EngineTimerName", Complete("$", [tab,
            .. "/root/Control/Signals/Table/EngineTimerNa".Select(c => new ConsoleKeyInfo(c, ConsoleKey.NoName, false, false, false)),
            tab, run]));
        Assert.Equal(scopeFromOwner, Success("pwd").Value);
        Assert.Equal(scopeFromOwner, Success(":pwd").Value);
        Assert.Contains("not found", Eval("cd $Missing").Error!);
        Assert.Equal(scopeFromOwner, Scope());
        Assert.Equal("/root/Control/Signals", Success("cd ..").Value);
        Assert.Equal("/root", Success("cd /").Value);
        Assert.Equal("/root", Success("cd ..").Value);
        Assert.Equal("/root/Control", Success(":cd /root/Control").Value);
        Assert.Equal("/root", Success("cd").Value);

        Success("var scopeNode = new Node { Name = \"ReplScope\" }; root.AddChild(scopeNode);");
        Success("cd $ReplScope");
        Success("here.Name = \"RenamedScope\";");
        Assert.Equal("/root/RenamedScope", Scope());
        Success("root.RemoveChild(here);");
        Assert.Equal("/root", Scope());
        Success("root.AddChild(scopeNode);");
        Assert.Equal("/root", Scope());
        Success("cd $RenamedScope");
        Success("here.QueueFree();");
        Assert.Equal("/root", Scope());
        Assert.Same(globals.root, globals.here);

        Success("Func<Node, Node> cd = node => node;");
        Assert.Equal("true", Success("cd (root) is Window").Value);
        Assert.Equal("true", Success("cd ($Control) is Control").Value);
        Success("cd (root); int ordinaryCdAnswer = 8;");
        Assert.Equal("8", Success("ordinaryCdAnswer").Value);
        Assert.Equal("/root", Success("cd root").Value);
        Success("int cd = 6; int pwd = 7;");
        Assert.Equal("6", Success("cd").Value);
        Assert.Equal("7", Success("pwd").Value);
        Assert.Equal("3", Success("cd / 2").Value);
        Assert.Equal("true", Success("cd > 0 ? true : false").Value);
        Assert.True(Success("cd ..").Committed);
        Assert.Equal("/root/Control", Success("cd $Control").Value);
        Assert.Equal("/root", Success(":cd").Value);
        Assert.Equal("/root", Success(":pwd").Value);
        // Families come from the live node, including an internal node and C# script subclasses.
        Success("var familyRoot = new Control { Name = \"FamilyRoot\" }; root.AddChild(familyRoot); " +
            "familyRoot.AddChild(new Node2D { Name = \"BlueFamily\" }); " +
            "familyRoot.AddChild(new Node3D { Name = \"RedFamily\" }); " +
            "familyRoot.AddChild(new AnimationPlayer { Name = \"AnimationFamily\" }); " +
            "familyRoot.AddChild(new Label { Name = \"HiddenFamily\" }, @internal: Node.InternalMode.Back);");
        var familyListing = Success("ls $FamilyRoot");
        Assert.Equal(globals.root.GetNode("FamilyRoot").GetTreeStringPretty(), familyListing.Tree);
        var formatted = familyListing.Styled!.Value;
        AnsiColor? At(FormattedString value, int position) => value.FormatSpans.ToArray().Single(s => s.Contains(position)).Formatting.Foreground;
        Assert.Equal(NodeColors.Color(NodeFamily.Control), At(formatted, formatted.Text!.IndexOf("FamilyRoot", StringComparison.Ordinal)));
        Assert.Equal(NodeColors.Color(NodeFamily.Node2D), At(formatted, formatted.Text.IndexOf("BlueFamily", StringComparison.Ordinal)));
        Assert.Equal(NodeColors.Color(NodeFamily.Node3D), At(formatted, formatted.Text.IndexOf("RedFamily", StringComparison.Ordinal)));
        Assert.Equal(NodeColors.Color(NodeFamily.Animation), At(formatted, formatted.Text.IndexOf("AnimationFamily", StringComparison.Ordinal)));
        Assert.Equal(NodeColors.Color(NodeFamily.Control), At(formatted, formatted.Text.IndexOf("HiddenFamily", StringComparison.Ordinal)));
        var callback = (IPromptCallbacks)new ReplPromptCallbacks(session);
        var coloredPath = "$FamilyRoot/RedFamily";
        var highlighting = Pump(Task.Run(() => callback.HighlightCallbackAsync(coloredPath, token))).ToArray();
        Assert.Equal(NodeColors.Color(NodeFamily.Control), highlighting.Single(s => s.Contains(2)).Formatting.Foreground);
        Assert.Equal(NodeColors.Grey, highlighting.Single(s => s.Contains(coloredPath.IndexOf('/'))).Formatting.Foreground);
        Assert.Equal(NodeColors.Color(NodeFamily.Node3D), highlighting.Single(s => s.Contains(coloredPath.Length - 1)).Formatting.Foreground);
        var span = Pump(Task.Run(() => callback.GetSpanToReplaceByCompletionAsync(coloredPath, coloredPath.Length, token)));
        var choices = Pump(Task.Run(() => callback.GetCompletionItemsAsync(coloredPath, coloredPath.Length, span, token)));
        var redChoice = choices.Single(c => c.DisplayText == coloredPath).DisplayTextFormatted;
        Assert.Equal(NodeColors.Color(NodeFamily.Node3D), At(redChoice, redChoice.Length - 1));
        var changedFamily = Success("cd $FamilyRoot/RedFamily");
        Assert.Equal(NodeColors.Color(NodeFamily.Node3D), At(changedFamily.Styled!.Value, changedFamily.Value!.Length - 1));
        var capturedScope = Pump(Task.Run(() => session.ScopeStyleAsync(token)));
        var promptStyle = ReplHost.CreatePromptConfiguration(scopePath: capturedScope.Path, terminalWidth: 80, scopeFamilies: capturedScope.Families).Prompt;
        if (PromptConfiguration.HasUserOptedOutFromColor) Assert.Empty(promptStyle.FormatSpans.ToArray());
        else
        {
            Assert.Equal(NodeColors.Color(NodeFamily.Node3D), At(promptStyle, 1));
            Assert.Equal(NodeColors.Color(NodeFamily.Node3D), At(promptStyle, promptStyle.Text!.LastIndexOf(']') - 1));
        }
        Assert.DoesNotContain("\x1b", NodeColors.Ansi(formatted, color: false), StringComparison.Ordinal);
        Assert.Contains("\x1b[38;2;252;127;127mRedFamily", NodeColors.Ansi(formatted, color: true), StringComparison.Ordinal);
        Success("cd /");
        Success("familyRoot.QueueFree();");
        Assert.Empty(engine.Errors.Drain());
    }
}

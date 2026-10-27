using System.Diagnostics;
using twodog.Repl;
using twodog.tests.EngineTests;

namespace twodog.tests.ReplTests;

[Collection(nameof(EngineRestartCollection))]
public class ReplIntegrationTests
{
    [Fact]
    public void LiveGameStatePersistsAndAwaitResumesOnGodotThreadWhileFramesAdvance()
    {
        // Eagerly load the game exactly as the scaffold does, before Godot starts.
        var game = typeof(showcase.CSharpTicker).Assembly;
        using var engine = new Engine("repl-integration", Engine.ResolveProjectDir(), "--headless") { CaptureErrors = true };
        engine.Start();
        Assert.True(engine.Update());
        using var dispatcher = new EngineDispatcher();
        var globals = new ReplGlobals(engine);
        using var scratch = globals.world;
        using var session = new ReplSession(globals, [game], dispatcher);

        var testCancellation = TestContext.Current.CancellationToken;
        T Pump<T>(Task<T> task, Action? perFrame = null)
        {
            var watch = Stopwatch.StartNew();
            while (!task.IsCompleted)
            {
                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), "REPL submission stopped making progress");
                perFrame?.Invoke();
                Assert.True(engine.Update());
                dispatcher.Pump();
                Thread.Sleep(1);
            }
            return task.GetAwaiter().GetResult();
        }
        ReplResult Eval(string text) => Pump(Task.Run(() => session.EvaluateAsync(text, testCancellation)));
        ReplResult Success(string text)
        {
            var result = Eval(text);
            Assert.Null(result.Error);
            Assert.True(result.Committed);
            return result;
        }

        // Warming language services must leave the user's submission chain untouched.
        Pump(Task.Run(async () =>
        {
            await new ReplLanguage(session).WarmUpAsync(testCancellation);
            return true;
        }));

        Success("var ownerThread = System.Environment.CurrentManagedThreadId; var startFrame = Godot.Engine.GetProcessFrames();");
#if REAL_T_IS_DOUBLE
        Assert.Equal("true", Success("typeof(Vector2).GetField(\"X\").FieldType == typeof(double) && new Vector2(1000000000.125, 0).X == 1000000000.125").Value);
#else
        Assert.Equal("true", Success("typeof(Vector2).GetField(\"X\").FieldType == typeof(float) && new Vector2(10.125f, 0).X == 10.125f").Value);
#endif
        Success("var madeNode = new Node { Name = \"ReplLive\" }; root.AddChild(madeNode);");
        Assert.Equal("\"ReplLive\"", Success("madeNode.Name.ToString()").Value);
        Assert.Equal("true", Success("await Task.Delay(50, ct); System.Environment.CurrentManagedThreadId == ownerThread && Godot.Engine.GetProcessFrames() > startFrame").Value);
        Assert.Equal("true", Success("await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame); System.Environment.CurrentManagedThreadId == ownerThread").Value);
        Assert.Null(Success("scene.GetNode<showcase.CSharpTicker>(\"Signals/Sources/CSharpTicker\").Tick();").Value);

        Assert.Equal("true", Success("$Control is Control").Value);
        Assert.Equal("true", Success("var typedTicker = $Control/Signals/Sources/CSharpTicker; typedTicker is showcase.CSharpTicker").Value);
        Assert.Equal("true", Success("$Control/Signals/Sources/CSharpTicker.Tick() > 0").Value);
        Success("var spaced = new Label { Name = \"My Node\" }; $Control.AddChild(spaced);");
        Assert.Equal("true", Success("$[\"Control/My Node\"] is Label").Value);
        Assert.Equal("\"$Control\"", Success("\"$Control\"").Value);
        Assert.Equal("\"Control\"", Success("$\"{$Control.Name}\"").Value);
        Assert.Equal("\"Control\"", Success("$\"Control\"").Value);
        var fullTree = Eval("ls");
        Assert.Null(fullTree.Error);
        Assert.False(fullTree.Committed);
        Assert.Equal(globals.root.GetTreeStringPretty(), fullTree.Tree);
        var subtree = Eval("ls $Control");
        Assert.Null(subtree.Error);
        Assert.Equal(globals.root.GetNode("Control").GetTreeStringPretty(), subtree.Tree);
        Assert.DoesNotContain("ReplLive", subtree.Tree!);
        Assert.Contains("My Node", Eval("ls $[\"Control/My Node\"]").Tree!);
        Assert.Contains("case-sensitive", Eval("ls $control").Error!);
        Assert.Contains("Usage:", Eval("ls $Control; throw new Exception(\"must not run\");").Error!);
        Assert.Equal("\"ReplLive\"", Success("madeNode.Name.ToString()").Value);

        var missingNode = Eval("$MissingNode");
        Assert.False(missingNode.Committed);
        Assert.Contains("was not found", missingNode.Error!);

        var scriptPath = Path.Combine(Path.GetTempPath(), $"2dog-repl-{Guid.NewGuid():N}.csx");
        try
        {
            File.WriteAllText(scriptPath, "int loadedAnswer = 40;");
            Assert.Equal("42", Success($"#load {System.Text.Json.JsonSerializer.Serialize(scriptPath)}\nloadedAnswer + 2").Value);
            Assert.Equal("40", Success("loadedAnswer").Value);
        }
        finally { File.Delete(scriptPath); }

        var callbacks = new ReplLanguage(session);
        const string text = "madeNode.Na";
        var span = Pump(Task.Run(() => callbacks.CompletionRangeAsync(text, text.Length, testCancellation)));
        var items = Pump(Task.Run(() => callbacks.CompletionsAsync(text, text.Length, span, testCancellation)));
        Assert.Contains(items, item => item.DisplayText == "Name");
        var selected = items.First(item => item.DisplayText == "Name");
        var edit = Pump(Task.Run(() => callbacks.CommitAsync(selected, text, text.Length, CommitKey.Tab, testCancellation)));
        Assert.Equal("Name", edit.NewText);
        // The same menu item survives extra typing; accepting must replace the new filter.
        const string filtered = "madeNode.Name";
        var filteredEdit = Pump(Task.Run(() => callbacks.CommitAsync(selected, filtered, filtered.Length, CommitKey.Tab, testCancellation)));
        Assert.Equal(4, filteredEdit.Range.Length);
        Assert.Equal("Name", filteredEdit.NewText);
        var commandSpan = Pump(Task.Run(() => callbacks.CompletionRangeAsync(":he", 3, testCancellation)));
        Assert.Equal(0, commandSpan.Start);
        Assert.Equal(3, commandSpan.Length);
        Assert.NotEmpty(Pump(Task.Run(() => selected.DescriptionAsync(testCancellation))).Text!);
        Assert.NotEmpty(Pump(Task.Run(() => callbacks.HighlightAsync("var value = 123;", testCancellation))));

        string ReadPrompt(string input, bool multiline, params ConsoleKeyInfo[] extraKeys)
        {
            var keys = input.Select(c => new ConsoleKeyInfo(c,
                c switch
                {
                    '$' => ConsoleKey.D4, '.' => ConsoleKey.OemPeriod, '/' => ConsoleKey.Oem2,
                    '(' => ConsoleKey.D9, ')' => ConsoleKey.D0, '"' => ConsoleKey.Oem7,
                    '[' => ConsoleKey.Oem4, ']' => ConsoleKey.Oem6, ' ' => ConsoleKey.Spacebar,
                    >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' => (ConsoleKey)char.ToUpperInvariant(c),
                    _ => ConsoleKey.NoName,
                },
                false, false, false)).Concat(extraKeys);
            return Pump(Task.Run(() => EditorDriver.ReadAsync(new ReplLanguage(session), keys, multiline, testCancellation)));
        }
        var pastedCode = "if (true)\n{\n    GD.Print(42);\n}";
        var pasteKeys = ("\x1b[200~" + pastedCode + "\x1b[201~").Select(c => new ConsoleKeyInfo(c,
            c == '\x1b' ? ConsoleKey.Escape : ConsoleKey.NoName, false, false, false))
            .Append(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        var pasted = Pump(Task.Run(() => EditorDriver.ReadQueuedAsync(new ReplLanguage(session), pasteKeys, windows: false)));
        Assert.Equal(pastedCode.Replace("\n", Environment.NewLine), pasted);

        var tab = new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false);
        var enter = new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false);
        var shiftEnter = new ConsoleKeyInfo('\r', ConsoleKey.Enter, true, false, false);
        var escape = new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false);
        // One Enter must submit an exact command rather than accept a suggestion.
        foreach (var command in new[] { "ls", "cd", "pwd", "rm", "cp", "mv", "exit", ":cd", ":pwd", ":rm", ":cp", ":mv", ":help", ":clear", ":reset", ":multiline", ":exit" })
        {
            Assert.Equal(command, ReadPrompt(command, false, enter));
            Assert.Equal(command, ReadPrompt(command, true, enter));
        }
        Success("int lsOther = 1; int cdOther = 2; int pwdOther = 3;");
        var down = new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false);
        foreach (var command in new[] { "ls", "cd", "pwd" })
            Assert.Equal(command, ReadPrompt(command, false, down, enter));
        Assert.Equal("ls", ReadPrompt("l", false, enter));
        Assert.Equal("ls", ReadPrompt("l", true, enter));
        Assert.Equal("cd", ReadPrompt("c", false, enter));
        Assert.Equal(":help", ReadPrompt(":he", false, enter));
        Assert.Equal("$Control", ReadPrompt("$Cont", false, tab, enter));
        Assert.Equal("ls $Control", ReadPrompt("ls $cont", false, tab, enter));
        Assert.Equal("ls $Control", ReadPrompt("ls ", false, tab, tab, enter));
        Assert.Equal("$Control/EnterVrButton", ReadPrompt("$Control/", false,
            new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false), tab, enter));
        Assert.Equal("root.Size", ReadPrompt("root.Si", false, escape, tab, tab, enter));
        // Method completion opens the call immediately; the next character lands inside its parentheses.
        var quote = new ConsoleKeyInfo('"', ConsoleKey.Oem7, true, false, false);
        Assert.Equal("world.Open(\"\")", ReadPrompt("world.Op", false, tab, quote, quote, enter));
        var closeParen = new ConsoleKeyInfo(')', ConsoleKey.D0, true, false, false);
        Assert.Equal("world.Open(\"\")", ReadPrompt("world.Op", false, tab, quote, quote, closeParen, enter));
        Assert.Equal("world.Open()", ReadPrompt("world.Open", false, tab, enter));
        Assert.Equal("world.Close()", ReadPrompt("world.Clo", false, tab, enter));
        Assert.Equal("world.Close()x", ReadPrompt("world.Clo", false, tab,
            new ConsoleKeyInfo('x', ConsoleKey.X, false, false, false), enter));
        Assert.Equal("world.GetType()", ReadPrompt("world.GetTy", false, tab, tab, escape, enter));
        Assert.Equal("new Node()", ReadPrompt("new Node", false, tab, enter));
        Assert.Equal("world.Open()", ReadPrompt("world.Op(", false, closeParen, enter));
        // Tab on a path that is already typed out moves on to its members, as it does without a menu.
        Assert.Equal("$[\"Control/My Node\"].", ReadPrompt("$[\"Control/My Node\"]", false, tab, escape, enter));
        Success("var dottedNode = new Node { Name = \"Repl(Child)\" }; root.AddChild(dottedNode);");
        Assert.Equal("$[\"Repl(Child)\"]", ReadPrompt("$[\"Repl(Child)\"]", false, enter, enter));
        Assert.Equal("$[\"Repl.Dot\"]", ReadPrompt("$[\"Repl.Dot\"]", false, enter));
        Success("dottedNode.QueueFree();");
        Assert.Equal("$Control/Signals/Sources/CSharpTicker.Tick()", ReadPrompt("$Control/Signals/Sources/CSharpTicker.Ti", false, tab, enter));
        // Repeated Tab now cycles against the original prefix, including path descendants.
        Assert.Equal("world.Scene", ReadPrompt("world.Sce", false, tab, tab, escape, enter));
        Assert.Equal("$Control/CenterContainer", ReadPrompt("$Cont", false, tab, tab, escape, enter));
        Assert.Equal("ls $Control/CenterContainer", ReadPrompt("ls $Cont", false, tab, tab, escape, enter));
        Assert.Equal("world.Open()", ReadPrompt("world.Open", false, tab, enter));
        Assert.Equal("world.Close()x", ReadPrompt("world.Close", false, tab,
            new ConsoleKeyInfo('x', ConsoleKey.X, false, false, false), enter));
        Assert.Equal("Console.Write(world.Close())", ReadPrompt("Console.Write(world.Close)", false,
            new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, false, false, false), tab, enter));
        Assert.Equal("$Control", ReadPrompt("$Cont", false, enter, enter));
        Assert.Equal("$Control" + Environment.NewLine, ReadPrompt("$Cont", true, tab, enter, shiftEnter));
        Assert.Equal("$Control" + Environment.NewLine, ReadPrompt("$Cont", false, tab, shiftEnter, enter));

        var controlEnter = new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, true);
        // A trailing space opts into argument completion; Enter still accepts it.
        Assert.Equal("ls $Control", ReadPrompt("ls ", false, tab, enter, controlEnter));
        Assert.Equal("cd $Control", ReadPrompt("cd ", false, tab, enter, controlEnter));
        Assert.Equal("cd $Control", ReadPrompt("cd ", true, tab, enter, controlEnter));
        Assert.Equal("cd" + Environment.NewLine, ReadPrompt("cd", false, shiftEnter, controlEnter));
        var shiftTab = new ConsoleKeyInfo('\t', ConsoleKey.Tab, true, false, false);
        var backspace = new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false);
        var shiftBackspace = new ConsoleKeyInfo('\b', ConsoleKey.Backspace, true, false, false);
        var left = new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, false, false, false);
        var leftFive = Enumerable.Repeat(left, 5).ToArray();
        Assert.Equal("ls $ControlFlair", ReadPrompt("ls $ContFlair", false, [.. leftFive, tab, controlEnter]));
        Assert.Equal("ls $Control/CenterContainerFlair", ReadPrompt("ls $ContFlair", false, [.. leftFive, tab, tab, controlEnter]));
        Assert.Equal("ls $ControlFlair", ReadPrompt("ls $ContFlair", false, [.. leftFive, tab, tab, shiftTab, controlEnter]));
        Assert.Equal("ls $ContFlair", ReadPrompt("ls $ContFlair", false, [.. leftFive, tab, tab, backspace, controlEnter]));
        Assert.Equal("$[\"Control/My NodeFlair\"]", ReadPrompt("$[\"Control/My NFlair\"]", false,
            [.. Enumerable.Repeat(left, 7), tab, controlEnter]));
        Assert.Equal("$[\"Control/My Node\"]", ReadPrompt("$[\"Control/My Node\"]", false, left, tab, controlEnter));
        Assert.Equal("$[\"Control/My NodeFlair\"]", ReadPrompt("$Control/MyFlair", false, [.. leftFive, tab, controlEnter]));
        Assert.Equal("$Control/MyFlair", ReadPrompt("$Control/MyFlair", false, [.. leftFive, tab, backspace, controlEnter]));
        Assert.Equal("world.", ReadPrompt("world.NoSuchMember", false, shiftBackspace, controlEnter));
        Assert.Equal("$Control/", ReadPrompt("$Control/NoSuchChild", false, shiftBackspace, controlEnter));
        Assert.Equal("alpha ", ReadPrompt("alpha beta", false, shiftBackspace, controlEnter));
        Assert.Equal("", ReadPrompt("", false, shiftBackspace, controlEnter));
        Assert.Equal("alpha beta", ReadPrompt("alpha beta", false, shiftBackspace,
            new ConsoleKeyInfo('\x1a', ConsoleKey.Z, false, false, true), controlEnter));
        Assert.Equal("world.", ReadPrompt("world.Scene", false,
            new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, true, false, true), shiftBackspace, controlEnter));
        Assert.Equal("$", ReadPrompt("$Cont", false, tab, shiftBackspace, controlEnter));
        Assert.Equal("world.Open()", ReadPrompt("world.Op", false, enter, enter));
        Assert.Equal("$Cont", ReadPrompt("$Cont", false, controlEnter));
        Assert.Equal("$Cont" + Environment.NewLine, ReadPrompt("$Cont", false, shiftEnter, controlEnter));
        Assert.Equal("NoSuchCompletion", ReadPrompt("NoSuchCompletion", false, enter));
        Assert.Equal("world.Clear()", ReadPrompt("world.Cl", false, tab, controlEnter));
        Assert.Equal("world.Close()", ReadPrompt("world.Cl", false, tab, tab, controlEnter));
        Assert.Equal("world.Clear()", ReadPrompt("world.Cl", false, tab, tab, shiftTab, controlEnter));
        Assert.Equal("world.Close()", ReadPrompt("world.Cl", false, tab, shiftTab, controlEnter));
        Assert.Equal("world.Cl", ReadPrompt("world.Cl", false, tab, tab, backspace, controlEnter));
        Assert.Equal("world.Op", ReadPrompt("world.Op", false, enter, backspace, controlEnter));
        Assert.Equal("world.Scene.", ReadPrompt("world.Sce", false, tab,
            new ConsoleKeyInfo('.', ConsoleKey.OemPeriod, false, false, false), escape, controlEnter));
        Assert.Equal("world.Scene", ReadPrompt("world.Sce", false, tab,
            new ConsoleKeyInfo('.', ConsoleKey.OemPeriod, false, false, false), backspace, escape, controlEnter));

        Success("var prefixAlpha = 1; var prefixAlphabet = 2;");
        Assert.Equal("prefixAlphabet", ReadPrompt("prefixAl", false, tab,
            new ConsoleKeyInfo('b', ConsoleKey.B, false, false, false), tab, controlEnter));
        Assert.Equal("prefixAlphab", ReadPrompt("prefixAl", false, tab,
            new ConsoleKeyInfo('b', ConsoleKey.B, false, false, false), tab, backspace, controlEnter));

        Success("var searchParent = new Node { Name = \"ReplSearch\" }; root.AddChild(searchParent); " +
            "searchParent.AddChild(new Node { Name = \"CycleEngineTimerInCSharp\" }); " +
            "searchParent.AddChild(new Node { Name = \"CycleEngineTimerInGDScript\" }); " +
            "var searchBranch = new Control { Name = \"CycleEngineTimerName\" }; searchParent.AddChild(searchBranch); " +
            "searchBranch.AddChild(new Node { Name = \"Leaf\" }); " +
            "searchParent.AddChild(new Node { Name = \"CycleEngineTimer With Space\" });");
        foreach (var decorator in new[] { "$", "?" })
        {
            Assert.Equal("$[\"ReplSearch/CycleEngineTimer With Space\"]", ReadPrompt(decorator + "CycleEngineT", false, tab, controlEnter));
            Assert.Equal("$ReplSearch/CycleEngineTimerInCSharp", ReadPrompt(decorator + "CycleEngineT", false, tab, tab, controlEnter));
            Assert.Equal("$ReplSearch/CycleEngineTimerInGDScript", ReadPrompt(decorator + "CycleEngineT", false, tab, tab, tab, controlEnter));
            Assert.Equal("$ReplSearch/CycleEngineTimerName/", ReadPrompt(decorator + "CycleEngineT", false, tab, shiftTab, controlEnter));
            Assert.Equal(decorator + "CycleEngineT", ReadPrompt(decorator + "CycleEngineT", false, tab, tab, backspace, controlEnter));
            Assert.Equal("$ReplSearch/CycleEngineTimerInCSharp", ReadPrompt(decorator + "cycleenginetimerinc", false, enter, controlEnter));
            Assert.Equal("ls $ReplSearch/CycleEngineTimerName/", ReadPrompt("ls " + decorator + "CycleEngineTimerNa", false, tab, controlEnter));
            Assert.Equal("$ReplSearch/CycleEngineTimerName/Leaf", ReadPrompt(decorator + "CycleEngineTimerNa", false, tab,
                new ConsoleKeyInfo('L', ConsoleKey.L, true, false, false), tab, controlEnter));
        }
        Assert.Equal("true", Success("$ReplSearch/CycleEngineTimerName/ is Control").Value);
        Assert.Contains("for completion", Eval("?CycleEngineT").Error!);
        Assert.Contains("Press Tab", Eval("ls ?CycleEngineT").Error!);
        Assert.Equal("$Control/CenterContainer/GDScriptLinkerProbe", ReadPrompt("?GDScriptL", false, tab, controlEnter));
        Success("searchParent.QueueFree();");

        foreach (var finish in new[] { controlEnter, new ConsoleKeyInfo('\x03', ConsoleKey.C, false, false, true) })
        {
            var editor = new ReplEditor(new ReplLanguage(session));
            var nextText = Pump(Task.Run(async () =>
            {
                await EditorDriver.RunAsync(editor, "$Cont".Select(c => new ConsoleKeyInfo(c, (ConsoleKey)char.ToUpperInvariant(c), false, false, false)).Append(finish), testCancellation);
                return (await EditorDriver.RunAsync(editor, [tab, escape, enter], testCancellation)).Text;
            }));
            Assert.Equal("", nextText); // Tab opened suggestions rather than inserting indentation.
        }

        var existingCall = "world.Open(\"res://tool_test.tscn\")";
        var openSpan = Pump(Task.Run(() => callbacks.CompletionRangeAsync(existingCall, 10, testCancellation)));
        var openItems = Pump(Task.Run(() => callbacks.CompletionsAsync(existingCall, 10, openSpan, testCancellation)));
        var openEdit = Pump(Task.Run(() => callbacks.CommitAsync(openItems.First(i => i.DisplayText == "Open"), existingCall, 10, CommitKey.Tab, testCancellation)));
        Assert.Equal("Open", openEdit.NewText);
        Assert.Equal(11, openEdit.NewCaret);
        var overloads = Pump(Task.Run(() => callbacks.SignaturesAsync("world.Open()", 11, testCancellation)));
        Assert.Contains(overloads.Items, o => o.Signature.Text.Contains("Open(string path)", StringComparison.Ordinal));
        Assert.Equal(0, overloads.Argument);
        var godotOverloads = Pump(Task.Run(() => callbacks.SignaturesAsync("$Control.GetNode(\"nested,(comma)\", ", 35, testCancellation)));
        Assert.Equal(1, godotOverloads.Argument);

        const string listInput = "ls $Cont";
        var listSpan = Pump(Task.Run(() => callbacks.CompletionRangeAsync(listInput, listInput.Length, testCancellation)));
        Assert.Equal(3, listSpan.Start);
        Assert.Contains(Pump(Task.Run(() => callbacks.CompletionsAsync("l", 1, new TextRange(0, 1), testCancellation))),
            item => item.DisplayText == "ls");
        var listColors = Pump(Task.Run(() => callbacks.HighlightAsync(listInput, testCancellation)));
        Assert.Contains(listColors, color => color.Start == 0 && color.Length == 2);
        Assert.Contains(listColors, color => color.Start == 3 && color.Length == 1 && color.Style.Foreground == NodeColors.Grey);
        Assert.Contains(listColors, color => color.Start == 4 && color.Length == 4);

        const string typedInput = "$Control.Si";
        var typedSpan = Pump(Task.Run(() => callbacks.CompletionRangeAsync(typedInput, typedInput.Length, testCancellation)));
        var typedItems = Pump(Task.Run(() => callbacks.CompletionsAsync(typedInput, typedInput.Length, typedSpan, testCancellation)));
        var size = Assert.Single(typedItems, item => item.DisplayText == "Size");
        var sizeEdit = Pump(Task.Run(() => callbacks.CommitAsync(size, typedInput, typedInput.Length, CommitKey.Tab, testCancellation)));
        Assert.Equal(typedInput.IndexOf("Si", StringComparison.Ordinal), sizeEdit.Range.Start);
        Assert.Equal(2, sizeEdit.Range.Length);
        Assert.Equal("Size", sizeEdit.NewText);
        const string nodeInput = "$Control/Signals/Sources/";
        var nodeSpan = Pump(Task.Run(() => callbacks.CompletionRangeAsync(nodeInput, nodeInput.Length, testCancellation)));
        var nodeItems = Pump(Task.Run(() => callbacks.CompletionsAsync(nodeInput, nodeInput.Length, nodeSpan, testCancellation)));
        Assert.Contains(nodeItems, item => item.DisplayText == "$Control/Signals/Sources/CSharpTicker");
        // The terminal caches items from '$'; they must re-rank correctly as paths deepen.
        var initialSpan = Pump(Task.Run(() => callbacks.CompletionRangeAsync("$", 1, testCancellation)));
        var initialItems = Pump(Task.Run(() => callbacks.CompletionsAsync("$", 1, initialSpan, testCancellation)));
        var cachedTicker = Assert.Single(initialItems, item => item.DisplayText == "$Control/Signals/Sources/CSharpTicker");
        var cachedParent = Assert.Single(initialItems, item => item.DisplayText == "$Control");
        Assert.True(cachedTicker.Priority(nodeInput, nodeInput.Length, nodeSpan) >
            cachedParent.Priority(nodeInput, nodeInput.Length, nodeSpan));
        const string quotedInput = "$[\"Control/Signals/Sources/\"]";
        var quotedSpan = Pump(Task.Run(() => callbacks.CompletionRangeAsync(quotedInput, quotedInput.Length, testCancellation)));
        Assert.True(cachedTicker.Priority(quotedInput, quotedInput.Length, quotedSpan) >= 0);
        const string gameInput = "$Control/Signals/Sources/CSharpTicker.Ti";
        var gameSpan = Pump(Task.Run(() => callbacks.CompletionRangeAsync(gameInput, gameInput.Length, testCancellation)));
        Assert.Contains(Pump(Task.Run(() => callbacks.CompletionsAsync(gameInput, gameInput.Length, gameSpan, testCancellation))), item => item.DisplayText == "Tick");
        var colors = Pump(Task.Run(() => callbacks.HighlightAsync(typedInput, testCancellation)));
        Assert.Contains(colors, span => span.Start == 0 && span.Length == 1 && span.Style.Foreground == NodeColors.Grey);
        Assert.Contains(colors, span => span.Start == 1 && span.Length == "Control".Length && span.Style.Foreground == NodeColors.Color(NodeFamily.Control));
        Assert.All(colors, span => Assert.True(span.Start >= 0 && span.End <= typedInput.Length));
        var mappedFailure = Eval("$Control.NoSuchProperty");
        Assert.StartsWith("(1,10): CS1061:", mappedFailure.Error);

        Success("var gameScene = scene; var game2D = root.FindWorld2D(); var game3D = root.FindWorld3D();");
        Success("world.Open(\"res://tool_test.tscn\");");
        Assert.Equal("true", Success("ReferenceEquals(scene, gameScene) && world.Scene is showcase.ToolNode && world.Viewport != root").Value);
        Assert.Equal("true", Success("world.Viewport.FindWorld2D() != game2D && world.Viewport.FindWorld3D() != game3D").Value);
        Assert.Equal("true", Success("$ReplWorld/ToolNode.ReadyCalled").Value);
        Assert.Equal("true", Success("var scratchFrames = $ReplWorld/ToolNode.ProcessCount; await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame); await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame); $ReplWorld/ToolNode.ProcessCount > scratchFrames").Value);
        Success("var detachedScratch = world.Scene; detachedScratch.GetParent().RemoveChild(detachedScratch);");
        Assert.Equal("true", Success("detachedScratch.GetParent() is null").Value);
        Success("world.Clear();");
        Assert.Equal("\"Scratch\"", Success("world.Scene.Name.ToString()").Value);
        Assert.Equal("true", Success("!GodotObject.IsInstanceValid(detachedScratch) || detachedScratch.IsQueuedForDeletion()").Value);
        Success("var detachedEmpty = world.Scene; detachedEmpty.GetParent().RemoveChild(detachedEmpty);");
        Success("world.Open(\"res://tool_test.tscn\");");
        Assert.Equal("true", Success("world.Scene is showcase.ToolNode && world.Scene.GetParent() == world.Viewport").Value);
        Assert.Equal("true", Success("!GodotObject.IsInstanceValid(detachedEmpty) || detachedEmpty.IsQueuedForDeletion()").Value);
        Success("world.Close();");
        Assert.Equal("true", Success("world.Viewport is null && ReferenceEquals(scene, gameScene)").Value);

        var compileFailure = Eval("var broken = ;");
        Assert.False(compileFailure.Committed);
        Assert.NotNull(compileFailure.Error);
        Assert.Equal("\"ReplLive\"", Success("madeNode.Name.ToString()").Value);
        Assert.Contains("repl failure", Eval("int survives = 42; throw new Exception(\"repl failure\");").Error!);
        Assert.Equal("42", Success("survives").Value);
        Success("class BadDisplay { public override string ToString() => throw new Exception(\"bad display\"); }");
        Assert.Contains("Result formatting failed", Eval("var badDisplay = new BadDisplay(); badDisplay").Error!);
        Assert.Equal("true", Success("badDisplay is BadDisplay").Value);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(testCancellation);
        var previousSubmissionToken = globals.ct;
        var cancelled = Pump(Task.Run(() => session.EvaluateAsync("await Task.Delay(Timeout.Infinite, ct);", cancellation.Token)),
            () => { if (globals.ct != previousSubmissionToken) cancellation.Cancel(); });
        Assert.True(cancelled.Cancelled);
        Assert.Equal("42", Success("survives").Value);
        Success("madeNode.QueueFree(); spaced.QueueFree();");
        Success("tree.ChangeSceneToFile(\"res://tool_test.tscn\"); await tree.ToSignal(tree, SceneTree.SignalName.SceneChanged);");
        Assert.Equal("true", Success("scene is showcase.ToolNode && $ToolNode.ReadyCalled").Value);
        Assert.True(engine.Update());
        Assert.Empty(engine.Errors.Drain());
    }

    [Fact]
    public void DispatcherCancelsQueuedWorkOnShutdownWithoutExecutingIt()
    {
        var dispatcher = new EngineDispatcher();
        var called = false;
        var task = dispatcher.InvokeAsync(() => { called = true; return Task.FromResult(1); }, TestContext.Current.CancellationToken);
        dispatcher.Dispose();
        Assert.False(called);
        Assert.True(task.IsCanceled);
        Assert.True(dispatcher.InvokeAsync(() => Task.FromResult(2), TestContext.Current.CancellationToken).IsCanceled);
        dispatcher.Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InputModesKeepCompletionAndSubmitKeysDistinct(bool multiline)
    {
        var words = new FixedWords("alpha");
        Assert.Equal("a" + (multiline ? Environment.NewLine + "b" : ""), await EditorDriver.ReadAsync(words, [.. Keys.Type("a"), Keys.Escape, Keys.Enter, .. Keys.Type("b"), Keys.ShiftEnter], multiline, TestContext.Current.CancellationToken));
        Assert.Equal("a" + (multiline ? "" : Environment.NewLine + "b"), await EditorDriver.ReadAsync(words, [.. Keys.Type("a"), Keys.Escape, Keys.ShiftEnter, .. Keys.Type("b"), Keys.Enter], multiline, TestContext.Current.CancellationToken));
        Assert.Equal("a" + Environment.NewLine + "b", await EditorDriver.ReadAsync(words, [.. Keys.Type("a"), Keys.Escape, Keys.AltEnter, .. Keys.Type("b"), Keys.ControlEnter], multiline, TestContext.Current.CancellationToken));
        // Enter accepts an open menu's selection; Shift+Enter and Ctrl+Enter never do.
        Assert.Equal("alpha", await EditorDriver.ReadAsync(words, [.. Keys.Type("a"), Keys.Enter, Keys.ControlEnter], multiline, TestContext.Current.CancellationToken));
        Assert.Equal("a", await EditorDriver.ReadAsync(words, [.. Keys.Type("a"), Keys.ControlEnter], multiline, TestContext.Current.CancellationToken));
        Assert.Equal("alpha", await EditorDriver.ReadAsync(words, [.. Keys.Type("a"), Keys.Tab, Keys.ControlEnter], multiline, TestContext.Current.CancellationToken));
    }

    private sealed class FixedWords(params string[] words) : PlainLanguage
    {
        public override Task<IReadOnlyList<ReplCompletionItem>> CompletionsAsync(string text, int caret, TextRange range, CancellationToken token)
            => Task.FromResult<IReadOnlyList<ReplCompletionItem>>(words.Select(w => new ReplCompletionItem(w)).ToArray());
    }

    [Theory]
    [InlineData("var x = 1;", true)]
    [InlineData("1 + 2", true)]
    [InlineData("if (true) {", false)]
    [InlineData("class Example {", false)]
    public void MultilineDetectionUsesCSharpSyntax(string text, bool complete)
        => Assert.Equal(complete, ReplSession.IsComplete(text));
}

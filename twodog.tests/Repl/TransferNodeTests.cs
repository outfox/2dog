using System.Diagnostics;
using Godot;
using PrettyPrompt;
using twodog.Repl;
using twodog.tests.EngineTests;

namespace twodog.tests.ReplTests;

public class TransferCommandTests
{
    [Theory]
    [InlineData("cp $Source $Parent", "$Source", "$Parent")]
    [InlineData("  mv $[\"My Node\"] $/root/Parent/;  ", "$[\"My Node\"]", "$/root/Parent/")]
    [InlineData(":mv source parent", "source", "parent")]
    [InlineData("cp source.GetChild(0) parent", "source.GetChild(0)", "parent")]
    [InlineData("cp source.GetChild( 0 ) parent.GetChild( 1 )", "source.GetChild( 0 )", "parent.GetChild( 1 )")]
    [InlineData(":cp (source ?? here) (parent ?? root)", "(source ?? here)", "(parent ?? root)")]
    [InlineData("mv await PickAsync() root", "await PickAsync()", "root")]
    [InlineData("mv source nodes[0]", "source", "nodes[0]")]
    public void TwoNodeExpressionsHaveMappedOperands(string text, string source, string parent)
    {
        Assert.True(NavigationCommand.TryParse(text, out var command));
        Assert.Null(command.Error);
        Assert.True(command.IsTransfer);
        Assert.Equal(source, command.Expression);
        Assert.Equal(parent, text.Substring(command.DestinationSpan!.Value.Start, command.DestinationSpan.Value.Length));
        var input = new NodePathInput(text, new Dictionary<string, NodePathTarget>(), command.ExpressionSpan, command.DestinationSpan);
        Assert.Equal(command.ExpressionSpan!.Value.Start, input.ToOriginal(input.ToGenerated(command.ExpressionSpan.Value.Start)));
        Assert.Equal(command.DestinationSpan.Value.Start, input.ToOriginal(input.ToGenerated(command.DestinationSpan.Value.Start)));
        Assert.True(ReplSession.IsComplete(text));
    }

    [Theory]
    [InlineData("mv(root)")]
    [InlineData("mv (root)")]
    [InlineData("mv (")]
    [InlineData("mv.")]
    [InlineData("mv .Name")]
    [InlineData("mv .")]
    [InlineData("mv[0]")]
    [InlineData("mv [")]
    [InlineData("@mv")]
    [InlineData("@mv.Name")]
    [InlineData("mv = root;")]
    [InlineData("mv += 1;")]
    [InlineData("mv ?? root")]
    [InlineData("mv is Node")]
    [InlineData("mv ? here : root")]
    [InlineData("cp(root); root.GetChild(0);")]
    [InlineData("cp / 2")]
    [InlineData("cp;")]
    [InlineData("mv;")]
    [InlineData("  cp ;  ")]
    public void CommandNamesStillSupportOrdinaryCSharp(string text)
        => Assert.False(NavigationCommand.TryParse(text, out _));

    [Theory]
    [InlineData("cp;")]
    [InlineData("mv;")]
    public void ShellAndExplicitTransferFormsKeepCommandPrecedence(string text)
    {
        Assert.True(NavigationCommand.TryParse(text, out _, forceShell: true));
        Assert.True(NavigationCommand.TryParse(":" + text, out var explicitCommand));
        Assert.True(explicitCommand.Explicit);
    }

    [Theory]
    [InlineData("cp $Source $Parent extra")]
    [InlineData("mv source parent; root.QueueFree();")]
    [InlineData(":cp Pick() parent; throw new Exception();")]
    [InlineData("cp $Source $")]
    [InlineData("mv source")]
    public void MalformedTransfersCannotRunAnOperand(string text)
    {
        Assert.True(NavigationCommand.TryParse(text, out var command));
        Assert.StartsWith("Usage:", command.Error);
    }

    [Theory]
    [InlineData("cp ")]
    [InlineData(":mv ")]
    [InlineData("cp $Source ")]
    [InlineData("mv source.GetChild(0) ")]
    public void EitherEmptyOperandOffersNodeCompletion(string text)
    {
        Assert.True(NavigationCommand.TryParse(text, out var command));
        Assert.True(command.AwaitingTargetAt(text.Length));
        if (command.ExpressionSpan is { } first) Assert.False(command.AwaitingTargetAt(first.Start));
    }
}

[Collection(nameof(EngineRestartCollection))]
public class TransferNodeIntegrationTests
{
    [Fact]
    public void CopyAndMovePreserveCSharpAndNodeIdentityAndValidateBeforeMutation()
    {
        var game = typeof(showcase.CSharpTicker).Assembly;
        using var engine = new twodog.Engine("repl-transfer", twodog.Engine.ResolveProjectDir(), "--headless") { CaptureErrors = true };
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
                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), "Transfer stopped making progress");
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
            Assert.True(result.Error is null, text + System.Environment.NewLine + result.Error);
            return result;
        }
        string Complete(string text, string? suffix = null)
        {
            var keys = text.Select(c => new ConsoleKeyInfo(c, char.IsLetterOrDigit(c) ? (ConsoleKey)char.ToUpperInvariant(c) : ConsoleKey.NoName,
                false, false, false)).Concat([new('\t', ConsoleKey.Tab, false, false, false)]);
            if (suffix is not null) keys = keys.Concat(suffix.Select(c => new ConsoleKeyInfo(c,
                char.IsLetterOrDigit(c) ? (ConsoleKey)char.ToUpperInvariant(c) : ConsoleKey.NoName, false, false, false)));
            var console = new PromptConsole(keys.Concat([new('\r', ConsoleKey.Enter, false, false, true)]));
            return Pump(Task.Run(async () =>
            {
                await using var prompt = new Prompt(callbacks: new ReplPromptCallbacks(session), console: console,
                    configuration: ReplHost.CreatePromptConfiguration());
                return (await prompt.ReadLineAsync()).Text;
            }));
        }
        Success("int kept = 42; var transfer = new Node { Name = \"ReplTransfer\" }; root.AddChild(transfer); " +
            "var source = new Node2D { Name = \"Source\", Position = new Vector2(10, 20) }; transfer.AddChild(source); " +
            "source.AddChild(new Label { Name = \"Nested\", Text = \"copied\" }); source.AddToGroup(\"repl-copy\"); source.SetMeta(\"score\", 9); " +
            "var destination = new Node2D { Name = \"Destination\", Position = new Vector2(100, 200) }; transfer.AddChild(destination); " +
            "var other = new Node2D { Name = \"Other Parent\", Position = new Vector2(-200, 50) }; transfer.AddChild(other);");
        Success("cd $ReplTransfer");
        Assert.Equal("cp $Source", Complete("cp $Sou"));
        Assert.Equal("cp source destination", Complete("cp source destin"));
        Assert.Equal(":mv source destination", Complete(":mv source destin"));
        Assert.Equal("mv source $Destination", Complete("mv source ?Dest"));
        Assert.Equal("cp $Source $Destination", Complete("cp $Source $Dest"));
        Assert.Equal("cp source.GetParent() destination", Complete("cp source.GetPar", " destination"));
        Assert.Equal("mv source destination.GetParent()", Complete("mv source destination.GetPar"));

        Assert.Contains("Usage:", Eval("cp").Error!);
        Assert.Contains("Usage:", Eval("mv source").Error!);
        Assert.Contains("root Window", Eval("cp root destination").Error!);
        Assert.Contains("root Window", Eval("mv root destination").Error!);
        Assert.Contains("descendants", Eval("mv source source.GetChild(0)").Error!);
        Assert.Contains("descendants", Eval("cp source source").Error!);
        Assert.Contains("Godot.Node", Eval("cp null destination").Error!);
        Assert.Contains("Godot.Node", Eval("mv source 42").Error!);
        Assert.Contains("Press Tab", Eval("cp source ?Dest").Error!);
        Assert.Contains("not found", Eval("mv $Missing $Destination").Error!);

        Assert.Contains("(1,11):", Eval("cp source missingParent").Error!);
        Assert.Contains("(1,4):", Eval("mv missingSource destination").Error!);
        var copy = Success("cp $Source $Destination");
        Assert.False(copy.Committed);
        Assert.Contains("/root/ReplTransfer/Destination/Source", copy.Value!);
        Assert.Equal("true", Success("destination.GetNode<Node2D>(\"Source\") != source").Value);
        Assert.Equal("\"copied\"", Success("destination.GetNode<Label>(\"Source/Nested\").Text").Value);
        Assert.Equal("true", Success("destination.GetNode(\"Source\").IsInGroup(\"repl-copy\")").Value);
        Assert.Equal("9", Success("(int)destination.GetNode(\"Source\").GetMeta(\"score\")").Value);
        Assert.Equal("true", Success("destination.GetNode<Node2D>(\"Source\").Position == source.Position").Value);
        Assert.Contains("already has a child", Eval("cp source destination").Error!);
        Assert.Contains("already has a child", Eval("mv source destination").Error!);
        Assert.Same(globals.here, globals.root.GetNode("ReplTransfer"));

        Success("int picks = 0; int pickThread = System.Environment.CurrentManagedThreadId; " +
            "Node Pick(Node node) { if (System.Environment.CurrentManagedThreadId != pickThread) throw new Exception(\"Wrong thread\"); picks++; return node; }");
        Assert.Contains("Usage:", Eval("mv Pick(source) other; picks++;").Error!);
        Assert.Equal("0", Success("picks").Value);
        Success("cd source");
        var position = globals.here is Node2D spatial ? spatial.GlobalPosition : throw new Exception();
        var move = Success("mv Pick(source) Pick(other)");
        Assert.False(move.Committed);
        Assert.Contains("/root/ReplTransfer/Other Parent/Source", move.Value!);
        Assert.Equal("2", Success("picks").Value);
        Assert.Equal("/root/ReplTransfer/Other Parent/Source", Success("pwd").Value);
        Assert.Equal(position, ((Node2D)globals.here).GlobalPosition);
        Assert.Equal("true", Success("here == source && source.GetParent() == other").Value);
        Assert.Contains("already has that parent", Success("mv source other").Value!);

        Success("var detached = new Node(); var cp = new[] { source }; Func<Node, Node> mv = node => node;");
        Assert.Contains("live nodes", Eval("mv source detached").Error!);
        Success("detached.Free();");
        Assert.Contains("live nodes", Eval("cp detached destination").Error!);
        Assert.Equal("true", Success("mv(root) == root").Value);
        Assert.Equal("true", Success("mv .Invoke(root) == root").Value);
        Assert.Equal("true", Success("cp[0] == source").Value);
        Assert.Equal("true", Success("@cp[0] == source && @mv(root) == root").Value);
        Assert.True(Success("mv").Committed);
        Assert.True(Success("cp").Committed);
        IPromptCallbacks callbacks = new ReplPromptCallbacks(session);
        foreach (var statement in new[] { "cp;", "mv;" })
        {
            // Roslyn parses an identifier statement, then rejects its expression
            // kind semantically. It must not become a shell command or mutate nodes.
            var result = Eval(statement);
            Assert.Contains("CS0201", result.Error!);
            Assert.DoesNotContain("Usage:", result.Error!, StringComparison.Ordinal);
            Assert.False(result.Committed);
            var prepared = Pump(Task.Run(() => session.PrepareAsync(statement, token)));
            Assert.Equal(statement, prepared.Input.Code);
            var highlighting = Pump(Task.Run(() => callbacks.HighlightCallbackAsync(statement, token)));
            Assert.DoesNotContain(highlighting, span => span.Start == 0 && span.Formatting.Foreground == PrettyPrompt.Highlighting.AnsiColor.BrightMagenta);
        }
        Assert.Equal("true", Success("cp[0] == source && mv(root) == root && source.GetParent() == other && destination.GetChildCount() == 1").Value);
        Assert.Equal("mv.Invoke()", Complete("mv.Inv"));
        Assert.Equal("cp.Length", Complete("cp.Len"));
        Success(":mv source transfer");
        Assert.Equal("/root/ReplTransfer/Source", Success("pwd").Value);
        Success("mv source $[\"/root/ReplTransfer/Other Parent\"]");
        Assert.Equal("/root/ReplTransfer/Other Parent/Source", Success("pwd").Value);
        Success(":cp (source) (transfer)");
        Assert.Equal("42", Success("kept").Value);
        Success("cd /");
        Success("rm transfer");
        Assert.False(engine.Iteration());
        Assert.Empty(engine.Errors.Drain());
    }
}

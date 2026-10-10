using System.Diagnostics;
using Godot;
using PrettyPrompt;
using twodog.Repl;
using twodog.tests.EngineTests;

namespace twodog.tests.ReplTests;

[Collection(nameof(EngineRestartCollection))]
public class RemoveNodeIntegrationTests
{
    [Fact]
    public void RemovalUsesScopedPathsAndCSharpExpressionsAndPreservesTheEngineRoot()
    {
        var game = typeof(showcase.CSharpTicker).Assembly;
        using var engine = new twodog.Engine("repl-remove", twodog.Engine.ResolveProjectDir(), "--headless") { CaptureErrors = true };
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
                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), "Removal stopped making progress");
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
        string Complete(string text)
        {
            var keys = text.Select(c => new ConsoleKeyInfo(c, char.IsLetterOrDigit(c) ? (ConsoleKey)char.ToUpperInvariant(c) : ConsoleKey.NoName,
                false, false, false)).Concat([new('\t', ConsoleKey.Tab, false, false, false), new('\r', ConsoleKey.Enter, false, false, true)]);
            var console = new PromptConsole(keys);
            return Pump(Task.Run(async () =>
            {
                await using var prompt = new Prompt(callbacks: new ReplPromptCallbacks(session), console: console,
                    configuration: ReplHost.CreatePromptConfiguration());
                return (await prompt.ReadLineAsync()).Text;
            }));
        }
        Success("int retained = 42; var removalParent = new Node { Name = \"ReplRemove\" }; root.AddChild(removalParent); " +
            "var savedRemoval = new Node { Name = \"Saved\" }; removalParent.AddChild(savedRemoval); " +
            "removalParent.AddChild(new Node { Name = \"Child\" }); removalParent.AddChild(new Node { Name = \"My Node\" }); " +
            "removalParent.AddChild(new Node { Name = \"Sibling\" });");
        Success("cd $ReplRemove");
        Assert.Equal("rm $Child", Complete("rm $Chi"));
        Assert.Equal(":rm $Child", Complete(":rm $Chi"));
        Assert.Equal("rm savedRemoval", Complete("rm savedRemov"));
        Assert.Equal("rm $Child", Complete("rm ?Chi"));
        Assert.Contains("Usage:", Eval("rm").Error!);
        Assert.Contains("Usage:", Eval(":rm").Error!);
        Assert.Contains("root Window", Eval("rm root").Error!);
        Assert.Contains("root Window", Eval("rm /").Error!);
        Assert.Contains("not found", Eval("rm $Missing").Error!);
        Assert.Contains("Godot.Node", Eval("rm 42").Error!);
        Assert.Contains("Usage:", Eval("rm savedRemoval; root.QueueFree();").Error!);
        Assert.Contains("Press Tab", Eval("rm ?Chi").Error!);
        Assert.Equal("/root/ReplRemove", Success("pwd").Value);

        var removed = Success("rm $Child");
        Assert.False(removed.Committed);
        Assert.Contains("/root/ReplRemove/Child", removed.Value!);
        Assert.False(engine.Iteration());
        Assert.Null(globals.here.GetNodeOrNull<Node>("Child"));
        Success("rm $[\"My Node\"]");
        Success(":rm $Sibling");
        Assert.False(engine.Iteration());
        Assert.Null(globals.here.GetNodeOrNull<Node>("My Node"));
        Assert.Null(globals.here.GetNodeOrNull<Node>("Sibling"));

        Success("int removalCalls = 0; int removalThread = System.Environment.CurrentManagedThreadId; " +
            "Node PickRemoval() { removalCalls++; if (System.Environment.CurrentManagedThreadId != removalThread) throw new Exception(\"Wrong thread\"); return savedRemoval; }");
        Success("rm PickRemoval()");
        Assert.Equal("1", Success("removalCalls").Value);
        Assert.False(engine.Iteration());
        Assert.Null(globals.here.GetNodeOrNull<Node>("Saved"));
        Assert.Equal("42", Success("retained").Value);
        Success("var remainingChild = new Node(); here.AddChild(remainingChild);");
        Success("rm here");
        Assert.Same(globals.root, globals.here);
        Assert.False(engine.Iteration());
        Assert.Null(globals.root.GetNodeOrNull<Node>("ReplRemove"));
        Assert.Equal("false", Success("GodotObject.IsInstanceValid(remainingChild)").Value);
        Assert.Equal("42", Success("retained").Value);
        Assert.Empty(engine.Errors.Drain());
    }
}

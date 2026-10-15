using System.Diagnostics;
using System.IO.Pipes;
using twodog.cli;
using twodog.Repl;
using twodog.tests.EngineTests;
using Cli = twodog.cli.Repl;

namespace twodog.tests.ReplTests;

// One-shot requests from the 2dog tool's client against a live pipe server.
[Collection(nameof(EngineRestartCollection))]
public class ExecSessionTests
{
    [Fact]
    public void NamedSessionsOutliveTheirConnectionsAndUnnamedRequestsStartFresh()
    {
        using var live = new LiveHost("repl-exec-sessions");
        live.Run(async token =>
        {
            var (created, _) = await Request(live.Pipe, Exec("var x = 41; world.Clear(); worlds.Add(\"lab\");", "a"), token);
            Assert.Equal((Cli.ReplStatus.Ok, "a"), (created.Status, created.Session));
            Assert.Equal("42", await Value(live.Pipe, "x + 1", "a", token));

            var (fresh, freshEvents) = await Request(live.Pipe, Exec("x"), token);
            Assert.Equal(Cli.ReplStatus.Error, fresh.Status);
            Assert.Null(fresh.Session);
            Assert.Contains("CS0103", Assert.Single(Results(freshEvents)).Text);
            // An unnamed request's world closes with its session.
            await Request(live.Pipe, Exec("world.Clear();"), token);
            // Worlds release scenes and windows that user code already detached.
            const string detached = "var loose = worlds.Add(\"loose\"); loose.Scene.GetParent().RemoveChild(loose.Scene); loose.Clear(); " +
                                    "loose.Viewport.GetParent().RemoveChild(loose.Viewport); worlds.Remove(\"loose\")";
            Assert.Equal("true", await Value(live.Pipe, detached, "a", token));
            await Request(live.Pipe, Exec("var x = \"b\";", "b"), token);
            Assert.Equal("41", await Value(live.Pipe, "x", "a", token));
            Assert.Equal("\"b\"", await Value(live.Pipe, "x", "b", token));

            var sessions = await Sessions(live.Pipe, token);
            Assert.Equal(["a", "b"], sessions.Select(s => s.Name));
            Assert.Equal(["default", "lab"], sessions[0].Worlds!);
            Assert.Empty(sessions[1].Worlds!);
            Assert.All(sessions, s => Assert.Equal(("auto", "/root", false), (s.Mode, s.Scope, s.Busy)));

            var (closed, _) = await Request(live.Pipe, new(Cli.ReplOps.Close, "a"), token);
            Assert.Equal("closed session 'a'", closed.Text);
            var missing = await Assert.ThrowsAsync<ToolException>(() => Request(live.Pipe, new(Cli.ReplOps.Close, "a"), token));
            Assert.Equal("no session named 'a'", missing.Message);
            Assert.Equal(["b"], (await Sessions(live.Pipe, token)).Select(s => s.Name));
        });
        live.Frames(3);
        Assert.DoesNotContain(live.Engine.Tree.Root.GetChildren(), node => node.Name.ToString().StartsWith("ReplWorld", StringComparison.Ordinal));
        live.Server.Dispose();
        Assert.Empty(live.Engine.Errors.Drain());
    }

    [Fact]
    public void ScriptsRunSubmissionsInOrderAndStopAtTheFirstFailure()
    {
        using var live = new LiveHost("repl-exec-scripts");
        live.Run(async token =>
        {
            const string script = ":sh\ncd $Control\n:cancel\n\nhere.Name\nls $Signals/Sources\n:cs\nvar n = 1;\n" +
                                  "for (var i = 0; i < 3; i++)\n{\n    n += i;\n}\n:clear\n:multiline\n:help\nn\n:reset\nn\nthrow new Exception(\"never\");\n";
            var (done, events) = await Request(live.Pipe, Exec(script, "s"), token);
            Assert.Equal(Cli.ReplStatus.Error, done.Status);
            var results = Results(events);
            // Consecutive C# statements join into one submission; commands and expressions without a semicolon stay apart.
            Assert.Equal([":sh", "cd $Control", "here.Name", "ls $Signals/Sources", ":cs", "var n = 1;\nfor (var i = 0; i < 3; i++)\n{\n    n += i;\n}",
                ":help", "n", ":reset", "n"], results.Select(r => r.Input!.ReplaceLineEndings("\n")));
            Assert.Equal([.. Enumerable.Repeat(Cli.ReplStatus.Ok, 9), Cli.ReplStatus.Error], results.Select(r => r.Status));
            Assert.Equal("Mode: :sh", results[0].Text);
            Assert.Equal("/root/Control", results[1].Text);
            Assert.Equal("Control", results[2].Text);
            Assert.Contains("CSharpTicker", results[3].Text);
            Assert.Null(results[5].Text);
            Assert.Contains("Modes (current: :cs)", results[6].Text);
            Assert.Equal("4", results[7].Text);
            Assert.Contains("C# session reset", results[8].Text);
            Assert.True(results[9].Error);
            // Diagnostics point at the line in the whole request.
            Assert.StartsWith("(18,1): CS0103", results[9].Text);

            // A joined run compiles as one: a compile error anywhere runs none of it.
            var (atomic, atomicEvents) = await Request(live.Pipe, Exec("var a = 1;\nvar c = missing;", "j"), token);
            Assert.Equal(Cli.ReplStatus.Error, atomic.Status);
            Assert.StartsWith("(2,9): CS0103", Assert.Single(Results(atomicEvents)).Text);
            Assert.Contains("CS0103", Assert.Single(Results((await Request(live.Pipe, Exec("a", "j"), token)).Events)).Text);
            Assert.Equal("6", await Value(live.Pipe, "var x = 2;\nvar y = 3;\nx * y", "j", token));

            // Later requests keep the session's mode and scope; exit ends a request, never the session.
            var (stopped, stoppedEvents) = await Request(live.Pipe, Exec("1\n:exit\n2", "s"), token);
            Assert.Equal(Cli.ReplStatus.Ok, stopped.Status);
            Assert.Equal(["1"], Results(stoppedEvents).Select(r => r.Text));
            Assert.Equal(["Mode: :auto", "/root/Control"], Results((await Request(live.Pipe, Exec(":auto\npwd\nexit\n3", "s"), token)).Events).Select(r => r.Text));
            var (thrown, thrownEvents) = await Request(live.Pipe, Exec("throw new InvalidOperationException(\"nope\");", "s"), token);
            Assert.Equal(Cli.ReplStatus.Error, thrown.Status);
            Assert.Contains("nope", Assert.Single(Results(thrownEvents)).Text);
            var (empty, emptyEvents) = await Request(live.Pipe, Exec(" \n", "s"), token);
            Assert.Equal(Cli.ReplStatus.Ok, empty.Status);
            Assert.Empty(emptyEvents);
        });
        live.Server.Dispose();
        Assert.Empty(live.Engine.Errors.Drain());
    }

    [Fact]
    public void ConsoleAndGodotOutputReturnToTheSubmittingRequest()
    {
        using var live = new LiveHost("repl-exec-output");
        live.Run(async token =>
        {
            const string code = "Console.Write(\"a\"); Console.WriteLine(\"b\"); Console.Error.WriteLine(\"c\"); Console.Out.Write('d'); " +
                                "Console.Out.Write(new[] { 'e', 'f' }, 0, 2); Console.Out.Flush(); GD.Print(\"godot\"); GD.PrintErr(\"godot-err\"); " +
                                "GD.PushWarning(\"careful\"); GD.PushError(\"broken\"); 5";
            var (done, events) = await Request(live.Pipe, Exec(code), token);
            Assert.Equal(Cli.ReplStatus.Ok, done.Status);
            Assert.Equal("ab" + Environment.NewLine + "def", Text(events, Cli.ReplEvents.Output, error: false));
            Assert.Equal("c" + Environment.NewLine, Text(events, Cli.ReplEvents.Output, error: true));
            Assert.Equal("godot\n", Text(events, Cli.ReplEvents.Log, error: false));
            var log = Text(events, Cli.ReplEvents.Log, error: true);
            Assert.Contains("godot-err", log);
            Assert.Contains("WARNING: careful", log);
            Assert.Contains("ERROR: broken", log);
            Assert.Equal("5", Assert.Single(Results(events)).Text);

            // Sessions interleave at awaits on the owner thread; each request keeps its own output.
            var slow = Request(live.Pipe, Exec("await Task.Delay(300, ct); Console.WriteLine(\"from a\");", "a"), token);
            var fast = Request(live.Pipe, Exec("Console.WriteLine(\"from b\");", "b"), token);
            Assert.Equal("from b" + Environment.NewLine, Text((await fast).Events, Cli.ReplEvents.Output, false));
            Assert.Equal("from a" + Environment.NewLine, Text((await slow).Events, Cli.ReplEvents.Output, false));

            // Background work outliving its request writes to the host console, not to a later request.
            await Request(live.Pipe, Exec("var late = Task.Run(async () => { await Task.Delay(200); Console.WriteLine(\"late\"); });", "c"), token);
            Assert.DoesNotContain((await Request(live.Pipe, Exec("await late;", "c"), token)).Events, e => e.Kind == Cli.ReplEvents.Output);
        });
        Assert.Equal(2, live.Engine.Errors.Drain().Length);

        // Script and shader reports keep Godot's labels; a closed capture passes writes back.
        var captured = new List<string>();
        var capture = new ReplCapture((text, error, log) => captured.Add($"{log}/{error}/{text}"));
        using (var logger = new ReplCapture.GodotLog())
        using (ReplCapture.Enter(capture))
        {
            logger._LogError("f", "file.gd", 3, "code", "", false, (int)Godot.Logger.ErrorType.Script, []);
            logger._LogError("f", "x.gdshader", 4, "", "bad shader", false, (int)Godot.Logger.ErrorType.Shader, []);
            logger._LogMessage("", false);
            // Native dispatch asks the instance which virtuals it overrides, in Godot's snake_case.
            Assert.True(logger.HasMethod(Godot.Logger.MethodName._LogMessage));
            Assert.True(logger.HasMethod(Godot.Logger.MethodName._LogError));
            Assert.False(logger.HasMethod("_log_nothing"));
            capture.Close();
            logger._LogMessage("after close", false);
        }
        Assert.Equal(["True/True/SCRIPT ERROR: code\n   at: f (file.gd:3)\n", "True/True/SHADER ERROR: bad shader\n   at: f (x.gdshader:4)\n"], captured);
        // Routing installs once: a later capture finds its router still in place.
        var routed = Console.Out;
        using (ReplCapture.Enter(capture)) Assert.Same(routed, Console.Out);
        // Outside a capture, routed writes reach the console as before.
        Console.Out.Write(' ');
        Console.Out.Write([], 0, 0);
        Assert.Equal(2, captured.Count);
        live.Server.Dispose();
        Assert.Empty(live.Engine.Errors.Drain());
    }

    [Fact]
    public void TimeoutsDisconnectsAndClosingCancelOnlyTheirOwnWork()
    {
        using var live = new LiveHost("repl-exec-cancel");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ReplLifetimeProbe.OnStarted = () => started.TrySetResult();
        var describeTimeout = ReplExecHost.DescribeTimeout;
        try
        {
            live.Run(async token =>
            {
                await Request(live.Pipe, Exec("var k = 3;", "a"), token);
                var (timedOut, timedOutEvents) = await Request(live.Pipe, Exec("1\nwhile (true) { }\n2", "a", timeout: 0.5), token);
                Assert.Equal(Cli.ReplStatus.Timeout, timedOut.Status);
                Assert.Equal([Cli.ReplStatus.Ok, Cli.ReplStatus.Timeout], Results(timedOutEvents).Select(r => r.Status));
                Assert.Equal("3", await Value(live.Pipe, "k", "a", token));

                // A client that goes away cancels its own request; its session stays.
                using (var client = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    var abandoned = Request(live.Pipe, Exec(Probe + "await Task.Delay(Timeout.Infinite, ct);", "a"), client.Token);
                    await started.Task.WaitAsync(token);
                    client.Cancel();
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);
                }
                Assert.Equal("3", await Value(live.Pipe, "k", "a", token));

                // Listing never waits for an engine thread held by a synchronous loop; closing ends the loop.
                started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                ReplExecHost.DescribeTimeout = TimeSpan.FromMilliseconds(50);
                var blocking = Request(live.Pipe, Exec(Probe + "while (true) { }", "b"), token);
                await started.Task.WaitAsync(token);
                var listed = await Sessions(live.Pipe, token);
                Assert.All(listed, s => Assert.True(s is { Scope: null, Worlds: null }));
                // Only b is known to be busy: the abandoned request releases a when its own cleanup finishes.
                Assert.Equal(["a", "b"], listed.Select(s => s.Name));
                Assert.True(listed[1].Busy);
                await Request(live.Pipe, new(Cli.ReplOps.Close, "b"), token);
                var (cancelled, cancelledEvents) = await blocking;
                Assert.Equal(Cli.ReplStatus.Cancelled, cancelled.Status);
                Assert.Equal(Cli.ReplStatus.Cancelled, Assert.Single(Results(cancelledEvents)).Status);
            });
        }
        finally
        {
            ReplLifetimeProbe.OnStarted = null;
            ReplExecHost.DescribeTimeout = describeTimeout;
        }
        live.Server.Dispose();
        Assert.Empty(live.Engine.Errors.Drain());
    }

    [Fact]
    public void WorkThatIgnoresCancellationTimesOutHonestlyAndClosingDoesNotWaitForIt()
    {
        using var live = new LiveHost("repl-exec-uncancellable");
        var grace = ReplExecHost.CancelGrace;
        ReplExecHost.CancelGrace = TimeSpan.FromMilliseconds(300);
        try
        {
            live.Run(async token =>
            {
                // ToSignal ignores the token: the request reports a timeout instead of waiting for the timer.
                const string ignoring = "await tree.ToSignal(tree.CreateTimer(4), SceneTreeTimer.SignalName.Timeout); \"late\"";
                // A warm session compiles quickly, so the timeout lands while ToSignal waits.
                Assert.Equal("1", await Value(live.Pipe, "1", "u", token));
                var watch = Stopwatch.StartNew();
                var (timedOut, events) = await Request(live.Pipe, Exec(ignoring, "u", timeout: 1), token);
                Assert.Equal(Cli.ReplStatus.Timeout, timedOut.Status);
                Assert.Contains("does not observe cancellation", Assert.Single(Results(events)).Text);
                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), watch.Elapsed.ToString());
                Assert.True(Assert.Single(await Sessions(live.Pipe, token)).Busy);
                // A later request waits behind it and says so.
                var (queued, queuedEvents) = await Request(live.Pipe, Exec("1", "u", timeout: 0.3), token);
                Assert.Equal(Cli.ReplStatus.Timeout, queued.Status);
                Assert.Contains("still running in this session", Assert.Single(Results(queuedEvents)).Text);

                // Closing gives up on the work after the grace period; meanwhile the name is reserved.
                ReplExecHost.CancelGrace = TimeSpan.FromSeconds(1);
                var closing = Request(live.Pipe, new(Cli.ReplOps.Close, "u"), token);
                await Task.Delay(200, token);
                Assert.Contains("is closing", (await Assert.ThrowsAsync<ToolException>(() => Request(live.Pipe, Exec("1", "u"), token))).Message);
                var (closed, _) = await closing;
                Assert.Contains("still running", closed.Text);
                Assert.Equal("2", await Value(live.Pipe, "2", "u", token));
                await Request(live.Pipe, new(Cli.ReplOps.Close, "u"), token);
                // The abandoned work ends with its timer; its session is then released.
                await Task.Delay(4000, token);
            });
        }
        finally { ReplExecHost.CancelGrace = grace; }
        live.Server.Dispose();
        Assert.Empty(live.Engine.Errors.Drain());
    }

    [Fact]
    public void ValuesReadAsCSharpAndStrictFailsSubmissionsThatLogGodotErrors()
    {
        using var live = new LiveHost("repl-exec-values");
        live.Run(async token =>
        {
            Assert.Equal("\"a<b>+&é\\n\"", await Value(live.Pipe, "\"a<b>+&é\\n\"", null, token));
            Assert.Equal("<Window> /root", await Value(live.Pipe, "root", null, token));
            Assert.Equal("<freed Node>", await Value(live.Pipe, "var gone = new Node(); gone.Free(); gone", null, token));
            Assert.Equal("<Node> Loose (not in the tree)", await Value(live.Pipe, "var loose = new Node { Name = \"Loose\" }; loose", "v", token));
            await Value(live.Pipe, "loose.Free()", "v", token);
            Assert.Equal("<PackedScene> res://playground.tscn", await Value(live.Pipe, "GD.Load<PackedScene>(\"res://playground.tscn\")", null, token));
            Assert.StartsWith("<Resource#", await Value(live.Pipe, "new Resource()", null, token));
            Assert.Equal("[\"x\", <Window> /root, 3]", await Value(live.Pipe, "new object[] { \"x\", root, 3 }", null, token));
            // Bare absolute paths navigate and list; Godot's GetTree() habit gets a pointer to 'tree'.
            Assert.Equal("/root/Control", await Value(live.Pipe, "cd /root/Control", "v", token));
            Assert.Contains("┖╴Control", await Value(live.Pipe, "ls /root/Control", "v", token));
            // A depth limit counts what it hides; the listing also arrives as a tree.
            var (_, listed) = await Request(live.Pipe, Exec("ls -d 0 /root/Control", "v"), token);
            var listing = Assert.Single(Results(listed));
            Assert.Matches(@"^ ┖╴Control \(\+\d+\)$", listing.Text);
            Assert.Equal(("Control", "Control", null), (listing.Tree!.Name, listing.Tree.Type, listing.Tree.Children));
            Assert.True(listing.Tree.Hidden > 0);
            var full = Assert.Single(Results((await Request(live.Pipe, Exec("ls /root/Control", "v"), token)).Events)).Tree!;
            Assert.Equal(listing.Tree.Hidden, Count(full) - 1);
            Assert.Contains("SceneTree is 'tree'", Assert.Single(Results((await Request(live.Pipe, Exec("GetTree()"), token)).Events)).Text);
            Assert.Equal("\"Godot.Timer\"", await Value(live.Pipe, "var timer = new Timer(); var kind = timer.GetType().FullName; timer.Free(); kind", null, token));
            Assert.StartsWith("# Working in a live 2dog game", await Value(live.Pipe, ":help agents", null, token));

            Assert.Equal("1", await Value(live.Pipe, "GD.PushError(\"lenient\"); 1", null, token));
            var (strict, events) = await Request(live.Pipe, Exec("GD.PushError(\"strict\"); 1\n2", strict: true), token);
            Assert.Equal(Cli.ReplStatus.Error, strict.Status);
            Assert.Contains("--strict", Assert.Single(Results(events)).Text);
        });
        live.Server.Dispose();
        Assert.Equal(["lenient", "strict"], live.Engine.Errors.Drain().Select(error => error.Text));
    }

    [Fact]
    public async Task AHostThatNeverAnswersEndsTheRequestAtItsDeadline()
    {
        var token = TestContext.Current.CancellationToken;
        var pipe = ReplDiscoveryInit.PipeName();
        await using var server = new NamedPipeServerStream(pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        // Reads the request like a host whose engine thread then never answers.
        var reading = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync(token);
            using var reader = new StreamReader(server, leaveOpen: true);
            while (await reader.ReadLineAsync(token) is not null) { }
        }, token);
        var done = await ReplCommand.RequestAsync(pipe, Exec("1"), _ => { }, token, TimeSpan.FromMilliseconds(300));
        await reading;
        Assert.Equal((Cli.ReplEvents.Done, Cli.ReplStatus.Timeout), (done.Kind, done.Status));
        Assert.Contains("gave no answer within", done.Text);
    }

    [Fact]
    public void MalformedRequestsFailAloneAndTheHostKeepsServing()
    {
        using var live = new LiveHost("repl-exec-malformed");
        live.Run(async token =>
        {
            var hello = Cli.ReplProtocol.ExecHello + Cli.ReplProtocol.Version;
            Assert.Contains("REPL protocol 1, the client '99'", Failure(await Raw(live.Pipe, [Cli.ReplProtocol.ExecHello + "99"], token)));
            Assert.Equal("expected one JSON request line", Failure(await Raw(live.Pipe, [hello, "{not json"], token)));
            Assert.Equal("expected one JSON request line", Failure(await Raw(live.Pipe, [hello, "null"], token)));
            Assert.Equal("", await Raw(live.Pipe, [hello], token, read: false));
            foreach (var (request, message) in new (Cli.ReplRequest, string)[]
                     {
                         (new("dance"), "unknown operation 'dance'"),
                         (new(Cli.ReplOps.Close), "close needs a session name"),
                         (new(Cli.ReplOps.Exec, "../escape", "1"), "'../escape' is not a session name"),
                         (new(Cli.ReplOps.Exec, null, "1", -1), "positive number of seconds"),
                     })
                Assert.Contains(message, (await Assert.ThrowsAsync<ToolException>(() => Request(live.Pipe, request, token))).Message);
            Assert.Equal("42", await Value(live.Pipe, "40 + 2", null, token));
            var (codeless, codelessEvents) = await Request(live.Pipe, new(Cli.ReplOps.Exec, "codeless"), token);
            Assert.Equal(Cli.ReplStatus.Ok, codeless.Status);
            Assert.Empty(codelessEvents);
        });
        live.Server.Dispose();
        Assert.Empty(live.Engine.Errors.Drain());
    }

    private const string Probe = "twodog.tests.ReplTests.ReplLifetimeProbe.Started(); ";

    private static Cli.ReplRequest Exec(string code, string? session = null, double? timeout = null, bool strict = false)
        => new(Cli.ReplOps.Exec, session, code, timeout, strict);

    internal static async Task<(Cli.ReplEvent Done, List<Cli.ReplEvent> Events)> Request(string pipe, Cli.ReplRequest request, CancellationToken token)
    {
        var events = new List<Cli.ReplEvent>();
        var done = await ReplCommand.RequestAsync(pipe, request, events.Add, token);
        return (done, events);
    }

    private static async Task<string?> Value(string pipe, string code, string? session, CancellationToken token)
    {
        var (done, events) = await Request(pipe, Exec(code, session), token);
        Assert.Equal(Cli.ReplStatus.Ok, done.Status);
        return Assert.Single(Results(events)).Text;
    }

    private static async Task<Cli.ReplSessionInfo[]> Sessions(string pipe, CancellationToken token)
        => Assert.Single((await Request(pipe, new(Cli.ReplOps.Sessions), token)).Events).Sessions!;

    private static List<Cli.ReplEvent> Results(List<Cli.ReplEvent> events) => events.Where(e => e.Kind == Cli.ReplEvents.Result).ToList();

    private static int Count(Cli.ReplTreeNode node) => 1 + (node.Children?.Sum(Count) ?? 0);

    private static string Text(List<Cli.ReplEvent> events, string kind, bool error)
        => string.Concat(events.Where(e => e.Kind == kind && e.Error == error).Select(e => e.Text));

    private static string? Failure(string reply)
    {
        var done = Cli.ReplProtocol.Deserialize<Cli.ReplEvent>(reply.Trim())!;
        Assert.Equal((Cli.ReplEvents.Done, Cli.ReplStatus.Error, true), (done.Kind, done.Status, done.Error));
        return done.Text;
    }

    private static async Task<string> Raw(string pipe, string[] lines, CancellationToken token, bool read = true)
    {
        await using var stream = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
        await stream.ConnectAsync(token);
        // Raw bytes: the host may answer and close before a writer would flush on disposal.
        await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(string.Concat(lines.Select(line => line + "\n"))), token);
        using var reader = new StreamReader(stream, leaveOpen: true);
        return read ? await reader.ReadToEndAsync(token) : "";
    }
}

/// <summary>A started headless engine with a listening pipe server; the test thread owns and pumps Godot.</summary>
internal sealed class LiveHost : IDisposable
{
    public Engine Engine { get; }
    public EngineDispatcher Dispatcher { get; } = new();
    public CancellationTokenSource Lifetime { get; }
    public string Pipe { get; } = ReplDiscoveryInit.PipeName();
    public ReplPipeServer Server { get; }

    public LiveHost(string name)
    {
        Engine = new Engine(name, Engine.ResolveProjectDir(), "--headless") { CaptureErrors = true };
        Engine.Start();
        Assert.False(Engine.Iteration());
        Lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Lifetime.CancelAfter(TimeSpan.FromSeconds(90));
        Server = new ReplPipeServer(Pipe, Engine, [typeof(showcase.CSharpTicker).Assembly, typeof(ReplLifetimeProbe).Assembly], Dispatcher, Lifetime.Token);
    }

    /// <summary>Runs client work on the pool while this thread pumps the engine.</summary>
    public void Run(Func<CancellationToken, Task> work) => Pump(Task.Run(() => work(Lifetime.Token)));

    public void Pump(Task task)
    {
        var watch = Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            Lifetime.Token.ThrowIfCancellationRequested();
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(80), "REPL work stopped making progress.");
            Assert.False(Engine.Iteration());
            Dispatcher.Pump();
            Thread.Sleep(1);
        }
        task.GetAwaiter().GetResult();
    }

    public void Frames(int count)
    {
        for (var i = 0; i < count; i++)
        {
            Assert.False(Engine.Iteration());
            Dispatcher.Pump();
        }
    }

    public void Dispose()
    {
        Server.Dispose();
        Dispatcher.Dispose();
        Engine.Dispose();
        Lifetime.Dispose();
    }
}

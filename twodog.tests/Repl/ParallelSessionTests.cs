using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.RegularExpressions;
using twodog.Repl;
using twodog.tests.EngineTests;

namespace twodog.tests.ReplTests;

[Collection(nameof(EngineRestartCollection))]
public class ParallelSessionTests
{
    [Fact]
    public void ListenerCanShutDownBeforeItsFirstConnection()
    {
        using var engine = new Engine("repl-unused-listener", Engine.ResolveProjectDir(), "--headless");
        using var dispatcher = new EngineDispatcher();
        using var server = new ReplPipeServer(ReplDiscoveryInit.PipeName(), engine, [], dispatcher, new CancellationToken(true));
        server.Dispose();
    }

    [Fact]
    public void PipeClientsOwnIndependentStateAndWorldsAndCanCancelWithoutBlockingInput()
    {
        var game = typeof(showcase.CSharpTicker).Assembly;
        using var engine = new Engine("repl-pipes", Engine.ResolveProjectDir(), "--headless") { CaptureErrors = true };
        engine.Start();
        Assert.False(engine.Iteration());
        using var dispatcher = new EngineDispatcher();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        lifetime.CancelAfter(TimeSpan.FromSeconds(45));
        var name = ReplDiscoveryInit.PipeName();
        using var server = new ReplPipeServer(name, engine, [game, typeof(ReplLifetimeProbe).Assembly], dispatcher, lifetime.Token);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = Environment.CurrentManagedThreadId;
        var cleaned = 0;
        var phase = "connecting";
        ReplLifetimeProbe.OnStarted = () => started.TrySetResult();
        ReplLifetimeProbe.OnCleanup = root =>
        {
            Assert.Equal(owner, Environment.CurrentManagedThreadId);
            Assert.True(root.IsInsideTree());
            Interlocked.Increment(ref cleaned);
        };
        try
        {
            var clients = Task.Run(async () =>
            {
                using var first = await Client.ConnectAsync(name, lifetime.Token);
                using var second = await Client.ConnectAsync(name, lifetime.Token);
                phase = "creating first session worlds";
                var one = await first.EvalAsync("var value = 11; world.Clear(); worlds.Add(\"lab\", \"res://playground.tscn\");", lifetime.Token);
                phase = "creating second session worlds";
                var two = await second.EvalAsync("var value = 22; world.Clear(); worlds.Add(\"lab\", \"res://playground.tscn\");", lifetime.Token);
                phase = "checking state isolation";
                Assert.DoesNotContain("Exception", one + two);
                Assert.Contains("11", await first.EvalAsync("value", lifetime.Token));
                Assert.Contains("22", await second.EvalAsync("value", lifetime.Token));
                Assert.Contains("true", await first.EvalAsync("worlds.Names.Length == 2 && worlds[\"lab\"].Scene.Name == \"Playground\" && worlds[\"lab\"].Viewport.FindWorld3D() != world.Viewport.FindWorld3D()", lifetime.Token));
                Assert.Contains("true", await second.EvalAsync("worlds[\"lab\"].Scene.GetNode(\"Spinner\") is showcase.SpinningCube", lifetime.Token));

                var probe = await first.EvalAsync("twodog.tests.ReplTests.ReplLifetimeProbe.Started();", lifetime.Token);
                Assert.DoesNotContain("CS", probe);
                Assert.True(started.Task.IsCompleted, probe);
                started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                await first.SendAsync("{ using var registration = ct.Register(() => throw new InvalidOperationException(\"callback failure\")); twodog.tests.ReplTests.ReplLifetimeProbe.Started(); await Task.Delay(Timeout.Infinite, ct); }");
                phase = "starting asynchronous submission";
                if (await Task.WhenAny(started.Task, first.PeekLineAsync(lifetime.Token)) != started.Task)
                    throw new InvalidOperationException("Unexpected submission output: " + await first.PeekLineAsync(lifetime.Token));
                await started.Task.WaitAsync(lifetime.Token);
                phase = "running another session during await";
                Assert.Contains("42", await second.EvalAsync("40+2", lifetime.Token));
                await first.SendAsync("throw new Exception(\"queued work must not run\");");
                var cancelled = await first.EvalAsync(":cancel", lifetime.Token);
                Assert.Contains("Submission cancelled.", cancelled);
                Assert.Contains("Cancellation callback failed", cancelled);
                Assert.DoesNotContain("queued work must not run", cancelled);
                Assert.Contains("11", await first.EvalAsync("value", lifetime.Token));

                phase = "starting synchronous loop";
                started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                await first.SendAsync("twodog.tests.ReplTests.ReplLifetimeProbe.Started(); try { while (true) { } } finally { twodog.tests.ReplTests.ReplLifetimeProbe.Cleanup(root); }");
                await started.Task.WaitAsync(lifetime.Token);
                phase = "cancelling synchronous loop";
                Assert.Contains("Submission cancelled.", await first.EvalAsync(":cancel", lifetime.Token));
                Assert.Equal(1, Volatile.Read(ref cleaned));
                Assert.Contains("42", await second.EvalAsync("40+2", lifetime.Token));

                // Closing a client cancels its awaiting work and releases all of its worlds.
                started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                phase = "disconnecting an active client";
                await first.SendAsync("twodog.tests.ReplTests.ReplLifetimeProbe.Started(); try { await Task.Delay(Timeout.Infinite, ct); } finally { twodog.tests.ReplTests.ReplLifetimeProbe.Cleanup(root); }");
                await started.Task.WaitAsync(lifetime.Token);
                first.Dispose();
                Assert.Contains("22", await second.EvalAsync("value", lifetime.Token));

                phase = "cancelling after orderly EOF";
                using var closing = await Client.ConnectAsync(name, lifetime.Token);
                started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                await closing.SendAsync("twodog.tests.ReplTests.ReplLifetimeProbe.Started(); while (true) { }");
                await started.Task.WaitAsync(lifetime.Token);
                await closing.SendAsync(":exit");
                await closing.SendAsync(":cancel");
                Assert.Contains("Submission cancelled.", await closing.Reader.ReadToEndAsync(lifetime.Token));
                phase = "exiting remaining client";
                await second.SendAsync(":exit");
                Assert.NotNull(await second.Reader.ReadToEndAsync(lifetime.Token));
            }, lifetime.Token);
            try { Pump(engine, dispatcher, clients, lifetime.Token); }
            catch (Exception error) { throw new InvalidOperationException("Pipe scenario failed while " + phase, error); }
            // Dispose also drains owner-thread session cleanup after streams have closed.
            server.Dispose();
            Assert.Equal(2, cleaned);
            Assert.DoesNotContain(engine.Tree.Root.GetChildren(), node => node is Godot.Window && node.Name.ToString().StartsWith("ReplWorld", StringComparison.Ordinal));
            Assert.Empty(engine.Errors.Drain());
        }
        finally { ReplLifetimeProbe.OnStarted = null; ReplLifetimeProbe.OnCleanup = null; }
    }

    [Fact]
    public void CheckpointsCancelEveryLoopFormAndEditingUsesSnapshotsWhileEngineIsBusy()
    {
        var game = typeof(showcase.CSharpTicker).Assembly;
        using var engine = new Engine("repl-checkpoints", Engine.ResolveProjectDir(), "--headless") { CaptureErrors = true };
        engine.Start();
        Assert.False(engine.Iteration());
        using var dispatcher = new EngineDispatcher();
        var globals = new ReplGlobals(engine);
        using var worlds = globals.worlds;
        using var session = new ReplSession(globals, [game, typeof(ReplLifetimeProbe).Assembly], dispatcher) { DetachedInput = true };
        var token = TestContext.Current.CancellationToken;
        Pump(engine, dispatcher, session.RefreshAsync(token), token);
        foreach (var loop in new[]
        {
            "while (true) continue;", "do { } while (true);", "for (;;) { }",
            "foreach (var n in Enumerable.Range(0, int.MaxValue)) { }",
            "foreach (var (a, b) in Enumerable.Repeat((1, 2), int.MaxValue)) { }",
        })
        {
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
            cancel.CancelAfter(TimeSpan.FromSeconds(15));
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ReplLifetimeProbe.OnStarted = () => started.TrySetResult();
            try
            {
                var editAndCancel = Task.Run(async () =>
                {
                    await started.Task.WaitAsync(cancel.Token);
                    var callbacks = new ReplLanguage(session);
                    var text = "$Cont";
                    var span = await callbacks.CompletionRangeAsync(text, text.Length, cancel.Token);
                    var matches = await callbacks.CompletionsAsync(text, text.Length, span, cancel.Token);
                    Assert.Contains(matches, item => item.ReplacementText == "$Control");
                    cancel.Cancel();
                }, token);
                var evaluation = Task.Run(() => session.EvaluateAsync("twodog.tests.ReplTests.ReplLifetimeProbe.Started(); " + loop, cancel.Token), token);
                Assert.True(Pump(engine, dispatcher, evaluation, token).Cancelled);
                Pump(engine, dispatcher, editAndCancel, token);
            }
            finally { ReplLifetimeProbe.OnStarted = null; }
        }
        var recovery = Task.Run(() => session.EvaluateAsync("40+2", token), token);
        Assert.Equal("42", Pump(engine, dispatcher, recovery, token).Value);
        Assert.Empty(engine.Errors.Drain());
    }

    [Fact]
    public void ResourceCompletionOffersScenesOrAllResourcesAndKeepsQuotesAndCasing()
    {
        var game = typeof(showcase.CSharpTicker).Assembly;
        using var engine = new Engine("repl-resources", Engine.ResolveProjectDir(), "--headless") { CaptureErrors = true };
        engine.Start();
        Assert.False(engine.Iteration());
        using var dispatcher = new EngineDispatcher();
        var globals = new ReplGlobals(engine);
        using var worlds = globals.worlds;
        using var session = new ReplSession(globals, [game], dispatcher);
        var token = TestContext.Current.CancellationToken;
        var callbacks = new ReplLanguage(session);
        foreach (var code in new[] { "world.Open(\"res://pl\")", "worlds.Add(\"test\", \"res://pl\")", "tree.ChangeSceneToFile(\"res://pl\")", "ResourceLoader.Load<PackedScene>(\"res://pl\")" })
        {
            var caret = code.IndexOf("res://pl", StringComparison.Ordinal) + 8;
            var completion = Task.Run(async () =>
            {
                var span = await callbacks.CompletionRangeAsync(code, caret, token);
                var items = await callbacks.CompletionsAsync(code, caret, span, token);
                Assert.Contains(items, item => item.ReplacementText == "res://playground.tscn");
                Assert.DoesNotContain(items, item => item.ReplacementText == "res://icon.svg");
                var chosen = items.Single(item => item.ReplacementText == "res://playground.tscn");
                var edit = await callbacks.CommitAsync(chosen, code, caret, CommitKey.Tab, token);
                Assert.Equal(code.Replace("res://pl", "res://playground.tscn", StringComparison.Ordinal), edit.Apply(code).Text);
            }, token);
            Pump(engine, dispatcher, completion, token);
        }
        var resources = ResourcePathCompletion.Capture();
        Assert.Contains("res://icon.svg", resources);
        Assert.Contains("res://signals/CSharpTicker.cs", resources);
        Assert.DoesNotContain(resources, path => path.Contains(".godot/", StringComparison.Ordinal) || path.Contains("showcase.repl/", StringComparison.Ordinal) || path.EndsWith(".import", StringComparison.Ordinal));
        Assert.Contains(ResourcePathCompletion.Items("GD.Load(\"res://", 15, resources), item => item.ReplacementText == "res://icon.svg");
        Assert.Null(ResourcePathCompletion.Span("var plain = \"hello\"", 16));
        Assert.Null(ResourcePathCompletion.Span("\"res://playground.tscn\"", 23));
        var keys = "world.Open(\"RES://pl".Select(c => new ConsoleKeyInfo(c, ConsoleKey.NoName, false, false, false))
            .Concat(new[] { new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false),
                new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false), new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, true) });
        var typed = Task.Run(() => EditorDriver.ReadAsync(new ReplLanguage(session), keys, token: token), token);
        Assert.Equal("world.Open(\"res://playground.tscn", Pump(engine, dispatcher, typed, token));
        Assert.Empty(engine.Errors.Drain());
    }

    [Fact]
    public void PipeClientStreamsMultilineInputDrainsOnEofAndDisconnectsWithoutStoppingTheHost()
    {
        var game = typeof(showcase.CSharpTicker).Assembly;
        using var engine = new Engine("repl-pipe-client", Engine.ResolveProjectDir(), "--headless") { CaptureErrors = true };
        engine.Start();
        Assert.False(engine.Iteration());
        using var dispatcher = new EngineDispatcher();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var name = ReplDiscoveryInit.PipeName();
        using var server = new ReplPipeServer(name, engine, [game], dispatcher, lifetime.Token);
        using var input = new StringReader("int answer = 0;\nfor (int n = 0; n < 7; n++) {\nanswer += 6;\n}\nanswer\n");
        using var output = new StringWriter();
        Assert.Equal(0, Pump(engine, dispatcher, Task.Run(() => ReplPipeClient.RunAsync(name, input, output, lifetime.Token)), lifetime.Token));
        Assert.Contains("42", output.ToString());
        Assert.False(engine.Iteration());
        using var nextInput = new StringReader("40+2\n:exit\nthis must not execute\n");
        using var nextOutput = new StringWriter();
        Assert.Equal(0, Pump(engine, dispatcher, Task.Run(() => ReplPipeClient.RunAsync(name, nextInput, nextOutput, lifetime.Token)), lifetime.Token));
        Assert.Contains("42", nextOutput.ToString());
        Assert.DoesNotContain("this must not execute", nextOutput.ToString());
        using var waitingInput = new WaitingInput("40+2\n");
        using var failedDestination = new FailedDestination();
        var failedClient = Task.Run(() => Assert.ThrowsAsync<IOException>(() => ReplPipeClient.RunAsync(name, waitingInput, failedDestination, lifetime.Token)), lifetime.Token);
        Assert.Equal("client output failed", Pump(engine, dispatcher, failedClient, lifetime.Token).Message);
        using var cancelledInput = new WaitingInput("40+2\n");
        using var cancelledOutput = new StringWriter();
        using var clientCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var cancelledClient = Task.Run(async () =>
        {
            var client = ReplPipeClient.RunAsync(name, cancelledInput, cancelledOutput, clientCancellation.Token);
            await cancelledInput.Waiting.Task.WaitAsync(lifetime.Token);
            clientCancellation.Cancel();
            return await client;
        }, lifetime.Token);
        Assert.Equal(0, Pump(engine, dispatcher, cancelledClient, lifetime.Token));
        Assert.False(engine.Iteration());
        Assert.Empty(engine.Errors.Drain());
    }

    [Fact]
    public void InteractivePipeClientShowsConnectionStatusAndAcceptsEditedInput()
    {
        var game = typeof(showcase.CSharpTicker).Assembly;
        using var engine = new Engine("repl-interactive-client", Engine.ResolveProjectDir(), "--headless") { CaptureErrors = true };
        engine.Start();
        Assert.False(engine.Iteration());
        using var dispatcher = new EngineDispatcher();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        lifetime.CancelAfter(TimeSpan.FromSeconds(30));
        var name = ReplDiscoveryInit.PipeName();
        using var server = new ReplPipeServer(name, engine, [game], dispatcher, lifetime.Token);
        var keys = Keys.Type("40+2").Append(new('\r', ConsoleKey.Enter, false, false, false))
            .Append(new('\x04', ConsoleKey.D, false, false, true));
        using var terminal = new ScriptedTerminal(null, keys, lifetime.Token);
        terminal.BeforeRead = () => Assert.Contains("Connected to REPL '" + name, terminal.Device.Text);
        using var input = new StringReader("");
        using var redirected = new StringWriter();
        Assert.Equal(0, Pump(engine, dispatcher, Task.Run(() => ReplPipeClient.RunAsync(name, input, redirected, lifetime.Token, () => terminal)), lifetime.Token));
        Assert.Contains("Connecting to REPL", terminal.Device.Text);
        Assert.Contains("godot> 40+2\n\u2192 42", terminal.Device.Text);
        Assert.Equal("", redirected.ToString());
        Assert.True(terminal.Restored);
        Assert.False(engine.Iteration());
        Assert.Empty(engine.Errors.Drain());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConnectedEditorCompletesSessionVariablesNodesMethodsAndSceneResources(bool color)
    {
        var game = typeof(showcase.CSharpTicker).Assembly;
        using var engine = new Engine("repl-connected-completion", Engine.ResolveProjectDir(), "--headless") { CaptureErrors = true };
        engine.Start();
        Assert.False(engine.Iteration());
        using var dispatcher = new EngineDispatcher();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        lifetime.CancelAfter(TimeSpan.FromSeconds(45));
        var name = ReplDiscoveryInit.PipeName();
        using var server = new ReplPipeServer(name, engine, [game], dispatcher, lifetime.Token);
        ConsoleKeyInfo run = new('\r', ConsoleKey.Enter, false, false, true);
        ConsoleKeyInfo tab = new('\t', ConsoleKey.Tab, false, false, false);
        const string first = "var colorAnswer = 42; \"color-ready\"";
        var keys = Keys.Type(first).Append(run)
            .Concat(Keys.Type("colorAns")).Append(tab).Append(tab).Append(run)
            .Concat(Keys.Type("$Contro")).Append(tab).Concat(Keys.Type(".Name")).Append(run)
            .Concat(Keys.Type("worlds.Ad")).Append(tab).Append(tab)
            .Concat(Keys.Type("\"lab\", \"res://playg")).Append(tab).Append(tab)
            .Concat(Keys.Type("\");")).Append(run)
            .Concat(Keys.Type("worlds[\"lab\"].Scene.SceneFilePath")).Append(run)
            .Concat(Keys.Type(":exit")).Append(run);
        using var terminal = new ScriptedTerminal(null, keys, lifetime.Token) { Color = color };
        var reads = 0;
        terminal.BeforeRead = () =>
        {
            if (reads++ != first.Length + 1) return;
            while (!terminal.Device.Text.Contains("→ \"color-ready\"", StringComparison.Ordinal))
            {
                lifetime.Token.ThrowIfCancellationRequested();
                Thread.Sleep(1);
            }
        };
        using var input = new StringReader("");
        using var redirected = new StringWriter();
        Assert.Equal(0, Pump(engine, dispatcher, Task.Run(() => ReplPipeClient.RunAsync(name, input, redirected, lifetime.Token, () => terminal)), lifetime.Token));
        var output = terminal.Device.Text;
        Assert.Contains("→ 42", output);
        Assert.Contains("→ Control", output);
        Assert.Contains("→ \"res://playground.tscn\"", output);
        Assert.Equal(color, terminal.Device.Raw.Contains("\x1b[36m→ ", StringComparison.Ordinal));
        Assert.Empty(engine.Errors.Drain());
    }

    [Fact]
    public void InteractiveClientCanConnectWithoutAGameSceneAfterAnEmptyClientDisconnects()
    {
        var game = typeof(showcase.CSharpTicker).Assembly;
        using var engine = new Engine("repl-empty-connection", Engine.ResolveProjectDir(), "--headless") { CaptureErrors = true };
        engine.Start();
        Assert.False(engine.Iteration());
        engine.Tree.CurrentScene!.Free();
        Assert.Null(engine.Tree.CurrentScene);
        using var dispatcher = new EngineDispatcher();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        lifetime.CancelAfter(TimeSpan.FromSeconds(30));
        var name = ReplDiscoveryInit.PipeName();
        using var server = new ReplPipeServer(name, engine, [game], dispatcher, lifetime.Token);
        var keys = Keys.Type(":exit").Append(new('\r', ConsoleKey.Enter, false, false, true));
        using var terminal = new ScriptedTerminal(null, keys, lifetime.Token);
        using var input = new StringReader("");
        using var output = new StringWriter();
        var connect = Task.Run(async () =>
        {
            using (var empty = await Client.ConnectAsync(name, lifetime.Token)) { }
            return await ReplPipeClient.RunAsync(name, input, output, lifetime.Token, () => terminal);
        });
        Assert.Equal(0, Pump(engine, dispatcher, connect, lifetime.Token));
        Assert.Contains("(no game scene loaded)", terminal.Device.Text);
        server.Dispose();
        Assert.DoesNotContain(engine.Tree.Root.GetChildren(), node => node.Name.ToString().StartsWith("ReplWorld", StringComparison.Ordinal));
        Assert.Empty(engine.Errors.Drain());
    }

    [Fact]
    public void InteractivePipeClientCanCancelAndPasteAndDoesNotExitOnControlDWithADraft()
    {
        var game = typeof(showcase.CSharpTicker).Assembly;
        using var engine = new Engine("repl-client-controls", Engine.ResolveProjectDir(), "--headless") { CaptureErrors = true };
        engine.Start();
        Assert.False(engine.Iteration());
        using var dispatcher = new EngineDispatcher();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        lifetime.CancelAfter(TimeSpan.FromSeconds(30));
        var name = ReplDiscoveryInit.PipeName();
        using var server = new ReplPipeServer(name, engine, [game], dispatcher, lifetime.Token);
        ConsoleKeyInfo enter = new('\r', ConsoleKey.Enter, false, false, false);
        using var terminal = new ScriptedTerminal(null, [new ConsoleKeyInfo('\x03', ConsoleKey.C, false, false, true)], lifetime.Token);
        terminal.Paste("40+2");
        foreach (var key in new[] { new ConsoleKeyInfo('\x04', ConsoleKey.D, false, false, true), enter }.Concat(Keys.Type(":exit")).Append(enter))
            terminal.Press(key);
        var nativeCancel = false;
        terminal.BeforeRead = () =>
        {
            if (nativeCancel) return;
            nativeCancel = true;
            // An interrupt signal from outside the key input, e.g. SIGINT, reaches the host too.
            terminal.Interrupt();
        };
        using var input = new StringReader("");
        using var redirected = new StringWriter();
        Assert.Equal(0, Pump(engine, dispatcher, Task.Run(() => ReplPipeClient.RunAsync(name, input, redirected, lifetime.Token, () => terminal)), lifetime.Token));
        Assert.True(terminal.Device.Text.Contains("\u2192 42", StringComparison.Ordinal), terminal.Device.Text);
        Assert.True(nativeCancel);
        Assert.True(terminal.Restored);
        Assert.Empty(engine.Errors.Drain());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnavailablePipeExplainsHowToStartAHostAndRestoresTheTerminal(bool interactive)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        lifetime.CancelAfter(TimeSpan.FromSeconds(20));
        using var terminal = new ScriptedTerminal(null, [], lifetime.Token);
        using var input = new StringReader("");
        using var output = new StringWriter();
        var name = ReplDiscoveryInit.PipeName("2dog-gone");
        var client = ReplPipeClient.RunAsync(name, input, output, lifetime.Token, interactive ? () => terminal : null);
        if (interactive)
        {
            Assert.Equal(1, await client);
            Assert.Contains("Connecting to REPL", terminal.Device.Text);
            Assert.Contains("--repl-listen " + name, terminal.Device.Text);
            Assert.True(terminal.Restored);
        }
        else await Assert.ThrowsAsync<TimeoutException>(() => client);
    }

    [Fact]
    public void NamedWorldRegistryRejectsCollisionsAndInvalidScenesAndRemovesOnlyTheChosenWorld()
    {
        _ = typeof(showcase.CSharpTicker).Assembly;
        using var engine = new Engine("repl-world-names", Engine.ResolveProjectDir(), "--headless") { CaptureErrors = true };
        engine.Start();
        Assert.False(engine.Iteration());
        var globals = new ReplGlobals(engine);
        using var worlds = globals.worlds;
        var first = worlds.Add("lab", "res://playground.tscn");
        var second = worlds.Add("empty");
        Assert.Equal("lab", first.Name);
        Assert.Equal("ReplWorld_lab", first.Viewport!.Name.ToString());
        Assert.Throws<ArgumentException>(() => worlds.Add("lab"));
        Assert.Throws<ArgumentException>(() => worlds.Add(" "));
        Assert.Throws<ArgumentException>(() => worlds.Add("bad", "res://missing.tscn"));
        Assert.Throws<ArgumentException>(() => worlds.Remove("default"));
        Assert.Equal(["default", "lab", "empty"], worlds.Names);
        Assert.Same(second, worlds["empty"]);
        Assert.True(worlds.Remove("lab"));
        Assert.False(worlds.Remove("lab"));
        Assert.Null(first.Scene);
        Assert.Null(first.Viewport);
        Assert.NotNull(second.Scene);
        Assert.Same(globals.world, worlds["default"]);
        Assert.Empty(engine.Errors.Drain());
    }

    private static void Pump(Engine engine, EngineDispatcher dispatcher, Task task, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            token.ThrowIfCancellationRequested();
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(60), "REPL work stopped making progress.");
            Assert.False(engine.Iteration());
            dispatcher.Pump();
            Thread.Sleep(1);
        }
        task.GetAwaiter().GetResult();
    }

    private static T Pump<T>(Engine engine, EngineDispatcher dispatcher, Task<T> task, CancellationToken token)
    {
        Pump(engine, dispatcher, (Task)task, token);
        return task.GetAwaiter().GetResult();
    }

    private sealed class WaitingInput(string text) : StringReader(text)
    {
        public TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            var line = await base.ReadLineAsync(cancellationToken);
            if (line is not null) return line;
            Waiting.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return null;
        }
    }

    private sealed class FailedDestination : StringWriter
    {
        public override void WriteLine(string? value) => throw new IOException("client output failed");
    }

    private sealed class Client : IDisposable
    {
        private readonly NamedPipeClientStream pipe;
        private readonly StreamWriter writer;
        public StreamReader Reader { get; }
        private bool disposed;
        private Task<string?>? pendingLine;
        private Client(NamedPipeClientStream pipe)
        {
            this.pipe = pipe;
            Reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        }
        public static async Task<Client> ConnectAsync(string name, CancellationToken token)
        {
            var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(token);
            return new Client(pipe);
        }
        public Task SendAsync(string code) => writer.WriteLineAsync(code);
        public Task<string?> PeekLineAsync(CancellationToken token) => pendingLine ??= Reader.ReadLineAsync(token).AsTask();
        private async Task<string?> ReadLineAsync(CancellationToken token)
        {
            var line = await PeekLineAsync(token);
            pendingLine = null;
            return line;
        }
        public async Task<string> EvalAsync(string code, CancellationToken token)
        {
            var marker = "finished-" + Guid.NewGuid().ToString("N");
            await SendAsync(code);
            await SendAsync("\"" + marker + "\"");
            var result = new StringBuilder();
            while (await ReadLineAsync(token) is { } line)
            {
                if (line.Contains(marker, StringComparison.Ordinal))
                {
                    Assert.Equal("", await ReadLineAsync(token));
                    return result.ToString();
                }
                result.AppendLine(line);
            }
            throw new IOException("Session ended before its result.");
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            writer.Dispose();
            Reader.Dispose();
            pipe.Dispose();
        }
    }
}

public class ReplPipeOptionTests
{
    [Theory]
    [InlineData("--repl-listen")]
    [InlineData("--repl-connect")]
    [InlineData("--repl-connect", "--headless")]
    [InlineData("--repl-listen", "x", "--repl-connect", "x")]
    [InlineData("--repl-listen", "x", "--repl-listen", "x")]
    [InlineData("--repl-connect", "x", "--repl-connect", "x")]
    [InlineData("--repl-connect", "x", "--headless")]
    [InlineData("--repl-listen", "no/slashes")]
    [InlineData("--repl-connect", ".hidden")]
    [InlineData("--repl-connect", "a-pipe-name-of-41-characters-is-much-long")]
    public void RejectsMalformedOptions(params string[] args) => Assert.Throws<ArgumentException>(() => ReplPipeOptions.Parse(args));

    [Fact]
    public void TerminalHostsListenUnderTheirProcessAndPublishTheirOwnClient()
    {
        Assert.Equal("chosen", ReplHost.ListenName("chosen", inputRedirected: true, outputRedirected: true));
        Assert.Null(ReplHost.ListenName(null, inputRedirected: true, outputRedirected: false));
        Assert.Null(ReplHost.ListenName(null, inputRedirected: false, outputRedirected: true));
        Assert.Equal("2dog-" + Environment.ProcessId, ReplHost.ListenName(null, inputRedirected: false, outputRedirected: false));
        Assert.False(ReplDiscovery.IsValidName(""));
        Assert.True(ReplDiscovery.IsValidName("my_game.repl-2"));
        Assert.True(ReplDiscovery.IsValidName(new string('p', 40)));
        var entry = typeof(ReplHost).Assembly;
        Assert.Equal(["/opt/dotnet/dotnet", entry.Location], ReplHost.ClientCommand("/opt/dotnet/dotnet", entry));
        Assert.Equal(["C:/game/Game.repl.exe"], ReplHost.ClientCommand("C:/game/Game.repl.exe", entry));
        Assert.Equal(["dotnet"], ReplHost.ClientCommand("dotnet", null));
        Assert.Equal([""], ReplHost.ClientCommand(null, null));
    }

    [Fact]
    public void OnlyHostOptionsAreRemovedFromGodotArguments()
    {
        var options = ReplPipeOptions.Parse(["--headless", "--repl-listen", "experiment", "--verbose"]);
        Assert.Equal("experiment", options.Listen);
        Assert.Null(options.Connect);
        Assert.Equal(["--headless", "--verbose"], options.EngineArgs);
        Assert.Equal("experiment", ReplPipeOptions.Parse(["--repl-connect", "experiment"]).Connect);
    }
}

public class ReplPipeOutputTests
{
    [Fact]
    public async Task LostOutputCancelsTheSessionAndPreservesTheIoFailureIfCancellationCallbacksThrow()
    {
        using var session = new CancellationTokenSource();
        using var registration = session.Token.Register(() => throw new InvalidOperationException("callback failure"));
        var output = new ReplPipeOutput(new LostOutput(), session);
        output.Write((string?)null);
        output.WriteLine("result");
        Assert.Equal("lost connection", (await Assert.ThrowsAsync<IOException>(() => output.DisposeAsync().AsTask())).Message);
        Assert.True(session.IsCancellationRequested);
        Assert.Throws<IOException>(() => output.Write('x'));
    }

    private sealed class LostOutput : StringWriter
    {
        public override Task WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
            => Task.FromException(new IOException("lost connection"));
    }
}

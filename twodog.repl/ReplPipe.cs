using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Reflection;
using System.Text;

namespace twodog.Repl;

internal sealed record ReplPipeOptions(string? Listen, string? Connect, string[] EngineArgs)
{
    internal static ReplPipeOptions Parse(string[] args)
    {
        string? listen = null, connect = null;
        var engineArgs = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is not ("--repl-listen" or "--repl-connect")) { engineArgs.Add(args[i]); continue; }
            var option = args[i];
            if (++i == args.Length || string.IsNullOrWhiteSpace(args[i]) || args[i].StartsWith('-'))
                throw new ArgumentException(option + " requires a local pipe name.");
            if (!ReplDiscovery.IsValidName(args[i]))
                throw new ArgumentException($"Invalid pipe name '{args[i]}'. Use up to 40 letters, digits, '-', '_' or '.'.");
            if (option == "--repl-listen")
            {
                if (listen is not null) throw new ArgumentException("--repl-listen was specified twice.");
                listen = args[i];
            }
            else
            {
                if (connect is not null) throw new ArgumentException("--repl-connect was specified twice.");
                connect = args[i];
            }
        }
        if (listen is not null && connect is not null) throw new ArgumentException("Choose --repl-listen or --repl-connect.");
        if (connect is not null && engineArgs.Count > 0) throw new ArgumentException("A pipe client does not accept Godot arguments.");
        return new(listen, connect, engineArgs.ToArray());
    }
}

// A deliberately small, local transport. Terminal and plain text streams own a session per connection;
// exec requests address named sessions that outlive their connections. The evaluator has no dependency on pipes.
internal sealed class ReplPipeServer : IDisposable
{
    private readonly string name;
    private readonly Engine engine;
    private readonly Assembly[] references;
    private readonly EngineDispatcher dispatcher;
    private readonly CancellationTokenSource stopped;
    private readonly ConcurrentDictionary<int, Task> sessions = new();
    private readonly ConcurrentDictionary<int, ReplWorlds> worlds = new();
    private readonly ReplExecHost exec;
    private readonly Task accepting;
    private int nextId;
    private bool disposed;

    internal ReplPipeServer(string name, Engine engine, Assembly[] references, EngineDispatcher dispatcher, CancellationToken lifetime)
    {
        this.name = name;
        this.engine = engine;
        this.references = references;
        this.dispatcher = dispatcher;
        stopped = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        exec = new ReplExecHost(engine, references, dispatcher, stopped.Token);
        var first = CreatePipe();
        accepting = Task.Run(() => AcceptAsync(first));
    }

    private NamedPipeServerStream CreatePipe() => new(name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private async Task AcceptAsync(NamedPipeServerStream pipe)
    {
        try
        {
            while (!stopped.IsCancellationRequested)
            {
                try { await pipe.WaitForConnectionAsync(stopped.Token).ConfigureAwait(false); }
                catch (IOException error) when (error.HResult == unchecked((int)0x800700E8))
                {
                    // Windows ERROR_NO_DATA: the client disconnected before we accepted it.
                    pipe.Dispose();
                    pipe = CreatePipe();
                    continue;
                }
                var id = Interlocked.Increment(ref nextId);
                var connected = pipe;
                sessions[id] = Task.Run(() => ServeAsync(id, connected));
                pipe = CreatePipe();
            }
        }
        catch (OperationCanceledException) when (stopped.IsCancellationRequested) { }
        finally { pipe.Dispose(); }
    }

    private async Task ServeAsync(int id, NamedPipeServerStream pipe)
    {
        using var sessionLifetime = CancellationTokenSource.CreateLinkedTokenSource(stopped.Token);
        ReplGlobals? globals = null;
        try
        {
            using (pipe)
            using (var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true))
            using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true })
            {
                await using var output = new ReplPipeOutput(writer, sessionLifetime);
                var first = await reader.ReadLineAsync(sessionLifetime.Token).ConfigureAwait(false);
                if (first is null) return;
                if (first.StartsWith(ReplProtocol.ExecHello, StringComparison.Ordinal))
                {
                    await exec.ServeAsync(first, reader, output, sessionLifetime.Token).ConfigureAwait(false);
                    return;
                }
                globals = new ReplGlobals(engine);
                worlds[id] = globals.worlds;
                if (first == ReplRemoteTerminal.Hello + ";ansi" || first == ReplRemoteTerminal.Hello + ";plain")
                {
                    await using var terminal = new ReplRemoteTerminal(reader, output, sessionLifetime, first.EndsWith(";ansi", StringComparison.Ordinal));
                    var scenePath = await dispatcher.InvokeAsync(() => Task.FromResult(engine.Tree.CurrentScene?.SceneFilePath ?? "(no game scene loaded)"), sessionLifetime.Token).ConfigureAwait(false);
                    await ReplHost.ReadEvalPrintAsync(globals, scenePath, references, dispatcher, terminal, sessionLifetime.Token).ConfigureAwait(false);
                    terminal.End();
                    // Flush the final frame before stopping the protocol reader.
                    await output.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    using var input = new ReplPrefixedReader(first, reader);
                    var terminal = new PipeTerminal(input, output);
                    await ReplHost.ReadEvalPrintAsync(globals, "", references, dispatcher, terminal, sessionLifetime.Token, cancelOnEof: true).ConfigureAwait(false);
                }
            }
        }
        catch (IOException) { /* A disconnected client only ends its own session. */ }
        catch (OperationCanceledException) when (sessionLifetime.IsCancellationRequested) { }
        finally
        {
            try
            {
                if (globals is not null)
                    await dispatcher.InvokeAsync(() => { globals.worlds.Dispose(); return Task.FromResult(true); }).ConfigureAwait(false);
                worlds.TryRemove(id, out _);
            }
            catch (OperationCanceledException) { /* Host disposal closes remaining worlds on the owner thread. */ }
            sessions.TryRemove(id, out _);
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { stopped.Cancel(); }
        finally
        {
            try
            {
                accepting.GetAwaiter().GetResult();
                var cleanup = Task.WhenAll(sessions.Values.Append(exec.CloseAllAsync()));
                while (!cleanup.IsCompleted)
                {
                    dispatcher.Pump();
                    (SynchronizationContext.Current as Godot.GodotSynchronizationContext)?.ExecutePendingContinuations();
                    Thread.Sleep(1);
                }
                cleanup.GetAwaiter().GetResult();
            }
            finally
            {
                try
                {
                    foreach (var owned in worlds.Values) owned.Dispose();
                    exec.Dispose();
                }
                finally { stopped.Dispose(); }
            }
        }
    }

    private sealed class PipeTerminal(TextReader input, TextWriter output) : IReplTerminal
    {
        public bool Interactive => false;
        public TextReader Input => input;
        public ReplOutput Output { get; } = new(output, output, false, false);
        public string HistoryFile => "";
        public void Restore() { }
    }
}

internal static class ReplPipeClient
{
    internal static Task<int> RunAsync(string name) => RunAsync(name, Console.In, Console.Out,
        createTerminal: !Console.IsInputRedirected && !Console.IsOutputRedirected ? () => new ConsoleClientTerminal() : null);

    internal static async Task<int> RunAsync(string name, TextReader input, TextWriter destination, CancellationToken cancellationToken = default,
        Func<IReplClientTerminal>? createTerminal = null)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var terminal = createTerminal?.Invoke();
        try
        {
            terminal?.Device.Write($"Connecting to REPL '{name}'...\r\n");
            return await ConnectAsync(name, input, destination, terminal, lifetime).ConfigureAwait(false);
        }
        catch (TimeoutException) when (terminal is not null)
        {
            terminal.Device.Write(Paint(terminal, $"Could not connect to REPL '{name}'. Start the host with --repl-listen {name}.", "31") + "\r\n");
            return 1;
        }
        finally { terminal?.Restore(); }
    }

    private static string Paint(IReplClientTerminal terminal, string text, string style) => terminal.Color ? $"\x1b[{style}m{text}\x1b[0m" : text;

    private static async Task<int> ConnectAsync(string name, TextReader input, TextWriter destination, IReplClientTerminal? terminal,
        CancellationTokenSource lifetime)
    {
        var lifetimeToken = lifetime.Token;
        using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(5000, lifetimeToken).ConfigureAwait(false);
        using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        terminal?.Device.Write(Paint(terminal, $"Connected to REPL '{name}'.", "2") + " Enter submits; Shift+Enter adds a line; Ctrl+C cancels work; :exit disconnects.\r\n");
        if (terminal is not null) return await ReplRemoteTerminalClient.RunAsync(reader, writer, terminal, lifetime).ConfigureAwait(false);
        var output = Task.Run(async () =>
        {
            try
            {
                while (await reader.ReadLineAsync(lifetimeToken).ConfigureAwait(false) is { } line)
                {
                    destination.WriteLine(line);
                }
            }
            finally { lifetime.Cancel(); }
        });
        using var sendGate = new SemaphoreSlim(1, 1);
        async Task SendAsync(string line)
        {
            await sendGate.WaitAsync().ConfigureAwait(false);
            try { await writer.WriteLineAsync(line).ConfigureAwait(false); }
            finally { sendGate.Release(); }
        }
        void Cancel(object? sender, ConsoleCancelEventArgs args)
        {
            args.Cancel = true;
            _ = CancelAsync();
        }
        async Task CancelAsync()
        {
            try { await SendAsync(":cancel").ConfigureAwait(false); }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }
        Console.CancelKeyPress += Cancel;
        try
        {
            var exitSent = false;
            while (!lifetime.IsCancellationRequested)
            {
                var line = await Task.Run(async () => await input.ReadLineAsync(lifetimeToken).ConfigureAwait(false)).WaitAsync(lifetimeToken).ConfigureAwait(false);
                if (line is null) break;
                await SendAsync(line).ConfigureAwait(false);
                if (line.Trim() == ":exit") { exitSent = true; break; }
            }
            if (!exitSent && !lifetime.IsCancellationRequested) await SendAsync(":exit").ConfigureAwait(false);
            await output.ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            // The reader cancels input in its finally block, just before its Task
            // becomes faulted. Await it so that race cannot hide an output failure.
            try { await output.ConfigureAwait(false); }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            return 0;
        }
        finally
        {
            Console.CancelKeyPress -= Cancel;
        }
    }

}

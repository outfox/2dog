using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PrettyPrompt.Consoles;

namespace twodog.Repl;

internal sealed record ReplTerminalMessage(int Id, string Method, string? Text = null, bool Value = false,
    ReplTerminalState? State = null, ReplTerminalKey? Key = null);
internal sealed record ReplTerminalState(int CursorTop, int BufferWidth, int WindowHeight, int WindowTop);
internal sealed record ReplTerminalKey(char Character, ConsoleKey Key, ConsoleModifiers Modifiers, string? Paste);

// Interactive clients forward terminal operations, so the real session supplies
// highlighting, completion, navigation and all editing behavior. Raw connections
// still exchange ordinary UTF-8 submissions/results without this protocol.
internal sealed class ReplRemoteTerminal : IReplTerminal, IAsyncDisposable
{
    internal const string Hello = "\u001e2dog.repl/terminal/1";
    private readonly ReplTerminalConnection connection;
    private readonly RemoteConsole console;
    public IConsole Editor => console;
    public bool Interactive => true;
    public TextReader Input => TextReader.Null;
    public ReplOutput Output { get; }
    public ReplPromptOutput PromptOutput { get; }
    public string HistoryFile => ""; // Per-connection in-memory history; no competing history-file writers.

    public ReplRemoteTerminal(TextReader reader, ReplPipeOutput writer, CancellationTokenSource lifetime, bool color)
    {
        connection = new(reader, writer, lifetime);
        console = new(connection, lifetime.Token);
        PromptOutput = new(console);
        console.Output = PromptOutput;
        Output = new(PromptOutput.Wrap(new RemoteWriter(connection, false)), PromptOutput.Wrap(new RemoteWriter(connection, true)), color, color);
    }

    public string? TakePaste() => console.TakePaste();
    public void SetCancellationHandler(Action? cancel) => connection.Cancel = cancel;
    public void Restore() { }
    public void End() => connection.Send(new(0, "end"));
    public ValueTask DisposeAsync() => connection.DisposeAsync();

    private sealed class RemoteWriter(ReplTerminalConnection connection, bool error) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
        public override void Write(string? value) => connection.Send(new(0, "write", value, error));
        public override void Write(char value) => Write(value.ToString());
        public override void WriteLine(string? value) => Write(value + NewLine);
    }

    private sealed class RemoteConsole(ReplTerminalConnection connection, CancellationToken lifetime) : IConsole
    {
        public ReplPromptOutput Output { get; set; } = null!;
        private Task<ReplTerminalMessage>? reading;
        private string? paste;
        private bool captureControlC;
        private ReplTerminalState State => connection.RequestAsync("state").GetAwaiter().GetResult().State!;
        public int CursorTop => State.CursorTop;
        public int BufferWidth => State.BufferWidth;
        public int WindowHeight => State.WindowHeight;
        public int WindowTop => State.WindowTop;
        public bool IsErrorRedirected => false;
        public bool KeyAvailable => !Output.Restoring && connection.RequestAsync("available").GetAwaiter().GetResult().Value;
        public bool CaptureControlC
        {
            get => captureControlC;
            set { captureControlC = value; connection.Send(new(0, "capture", Value: value)); }
        }
        // Native Ctrl+C arrives separately from key input, including while draining work.
        public event ConsoleCancelEventHandler CancelKeyPress { add { } remove { } }
        public string? TakePaste() { var value = paste; paste = null; return value; }
        public ConsoleKeyInfo ReadKey(bool intercept)
        {
            lifetime.ThrowIfCancellationRequested();
            if (Output.ReadRestoreKey() is { } restored) return restored;
            reading ??= connection.RequestAsync("read");
            while (!reading.IsCompleted)
            {
                lifetime.ThrowIfCancellationRequested();
                Output.CheckForOutput();
                Thread.Sleep(5);
            }
            var key = reading.GetAwaiter().GetResult().Key!;
            reading = null;
            paste = key.Paste;
            return new(key.Character, key.Key, key.Modifiers.HasFlag(ConsoleModifiers.Shift),
                key.Modifiers.HasFlag(ConsoleModifiers.Alt), key.Modifiers.HasFlag(ConsoleModifiers.Control));
        }
        public void Write(string? value) => connection.Send(new(0, "write", value));
        public void WriteLine(string? value) => Write(value + Environment.NewLine);
        public void WriteError(string? value) => connection.Send(new(0, "write", value, true));
        public void WriteErrorLine(string? value) => WriteError(value + Environment.NewLine);
        public void Write(ReadOnlySpan<char> value) => Write(value.ToString());
        public void WriteLine(ReadOnlySpan<char> value) => WriteLine(value.ToString());
        public void WriteError(ReadOnlySpan<char> value) => WriteError(value.ToString());
        public void WriteErrorLine(ReadOnlySpan<char> value) => WriteErrorLine(value.ToString());
        public void Clear() => connection.Send(new(0, "clear"));
        public void ShowCursor() => connection.Send(new(0, "cursor", Value: true));
        public void HideCursor() => connection.Send(new(0, "cursor"));
        public void InitVirtualTerminalProcessing() => connection.Send(new(0, "init"));
        public void SetNewlineAutoReturn(bool enabled) => connection.Send(new(0, "newline", Value: enabled));
        public void SetModifyOtherKeys(bool enabled) => connection.Send(new(0, "modify", Value: enabled));
    }
}

internal sealed class ReplTerminalConnection : IAsyncDisposable
{
    private readonly ReplPipeOutput writer;
    private readonly CancellationTokenSource session;
    private readonly CancellationTokenSource stopped;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<ReplTerminalMessage>> requests = new();
    private readonly Task receiving;
    private int nextId;
    public Action? Cancel { get; set; }

    public ReplTerminalConnection(TextReader reader, ReplPipeOutput writer, CancellationTokenSource session)
    {
        this.writer = writer;
        this.session = session;
        stopped = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        receiving = Task.Run(async () =>
        {
            Exception failure = new IOException("The connected terminal closed.");
            try
            {
                while (await reader.ReadLineAsync(stopped.Token).ConfigureAwait(false) is { } line)
                {
                    var message = JsonSerializer.Deserialize<ReplTerminalMessage>(line) ?? throw new IOException("Empty terminal message.");
                    if (message.Method == "cancel") Cancel?.Invoke();
                    else if (requests.TryRemove(message.Id, out var request))
                    {
                        if (message.Method == "error") request.TrySetException(new IOException(message.Text));
                        else request.TrySetResult(message);
                    }
                }
            }
            catch (Exception error) when (error is IOException or OperationCanceledException or JsonException) { failure = error; }
            finally
            {
                // Publish the ended session before waking request continuations. Otherwise a
                // failed request can resume while its session still appears live.
                try { session.Cancel(); }
                catch (AggregateException) { /* A user cancellation callback cannot prevent disconnect cleanup. */ }
                foreach (var request in requests.Values) request.TrySetException(failure);
            }
        });
    }

    public void Send(ReplTerminalMessage message) => writer.WriteLine(JsonSerializer.Serialize(message));
    public async Task<ReplTerminalMessage> RequestAsync(string method)
    {
        var id = Interlocked.Increment(ref nextId);
        var result = new TaskCompletionSource<ReplTerminalMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        requests[id] = result;
        try
        {
            Send(new(id, method));
            return await result.Task.WaitAsync(session.Token).ConfigureAwait(false);
        }
        finally { requests.TryRemove(id, out _); }
    }

    public async ValueTask DisposeAsync()
    {
        stopped.Cancel();
        await receiving.ConfigureAwait(false);
        stopped.Dispose();
    }
}

internal static class ReplRemoteTerminalClient
{
    public static async Task<int> RunAsync(TextReader reader, TextWriter writer, IReplTerminal terminal, CancellationTokenSource lifetime)
    {
        var token = lifetime.Token;
        var console = terminal.Editor;
        using var sendGate = new SemaphoreSlim(1, 1);
        async Task SendAsync(ReplTerminalMessage message)
        {
            await sendGate.WaitAsync(token).ConfigureAwait(false);
            try { await writer.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(), token).ConfigureAwait(false); }
            finally { sendGate.Release(); }
        }
        Task? reading = null;
        void Cancel(object? sender, ConsoleCancelEventArgs args)
        {
            args.Cancel = true;
            _ = SendCancellationAsync();
        }
        async Task SendCancellationAsync()
        {
            try { await SendAsync(new(0, "cancel")).ConfigureAwait(false); }
            catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException) { }
        }
        console.CancelKeyPress += Cancel;
        try
        {
            await writer.WriteLineAsync(ReplRemoteTerminal.Hello + (terminal.Color ? ";ansi" : ";plain")).ConfigureAwait(false);
            while (await reader.ReadLineAsync(token).ConfigureAwait(false) is { } line)
            {
                var message = JsonSerializer.Deserialize<ReplTerminalMessage>(line) ?? throw new IOException("Empty terminal message.");
                switch (message.Method)
                {
                    case "write":
                        var text = terminal.Color ? message.Text : Regex.Replace(message.Text ?? "", @"\x1b\[[0-9;:]*m", "");
                        terminal.Output.Write(text, message.Value);
                        break;
                    case "state":
                        await SendAsync(new(message.Id, "response", State: new(console.CursorTop, console.BufferWidth, console.WindowHeight, console.WindowTop))).ConfigureAwait(false);
                        break;
                    case "available":
                        await SendAsync(new(message.Id, "response", Value: console.KeyAvailable)).ConfigureAwait(false);
                        break;
                    case "read":
                        reading = Task.Run(async () =>
                        {
                            try
                            {
                                var key = console.ReadKey(true);
                                var paste = key is { Key: ConsoleKey.Insert, Modifiers: ConsoleModifiers.Shift } ? terminal.TakePaste() : null;
                                await SendAsync(new(message.Id, "response", Key: new(key.KeyChar, key.Key, key.Modifiers, paste))).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                            catch (Exception error) { await SendAsync(new(message.Id, "error", error.Message)).ConfigureAwait(false); }
                        });
                        break;
                    case "capture": console.CaptureControlC = message.Value; break;
                    case "newline": console.SetNewlineAutoReturn(message.Value); break;
                    case "modify": console.SetModifyOtherKeys(message.Value); break;
                    case "cursor": if (message.Value) console.ShowCursor(); else console.HideCursor(); break;
                    case "clear": console.Clear(); break;
                    case "init": console.InitVirtualTerminalProcessing(); break;
                    case "end": return 0;
                    default: throw new IOException("Unknown terminal operation: " + message.Method);
                }
            }
            return 0;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return 0; }
        finally
        {
            console.CancelKeyPress -= Cancel;
            lifetime.Cancel();
            if (reading is not null) await reading.ConfigureAwait(false);
        }
    }
}

internal sealed class ReplPrefixedReader(string firstLine, TextReader reader) : TextReader
{
    private string? first = firstLine;
    public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        => Interlocked.Exchange(ref first, null) is { } line ? ValueTask.FromResult<string?>(line) : reader.ReadLineAsync(cancellationToken);
}

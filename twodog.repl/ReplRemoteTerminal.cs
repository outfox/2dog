using System.Text.Json;
using System.Threading.Channels;

namespace twodog.Repl;

/// <summary>Client to host: "key", "text" (a paste), "size" and "cancel" (an interrupt signal).
/// Host to client: "write" (terminal output) and "end".</summary>
internal sealed record ReplTerminalMessage(string Method, string? Text = null, ReplTerminalKey? Key = null, int Width = 0, int Height = 0);
internal sealed record ReplTerminalKey(char Character, ConsoleKey Key, ConsoleModifiers Modifiers);

// Interactive clients forward keys and draw what the host renders, so the session on the host supplies
// highlighting, completion, navigation and all editing behavior. Raw connections exchange plain UTF-8 text instead.
internal sealed class ReplRemoteTerminal : IReplTerminal, IAsyncDisposable
{
    internal const string Hello = "\u001e2dog.repl/terminal/2";
    private readonly Channel<TerminalInput> inputs = Channel.CreateUnbounded<TerminalInput>(new UnboundedChannelOptions { SingleReader = true });
    private readonly RemoteDevice device;
    private readonly CancellationTokenSource stopped;
    private readonly Task receiving;
    private Action? cancel;
    public bool Interactive => true;
    public TextReader Input => TextReader.Null;
    public ReplOutput Output { get; }
    public ReplScreen Screen { get; }
    public bool Color { get; }
    public string HistoryFile => ""; // Per-connection in-memory history; no competing history-file writers.

    public ReplRemoteTerminal(TextReader reader, ReplPipeOutput writer, CancellationTokenSource lifetime, bool color)
    {
        Color = color;
        device = new RemoteDevice(writer);
        Screen = new ReplScreen(device, color);
        Output = new ReplOutput(new ScreenWriter(Screen, false), new ScreenWriter(Screen, true), color, color);
        stopped = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        receiving = Task.Run(() => ReceiveAsync(reader, lifetime));
    }

    private async Task ReceiveAsync(TextReader reader, CancellationTokenSource lifetime)
    {
        Exception failure = new IOException("The connected terminal closed.");
        try
        {
            while (await reader.ReadLineAsync(stopped.Token).ConfigureAwait(false) is { } line)
            {
                var message = JsonSerializer.Deserialize<ReplTerminalMessage>(line) ?? throw new IOException("Empty terminal message.");
                switch (message.Method)
                {
                    case "key" when message.Key is { } key:
                        inputs.Writer.TryWrite(new KeyInput(new ConsoleKeyInfo(key.Character, key.Key, key.Modifiers.HasFlag(ConsoleModifiers.Shift),
                            key.Modifiers.HasFlag(ConsoleModifiers.Alt), key.Modifiers.HasFlag(ConsoleModifiers.Control))));
                        break;
                    case "text":
                        inputs.Writer.TryWrite(new TextInput(message.Text ?? ""));
                        break;
                    case "size":
                        device.Resize(message.Width, message.Height);
                        Screen.Invalidate();
                        break;
                    case "cancel":
                        Volatile.Read(ref cancel)?.Invoke();
                        break;
                    default:
                        throw new IOException("Unknown terminal operation: " + message.Method);
                }
            }
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or JsonException) { failure = error; }
        finally
        {
            // Publish the ended session before waking the reader, so it never resumes into a live-looking session.
            try { lifetime.Cancel(); }
            catch (AggregateException) { /* A user cancellation callback cannot prevent disconnect cleanup. */ }
            inputs.Writer.TryComplete(failure);
        }
    }

    public async Task<TerminalInput> ReadAsync(CancellationToken token)
    {
        try { return await inputs.Reader.ReadAsync(token).ConfigureAwait(false); }
        catch (ChannelClosedException closed) { throw closed.InnerException ?? new IOException("The connected terminal closed."); }
    }

    public void SetCancellationHandler(Action? handler) => Volatile.Write(ref cancel, handler);

    public void Restore() => Screen.Dispose();

    /// <summary>Tells the client that the session ended, after the screen's final output.</summary>
    public void End()
    {
        Screen.Dispose();
        device.Send(new ReplTerminalMessage("end"));
    }

    public async ValueTask DisposeAsync()
    {
        Screen.Dispose();
        await stopped.CancelAsync().ConfigureAwait(false);
        await receiving.ConfigureAwait(false);
        stopped.Dispose();
    }

    private sealed class RemoteDevice(ReplPipeOutput writer) : ITerminalDevice
    {
        // Width and height in one atomic value; the client reports its size after connecting.
        private long size = 120L << 32 | 40;
        public (int Width, int Height) Size => ((int)(Interlocked.Read(ref size) >> 32), (int)Interlocked.Read(ref size));
        public void Resize(int width, int height) => Interlocked.Exchange(ref size, (long)Math.Max(1, width) << 32 | (uint)Math.Max(1, height));
        public void Write(string ansi) => Send(new ReplTerminalMessage("write", ansi));
        public void Send(ReplTerminalMessage message) => writer.WriteLine(JsonSerializer.Serialize(message));
    }
}

/// <summary>The client end of an interactive connection: its own keys and terminal, the host's editor.</summary>
internal interface IReplClientTerminal
{
    ITerminalDevice Device { get; }
    bool Color { get; }
    Task<TerminalInput> ReadAsync(CancellationToken token);
    /// <summary>Raised for an interrupt that bypassed key input, such as SIGINT.</summary>
    event Action? Interrupted;
    void Restore();
}

internal sealed class ConsoleClientTerminal : IReplClientTerminal
{
    private readonly ITerminalDevice device;
    private readonly ReplKeyReader keys;
    private readonly bool? originalControlC;
    public ITerminalDevice Device => device;
    public bool Color => ReplOutput.ColorEnabled;
    public event Action? Interrupted;

    public ConsoleClientTerminal() : this(new ConsoleDevice(), ReplConsole.ConsoleKeys()) { }

    internal ConsoleClientTerminal(ITerminalDevice device, ReplKeyReader keys)
    {
        this.device = device;
        this.keys = keys;
        originalControlC = ReplConsole.ControlCAsInput(true);
        Console.CancelKeyPress += OnCancel;
    }

    internal void OnCancel(object? sender, ConsoleCancelEventArgs args)
    {
        args.Cancel = true;
        Interrupted?.Invoke();
    }

    public Task<TerminalInput> ReadAsync(CancellationToken token) => ReplConsole.ReadKeysAsync(keys, token);

    public void Restore()
    {
        Console.CancelKeyPress -= OnCancel;
        if (originalControlC is { } controlC) ReplConsole.ControlCAsInput(controlC);
        (device as IDisposable)?.Dispose();
    }
}

internal static class ReplRemoteTerminalClient
{
    public static async Task<int> RunAsync(TextReader reader, TextWriter writer, IReplClientTerminal terminal, CancellationTokenSource lifetime)
    {
        var token = lifetime.Token;
        using var sendGate = new SemaphoreSlim(1, 1);
        async Task SendAsync(ReplTerminalMessage message)
        {
            await sendGate.WaitAsync(token).ConfigureAwait(false);
            try { await writer.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(), token).ConfigureAwait(false); }
            finally { sendGate.Release(); }
        }
        void Interrupt() => _ = SendQuietlyAsync(new ReplTerminalMessage("cancel"));
        async Task SendQuietlyAsync(ReplTerminalMessage message)
        {
            try { await SendAsync(message).ConfigureAwait(false); }
            catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException) { }
        }
        terminal.Interrupted += Interrupt;
        Task? input = null;
        try
        {
            await writer.WriteLineAsync(ReplRemoteTerminal.Hello + (terminal.Color ? ";ansi" : ";plain")).ConfigureAwait(false);
            var size = terminal.Device.Size;
            await SendAsync(new ReplTerminalMessage("size", Width: size.Width, Height: size.Height)).ConfigureAwait(false);
            input = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        var read = terminal.ReadAsync(token);
                        // Report resizes while waiting for keys.
                        while (!read.IsCompleted)
                        {
                            await Task.WhenAny(read, Task.Delay(250, token)).ConfigureAwait(false);
                            if (terminal.Device.Size is var current && current != size)
                            {
                                size = current;
                                await SendAsync(new ReplTerminalMessage("size", Width: size.Width, Height: size.Height)).ConfigureAwait(false);
                            }
                        }
                        await SendAsync(await read.ConfigureAwait(false) switch
                        {
                            KeyInput key => new ReplTerminalMessage("key", Key: new(key.Key.KeyChar, key.Key.Key, key.Key.Modifiers)),
                            TextInput text => new ReplTerminalMessage("text", text.Text),
                            _ => throw new InvalidOperationException(),
                        }).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (IOException) { lifetime.Cancel(); }
            }, token);
            while (await reader.ReadLineAsync(token).ConfigureAwait(false) is { } line)
            {
                var message = JsonSerializer.Deserialize<ReplTerminalMessage>(line) ?? throw new IOException("Empty terminal message.");
                switch (message.Method)
                {
                    case "write": terminal.Device.Write(message.Text ?? ""); break;
                    case "end": return 0;
                    default: throw new IOException("Unknown terminal operation: " + message.Method);
                }
            }
            return 0;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return 0; }
        finally
        {
            terminal.Interrupted -= Interrupt;
            lifetime.Cancel();
            if (input is not null)
            {
                try { await input.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
        }
    }
}

internal sealed class ReplPrefixedReader(string firstLine, TextReader reader) : TextReader
{
    private string? first = firstLine;
    public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        => Interlocked.Exchange(ref first, null) is { } line ? ValueTask.FromResult<string?>(line) : reader.ReadLineAsync(cancellationToken);
}

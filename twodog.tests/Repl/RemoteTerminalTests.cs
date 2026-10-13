using System.Text.Json;
using System.Threading.Channels;
using twodog.Repl;

namespace twodog.tests.ReplTests;

public class RemoteTerminalTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TerminalRelayPreservesKeysPasteModesDimensionsAndNegotiatedColors(bool color)
    {
        using var serverLifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var clientLifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var toServer = new Messages();
        using var toClient = new Messages();
        using var clientWriter = new MessageWriter(toServer);
        using var serverWriter = new MessageWriter(toClient);
        ConsoleKeyInfo letter = new('X', ConsoleKey.X, true, true, true);
        ConsoleKeyInfo paste = new('\0', ConsoleKey.Insert, true, false, false);
        using var client = new HostLifecycleTests.Terminal(null, [letter, paste], clientLifetime.Token) { Color = color, Paste = "a\nb" };
        var running = ReplRemoteTerminalClient.RunAsync(toClient, clientWriter, client, clientLifetime);
        Assert.Equal(ReplRemoteTerminal.Hello + (color ? ";ansi" : ";plain"), await toServer.ReadLineAsync(serverLifetime.Token));
        await using var output = new ReplPipeOutput(serverWriter, serverLifetime);
        await using var remote = new ReplRemoteTerminal(toServer, output, serverLifetime, color);
        var console = remote.Editor;
        Assert.True(remote.Interactive);
        Assert.Same(TextReader.Null, remote.Input);
        Assert.Equal("", remote.HistoryFile);
        remote.Restore();
        Assert.Equal(0, console.CursorTop);
        Assert.Equal(120, console.BufferWidth);
        Assert.Equal(40, console.WindowHeight);
        Assert.Equal(0, console.WindowTop);
        Assert.False(console.IsErrorRedirected);
        Assert.False(console.KeyAvailable);
        console.CaptureControlC = true;
        Assert.True(console.CaptureControlC);
        console.CaptureControlC = false;
        console.InitVirtualTerminalProcessing();
        console.HideCursor();
        console.ShowCursor();
        console.Clear();
        console.SetNewlineAutoReturn(false);
        console.SetModifyOtherKeys(true);
        console.SetModifyOtherKeys(false);
        Assert.Equal(letter, console.ReadKey(true));
        Assert.Null(remote.TakePaste());
        Assert.Equal(paste, console.ReadKey(true));
        Assert.Equal("a\nb", remote.TakePaste());
        Assert.Null(remote.TakePaste());
        console.Write("string");
        console.WriteLine("line");
        console.Write("span".AsSpan());
        console.WriteLine("spanline".AsSpan());
        console.WriteError("error");
        console.WriteErrorLine("errorline");
        console.WriteError("errorspan".AsSpan());
        console.WriteErrorLine("errorspanline".AsSpan());
        remote.Output.Result("42");
        remote.Output.Error("failure");
        // Even a colorless client needs cursor control sequences for the editor.
        console.Write("\x1b[31mred\x1b[0m\x1b[2K");
        console.Write((string?)null);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        remote.SetCancellationHandler(() => cancelled.TrySetResult());
        Assert.True(client.Prompt.CancelExecution());
        await cancelled.Task.WaitAsync(serverLifetime.Token);
        remote.SetCancellationHandler(null);
        Assert.Equal(120, console.BufferWidth); // Barrier after the ordered terminal writes.
        Assert.False(client.Prompt.CaptureControlC);
        Assert.False(client.Prompt.NewlineAutoReturn);
        Assert.Equal(1, client.Prompt.Clears);
        Assert.Equal(["init", "hide", "show", "modify:True", "modify:False"], client.Prompt.Operations);
        Assert.Contains("stringline" + Environment.NewLine + "spanspanline", client.Stdout.ToString());
        Assert.Contains("→ ", client.Stdout.ToString());
        Assert.Contains("42", client.Stdout.ToString());
        Assert.Contains("failure", client.Stderr.ToString());
        Assert.Contains("errorspan", client.Stderr.ToString());
        Assert.Contains("\x1b[2K", client.Stdout.ToString());
        Assert.Equal(color, client.Stdout.ToString().Contains("\x1b[31m", StringComparison.Ordinal));
        Assert.Equal(color, client.Stderr.ToString().Contains("\x1b[31m", StringComparison.Ordinal));
        remote.End();
        await output.DisposeAsync();
        Assert.Equal(0, await running.WaitAsync(serverLifetime.Token));
    }

    [Fact]
    public async Task KeyboardFailuresAreReportedToTheHostWithoutLosingTheConnection()
    {
        using var serverLifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var clientLifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var toServer = new Messages();
        using var toClient = new Messages();
        using var clientWriter = new MessageWriter(toServer);
        using var serverWriter = new MessageWriter(toClient);
        using var client = new HostLifecycleTests.Terminal(null, [], clientLifetime.Token);
        client.Prompt.BeforeRead = () => throw new IOException("keyboard disconnected");
        var running = ReplRemoteTerminalClient.RunAsync(toClient, clientWriter, client, clientLifetime);
        await toServer.ReadLineAsync(serverLifetime.Token);
        await using var output = new ReplPipeOutput(serverWriter, serverLifetime);
        await using var remote = new ReplRemoteTerminal(toServer, output, serverLifetime, true);
        Assert.Equal("keyboard disconnected", (await Assert.ThrowsAsync<IOException>(() => Task.Run(() => remote.Editor.ReadKey(true)))).Message);
        client.Prompt.BeforeRead = null;
        remote.End();
        await output.DisposeAsync();
        Assert.Equal(0, await running.WaitAsync(serverLifetime.Token));
    }

    [Theory]
    [InlineData("{broken", typeof(JsonException))]
    [InlineData("null", typeof(IOException))]
    [InlineData("{\"Id\":1,\"Method\":\"unknown\"}", typeof(IOException))]
    public async Task InvalidClientOperationsFailAndReleaseTheInputReader(string frame, Type error)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var input = new Messages();
        using var output = new StringWriter();
        using var terminal = new HostLifecycleTests.Terminal(null, [], lifetime.Token);
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        terminal.Prompt.BeforeRead = () => reading.TrySetResult();
        var running = ReplRemoteTerminalClient.RunAsync(input, output, terminal, lifetime);
        input.Send(JsonSerializer.Serialize(new ReplTerminalMessage(2, "read")));
        await reading.Task.WaitAsync(lifetime.Token);
        input.Send(frame);
        await Assert.ThrowsAsync(error, () => running);
        Assert.True(lifetime.IsCancellationRequested);
        Assert.False(terminal.Prompt.CancelExecution());
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData(null)]
    public async Task LostOrInvalidResponsesCancelTheSessionIncludingPendingRequests(string? frame)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var registration = lifetime.Token.Register(() => throw new InvalidOperationException("user callback"));
        using var input = new Messages();
        using var outputFrames = new Messages();
        using var writer = new MessageWriter(outputFrames);
        var output = new ReplPipeOutput(writer, lifetime);
        await using var connection = new ReplTerminalConnection(input, output, lifetime);
        var requests = new[] { connection.RequestAsync("state"), connection.RequestAsync("available") };
        foreach (var request in requests) await outputFrames.ReadLineAsync(lifetime.Token);
        if (frame is null) input.Complete();
        else input.Send(frame);
        foreach (var request in requests)
        {
            await Assert.ThrowsAnyAsync<Exception>(() => request);
            Assert.True(lifetime.IsCancellationRequested);
        }
        try { await output.DisposeAsync(); }
        catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task ClosingAnIdleClientCancelsAndJoinsItsWaitingKeyRead()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var input = new Messages();
        using var output = new StringWriter();
        using var terminal = new HostLifecycleTests.Terminal(null, [], lifetime.Token);
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        terminal.Prompt.BeforeRead = () => reading.TrySetResult();
        var running = ReplRemoteTerminalClient.RunAsync(input, output, terminal, lifetime);
        input.Send(JsonSerializer.Serialize(new ReplTerminalMessage(1, "read")));
        await reading.Task.WaitAsync(lifetime.Token);
        input.Complete();
        Assert.Equal(0, await running);
        Assert.True(lifetime.IsCancellationRequested);
    }

    [Fact]
    public async Task CancellingAnIdleClientEndsTheProtocolAndDetachesItsCancelHandler()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var input = new Messages();
        using var output = new StringWriter();
        using var terminal = new HostLifecycleTests.Terminal(null, [], lifetime.Token);
        var running = ReplRemoteTerminalClient.RunAsync(input, output, terminal, lifetime);
        lifetime.Cancel();
        Assert.Equal(0, await running);
        Assert.False(terminal.Prompt.CancelExecution());
    }

    private sealed class Messages : TextReader
    {
        private readonly Channel<string> lines = Channel.CreateUnbounded<string>();
        public void Send(string line) => lines.Writer.TryWrite(line);
        public void Complete() => lines.Writer.TryComplete();
        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            while (await lines.Reader.WaitToReadAsync(cancellationToken))
                if (lines.Reader.TryRead(out var line)) return line;
            return null;
        }
    }

    private sealed class MessageWriter(Messages destination) : StringWriter
    {
        public override Task WriteLineAsync(string? value) { destination.Send(value ?? ""); return Task.CompletedTask; }
        public override Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            destination.Send(buffer.ToString());
            return Task.CompletedTask;
        }
        public override Task WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
            => WriteLineAsync(buffer.ToString().TrimEnd('\r', '\n').AsMemory(), cancellationToken);
    }
}

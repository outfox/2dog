using System.Text.Json;
using System.Threading.Channels;
using twodog.Repl;

namespace twodog.tests.ReplTests;

public class RemoteTerminalTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TerminalRelayForwardsKeysPastesSizeAndInterruptsAndDrawsTheHostScreen(bool color)
    {
        using var serverLifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var clientLifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var toServer = new Messages();
        using var toClient = new Messages();
        using var clientWriter = new MessageWriter(toServer);
        using var serverWriter = new MessageWriter(toClient);
        ConsoleKeyInfo letter = new('X', ConsoleKey.X, true, true, true);
        using var client = new ScriptedTerminal(null, [letter], clientLifetime.Token) { Color = color };
        client.Paste("a\nb");
        var running = ReplRemoteTerminalClient.RunAsync(toClient, clientWriter, client, clientLifetime);
        Assert.Equal(ReplRemoteTerminal.Hello + (color ? ";ansi" : ";plain"), await toServer.ReadLineAsync(serverLifetime.Token));
        await using var output = new ReplPipeOutput(serverWriter, serverLifetime);
        await using var remote = new ReplRemoteTerminal(toServer, output, serverLifetime, color);
        Assert.True(remote.Interactive);
        Assert.Same(TextReader.Null, remote.Input);
        Assert.Equal("", remote.HistoryFile);
        Assert.Equal(color, remote.Color);
        Assert.Equal(letter, Assert.IsType<KeyInput>(await remote.ReadAsync(serverLifetime.Token)).Key);
        Assert.Equal("a\nb", Assert.IsType<TextInput>(await remote.ReadAsync(serverLifetime.Token)).Text);
        Assert.Equal((120, 40), remote.Screen.Size);
        client.Device.Resize(90, 30);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (remote.Screen.Size != (90, 30) && DateTime.UtcNow < deadline) await Task.Delay(10, serverLifetime.Token);
        Assert.Equal((90, 30), remote.Screen.Size);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        remote.SetCancellationHandler(() => cancelled.TrySetResult());
        client.Interrupt();
        await cancelled.Task.WaitAsync(serverLifetime.Token);
        remote.SetCancellationHandler(null);
        remote.Output.Result("42");
        remote.Output.Error("failure");
        remote.Screen.SetRegion((width, height) => RegionLayout.Build("godot> ", new EditorView("draft", 5, null, [], null, null, false), null, width, height, color));
        remote.Screen.Flush();
        remote.End();
        await output.DisposeAsync();
        Assert.Equal(0, await running.WaitAsync(serverLifetime.Token));
        Assert.Contains("→ 42", client.Device.Text);
        Assert.Contains("failure", client.Device.Text);
        Assert.Equal(color, client.Device.Raw.Contains("\x1b[31m", StringComparison.Ordinal));
        // The region is removed when the session ends.
        Assert.DoesNotContain("godot> draft", client.Device.Screen);
    }

    [Fact]
    public async Task KeyboardFailuresEndTheClientAndTheHostSeesAClosedTerminal()
    {
        using var serverLifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var clientLifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var toServer = new Messages();
        using var toClient = new Messages();
        using var clientWriter = new MessageWriter(toServer);
        using var serverWriter = new MessageWriter(toClient);
        using var client = new ScriptedTerminal(null, [], clientLifetime.Token) { BeforeRead = () => throw new IOException("keyboard disconnected") };
        var running = ReplRemoteTerminalClient.RunAsync(toClient, clientWriter, client, clientLifetime);
        await toServer.ReadLineAsync(serverLifetime.Token);
        var output = new ReplPipeOutput(serverWriter, serverLifetime);
        await using (var remote = new ReplRemoteTerminal(toServer, output, serverLifetime, true))
        {
            Assert.Equal(0, await running.WaitAsync(TestContext.Current.CancellationToken));
            toServer.Complete();
            await Assert.ThrowsAsync<IOException>(() => remote.ReadAsync(TestContext.Current.CancellationToken));
            Assert.True(serverLifetime.IsCancellationRequested);
        }
        // The ended session also stops its output writer.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await output.DisposeAsync());
    }

    [Theory]
    [InlineData("{broken", typeof(JsonException))]
    [InlineData("null", typeof(IOException))]
    [InlineData("{\"Method\":\"unknown\"}", typeof(IOException))]
    public async Task InvalidHostFramesFailTheClientAndReleaseItsKeyboard(string frame, Type error)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var input = new Messages();
        using var output = new StringWriter();
        using var terminal = new ScriptedTerminal(null, [], lifetime.Token);
        var running = ReplRemoteTerminalClient.RunAsync(input, output, terminal, lifetime);
        input.Send(frame);
        await Assert.ThrowsAsync(error, () => running);
        Assert.True(lifetime.IsCancellationRequested);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("{\"Method\":\"unknown\"}")]
    [InlineData("{\"Method\":\"key\"}")]
    [InlineData(null)]
    public async Task LostOrInvalidClientFramesEndTheHostSession(string? frame)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var registration = lifetime.Token.Register(() => throw new InvalidOperationException("user callback"));
        using var input = new Messages();
        using var frames = new Messages();
        using var writer = new MessageWriter(frames);
        var output = new ReplPipeOutput(writer, lifetime);
        await using var remote = new ReplRemoteTerminal(input, output, lifetime, true);
        var read = remote.ReadAsync(CancellationToken.None);
        if (frame is null) input.Complete();
        else input.Send(frame);
        await Assert.ThrowsAnyAsync<Exception>(() => read);
        Assert.True(lifetime.IsCancellationRequested);
        try { await output.DisposeAsync(); }
        catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task ClosingOrCancellingAnIdleClientEndsItsInputLoop()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var input = new Messages();
        using var output = new StringWriter();
        using var terminal = new ScriptedTerminal(null, [], lifetime.Token);
        var running = ReplRemoteTerminalClient.RunAsync(input, output, terminal, lifetime);
        input.Send(JsonSerializer.Serialize(new ReplTerminalMessage("write", "hello")));
        input.Complete();
        Assert.Equal(0, await running);
        Assert.True(lifetime.IsCancellationRequested);
        Assert.Contains("hello", terminal.Device.Text);

        using var cancelled = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var idle = new Messages();
        using var idleTerminal = new ScriptedTerminal(null, [], cancelled.Token);
        var waiting = ReplRemoteTerminalClient.RunAsync(idle, output, idleTerminal, cancelled);
        cancelled.Cancel();
        Assert.Equal(0, await waiting);
    }

    internal sealed class Messages : TextReader
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

    internal sealed class MessageWriter(Messages destination) : StringWriter
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

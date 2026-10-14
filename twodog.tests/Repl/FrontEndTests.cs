using twodog.Repl;

namespace twodog.tests.ReplTests;

public class FrontEndTests
{
    [Fact]
    public async Task StatusLineShowsRunningAndQueuedWorkTheInputModeAndFitsNarrowTerminals()
    {
        var token = TestContext.Current.CancellationToken;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var work = new ReplWorkQueue(lifetime.Token);
        using var terminal = new ScriptedTerminal(null, [.. Keys.Type("draft"), Keys.ControlC, .. Keys.Type("40"), Keys.Enter, Keys.ControlEnter]) { Color = false };
        var frontEnd = new ReplFrontEnd(terminal, PlainLanguage.Instance, work, "repl-pipe");
        string Status() => terminal.Device.Screen.LastOrDefault(line => line.Length > 0) ?? "";
        async Task WaitFor(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!condition())
            {
                Assert.True(DateTime.UtcNow < deadline, Status());
                await Task.Delay(10, token);
            }
        }
        await WaitFor(() => Status().StartsWith("pipe repl-pipe", StringComparison.Ordinal) && Status().EndsWith(":help", StringComparison.Ordinal));
        var gate = new TaskCompletionSource();
        var first = work.Enqueue(_ => gate.Task);
        var second = work.Enqueue(_ => gate.Task);
        await WaitFor(() => Status().Contains("running, 1 queued", StringComparison.Ordinal));
        gate.SetResult();
        await Task.WhenAll(first, second);
        await WaitFor(() => Status().StartsWith("pipe", StringComparison.Ordinal));
        frontEnd.SetMultiline(true);
        await WaitFor(() => Status().Contains("Ctrl+Enter run", StringComparison.Ordinal));
        terminal.Device.Resize(24, 10);
        await WaitFor(() => Status() == "pipe repl-pipe");
        Assert.Null(await frontEnd.ReadLineAsync(token));
        var submission = await frontEnd.ReadLineAsync(token);
        Assert.Equal("40" + Environment.NewLine, submission!.Text);
        frontEnd.Echo(submission);
        terminal.Screen!.Flush();
        Assert.Contains("godot> draft ^C", terminal.Device.Text);
        Assert.Contains("godot> 40", terminal.Device.Text);
        Assert.DoesNotContain("\x1b[2m", terminal.Device.Raw);
    }
}

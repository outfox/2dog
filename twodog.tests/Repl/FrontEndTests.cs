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
        // The status line sits directly above the prompt.
        string Status() => Array.FindLastIndex(terminal.Device.Screen, line => line.StartsWith("godot>", StringComparison.Ordinal)) is var prompt and > 0
            ? terminal.Device.Screen[prompt - 1] : "";
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

    [Fact]
    public async Task CaretMovesAndOverwriteModeShowOnTheTerminalAtOnce()
    {
        var token = TestContext.Current.CancellationToken;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var work = new ReplWorkQueue(lifetime.Token);
        using var terminal = new ScriptedTerminal(null, [], waitForInput: token) { Color = false };
        var frontEnd = new ReplFrontEnd(terminal, PlainLanguage.Instance, work);
        var read = frontEnd.ReadLineAsync(token);
        int Prompt() => Array.FindLastIndex(terminal.Device.Screen, line => line.StartsWith("godot>", StringComparison.Ordinal));
        // The last cursor shape request: a steady block, or the user's default.
        bool Block() => terminal.Device.Raw is var raw && raw.LastIndexOf("\x1b[2 q", StringComparison.Ordinal) > raw.LastIndexOf("\x1b[0 q", StringComparison.Ordinal);
        async Task Expect(string line, int column, bool block)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!(Prompt() is var row and >= 0 && terminal.Device.Screen[row] == line && terminal.Device.Cursor == (row, column) && Block() == block))
            {
                Assert.True(DateTime.UtcNow < deadline, $"{terminal.Device.Cursor} {string.Join("\n", terminal.Device.Screen)}");
                await Task.Delay(10, token);
            }
        }
        foreach (var key in Keys.Type("ls Ent")) terminal.Press(key);
        await Expect("godot> ls Ent", 13, block: false);
        // Moving the caret changes neither the text nor its highlighting, and still redraws.
        terminal.Press(Keys.Tab);
        terminal.Press(Keys.Left);
        terminal.Press(Keys.Left);
        await Expect("godot> ls Ent", 11, block: false);
        terminal.Press(new('\0', ConsoleKey.Insert, false, false, false));
        foreach (var key in Keys.Type("xy")) terminal.Press(key);
        await Expect("godot> ls Exy", 13, block: true);
        Assert.Contains("overwrite", terminal.Device.Screen[Prompt() - 1]);
        terminal.Press(new('\0', ConsoleKey.Insert, false, false, false));
        terminal.Press(new('\0', ConsoleKey.Home, false, false, false));
        await Expect("godot> ls Exy", 7, block: false);
        Assert.DoesNotContain("overwrite", terminal.Device.Screen[Prompt() - 1]);
        terminal.Press(new('\0', ConsoleKey.Insert, false, false, false));
        await Expect("godot> ls Exy", 7, block: true);
        terminal.Press(Keys.Enter);
        Assert.Equal("ls Exy", (await read)!.Text);
        // Leaving the screen restores the user's cursor shape, even in overwrite mode.
        terminal.Screen!.Dispose();
        Assert.False(Block());
    }
}

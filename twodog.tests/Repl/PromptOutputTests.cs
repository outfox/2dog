using PrettyPrompt;
using PrettyPrompt.Consoles;
using twodog.Repl;

namespace twodog.tests.ReplTests;

public class PromptOutputTests
{
    [Theory]
    [InlineData("1234")]
    [InlineData("12\n34")]
    [InlineData("\u4e16\u754c\u4e16\u754c")]
    [InlineData("draft with \ud83e\udd8a at the end")]
    public async Task ResultsSuspendRenderingAndRestoreTheDraftAndCaretWithoutSubmittingIt(string draft)
    {
        var keys = Type(draft).Append(new('\0', ConsoleKey.LeftArrow, false, false, false))
            .Concat(Type("9")).Append(new('\r', ConsoleKey.Enter, false, false, false));
        var console = new PromptConsole(keys);
        var state = new ReplPromptOutput(console);
        console.Output = state;
        using var stdout = new CheckedWriter(console);
        using var stderr = new CheckedWriter(console);
        var output = new ReplOutput(state.Wrap(stdout), state.Wrap(stderr), false, false);
        using var progress = state.Wrap(stdout);
        var reads = 0;
        console.BeforeRead = () =>
        {
            if (reads++ != draft.Length + 1) return;
            Assert.False(console.NewlineAutoReturn);
            progress.Write('>');
            progress.Write(" progress ");
            output.Tree("root\n  child\n");
            output.Error("work failed");
            Assert.Equal("", stdout.ToString());
        };
        await using var prompt = new Prompt(callbacks: new DraftCallbacks(state), console: console,
            configuration: ReplHost.CreatePromptConfiguration());
        var result = await state.ReadLineAsync(prompt, 7);
        Assert.True(result.IsSuccess);
        Assert.Equal(draft.Insert(draft.Length - 1, "9").ReplaceLineEndings(), result.Text);
        Assert.Equal("> progress root\n  child" + Environment.NewLine + Environment.NewLine, stdout.ToString());
        Assert.Contains("work failed", stderr.ToString());
        Assert.Contains("\r\x1b[0m\x1b[J", string.Concat(console.Writes));
        Assert.True(console.NewlineAutoReturn);
        Assert.Null(state.ReadRestoreKey());
        output.Message("after submit");
        Assert.EndsWith("after submit" + Environment.NewLine, stdout.ToString());
    }

    [Theory]
    [InlineData("abcd", 4, 4, 1)]
    [InlineData("ab\ncd", 4, 10, 1)]
    [InlineData("abc\u4e16", 3, 4, 1)]
    [InlineData("\u4e16\u754c", 2, 4, 1)]
    [InlineData("abc", 0, 4, 0)]
    public void ClearingUsesTheWrappedCaretRow(string text, int caret, int width, int row)
        => Assert.Equal(row, ReplPromptOutput.CaretRow(text, caret, width));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(16, 4)]
    [InlineData(40, 4)]
    [InlineData(80, 7)]
    public void ClearingStartsAtTheVisibleDraftForInputTallerThanTheWindow(int caret, int row)
        => Assert.Equal(row, ReplPromptOutput.VisibleCaretRow(new string('x', 80), caret, 4, 10));

    internal static IEnumerable<ConsoleKeyInfo> Type(string text) => text.Select(c => new ConsoleKeyInfo(c,
        c == '\n' ? ConsoleKey.Enter : ConsoleKey.NoName, c == '\n', false, false));

    private sealed class DraftCallbacks(ReplPromptOutput output) : PromptCallbacks
    {
        protected override Task<(string Text, int Caret)> FormatInput(string text, int caret, KeyPress keyPress, CancellationToken cancellationToken)
            => Task.FromResult(output.Remember(text, caret));
    }

    private sealed class CheckedWriter(PromptConsole console) : StringWriter
    {
        public override void WriteLine(string? value)
        {
            Assert.True(console.NewlineAutoReturn, "Output was written while the renderer owned newline processing.");
            base.WriteLine(value);
        }
    }
}

[Collection(nameof(twodog.tests.EngineTests.EngineRestartCollection))]
public class ConsoleOutputCoordinationTests
{
    [Fact]
    public void SystemOutputUsesTheSameRenderHandoffForResultsAndErrors()
    {
        var originalOutput = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            var state = new ReplPromptOutput(new PromptConsole([]));
            var output = ReplOutput.ForConsole(state);
            output.Tree("root\n  child\n");
            output.Error("failure");
            Assert.Contains("root\n  child", stdout.ToString());
            Assert.Contains("failure", stderr.ToString());
            var terminal = new ReplConsole(CancellationToken.None);
            // An empty input buffer must report no key without blocking on stdin.
            Assert.False(((IConsole)terminal).KeyAvailable);
            ((IConsole)terminal).SetModifyOtherKeys(false);
            terminal.Restore();
        }
        finally { Console.SetOut(originalOutput); Console.SetError(originalError); }
    }
}

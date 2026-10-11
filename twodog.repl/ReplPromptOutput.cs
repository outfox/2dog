using System.Text;
using PrettyPrompt;
using PrettyPrompt.Consoles;
using PrettyPrompt.Rendering;

namespace twodog.Repl;

// PrettyPrompt owns the cursor and newline mode until ReadLineAsync returns.
// Wake it at an input boundary, print queued output with normal terminal modes,
// then start a fresh render with the same draft and caret. No work thread writes
// into an active render, and no unfinished draft is submitted or added to history.
internal sealed class ReplPromptOutput(IConsole console)
{
    private readonly object gate = new();
    private readonly Queue<Action> pending = new();
    private bool reading;
    private (string Text, int Caret) draft = ("", 0);
    private (string Text, int Caret)? restore;

    public TextWriter Wrap(TextWriter writer) => new OutputWriter(this, writer);

    private void Write(Action action)
    {
        lock (gate)
        {
            if (reading) pending.Enqueue(action);
            else action();
        }
    }

    public void CheckForOutput()
    {
        lock (gate)
            if (reading && pending.Count > 0) throw new OutputPendingException();
    }

    public ConsoleKeyInfo? ReadRestoreKey()
    {
        CheckForOutput();
        if (restore is null) return null;
        return new('\0', ConsoleKey.F24, false, false, false);
    }

    public bool Restoring => restore is not null;

    public (string Text, int Caret) Remember(string text, int caret)
    {
        draft = restore ?? (text, caret);
        restore = null;
        return draft;
    }

    public async Task<PromptResult> ReadLineAsync(Prompt prompt, int promptWidth)
    {
        draft = ("", 0);
        while (true)
        {
            lock (gate) reading = true;
            try { return await prompt.ReadLineAsync().ConfigureAwait(false); }
            catch (OutputPendingException)
            {
                // The ReadLine finally block has restored normal newline behavior.
                // Remove this render (including helper panes) before printing output.
                var row = VisibleCaretRow(draft.Text, draft.Caret, Math.Max(1, console.BufferWidth - promptWidth), console.WindowHeight);
                console.Write((row > 0 ? $"\x1b[{row}A" : "") + "\r\x1b[0m\x1b[J");
                restore = draft;
            }
            finally
            {
                lock (gate)
                {
                    reading = false;
                    while (pending.TryDequeue(out var action)) action();
                }
            }
        }
    }

    internal static int CaretRow(string text, int caret, int width)
    {
        var row = 0;
        var column = 0;
        for (var i = 0; i < caret; i++)
        {
            column += UnicodeWidth.GetWidth(text[i]);
            if (text[i] == '\n' || column >= width ||
                i + 1 < text.Length && UnicodeWidth.GetWidth(text[i + 1]) > 1 && column + UnicodeWidth.GetWidth(text[i + 1]) > width)
            {
                row++;
                column = 0;
            }
        }
        return row;
    }

    internal static int VisibleCaretRow(string text, int caret, int width, int windowHeight)
    {
        var row = CaretRow(text, caret, width);
        var lines = CaretRow(text, text.Length, width) + 1;
        var height = Math.Max(1, windowHeight - 2);
        // PrettyPrompt centers the viewport around the caret for tall drafts.
        var start = Math.Clamp(row - height / 2, 0, Math.Max(0, lines - height));
        return row - start;
    }

    private sealed class OutputPendingException : Exception;

    private sealed class OutputWriter(ReplPromptOutput owner, TextWriter destination) : TextWriter
    {
        public override Encoding Encoding => destination.Encoding;
        public override void Write(string? value) => owner.Write(() => destination.Write(value));
        public override void Write(char value) => owner.Write(() => destination.Write(value));
        public override void WriteLine(string? value) => owner.Write(() => destination.WriteLine(value));
    }
}

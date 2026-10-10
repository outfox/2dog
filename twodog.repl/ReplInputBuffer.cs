namespace twodog.Repl;

// PrettyPrompt batches queued text as a paste. Keep editing/submission keys at
// event boundaries, even when a slow callback lets several keystrokes accumulate.
internal sealed class ReplInputBuffer(Func<bool> available, Func<ConsoleKeyInfo> read, bool windows)
{
    private ConsoleKeyInfo? pending;
    private ConsoleKeyInfo previous;
    private bool escapeSequence;
    private readonly Queue<ConsoleKeyInfo> replay = new();
    private string? pastedText;

    private ConsoleKeyInfo ReadNext() => replay.TryDequeue(out var key) ? key : read();
    private bool Available => replay.Count > 0 || available();

    public string? TakePaste()
    {
        var text = pastedText;
        pastedText = null;
        return text;
    }

    public ConsoleKeyInfo ReadKey()
    {
        var key = pending ?? ReadNext();
        pending = null;
        if (key.Key == ConsoleKey.Escape && TryStartPaste())
        {
            var text = new System.Text.StringBuilder();
            while (true)
            {
                var next = ReadNext();
                text.Append(next.KeyChar);
                if (next.KeyChar != '~') continue;
                var suffix = EndsWith(text, "\x1b[201~") ? 6 : EndsWith(text, "\u001b201~") ? 5 : 0;
                if (suffix == 0) continue;
                pastedText = text.ToString(0, text.Length - suffix);
                key = new ConsoleKeyInfo('\0', ConsoleKey.Insert, true, false, false);
                break;
            }
        }
        previous = key;
        return key;
    }

    private bool TryStartPaste()
    {
        // Bracketed paste distinguishes literal newlines/tabs from editing keys.
        // If this is an ordinary Escape or a different sequence, replay it intact.
        const string full = "[200~", unix = "200~";
        var keys = new List<ConsoleKeyInfo>();
        var prefix = "";
        while (Available && keys.Count < full.Length)
        {
            var next = ReadNext();
            keys.Add(next);
            prefix += next.KeyChar;
            if (prefix == full || prefix == unix) return true;
            if (!full.StartsWith(prefix, StringComparison.Ordinal) && !unix.StartsWith(prefix, StringComparison.Ordinal)) break;
        }
        foreach (var key in keys) replay.Enqueue(key);
        return false;
    }

    private static bool EndsWith(System.Text.StringBuilder text, string suffix)
    {
        if (text.Length < suffix.Length) return false;
        for (var i = 1; i <= suffix.Length; i++)
            if (text[text.Length - i] != suffix[^i]) return false;
        return true;
    }

    public bool KeyAvailable
    {
        get
        {
            // Enter may finish the host altogether. Leave subsequent keys in the
            // OS queue for the next prompt or the shell that launched this process.
            if (!escapeSequence && !PlainText(previous) && (windows || previous.Key != ConsoleKey.Escape)) return false;
            if (pending is null && !Available)
            {
                escapeSequence = false;
                return false;
            }
            // Unix terminals can report modified keys as an ANSI escape sequence.
            // Let PrettyPrompt decode that sequence. Windows ReadKey already decodes keys.
            pending ??= ReadNext();
            if (escapeSequence)
            {
                // Do not swallow a following Enter/Tab into a completed sequence.
                if (previous.KeyChar is >= '@' and <= '~' and not '[') escapeSequence = false;
                else return true;
            }
            if (!windows && previous.Key == ConsoleKey.Escape && (pending.Value.KeyChar == '[' || char.IsAsciiDigit(pending.Value.KeyChar)))
            {
                escapeSequence = true;
                return true;
            }
            return PlainText(previous) && PlainText(pending.Value);
        }
    }

    private static bool PlainText(ConsoleKeyInfo key) => !char.IsControl(key.KeyChar) &&
        (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0;
}

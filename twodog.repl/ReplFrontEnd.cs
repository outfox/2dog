namespace twodog.Repl;

/// <summary>The interactive front end: the editor drawn as the screen's input region, with a status line.
/// Input stays live while submissions run, so typing ahead and Ctrl+C always work.</summary>
internal sealed class ReplFrontEnd
{
    private static readonly string[] Spinner = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];
    private static readonly TextStyle Dim = new(Dim: true), Key = new(TermColor.Cyan), Busy = new(TermColor.Yellow);
    private readonly IReplTerminal terminal;
    private readonly ReplScreen screen;
    private readonly ReplWorkQueue work;
    private readonly string? pipe;
    private readonly ReplEditor editor;
    private StyledText prompt = "godot> ";

    public ReplFrontEnd(IReplTerminal terminal, IReplLanguage language, ReplWorkQueue work, string? pipe = null)
    {
        this.terminal = terminal;
        screen = terminal.Screen!;
        this.work = work;
        this.pipe = pipe;
        editor = new ReplEditor(language, new ReplHistory(terminal.HistoryFile));
        editor.Changed += screen.Invalidate;
        editor.Copied += text => screen.Control("\x1b]52;c;" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text)) + "\x07");
        screen.Animating = () => work.Pending > 0;
        screen.SetRegion(Layout);
    }

    /// <summary>Swaps Enter (newline) and Shift+Enter (run).</summary>
    public void SetMultiline(bool multiline)
    {
        editor.Multiline = multiline;
        screen.Invalidate();
    }

    public void SetPrompt(string? scope, IReadOnlyDictionary<string, NodeFamily>? families, ReplMode mode)
    {
        prompt = ReplHost.CreatePromptText(scope, screen.Size.Width, families, screen.Color, mode);
        screen.Invalidate();
    }

    /// <summary>The next submission; null when the user cancelled the draft with Ctrl+C.</summary>
    public async Task<ReplSubmission?> ReadLineAsync(CancellationToken token)
    {
        var result = await editor.ReadLineAsync(terminal.ReadAsync, token, screen.Clear).ConfigureAwait(false);
        // The submitted input as typed and highlighted, with the prompt it was typed at.
        var echo = (prompt + new StyledText(result.Text.ReplaceLineEndings("\n").Replace("\n", "\n" + new string(' ', CellWidth.Of(prompt.Text))),
            Shift(result.Highlights, result.Text, CellWidth.Of(prompt.Text)))).ToAnsi(screen.Color);
        if (result.Action == EditorAction.Submit) return new ReplSubmission(result.Text, echo);
        if (result.Text.Length > 0) screen.Write(echo + (screen.Color ? "\x1b[2m ^C\x1b[0m" : " ^C") + "\n");
        return null;
    }

    /// <summary>Writes a submission into the scrollback, just before its results.</summary>
    public void Echo(ReplSubmission submission) => screen.Write(submission.Echo + "\n");

    // Spans move right by the continuation indent on each following line.
    private static IEnumerable<StyledSpan> Shift(IReadOnlyList<StyledSpan> spans, string text, int indent)
    {
        text = text.ReplaceLineEndings("\n");
        foreach (var span in spans)
        {
            if (span.End > text.Length) continue;
            var before = text.AsSpan(0, span.Start).Count('\n');
            if (text.AsSpan(span.Start, span.Length).Contains('\n')) continue;
            yield return span.Offset(before * indent);
        }
    }

    private RegionFrame Layout(int width, int height) => RegionLayout.Build(prompt, editor.View, Status(width), width, height, screen.Color);

    private StyledText Status(int width)
    {
        var pending = work.Pending;
        var left = pending > 0
            ? new StyledText(Spinner[Environment.TickCount64 / 100 % Spinner.Length] + (pending > 1 ? $" running, {pending - 1} queued" : " running"), Busy) +
                new StyledText("  Ctrl+C cancels", Dim)
            : new StyledText(pipe is null ? "" : "pipe " + pipe, Dim);
        var hints = editor.Multiline
            ? Hint("Enter", "newline") + Hint("Ctrl+Enter", "run") + Hint(":help", "")
            : Hint("Tab", "complete") + Hint("Shift+Enter", "newline") + Hint(":help", "");
        var gap = width - 1 - CellWidth.Of(left.Text) - CellWidth.Of(hints.Text);
        return gap > 1 ? left + new string(' ', gap) + hints : left;
    }

    private static StyledText Hint(string key, string action)
        => new StyledText(key, Key) + new StyledText(action.Length > 0 ? " " + action + "  " : "", Dim);
}

/// <summary>Submitted text and how it appears in the scrollback, once it runs.</summary>
internal sealed record ReplSubmission(string Text, string Echo);

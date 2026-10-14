using Microsoft.CodeAnalysis.CSharp;

namespace twodog.Repl;

internal abstract record TerminalInput;
internal sealed record KeyInput(ConsoleKeyInfo Key) : TerminalInput;
/// <summary>Pasted or typed-ahead text, inserted literally: its newlines and tabs never act as editing keys.</summary>
internal sealed record TextInput(string Text) : TerminalInput;

internal enum EditorAction { Submit, Cancel, Clear }
internal sealed record EditorResult(EditorAction Action, string Text = "", IReadOnlyList<StyledSpan>? Spans = null)
{
    public IReadOnlyList<StyledSpan> Highlights => Spans ?? [];
}

internal sealed record MenuView(IReadOnlyList<StyledText> Items, int? Selected, int First, int Total, StyledText Description);
internal sealed record EditorView(string Text, int Caret, TextRange? Selection, IReadOnlyList<StyledSpan> Highlights,
    MenuView? Menu, StyledText? Signature, bool Multiline);

/// <summary>The input line: text, caret, selection, undo, history, completion and signature help. It never touches a
/// terminal: inputs come in one at a time and <see cref="View"/> describes what to draw.</summary>
internal sealed class ReplEditor(IReplLanguage language, ReplHistory? history = null)
{
    internal const int MenuRows = 8;
    private readonly ReplHistory history = history ?? new ReplHistory();
    private readonly Lock gate = new();
    private string text = "";
    private int caret;
    private int? anchor;
    private readonly Stack<(string Text, int Caret)> undo = new(), redo = new();
    private bool typing, continuing;
    private IReadOnlyList<ReplCompletionItem>? menuItems;
    // The open menu's matching items, best first; empty while nothing matches the typed word.
    private List<ReplCompletionItem>? sorted;
    private int? selected;
    private int first;
    private TextRange menuRange;
    private IReadOnlyList<SignatureItem>? signatures;
    private int argument;
    private CompletionCycle? cycle;
    private int historyIndex = -1;
    private string historyPrefix = "", historyDraft = "";
    private string clipboard = "";
    // Published state, also updated by background highlighting and documentation lookups.
    private EditorView view = new("", 0, null, [], null, null, false);
    private string highlighted = "";
    private string? requested;
    private IReadOnlyList<StyledSpan> highlights = [];
    private ReplCompletionItem? described;
    private StyledText description = StyledText.Empty;

    public bool Multiline { get; set; }
    public string Text => text;
    public int Caret => caret;
    /// <summary>Raised from any thread when background work changes <see cref="View"/>.</summary>
    public event Action? Changed;
    /// <summary>Raised with text copied or cut, so a terminal can offer it to the system clipboard.</summary>
    public event Action<string>? Copied;

    public EditorView View { get { lock (gate) return view; } }

    /// <summary>Reads inputs until the user submits or cancels; <see cref="EditorAction.Clear"/> requests are handled by <paramref name="clear"/>.</summary>
    public async Task<EditorResult> ReadLineAsync(Func<CancellationToken, Task<TerminalInput>> read, CancellationToken token, Action? clear = null)
    {
        while (true)
        {
            var result = await HandleAsync(await read(token).ConfigureAwait(false), token).ConfigureAwait(false);
            if (result is { Action: EditorAction.Clear }) clear?.Invoke();
            else if (result is not null) return result;
        }
    }

    public async Task<EditorResult?> HandleAsync(TerminalInput input, CancellationToken token)
    {
        var result = input is TextInput paste
            ? await PasteAsync(paste.Text, token).ConfigureAwait(false)
            : await KeyAsync(((KeyInput)input).Key, token).ConfigureAwait(false);
        if (result is { Action: EditorAction.Submit or EditorAction.Cancel })
        {
            lock (gate) result = result with { Spans = highlighted == text ? highlights : [] };
            if (result.Action == EditorAction.Submit) history.Add(result.Text);
            else result = result with { Text = text };
            Reset();
        }
        Publish();
        return result;
    }

    private void Reset()
    {
        text = "";
        caret = 0;
        anchor = null;
        undo.Clear();
        redo.Clear();
        typing = false;
        CloseMenu();
        signatures = null;
        cycle = null;
        historyIndex = -1;
    }

    private EditorResult Submit(string submitted) => new(EditorAction.Submit, submitted.ReplaceLineEndings());

    private async Task<EditorResult?> KeyAsync(ConsoleKeyInfo key, CancellationToken token)
    {
        var control = key.Modifiers.HasFlag(ConsoleModifiers.Control);
        var shift = key.Modifiers.HasFlag(ConsoleModifiers.Shift);
        var alt = key.Modifiers.HasFlag(ConsoleModifiers.Alt);
        // Tab, Backspace and identifier characters may continue a completion cycle; everything else ends it.
        var active = cycle;
        cycle = null;
        // Consecutive typed characters form one undo step.
        continuing = typing;
        typing = false;
        switch (key.Key)
        {
            case ConsoleKey.C when control && !alt:
                if (Selection() is not { } copied) return new EditorResult(EditorAction.Cancel);
                Copy(text.Substring(copied.Start, copied.Length));
                return null;
            case ConsoleKey.D when control && !alt && text.Length == 0:
                return Submit(":exit");
            case ConsoleKey.L when control && !alt:
                return new EditorResult(EditorAction.Clear);
            case ConsoleKey.Enter:
                return await EnterAsync(control, shift, alt, token).ConfigureAwait(false);
            case ConsoleKey.Tab when !control && !alt:
                await TabAsync(shift, active, token).ConfigureAwait(false);
                return null;
            case ConsoleKey.Spacebar when control:
                if (shift) await UpdateSignaturesAsync(open: true, token).ConfigureAwait(false);
                else if (anchor is null) await OpenMenuAsync(token).ConfigureAwait(false);
                return null;
            case ConsoleKey.Escape:
                if (sorted is not null) CloseMenu();
                else signatures = null;
                return null;
            case ConsoleKey.UpArrow or ConsoleKey.DownArrow when sorted is { Count: > 0 } && !shift && !alt:
                MoveSelection(key.Key == ConsoleKey.DownArrow ? 1 : -1);
                return null;
            case ConsoleKey.UpArrow or ConsoleKey.DownArrow when !control && !alt:
                await VerticalAsync(key.Key == ConsoleKey.DownArrow ? 1 : -1, shift, token).ConfigureAwait(false);
                return null;
            case ConsoleKey.Backspace when !control && !alt && !shift && active is { Applied: true } && active.Matches(text, caret):
                // Restore the prefix that started the cycle, including inserted parentheses.
                Checkpoint();
                SetText(active.Original, active.OriginalCaret);
                await AfterEditAsync(null, token).ConfigureAwait(false);
                return null;
            case ConsoleKey.Backspace:
                await DeleteAsync(control || alt || shift ? WordLeft(caret) : PreviousCaret(caret), token).ConfigureAwait(false);
                return null;
            case ConsoleKey.Delete:
                await DeleteAsync(control || alt ? WordRight(caret) : NextCaret(caret), token).ConfigureAwait(false);
                return null;
            case ConsoleKey.D when control && !alt:
                await DeleteAsync(NextCaret(caret), token).ConfigureAwait(false);
                return null;
            case ConsoleKey.LeftArrow or ConsoleKey.RightArrow or ConsoleKey.Home or ConsoleKey.End:
                await MoveAsync(key.Key, control, shift, token).ConfigureAwait(false);
                return null;
            case ConsoleKey.Z when control && !alt:
                Restore(shift ? redo : undo, shift ? undo : redo);
                return null;
            case ConsoleKey.Y when control && !alt:
                Restore(redo, undo);
                return null;
            case ConsoleKey.A when control && !alt:
                CloseMenu();
                anchor = 0;
                caret = text.Length;
                return null;
            case ConsoleKey.X when control && !alt:
                if (Selection() is { } cut)
                {
                    Copy(text.Substring(cut.Start, cut.Length));
                    await DeleteAsync(caret, token).ConfigureAwait(false);
                }
                return null;
            case ConsoleKey.V when control && !alt:
                if (clipboard.Length > 0) await PasteAsync(clipboard, token).ConfigureAwait(false);
                return null;
            case ConsoleKey.K when control && !alt:
                await DeleteRangeAsync(caret, LineEnd(caret), token).ConfigureAwait(false);
                return null;
            case ConsoleKey.U when control && !alt:
                await DeleteRangeAsync(LineStart(caret), caret, token).ConfigureAwait(false);
                return null;
        }
        // AltGr arrives as Control+Alt on Windows and still types its character.
        if (char.IsControl(key.KeyChar) || key.KeyChar == '\0' || control != alt) return null;
        await TypeAsync(key.KeyChar, active, token).ConfigureAwait(false);
        return null;
    }

    private async Task<EditorResult?> EnterAsync(bool control, bool shift, bool alt, CancellationToken token)
    {
        if (control) return Submit(text);
        if (!shift && !alt)
        {
            // Exact commands run at once, even when the menu offers a longer match.
            if (language.IsBareCommand(text)) return Submit(text);
            if (sorted is not null && selected is { } index)
            {
                await CommitAsync(sorted[index], CommitKey.Enter, remember: true, token).ConfigureAwait(false);
                if (!language.IsBareCommand(text)) return null;
                cycle = null;
                return Submit(text);
            }
        }
        CloseMenu();
        if (shift == Multiline && !alt) return Submit(text);
        await InsertAsync("\n", token).ConfigureAwait(false);
        return null;
    }

    private async Task TabAsync(bool shift, CompletionCycle? active, CancellationToken token)
    {
        if (anchor is not null) return;
        var direction = shift ? -1 : 1;
        if (active is not null && active.Matches(text, caret))
        {
            if (!active.Applied)
            {
                // Typing refined the prefix: rank the cycle's items against it again.
                var range = await language.CompletionRangeAsync(text, caret, token).ConfigureAwait(false);
                var matches = Ranked(active.Items, range, null);
                if (matches.Length == 0)
                {
                    cycle = active;
                    return;
                }
                active = new CompletionCycle(text, caret, matches, direction > 0 ? -1 : 0);
            }
            var next = active.Next(direction);
            var edit = await language.CommitAsync(next, active.Original, active.OriginalCaret, CommitKey.Tab, token).ConfigureAwait(false);
            Checkpoint();
            var (cycled, cycledCaret) = active.Apply(edit);
            SetText(cycled, cycledCaret);
            cycle = active;
            CloseMenu();
            await UpdateSignaturesAsync(open: true, token).ConfigureAwait(false);
            return;
        }
        if (sorted is not null)
        {
            if (selected is { } index) await CommitAsync(sorted[index], CommitKey.Tab, remember: true, token).ConfigureAwait(false);
            else CloseMenu();
            await UpdateSignaturesAsync(open: true, token).ConfigureAwait(false);
            return;
        }
        if (shift) return;
        var action = await language.TabAsync(text, caret, token).ConfigureAwait(false);
        if (action.Edit is { } continuation)
        {
            Checkpoint();
            var (continued, continuedCaret) = continuation.Apply(text);
            SetText(continued, continuedCaret);
            if (continuation.NewText.Length == 1 && language.ShouldOpenCompletion(text, caret, continuation.NewText[0]))
                await OpenMenuAsync(token).ConfigureAwait(false);
        }
        else if (action.OpenMenu) await OpenMenuAsync(token).ConfigureAwait(false);
        await UpdateSignaturesAsync(open: true, token).ConfigureAwait(false);
    }

    private ReplCompletionItem[] Ranked(IEnumerable<ReplCompletionItem> items, TextRange range, ReplCompletionItem? keep)
        => items.Select(i => (Item: i, Priority: i.Priority(text, caret, range)))
            .Where(i => i.Priority >= 0 || ReferenceEquals(i.Item, keep))
            .OrderByDescending(i => i.Priority).Select(i => i.Item).ToArray();

    private async Task CommitAsync(ReplCompletionItem item, CommitKey key, bool remember, CancellationToken token)
    {
        var range = await language.CompletionRangeAsync(text, caret, token).ConfigureAwait(false);
        var edit = await language.CommitAsync(item, text, caret, key, token).ConfigureAwait(false);
        if (remember)
        {
            var matches = Ranked(menuItems!, range, item);
            cycle = new CompletionCycle(text, caret, matches, Array.IndexOf(matches, item));
            cycle.Apply(edit);
        }
        Checkpoint();
        var (accepted, acceptedCaret) = edit.Apply(text);
        SetText(accepted, acceptedCaret);
        CloseMenu();
    }

    private async Task TypeAsync(char typed, CompletionCycle? active, CancellationToken token)
    {
        if (typed == ')' && anchor is null && await language.SkipsCloseParenAsync(text, caret, token).ConfigureAwait(false))
        {
            await MoveToAsync(caret + 1, extend: false, token).ConfigureAwait(false);
            return;
        }
        // More letters refine the cycle's prefix; the next Tab ranks its items again.
        if (active is not null && anchor is null && active.Matches(text, caret) && caret > 0 &&
            SyntaxFacts.IsIdentifierPartCharacter(text[caret - 1]) && SyntaxFacts.IsIdentifierPartCharacter(typed))
        {
            Insert(typed.ToString(), coalesce: true);
            active.Refine(text, caret);
            cycle = active;
            await AfterEditAsync(null, token).ConfigureAwait(false);
            return;
        }
        if (sorted is not null && selected is { } index && anchor is null)
        {
            var item = sorted[index];
            if ((typed is '.' or '(' || item.CommitCharacters.Contains(typed)) && language.AcceptsCommit(item, text, caret, typed))
                await CommitAsync(item, CommitKey.Character, remember: false, token).ConfigureAwait(false);
        }
        var before = sorted is null ? (TextRange?)null : menuRange;
        Insert(typed.ToString(), coalesce: true);
        if (before is null && language.ShouldOpenCompletion(text, caret, typed)) await OpenMenuAsync(token).ConfigureAwait(false);
        await AfterEditAsync(before, token, signatures: typed is '(' or ',').ConfigureAwait(false);
    }

    private async Task<EditorResult?> PasteAsync(string pasted, CancellationToken token)
    {
        // Copied lines usually end with a line break; the user still decides when to run them.
        pasted = new string(pasted.ReplaceLineEndings("\n").Where(c => c is '\n' or '\t' || !char.IsControl(c)).ToArray()).TrimEnd('\n');
        if (pasted.Length == 0) return null;
        CloseMenu();
        await InsertAsync(pasted, token).ConfigureAwait(false);
        return null;
    }

    private async Task InsertAsync(string inserted, CancellationToken token)
    {
        Insert(inserted, coalesce: false);
        await AfterEditAsync(null, token).ConfigureAwait(false);
    }

    private void Insert(string inserted, bool coalesce)
    {
        if (!coalesce || !continuing || anchor is not null) Checkpoint();
        typing = coalesce;
        if (Selection() is { } replaced)
        {
            text = text.Remove(replaced.Start, replaced.Length);
            caret = replaced.Start;
            anchor = null;
        }
        SetText(text.Insert(caret, inserted), caret + inserted.Length);
    }

    private async Task DeleteAsync(int target, CancellationToken token)
    {
        if (Selection() is { } removed) await DeleteRangeAsync(removed.Start, removed.End, token).ConfigureAwait(false);
        else await DeleteRangeAsync(Math.Min(caret, target), Math.Max(caret, target), token).ConfigureAwait(false);
    }

    private async Task DeleteRangeAsync(int start, int end, CancellationToken token)
    {
        anchor = null;
        if (start == end) return;
        var before = sorted is null ? (TextRange?)null : menuRange;
        Checkpoint();
        SetText(text.Remove(start, end - start), start);
        await AfterEditAsync(before, token).ConfigureAwait(false);
    }

    // After any edit: refilter an open menu (closing it once the completed word moves) and refresh signatures.
    private async Task AfterEditAsync(TextRange? menuBefore, CancellationToken token, bool signatures = false)
    {
        if (sorted is not null)
        {
            var range = await language.CompletionRangeAsync(text, caret, token).ConfigureAwait(false);
            if (menuBefore is { } previous && range.Start != previous.Start && range.End != previous.End) CloseMenu();
            else Filter(range);
        }
        await UpdateSignaturesAsync(signatures, token).ConfigureAwait(false);
    }

    private async Task OpenMenuAsync(CancellationToken token)
    {
        var range = await language.CompletionRangeAsync(text, caret, token).ConfigureAwait(false);
        var items = await language.CompletionsAsync(text, caret, range, token).ConfigureAwait(false);
        if (items.Count == 0)
        {
            CloseMenu();
            return;
        }
        menuItems = items;
        Filter(range);
    }

    private void Filter(TextRange range)
    {
        menuRange = range;
        sorted = Ranked(menuItems!, range, null).ToList();
        selected = sorted.Count > 0 ? 0 : null;
        first = 0;
    }

    private void CloseMenu()
    {
        menuItems = null;
        sorted = null;
        selected = null;
        first = 0;
    }

    private void MoveSelection(int direction)
    {
        // A non-empty menu always has a selection.
        var index = Math.Clamp(selected!.Value + direction, 0, sorted!.Count - 1);
        selected = index;
        first = Math.Clamp(first, index - MenuRows + 1, index);
    }

    private async Task UpdateSignaturesAsync(bool open, CancellationToken token)
    {
        if (!open && signatures is null) return;
        var (items, index) = await language.SignaturesAsync(text, caret, token).ConfigureAwait(false);
        signatures = items.Count == 0 ? null : items;
        argument = index;
    }

    private async Task VerticalAsync(int direction, bool extend, CancellationToken token)
    {
        var line = LineStart(caret);
        var column = caret - line;
        if (direction < 0 && line > 0)
        {
            var previous = LineStart(line - 1);
            await MoveToAsync(Math.Min(previous + column, line - 1), extend, token).ConfigureAwait(false);
            return;
        }
        var end = LineEnd(caret);
        if (direction > 0 && end < text.Length)
        {
            await MoveToAsync(Math.Min(end + 1 + column, LineEnd(end + 1)), extend, token).ConfigureAwait(false);
            return;
        }
        // The first and last lines browse history, filtered by the text typed before browsing started.
        if (extend || direction > 0 && historyIndex < 0) return;
        if (historyIndex < 0)
        {
            historyIndex = history.Count;
            historyPrefix = historyDraft = text;
        }
        var found = history.Find(historyIndex, direction, historyPrefix, text);
        if (found is null && direction < 0) return;
        var (index, entry) = found ?? (-1, historyDraft);
        CloseMenu();
        signatures = null;
        undo.Clear();
        redo.Clear();
        text = entry;
        caret = entry.Length;
        anchor = null;
        historyIndex = index;
    }

    private async Task MoveAsync(ConsoleKey key, bool control, bool shift, CancellationToken token)
    {
        var target = key switch
        {
            ConsoleKey.LeftArrow => control ? WordLeft(caret) : PreviousCaret(caret),
            ConsoleKey.RightArrow => control ? WordRight(caret) : NextCaret(caret),
            ConsoleKey.Home => control ? 0 : LineStart(caret),
            _ => control ? text.Length : LineEnd(caret),
        };
        // Leaving the completed word, selecting or jumping closes the menu.
        if (sorted is not null && (shift || control || key is ConsoleKey.Home or ConsoleKey.End || !menuRange.Contains(target))) CloseMenu();
        if (!shift && anchor is { } start && key is ConsoleKey.LeftArrow or ConsoleKey.RightArrow && !control)
            target = key == ConsoleKey.LeftArrow ? Math.Min(start, caret) : Math.Max(start, caret);
        await MoveToAsync(target, shift, token).ConfigureAwait(false);
    }

    private async Task MoveToAsync(int target, bool extend, CancellationToken token)
    {
        if (extend) anchor ??= caret;
        else anchor = null;
        caret = target;
        typing = false;
        historyIndex = -1;
        var before = sorted is null ? (TextRange?)null : menuRange;
        await AfterEditAsync(before, token).ConfigureAwait(false);
    }

    private TextRange? Selection() => anchor is { } start && start != caret
        ? new TextRange(Math.Min(start, caret), Math.Abs(start - caret)) : null;

    private void Copy(string copied)
    {
        clipboard = copied;
        Copied?.Invoke(copied);
    }

    private void Checkpoint()
    {
        undo.Push((text, caret));
        redo.Clear();
        historyIndex = -1;
    }

    private void Restore(Stack<(string Text, int Caret)> from, Stack<(string Text, int Caret)> to)
    {
        if (!from.TryPop(out var state)) return;
        to.Push((text, caret));
        CloseMenu();
        signatures = null;
        anchor = null;
        typing = false;
        text = state.Text;
        caret = state.Caret;
    }

    private void SetText(string value, int position)
    {
        text = value;
        caret = Math.Clamp(position, 0, value.Length);
        anchor = null;
    }

    private int LineStart(int position) => position == 0 ? 0 : text.LastIndexOf('\n', position - 1) + 1;
    private int LineEnd(int position) => text.IndexOf('\n', position) is var end and >= 0 ? end : text.Length;
    private int PreviousCaret(int position) => position == 0 ? 0 : position - (position > 1 && char.IsSurrogatePair(text[position - 2], text[position - 1]) ? 2 : 1);
    private int NextCaret(int position) => position >= text.Length ? text.Length : position + (position + 1 < text.Length && char.IsSurrogatePair(text[position], text[position + 1]) ? 2 : 1);
    private static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_';

    // Dots, slashes and other punctuation are word breaks.
    private int WordLeft(int position)
    {
        while (position > 0 && char.IsWhiteSpace(text[position - 1])) position--;
        if (position > 0 && !IsWord(text[position - 1])) return position - 1;
        while (position > 0 && IsWord(text[position - 1])) position--;
        return position;
    }

    private int WordRight(int position)
    {
        if (position < text.Length && !IsWord(text[position]) && !char.IsWhiteSpace(text[position])) position++;
        else while (position < text.Length && IsWord(text[position])) position++;
        while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
        return position;
    }

    // Snapshot the state for rendering, and start background highlighting and documentation lookups.
    private void Publish()
    {
        var item = sorted is not null && selected is { } index ? sorted[index] : null;
        lock (gate)
        {
            if (!ReferenceEquals(item, described))
            {
                described = item;
                description = StyledText.Empty;
                if (item is not null) _ = DescribeAsync(item);
            }
            view = Compose();
            if (highlighted == text || requested == text) return;
            requested = text;
        }
        _ = HighlightAsync(text);
    }

    // Under the gate.
    private EditorView Compose()
    {
        var menu = sorted is not { Count: > 0 } ? null : new MenuView(sorted.Skip(first).Take(MenuRows).Select(i => i.Display).ToArray(),
            selected - first, first, sorted.Count, description);
        var spans = highlighted == text ? highlights : highlights.Where(s => s.End <= CommonPrefix(highlighted, text)).ToArray();
        return new EditorView(text, caret, Selection(), spans, menu, Signature(), Multiline);
    }

    private static int CommonPrefix(string a, string b)
    {
        var length = 0;
        while (length < a.Length && length < b.Length && a[length] == b[length]) length++;
        return length;
    }

    private async Task DescribeAsync(ReplCompletionItem item)
    {
        StyledText result;
        try { result = await item.DescriptionAsync(CancellationToken.None).ConfigureAwait(false); }
        catch (Exception error) when (error is not OutOfMemoryException) { return; }
        lock (gate)
        {
            if (!ReferenceEquals(described, item)) return;
            description = result;
            view = view with { Menu = view.Menu is { } menu ? menu with { Description = result } : null };
        }
        Changed?.Invoke();
    }

    private async Task HighlightAsync(string requested)
    {
        IReadOnlyList<StyledSpan> spans;
        try { spans = await language.HighlightAsync(requested, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception error) when (error is not OutOfMemoryException) { return; }
        lock (gate)
        {
            if (view.Text != requested) return;
            highlighted = requested;
            highlights = spans;
            view = view with { Highlights = spans };
        }
        Changed?.Invoke();
    }

    // The best overload for the current argument, with that parameter emphasized.
    private StyledText? Signature()
    {
        if (signatures is null) return null;
        var item = signatures.FirstOrDefault(s => s.Parameters.Count > argument) ?? signatures[0];
        var signature = item.Signature;
        var parameter = ParameterRange(signature.Text, argument);
        if (parameter is { } range)
            signature = new StyledText(signature.Text, signature.Spans.Append(new StyledSpan(range.Start, range.Length, new TextStyle(Bold: true, Underline: true))));
        var line = signature;
        if (signatures.Count > 1) line += new StyledText($"  (+{signatures.Count - 1} overload{(signatures.Count > 2 ? "s" : "")})", new TextStyle(Dim: true));
        var documentation = argument < item.Parameters.Count && item.Parameters[argument].Documentation.Length > 0
            ? item.Parameters[argument].Name + ": " + item.Parameters[argument].Documentation : item.Summary;
        return documentation.Length == 0 ? line : line + new StyledText("\n" + documentation, new TextStyle(Dim: true));
    }

    private static TextRange? ParameterRange(string signature, int index)
    {
        var open = signature.IndexOf('(');
        if (open < 0) return null;
        var depth = 0;
        var start = open + 1;
        for (var i = start; i < signature.Length; i++)
        {
            var c = signature[i];
            if (c is '(' or '<' or '[') depth++;
            else if (c is ')' or '>' or ']' && depth-- == 0 || c == ',' && depth == 0)
            {
                if (index-- == 0) return Trim(start, i);
                if (c == ')') return null;
                start = i + 1;
            }
        }
        // An unfinished list ends with its last parameter.
        return index == 0 ? Trim(start, signature.Length) : null;

        TextRange? Trim(int from, int to)
        {
            while (from < to && signature[from] == ' ') from++;
            return to > from ? new TextRange(from, to - from) : null;
        }
    }
}

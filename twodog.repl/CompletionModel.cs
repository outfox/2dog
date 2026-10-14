namespace twodog.Repl;

internal readonly record struct TextRange(int Start, int Length)
{
    public int End => Start + Length;
    public bool IsEmpty => Length == 0;
    public bool Contains(int position) => position >= Start && position <= End;
}

/// <summary>Replaces <see cref="Range"/> with <see cref="NewText"/>; the caret defaults to the end of the new text.</summary>
internal sealed record CompletionEdit(TextRange Range, string NewText, int? NewCaret = null)
{
    public (string Text, int Caret) Apply(string text)
        => (text.Remove(Range.Start, Range.Length).Insert(Range.Start, NewText), NewCaret ?? Range.Start + NewText.Length);
}

/// <summary>How the user accepted a completion. Tab, Shift+Tab and Enter also complete calls with parentheses.</summary>
internal enum CommitKey { Tab, Enter, Character }

/// <summary>One completion candidate. Subclasses rank themselves against the typed prefix and may rewrite more text.</summary>
internal class ReplCompletionItem(string replacementText, StyledText? display = null, string? filterText = null,
    Func<CancellationToken, Task<StyledText>>? description = null, string commitCharacters = "",
    Func<string, int, CancellationToken, Task<CompletionEdit>>? edit = null)
{
    public string ReplacementText { get; } = replacementText;
    public StyledText Display { get; } = display ?? replacementText;
    public string DisplayText => Display.Text;
    public string FilterText { get; } = filterText ?? replacementText;
    /// <summary>Characters that accept this item in addition to the language's usual commit characters.</summary>
    public string CommitCharacters { get; } = commitCharacters;
    public bool HasEdit => edit is not null;

    public Task<StyledText> DescriptionAsync(CancellationToken token)
        => description?.Invoke(token) ?? Task.FromResult(StyledText.Empty);

    /// <summary>The item's own edit; null if it simply replaces the language's completion range.</summary>
    public Task<CompletionEdit>? EditAsync(string text, int caret, CancellationToken token) => edit?.Invoke(text, caret, token);

    /// <summary>Negative for items that do not match the typed text. Higher values sort first.</summary>
    public virtual int Priority(string text, int caret, TextRange range)
    {
        if (range.IsEmpty) return 0;
        var typed = text.Substring(range.Start, range.Length);
        var matches = typed.Length <= FilterText.Length;
        var (longer, shorter) = matches ? (FilterText, typed) : (typed, FilterText);
        var priority = longer.StartsWith(shorter, StringComparison.Ordinal) ? 4
            : longer.StartsWith(shorter, StringComparison.OrdinalIgnoreCase) ? 3
            : longer.Contains(shorter, StringComparison.Ordinal) ? 2
            : longer.Contains(shorter, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        if (priority == 0) return int.MinValue;
        // Text longer than the item only contains it: still better than no match, but never selected.
        return matches ? priority : int.MinValue + priority;
    }
}

internal sealed record SignatureParameter(string Name, string Documentation);

internal sealed record SignatureItem(StyledText Signature, string Summary, IReadOnlyList<SignatureParameter> Parameters);

/// <summary>What Tab does when no completion menu is open.</summary>
internal sealed record TabAction(CompletionEdit? Edit, bool OpenMenu)
{
    public static readonly TabAction Ignore = new(null, false), Open = new(null, true);
}

/// <summary>Language services for the line editor. All members may run concurrently with evaluation.</summary>
internal interface IReplLanguage
{
    Task<TextRange> CompletionRangeAsync(string text, int caret, CancellationToken token);
    Task<IReadOnlyList<ReplCompletionItem>> CompletionsAsync(string text, int caret, TextRange range, CancellationToken token);
    /// <summary>The edit for accepting <paramref name="item"/>, e.g. adding call parentheses for <see cref="CommitKey.Tab"/>.</summary>
    Task<CompletionEdit> CommitAsync(ReplCompletionItem item, string text, int caret, CommitKey key, CancellationToken token);
    /// <summary>Whether typing <paramref name="key"/> accepts the selected item, rather than inserting a literal character.</summary>
    bool AcceptsCommit(ReplCompletionItem item, string text, int caret, char key);
    bool ShouldOpenCompletion(string text, int caret, char typed);
    Task<TabAction> TabAsync(string text, int caret, CancellationToken token);
    /// <summary>Whether a typed ')' should step over the closing parenthesis at the caret.</summary>
    Task<bool> SkipsCloseParenAsync(string text, int caret, CancellationToken token);
    Task<IReadOnlyList<StyledSpan>> HighlightAsync(string text, CancellationToken token);
    Task<(IReadOnlyList<SignatureItem> Items, int Argument)> SignaturesAsync(string text, int caret, CancellationToken token);
    /// <summary>Exact commands that Enter runs immediately, even with the completion menu open.</summary>
    bool IsBareCommand(string text);
}

/// <summary>Plain text editing without language support.</summary>
internal class PlainLanguage : IReplLanguage
{
    public static readonly PlainLanguage Instance = new();

    public virtual Task<TextRange> CompletionRangeAsync(string text, int caret, CancellationToken token)
    {
        var start = caret;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_')) start--;
        return Task.FromResult(new TextRange(start, caret - start));
    }

    public virtual Task<IReadOnlyList<ReplCompletionItem>> CompletionsAsync(string text, int caret, TextRange range, CancellationToken token)
        => Task.FromResult<IReadOnlyList<ReplCompletionItem>>([]);

    public virtual async Task<CompletionEdit> CommitAsync(ReplCompletionItem item, string text, int caret, CommitKey key, CancellationToken token)
        => await (item.EditAsync(text, caret, token) ?? Task.FromResult(new CompletionEdit(await CompletionRangeAsync(text, caret, token), item.ReplacementText)));

    public virtual bool AcceptsCommit(ReplCompletionItem item, string text, int caret, char key) => true;

    public virtual bool ShouldOpenCompletion(string text, int caret, char typed)
    {
        if (caret > 0 && text[caret - 1] is '.' or '(') return true;
        // A word starting the input, or any later word.
        if (caret == 1 && !char.IsWhiteSpace(text[0]) && (text.Length == 1 || !char.IsLetterOrDigit(text[1]))) return true;
        return caret >= 2 && char.IsWhiteSpace(text[caret - 2]) && char.IsLetter(text[caret - 1]);
    }

    public virtual Task<TabAction> TabAsync(string text, int caret, CancellationToken token) => Task.FromResult(TabAction.Open);
    public virtual Task<bool> SkipsCloseParenAsync(string text, int caret, CancellationToken token) => Task.FromResult(false);
    public virtual Task<IReadOnlyList<StyledSpan>> HighlightAsync(string text, CancellationToken token)
        => Task.FromResult<IReadOnlyList<StyledSpan>>([]);
    public virtual Task<(IReadOnlyList<SignatureItem> Items, int Argument)> SignaturesAsync(string text, int caret, CancellationToken token)
        => Task.FromResult<(IReadOnlyList<SignatureItem>, int)>(([], 0));
    public virtual bool IsBareCommand(string text) => false;
}

using PrettyPrompt.Completion;

namespace twodog.Repl;

// Keep the original buffer, not just a word: complex Roslyn edits and automatic
// parentheses must be reversible without damaging surrounding code.
internal sealed class CompletionCycle(string original, int originalCaret, IReadOnlyList<CompletionItem> items, int index)
{
    public string Original { get; private set; } = original;
    public int OriginalCaret { get; private set; } = originalCaret;
    public IReadOnlyList<CompletionItem> Items { get; } = items;
    public int Index { get; private set; } = index;
    public string Text { get; private set; } = original;
    public int Caret { get; private set; } = originalCaret;

    public bool Applied { get; private set; }
    public void Refine(string text, int caret)
    {
        Original = Text = text;
        OriginalCaret = Caret = caret;
        Applied = false;
    }
    public bool Matches(string text, int caret) => Text == text && Caret == caret;
    public CompletionItem Next(int direction)
    {
        Index = (Index + direction + Items.Count) % Items.Count;
        return Items[Index];
    }
    public (string Text, int Caret) Apply(CompletionEdit edit)
    {
        Applied = true;
        Text = Original.Remove(edit.SpanToReplace.Start, edit.SpanToReplace.Length).Insert(edit.SpanToReplace.Start, edit.NewText);
        Caret = edit.NewCaret ?? edit.SpanToReplace.Start + edit.NewText.Length;
        return (Text, Caret);
    }
}

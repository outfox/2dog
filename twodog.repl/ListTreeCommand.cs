using System.Diagnostics.CodeAnalysis;

namespace twodog.Repl;

internal sealed record ListTreeCommand(int Start, int ArgumentStart, string? Path, string? Error)
{
    public bool AwaitingTarget => ArgumentStart > Start + 2 && Path is null && Error is null;
    private const string Usage = "Usage: ls or ls $Control/Child. Use ls $[\"Control/My Node\"] for spaces or punctuation.";

    public static bool TryParse(string text, [NotNullWhen(true)] out ListTreeCommand? command)
    {
        command = null;
        var start = 0;
        while (start < text.Length && char.IsWhiteSpace(text[start])) start++;
        if (!text.AsSpan(start).StartsWith("ls", StringComparison.Ordinal) ||
            text.Length > start + 2 && !char.IsWhiteSpace(text[start + 2]) && text[start + 2] != ';') return false;
        var argumentStart = start + 2;
        while (argumentStart < text.Length && char.IsWhiteSpace(text[argumentStart])) argumentStart++;
        var argument = text[argumentStart..].TrimEnd();
        if (argument.EndsWith(';')) argument = argument[..^1].TrimEnd();
        if (argument.Length == 0)
        {
            command = new(start, argumentStart, null, null);
            return true;
        }
        var paths = NodePathInput.Find(argument);
        if (paths.Any(path => path.Search))
        {
            command = new(start, argumentStart, null, "Press Tab to choose a full $ path for the node search before running ls.");
            return true;
        }
        var valid = paths.Count == 1 && paths[0] is { Complete: true, Path.Length: > 0, Span.Start: 0 } path &&
            path.Span.End == argument.Length;
        command = new(start, argumentStart, valid ? paths[0].LookupPath : null, valid ? null : Usage);
        return true;
    }
}

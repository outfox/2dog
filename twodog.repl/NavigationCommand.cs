using System.Diagnostics.CodeAnalysis;

namespace twodog.Repl;

internal sealed record NavigationCommand(string Name, int Start, int ArgumentStart, string? Path, string? Error, bool Explicit)
{
    public int Length => Name.Length + (Explicit ? 1 : 0);
    public bool AwaitingTarget => Name == "cd" && ArgumentStart > Start + Length && Path is null && Error is null;
    public bool InArgument(int caret) => Name == "cd" && ArgumentStart > Start + Length && caret >= ArgumentStart;
    private const string Usage = "Usage: cd $Child, cd .., or cd /. Use :cd $[\"My Node\"] for spaces; :cd also accepts absolute /root paths.";

    public static bool TryParse(string text, [NotNullWhen(true)] out NavigationCommand? command)
    {
        command = null;
        var start = 0;
        while (start < text.Length && char.IsWhiteSpace(text[start])) start++;
        var explicitCommand = start < text.Length && text[start] == ':';
        var nameStart = start + (explicitCommand ? 1 : 0);
        var end = nameStart;
        while (end < text.Length && char.IsLetter(text[end])) end++;
        var name = text[nameStart..end];
        if (name is not ("cd" or "pwd") || end < text.Length && !char.IsWhiteSpace(text[end]) && text[end] != ';') return false;
        var argumentStart = end;
        while (argumentStart < text.Length && char.IsWhiteSpace(text[argumentStart])) argumentStart++;
        var argument = text[argumentStart..].TrimEnd();
        if (argument.EndsWith(';')) argument = argument[..^1].TrimEnd();
        if (name == "pwd")
        {
            if (argument.Length > 0 && !explicitCommand) return false;
            command = new(name, start, argumentStart, null, argument.Length == 0 ? null : "Usage: pwd or :pwd", explicitCommand);
            return true;
        }
        if (argument.Length == 0 || argument is "/" or "." or "..")
        {
            command = new(name, start, argumentStart, argument.Length == 0 ? null : argument, null, explicitCommand);
            return true;
        }
        // Slash is also C# division. Bare absolute paths therefore need either
        // the $ decorator or :cd; assignments, calls and member access stay C#.
        if (explicitCommand && argument.StartsWith("/root", StringComparison.Ordinal) && !argument.Any(char.IsWhiteSpace))
        {
            command = new(name, start, argumentStart, argument.TrimEnd('/'), null, true);
            return true;
        }
        var paths = NodePathInput.Find(argument);
        if (argument.StartsWith('?') && paths.Count == 1 && paths[0].Search && paths[0].Span.End == argument.Length)
        {
            command = new(name, start, argumentStart, null, "Press Tab to choose a full $ path before changing scope.", explicitCommand);
            return true;
        }
        if (!argument.StartsWith('$') && !explicitCommand) return false;
        var valid = paths.Count == 1 && paths[0] is { Complete: true, Search: false, Path.Length: > 0, Span.Start: 0 } path &&
            path.Span.End == argument.Length;
        command = new(name, start, argumentStart, valid ? paths[0].LookupPath : null, valid ? null : Usage, explicitCommand);
        return true;
    }
}

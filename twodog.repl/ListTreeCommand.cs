using System.Diagnostics.CodeAnalysis;

namespace twodog.Repl;

/// <param name="Depth">How many levels below the node to list; null lists them all.</param>
internal sealed record ListTreeCommand(int Start, int ArgumentStart, string? Path, string? Error, bool Explicit = false, int? Depth = null)
{
    public int Length => Explicit ? 3 : 2;
    public bool InArgument(int caret) => ArgumentStart > Start + Length && caret >= ArgumentStart;
    public bool AwaitingTarget => InArgument(ArgumentStart) && Path is null && Error is null;
    private const string Usage = "Usage: ls [-d N] or ls [-d N] $Control/Child; -d lists N levels below the node. Use ls $[\"Control/My Node\"] for spaces or punctuation.";

    public static bool TryParse(string text, [NotNullWhen(true)] out ListTreeCommand? command, bool forceShell = false)
    {
        command = null;
        var start = 0;
        while (start < text.Length && char.IsWhiteSpace(text[start])) start++;
        var explicitCommand = start < text.Length && text[start] == ':';
        var nameStart = start + (explicitCommand ? 1 : 0);
        if (!text.AsSpan(nameStart).StartsWith("ls", StringComparison.Ordinal) ||
            text.Length > nameStart + 2 && !char.IsWhiteSpace(text[nameStart + 2]) && text[nameStart + 2] != ';') return false;
        var argumentStart = nameStart + 2;
        while (argumentStart < text.Length && char.IsWhiteSpace(text[argumentStart])) argumentStart++;
        var argument = text[argumentStart..].TrimEnd();
        if (argument.EndsWith(';')) argument = argument[..^1].TrimEnd();
        int? depth = null;
        if (argument == "-d" || argument.StartsWith("-d ", StringComparison.Ordinal))
        {
            // ls -d N [path]: the option ends at the number, and the path follows it.
            var position = 2;
            while (position < argument.Length && argument[position] == ' ') position++;
            var digits = position;
            while (position < argument.Length && char.IsAsciiDigit(argument[position])) position++;
            if (position == digits || position < argument.Length && argument[position] != ' ' || !int.TryParse(argument[digits..position], out var levels))
            {
                command = new(start, argumentStart, null, Usage, explicitCommand);
                return true;
            }
            depth = levels;
            argumentStart += position;
            // Trailing whitespace was trimmed from the argument, but the path starts after it.
            while (argumentStart < text.Length && char.IsWhiteSpace(text[argumentStart])) argumentStart++;
            argument = argument[position..].TrimStart();
        }
        if (argument.Length == 0)
        {
            command = new(start, argumentStart, null, null, explicitCommand, depth);
            return true;
        }
        if (NodePathInput.IsAbsolutePath(argument))
        {
            command = new(start, argumentStart, argument.TrimEnd('/'), null, explicitCommand, depth);
            return true;
        }
        // In auto mode, preserve ordinary C# expressions using a persistent ls
        // variable. Shell mode deliberately keeps command-name precedence.
        if (!explicitCommand && !forceShell && depth is null && NodeTransferCommand.IsCSharp(text, argument)) return false;
        var paths = NodePathInput.Find(argument);
        if (paths.Any(path => path.Search))
        {
            command = new(start, argumentStart, null, "Press Tab to choose a full $ path for the node search before running ls.", explicitCommand, depth);
            return true;
        }
        var valid = paths.Count == 1 && paths[0] is { Complete: true, Path.Length: > 0, Span.Start: 0 } path &&
            path.Span.End == argument.Length;
        command = new(start, argumentStart, valid ? paths[0].LookupPath : null, valid ? null : Usage, explicitCommand, depth);
        return true;
    }
}

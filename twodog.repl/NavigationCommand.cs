using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace twodog.Repl;

internal sealed record NavigationCommand(string Name, int Start, int ArgumentStart, string? Path, string? Error, bool Explicit, string? Expression = null, TextSpan? DestinationSpan = null)
{
    public int Length => Name.Length + (Explicit ? 1 : 0);
    public TextSpan? ExpressionSpan => Expression is null ? null : new TextSpan(ArgumentStart, Expression.Length);
    public bool IsTransfer => Name is "cp" or "mv";
    public bool AwaitingTarget => ArgumentStart > Start + Length && (IsTransfer
        ? Expression is null || DestinationSpan is { Length: 0 } destination && destination.Start > ExpressionSpan!.Value.End
        : Name is "cd" or "rm" && Path is null && Expression is null && Error is null);
    public bool InArgument(int caret) => Name is "cd" or "rm" or "cp" or "mv" && ArgumentStart > Start + Length && caret >= ArgumentStart;
    public bool AwaitingTargetAt(int caret) => AwaitingTarget && caret >= (DestinationSpan?.Start ?? ArgumentStart);
    internal static string Usage(string name) => name is "cp" or "mv"
        ? "Usage: " + name + " $Source $Parent or " + name + " sourceExpression parentExpression. The parent must exist; names must not collide. Use :" + name + " to force command handling."
        : name == "rm"
        ? "Usage: rm $Child or rm nodeExpression. A target is required; the root Window cannot be removed."
        : "Usage: cd $Child, cd nodeExpression, cd .., or cd /. Use :cd $[\"My Node\"] for spaces; :cd also accepts absolute /root paths.";

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
        if (name is not ("cd" or "pwd" or "rm" or "cp" or "mv") || end < text.Length && !char.IsWhiteSpace(text[end]) && text[end] != ';') return false;
        var argumentStart = end;
        while (argumentStart < text.Length && char.IsWhiteSpace(text[argumentStart])) argumentStart++;
        var argument = text[argumentStart..].TrimEnd();
        if (argument.EndsWith(';')) argument = argument[..^1].TrimEnd();
        if (name is "cp" or "mv")
        {
            if (!explicitCommand && NodeTransferCommand.IsCSharp(text, argument)) return false;
            command = NodeTransferCommand.Parse(text, name, start, argumentStart, argument, explicitCommand);
            return true;
        }
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
        // the $ decorator or :cd.
        if (explicitCommand && argument.StartsWith("/root", StringComparison.Ordinal) && !argument.Any(char.IsWhiteSpace))
        {
            command = new(name, start, argumentStart, argument.TrimEnd('/'), null, true);
            return true;
        }
        var paths = NodePathInput.Find(argument);
        if (argument.StartsWith('?') && paths.Count == 1 && paths[0].Search && paths[0].Span.End == argument.Length)
        {
            command = new(name, start, argumentStart, null, "Press Tab to choose a full $ path before running " + name + ".", explicitCommand);
            return true;
        }
        var validPath = paths.Count == 1 && paths[0] is { Complete: true, Search: false, Path.Length: > 0, Span.Start: 0 } path &&
            path.Span.End == argument.Length;
        if (validPath)
        {
            command = new(name, start, argumentStart, paths[0].LookupPath, null, explicitCommand);
            return true;
        }
        // Prefer ordinary C# calls, assignments and arithmetic, including
        // submissions with several statements. A declaration-shaped `cd x;`
        // remains navigation; :cd disambiguates parenthesized expressions.
        if (!explicitCommand && !argument.StartsWith('$'))
        {
            var ordinaryInput = new NodePathInput(text, new Dictionary<string, NodePathTarget>());
            var ordinary = CSharpSyntaxTree.ParseText(ordinaryInput.Code, new CSharpParseOptions(kind: SourceCodeKind.Script)).GetRoot();
            if (!ordinary.ContainsDiagnostics && ordinary is CompilationUnitSyntax unit &&
                unit.Members.FirstOrDefault() is GlobalStatementSyntax { Statement: ExpressionStatementSyntax }) return false;
        }
        var input = new NodePathInput(argument, new Dictionary<string, NodePathTarget>());
        var expression = SyntaxFactory.ParseExpression(input.Code, consumeFullText: true);
        // Parse exactly one expression. A malformed command must not execute a
        // leading call followed by additional statements or script directives.
        var validExpression = !expression.ContainsDiagnostics && !expression.ContainsDirectives &&
            input.References.All(p => p.Complete && !p.Search && p.Path.Length > 0);
        command = new(name, start, argumentStart, null, validExpression ? null : Usage(name), explicitCommand, argument);
        return true;
    }
}

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace twodog.Repl;

// Two expressions, not a shell grammar. Roslyn supplies token boundaries and
// validates both operands before either is allowed to execute.
internal static class NodeTransferCommand
{
    public static bool IsCSharp(string text, string argument)
    {
        if (argument.Length == 0 || argument[0] == '$' ||
            argument[0] == '?' && !argument.StartsWith("??", StringComparison.Ordinal) && !argument.Contains(':')) return false;
        // Preserve unfinished calls/member/index access while editing, too.
        if (argument[0] is '(' or '.' or '[' or '=' or '+' or '-' or '*' or '/' or '%' or
            '&' or '|' or '^' or '!' or '<' or '>') return true;
        var input = new NodePathInput(text, new Dictionary<string, NodePathTarget>());
        var root = CSharpSyntaxTree.ParseText(input.Code, new CSharpParseOptions(kind: SourceCodeKind.Script)).GetRoot();
        return !root.ContainsDiagnostics && root is CompilationUnitSyntax unit &&
            unit.Members.FirstOrDefault() is GlobalStatementSyntax { Statement: ExpressionStatementSyntax };
    }

    public static NavigationCommand Parse(string text, string name, int start, int argumentStart, string argument, bool explicitCommand)
    {
        if (argument.Length == 0) return new(name, start, argumentStart, null, null, explicitCommand);
        var input = new NodePathInput(argument, new Dictionary<string, NodePathTarget>());
        var boundaries = new List<(int End, int Next)>();
        var depth = 0;
        foreach (var token in SyntaxFactory.ParseTokens(input.Code))
        {
            if (token.Kind() is SyntaxKind.OpenParenToken or SyntaxKind.OpenBracketToken or SyntaxKind.OpenBraceToken) depth++;
            if (token.Kind() is SyntaxKind.CloseParenToken or SyntaxKind.CloseBracketToken or SyntaxKind.CloseBraceToken) depth--;
            if (depth != 0 || token.IsKind(SyntaxKind.EndOfFileToken)) continue;
            var end = input.ToOriginal(token.Span.End, end: true);
            var next = end;
            while (next < argument.Length && char.IsWhiteSpace(argument[next])) next++;
            if (next > end) boundaries.Add((end, next));
        }
        var valid = boundaries.Where(b => IsExpression(argument[..b.End]) && IsExpression(argument[b.Next..])).ToArray();
        // Keep partial operands available to the same completion pipeline.
        (int End, int Next) split = valid.Length == 1 ? valid[0] : boundaries.FirstOrDefault((argument.Length, argument.Length));
        var destinationStart = argumentStart + split.Next;
        while (destinationStart < text.Length && char.IsWhiteSpace(text[destinationStart])) destinationStart++;
        var destinationLength = Math.Max(0, argumentStart + argument.Length - destinationStart);
        var error = valid.Length == 1 ? null : NavigationCommand.Usage(name);
        if (valid.Length == 1 && input.References.Any(p => p.Search))
            error = "Press Tab to choose full $ paths before running " + name + ".";
        return new(name, start, argumentStart, null, error, explicitCommand, argument[..split.End],
            new TextSpan(destinationStart, destinationLength));
    }

    private static bool IsExpression(string text)
    {
        var input = new NodePathInput(text, new Dictionary<string, NodePathTarget>());
        // Translating adjacent aliases introduces parentheses that C# could
        // mistake for a call. They are separate node operands, never a call.
        if (input.References.Zip(input.References.Skip(1)).Any(pair =>
            text[pair.First.Span.End..pair.Second.Span.Start].All(char.IsWhiteSpace))) return false;
        var expression = SyntaxFactory.ParseExpression(input.Code, consumeFullText: true);
        return !expression.ContainsDiagnostics && !expression.ContainsDirectives &&
            input.References.All(p => p.Search || p.Complete && p.Path.Length > 0);
    }
}

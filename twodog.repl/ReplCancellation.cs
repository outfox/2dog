using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace twodog.Repl;

/// <summary>Cancellation checkpoints inserted into submitted C# loops. This is cooperative, not a sandbox.</summary>
public static class ReplCancellation
{
    private static readonly AsyncLocal<CancellationToken> Current = new();
    /// <summary>Checks the current submission's cancellation token.</summary>
    public static void Check() => Current.Value.ThrowIfCancellationRequested();
    internal static IDisposable Enter(CancellationToken token)
    {
        var previous = Current.Value;
        Current.Value = token;
        return new Scope(previous);
    }
    private sealed class Scope(CancellationToken previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }

    internal static string Instrument(string code)
        => new Checkpoints().Visit(CSharpSyntaxTree.ParseText(code, new CSharpParseOptions(kind: SourceCodeKind.Script)).GetRoot())!.ToFullString();

    private sealed class Checkpoints : CSharpSyntaxRewriter
    {
        private static StatementSyntax Body(StatementSyntax body)
        {
            var check = SyntaxFactory.ParseStatement("global::twodog.Repl.ReplCancellation.Check();");
            return body is BlockSyntax block ? block.WithStatements(block.Statements.Insert(0, check)) : SyntaxFactory.Block(check, body);
        }
        public override SyntaxNode? VisitWhileStatement(WhileStatementSyntax node)
        {
            var visited = (WhileStatementSyntax)base.VisitWhileStatement(node)!;
            return visited.WithStatement(Body(visited.Statement));
        }
        public override SyntaxNode? VisitDoStatement(DoStatementSyntax node)
        {
            var visited = (DoStatementSyntax)base.VisitDoStatement(node)!;
            return visited.WithStatement(Body(visited.Statement));
        }
        public override SyntaxNode? VisitForStatement(ForStatementSyntax node)
        {
            var visited = (ForStatementSyntax)base.VisitForStatement(node)!;
            return visited.WithStatement(Body(visited.Statement));
        }
        public override SyntaxNode? VisitForEachStatement(ForEachStatementSyntax node)
        {
            var visited = (ForEachStatementSyntax)base.VisitForEachStatement(node)!;
            return visited.WithStatement(Body(visited.Statement));
        }
        public override SyntaxNode? VisitForEachVariableStatement(ForEachVariableStatementSyntax node)
        {
            var visited = (ForEachVariableStatementSyntax)base.VisitForEachVariableStatement(node)!;
            return visited.WithStatement(Body(visited.Statement));
        }
    }
}

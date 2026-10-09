using System.Text;
using Godot;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace twodog.Repl;

internal sealed record NodePathReference(TextSpan Span, string Path, bool Complete = true, bool Search = false)
{
    public string LookupPath => Path.TrimEnd('/');
}
internal sealed record NodePathTarget(string Path, string TypeName, string DisplayType);

// Keep the user's text in the editor/history. Roslyn receives ordinary typed C#, with
// a bidirectional position map for completion, highlighting and diagnostic locations.
internal sealed class NodePathInput
{
    private readonly List<(TextSpan Original, TextSpan Generated)> replacements = [];
    public string Original { get; }
    public string Code { get; }
    public IReadOnlyList<NodePathReference> References { get; }

    public NodePathInput(string text, IReadOnlyDictionary<string, NodePathTarget> nodes)
    {
        Original = text;
        References = Find(text);
        var code = new StringBuilder();
        var position = 0;
        foreach (var reference in References)
        {
            code.Append(text, position, reference.Span.Start - position);
            var start = code.Length;
            var type = nodes.TryGetValue(reference.LookupPath, out var node) ? node.TypeName : "global::Godot.Node";
            code.Append("(root.GetNode<").Append(type).Append(">(")
                .Append(SymbolDisplay.FormatLiteral(reference.LookupPath, quote: true)).Append("))");
            replacements.Add((reference.Span, TextSpan.FromBounds(start, code.Length)));
            position = reference.Span.End;
        }
        Code = code.Append(text, position, text.Length - position).ToString();
    }

    public NodePathReference? At(int caret) => References.FirstOrDefault(r => caret > r.Span.Start && caret <= r.Span.End);
    public bool IsGenerated(TextSpan span) => replacements.Any(r => r.Generated.OverlapsWith(span));
    public int ToGenerated(int position) => Map(position, toGenerated: true);
    public int ToOriginal(int position, bool end = false) => Map(position, toGenerated: false, end);
    public TextSpan ToOriginal(TextSpan span) => TextSpan.FromBounds(ToOriginal(span.Start), ToOriginal(span.End, end: true));

    private int Map(int position, bool toGenerated, bool end = false)
    {
        var delta = 0;
        foreach (var replacement in replacements)
        {
            var from = toGenerated ? replacement.Original : replacement.Generated;
            var to = toGenerated ? replacement.Generated : replacement.Original;
            if (position <= from.Start) return position + delta;
            if (position < from.End) return end ? to.End : to.Start;
            delta += to.Length - from.Length;
        }
        return position + delta;
    }

    internal static IReadOnlyList<NodePathReference> Find(string text)
    {
        // Roslyn knows comments, regular/verbatim/raw strings and interpolation holes.
        // Only a standalone invalid '$' token starts our extension; $"..." remains C#.
        var tokens = CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(kind: SourceCodeKind.Script))
            .GetRoot().DescendantTokens(descendIntoTrivia: true).ToArray();
        var dollars = tokens.Where(t => t.IsKind(SyntaxKind.BadToken) && t.Text == "$")
            .Select(t => t.SpanStart).Distinct().Order();
        var result = new List<NodePathReference>();
        foreach (var start in dollars)
        {
            if (result.Count > 0 && start < result[^1].Span.End) continue;
            var end = start + 1;
            if (end < text.Length && text[end] == '[')
            {
                var literalStart = end + 1;
                while (literalStart < text.Length && char.IsWhiteSpace(text[literalStart])) literalStart++;
                var token = SyntaxFactory.ParseToken(text[literalStart..]);
                if (!token.IsKind(SyntaxKind.StringLiteralToken))
                {
                    result.Add(new(new TextSpan(start, end - start + 1), "", false));
                    continue;
                }
                end = literalStart + token.Span.End;
                while (end < text.Length && char.IsWhiteSpace(text[end])) end++;
                var complete = end < text.Length && text[end] == ']' && !token.ContainsDiagnostics;
                if (complete) end++;
                result.Add(new(TextSpan.FromBounds(start, end), token.ValueText, complete));
                continue;
            }
            while (end < text.Length && (SyntaxFacts.IsIdentifierPartCharacter(text[end]) || text[end] == '/')) end++;
            result.Add(new(TextSpan.FromBounds(start, end), text[(start + 1)..end]));
        }
        foreach (var token in tokens.Where(t => t.IsKind(SyntaxKind.QuestionToken)))
        {
            var start = token.SpanStart;
            if (result.Any(r => r.Span.Contains(start))) continue;
            // A search starts an expression. Leave nullable types, ternaries,
            // null-conditional access, strings and comments to ordinary C#.
            var previous = token.GetPreviousToken(includeSkipped: true);
            var context = previous.Kind();
            var listing = text[..start].Trim() == "ls";
            if (!listing && previous.RawKind != 0 && context is not (
                SyntaxKind.OpenParenToken or SyntaxKind.OpenBracketToken or SyntaxKind.OpenBraceToken or
                SyntaxKind.CommaToken or SyntaxKind.SemicolonToken or SyntaxKind.EqualsToken or SyntaxKind.EqualsGreaterThanToken or
                SyntaxKind.ReturnKeyword or SyntaxKind.PlusToken or SyntaxKind.MinusToken or SyntaxKind.AsteriskToken or
                SyntaxKind.SlashToken or SyntaxKind.PercentToken or SyntaxKind.AmpersandAmpersandToken or SyntaxKind.BarBarToken)) continue;
            var end = start + 1;
            if (end < text.Length && text[end] is '.' or '?' or '[') continue;
            while (end < text.Length && SyntaxFacts.IsIdentifierPartCharacter(text[end])) end++;
            result.Add(new(TextSpan.FromBounds(start, end), text[(start + 1)..end], false, Search: true));
        }
        return result.OrderBy(r => r.Span.Start).ToArray();
    }

    // Called only through the engine dispatcher. Editor callbacks never traverse Godot off-thread.
    public static IReadOnlyDictionary<string, NodePathTarget> Capture(Window root, CancellationToken token)
    {
        var nodes = new Dictionary<string, NodePathTarget>(StringComparer.Ordinal);
        var pending = new Stack<Node>();
        pending.Push(root);
        while (pending.TryPop(out var node))
        {
            token.ThrowIfCancellationRequested();
            if (!GodotObject.IsInstanceValid(node) || node.IsQueuedForDeletion()) continue;
            using var relative = root.GetPathTo(node);
            using var absolute = node.GetPath();
            var type = ReferenceableType(node.GetType());
            var target = new NodePathTarget(relative.ToString(), type, node.GetType().FullName ?? type);
            nodes[target.Path] = target;
            nodes[absolute.ToString()] = target;
            foreach (var child in node.GetChildren()) pending.Push(child);
        }
        return nodes;
    }

    private static string ReferenceableType(Type actual)
    {
        for (Type? type = actual; type is not null; type = type.BaseType)
        {
            if (!type.IsVisible || type.IsGenericType || type.Assembly.IsDynamic || string.IsNullOrEmpty(type.Assembly.Location)) continue;
            var parts = type.FullName?.Replace('+', '.').Split('.');
            if (parts is null || parts.Any(p => !SyntaxFacts.IsValidIdentifier(p) && SyntaxFacts.GetKeywordKind(p) == SyntaxKind.None)) continue;
            return "global::" + string.Join(".", parts.Select(p => SyntaxFacts.GetKeywordKind(p) == SyntaxKind.None ? p : "@" + p));
        }
        return "global::Godot.Node";
    }
}

using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using PrettyPrompt.Completion;
using PrettyPrompt.Highlighting;
using PromptSpan = PrettyPrompt.Documents.TextSpan;

namespace twodog.Repl;

// Language-aware edits on top of Roslyn's completion service and PrettyPrompt's editor.
// Semantic queries inspect metadata only; they never invoke a live object's getters.
internal static class ReplCompletion
{
    public static async Task<char?> ContinuationAsync(PreparedInput prepared, int caret, CancellationToken token)
    {
        var reference = prepared.Input.CompletionAt(caret);
        if (reference is { } incomplete &&
            (incomplete.Search || !incomplete.Complete || incomplete.Path.Length == 0 || incomplete.Path.EndsWith('/') ||
                caret != incomplete.Span.End || !prepared.Nodes.ContainsKey(incomplete.LookupPath))) return null;
        if (reference is { Complete: true } path && caret == path.Span.End && prepared.Nodes.ContainsKey(path.Path))
        {
            if (!prepared.Input.Original.AsSpan(path.Span.Start).StartsWith("$[") &&
                prepared.Nodes.Values.Any(n => (path.Path.StartsWith('/') ? n.AbsolutePath ?? n.Path : n.Path).StartsWith(path.Path + "/", StringComparison.Ordinal))) return '/';
            return prepared.Mode.TryListing(prepared.Input.Original, out _) || prepared.Mode.TryNavigation(prepared.Input.Original, out _) ? null : '.';
        }
        if (prepared.Mode.TryListing(prepared.Input.Original, out _) ||
            prepared.Mode.TryNavigation(prepared.Input.Original, out var navigation) && navigation.Expression is null) return null;
        var position = prepared.Input.ToGenerated(caret);
        var root = await prepared.Document.GetSyntaxRootAsync(token);
        var expression = ExpressionAt(root!, position);
        if (expression is null) return null;
        var model = (await prepared.Document.GetSemanticModelAsync(token))!;
        if (expression is not InvocationExpressionSyntax && model.GetMemberGroup(expression, token).OfType<IMethodSymbol>().Any()) return '(';
        if (expression.Parent is ObjectCreationExpressionSyntax creation && creation.Type == expression) return '(';
        var info = model.GetSymbolInfo(expression, token);
        // A bare namespace/type is not a C# value. Roslyn reports it as a
        // candidate, but Tab should still let the user continue with a member.
        var symbol = info.Symbol ?? info.CandidateSymbols.FirstOrDefault(s => s is INamespaceSymbol or INamedTypeSymbol);
        if (model.GetTypeInfo(expression, token).Type?.TypeKind == TypeKind.Delegate) return '(';
        if (symbol is INamespaceSymbol or INamedTypeSymbol or ILocalSymbol or IParameterSymbol or IPropertySymbol or IFieldSymbol) return '.';
        var type = model.GetTypeInfo(expression, token).Type;
        return type is { TypeKind: not TypeKind.Error, SpecialType: not SpecialType.System_Void } ? '.' : null;
    }

    private static ExpressionSyntax? ExpressionAt(SyntaxNode root, int position)
        => position == 0 ? null : root.FindToken(position - 1).Parent?.AncestorsAndSelf().OfType<ExpressionSyntax>()
            .Where(e => e.Span.End == position && e is IdentifierNameSyntax or GenericNameSyntax or MemberAccessExpressionSyntax
                or MemberBindingExpressionSyntax or InvocationExpressionSyntax or ElementAccessExpressionSyntax or ParenthesizedExpressionSyntax)
            .LastOrDefault();

    public static async Task<CompletionEdit> CallEditAsync(PreparedInput prepared, TextSpan span, string name, CancellationToken token)
    {
        var text = prepared.Input.Original.Remove(span.Start, span.Length).Insert(span.Start, name);
        var end = span.Start + name.Length;
        prepared.Mode.TryNavigation(text, out var navigation);
        var input = new NodePathInput(text, prepared.Nodes, navigation?.ExpressionSpan, navigation?.DestinationSpan);
        var document = prepared.Document.WithText(SourceText.From(input.Code));
        var root = (await document.GetSyntaxRootAsync(token))!;
        var model = (await document.GetSemanticModelAsync(token))!;
        var position = input.ToGenerated(end);
        var generic = root.FindToken(position - 1).Parent as GenericNameSyntax;
        // The identifier's existing type arguments belong before its call parentheses.
        // Keep an unfinished type list editable rather than inserting () in front of it.
        if (generic is not null && generic.Identifier.Span.End == position && generic.TypeArgumentList.GreaterThanToken.IsMissing)
            return new CompletionEdit(new PromptSpan(span.Start, span.Length), name, end);
        var callEnd = generic is not null && generic.Identifier.Span.End == position
            ? input.ToOriginal(generic.Span.End) : end;
        var expression = ExpressionAt(root, input.ToGenerated(callEnd));
        var methods = expression is null ? [] : model.GetMemberGroup(expression, token).OfType<IMethodSymbol>().ToArray();
        // In expression context Roslyn parses an unfinished <T as a comparison.
        // Parse the name separately only for generic methods; numeric comparisons
        // have a missing type argument and must still receive call parentheses.
        if (generic is null && methods.Any(m => m.Arity > 0) &&
            SyntaxFactory.ParseName(name + text[end..], consumeFullText: false) is GenericNameSyntax suffix &&
            suffix.TypeArgumentList.GreaterThanToken.IsMissing &&
            (suffix.TypeArgumentList.Arguments.Any(type => !type.IsMissing) || suffix.Span.End == suffix.TypeArgumentList.LessThanToken.Span.End))
            return new CompletionEdit(new PromptSpan(span.Start, span.Length), name, end);
        var hasArguments = methods.Length == 0 || methods.Any(m => m.Parameters.Length > 0);
        var following = callEnd;
        while (following < text.Length && char.IsWhiteSpace(text[following])) following++;
        // Completing in the middle of existing code must preserve its argument list.
        return following < text.Length && text[following] == '('
            ? new CompletionEdit(new PromptSpan(span.Start, span.Length), name, following + 1)
            : new CompletionEdit(new PromptSpan(span.Start, span.Length + callEnd - end),
                name + text[end..callEnd] + "()", callEnd + (hasArguments ? 1 : 2));
    }

    public static async Task<(IReadOnlyList<OverloadItem>, int ArgumentIndex)> OverloadsAsync(PreparedInput prepared, int caret, CancellationToken token)
    {
        var position = prepared.Input.ToGenerated(caret);
        if (position == 0) return ([], 0);
        var root = (await prepared.Document.GetSyntaxRootAsync(token))!;
        var arguments = root.FindToken(position - 1).Parent?.AncestorsAndSelf().OfType<ArgumentListSyntax>()
            .FirstOrDefault(a => a.OpenParenToken.Span.End <= position &&
                (a.CloseParenToken.IsMissing || position <= a.CloseParenToken.SpanStart));
        if (arguments is null) return ([], 0);
        var model = (await prepared.Document.GetSemanticModelAsync(token))!;
        var methods = arguments.Parent switch
        {
            InvocationExpressionSyntax invocation => model.GetMemberGroup(invocation.Expression, token).OfType<IMethodSymbol>(),
            ObjectCreationExpressionSyntax creation => (model.GetTypeInfo(creation, token).Type as INamedTypeSymbol)?.InstanceConstructors.AsEnumerable() ?? [],
            _ => [],
        };
        var items = methods.Where(m => model.IsAccessible(position, m)).OrderBy(m => m.Parameters.Length)
            .ThenBy(m => m.ToDisplayString(), StringComparer.Ordinal).Select(method =>
            {
                XElement? documentation = null;
                var xml = method.GetDocumentationCommentXml(cancellationToken: token);
                if (!string.IsNullOrWhiteSpace(xml))
                {
                    try { documentation = XElement.Parse(xml); }
                    catch (System.Xml.XmlException) { /* Metadata documentation can be malformed. */ }
                }
                return new OverloadItem(Signature(method.ToMinimalDisplayParts(model, position)),
                    Text(documentation?.Element("summary")), default,
                    method.Parameters.Select(p => new OverloadItem.Parameter(p.Name,
                        Text(documentation?.Elements("param").FirstOrDefault(e => (string?)e.Attribute("name") == p.Name)))).ToArray());
            }).ToArray();
        return (items, arguments.Arguments.GetSeparators().Count(s => s.SpanStart < position));
    }

    private static string Text(XElement? element) => element is null ? "" : Regex.Replace(element.Value, @"\s+", " ").Trim();

    private static FormattedString Signature(IEnumerable<SymbolDisplayPart> parts)
    {
        var text = new System.Text.StringBuilder();
        var spans = new List<FormatSpan>();
        foreach (var part in parts)
        {
            AnsiColor? color = part.Kind switch
            {
                SymbolDisplayPartKind.MethodName => AnsiColor.BrightYellow,
                SymbolDisplayPartKind.Keyword => AnsiColor.BrightBlue,
                SymbolDisplayPartKind.ClassName or SymbolDisplayPartKind.StructName or SymbolDisplayPartKind.InterfaceName => AnsiColor.BrightCyan,
                SymbolDisplayPartKind.ParameterName => AnsiColor.Cyan,
                _ => null,
            };
            var value = part.ToString();
            if (color is { } c) spans.Add(new FormatSpan(text.Length, value.Length, c));
            text.Append(value);
        }
        return new FormattedString(text.ToString(), spans);
    }
}

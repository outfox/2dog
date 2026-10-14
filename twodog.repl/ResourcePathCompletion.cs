using Godot;
using Microsoft.CodeAnalysis.CSharp;

namespace twodog.Repl;

internal static class ResourcePathCompletion
{
    // Godot lists original resource names even in exported packs, without import sidecars.
    internal static string[] Capture()
    {
        var paths = new List<string>();
        void Visit(string directory)
        {
            foreach (var entry in ResourceLoader.ListDirectory(directory))
            {
                var path = directory + entry;
                if (entry.StartsWith('.')) continue;
                if (entry.EndsWith('/'))
                {
                    if (!Godot.FileAccess.FileExists(path + ".gdignore")) Visit(path);
                }
                else paths.Add(path);
            }
        }
        Visit("res://");
        return paths.Order(StringComparer.Ordinal).ToArray();
    }

    internal static TextRange? Span(string text, int caret)
    {
        if (caret <= 0) return null;
        var tree = CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(kind: Microsoft.CodeAnalysis.SourceCodeKind.Script));
        var token = tree.GetRoot().FindToken(caret - 1);
        if (token.RawKind != (int)SyntaxKind.StringLiteralToken) return null;
        var start = token.SpanStart + (token.Text.StartsWith("@\"", StringComparison.Ordinal) ? 2 : 1);
        if (caret == token.Span.End && token.Text.EndsWith('"') && token.Text.Length > 1) return null;
        if (caret < start || !text.AsSpan(start, caret - start).StartsWith("res://", StringComparison.OrdinalIgnoreCase)) return null;
        return new TextRange(start, caret - start);
    }

    internal static IReadOnlyList<ReplCompletionItem> Items(string text, int caret, string[] resources, IReadOnlySet<string>? sceneResources = null)
    {
        // Scene-specific APIs accept PackedScene paths; general loaders offer every resource.
        var before = text[..(Span(text, caret)?.Start ?? caret)];
        var scenes = System.Text.RegularExpressions.Regex.IsMatch(before,
            @"(?:\.Open\s*\(|\.ChangeSceneToFile\s*\(|\.Add\s*\([^,]*,\s*|Load\s*<\s*PackedScene\s*>\s*\()\s*@?""$",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return resources.Where(path => !scenes || (sceneResources?.Contains(path) ??
                (path.EndsWith(".tscn", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".scn", StringComparison.OrdinalIgnoreCase))))
            .Select(path => (ReplCompletionItem)new ResourceItem(path)).ToArray();
    }

    private sealed class ResourceItem(string path) : ReplCompletionItem(path,
        edit: (text, caret, _) => Task.FromResult(new CompletionEdit(Span(text, caret) ?? new TextRange(caret, 0), path)))
    {
        public override int Priority(string text, int caret, TextRange span)
            => ReplacementText.StartsWith(text[span.Start..caret], StringComparison.OrdinalIgnoreCase) ? 1 : int.MinValue;
    }
}

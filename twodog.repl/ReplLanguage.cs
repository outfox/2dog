using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace twodog.Repl;

// C#, node path, resource path and shell command services for one session's editor.
// Semantic queries inspect metadata only; they never invoke a live object's getters.
internal sealed class ReplLanguage(ReplSession session) : PlainLanguage
{
    private readonly Lock gate = new();
    private Task<PreparedInput>? preparedTask;
    private string? preparedText;
    private int preparedVersion;

    private Task<PreparedInput> PrepareAsync(string text, CancellationToken token)
    {
        lock (gate)
        {
            if (preparedTask is null || preparedTask.IsCanceled || preparedTask.IsFaulted || preparedText != text || preparedVersion != session.Version)
            {
                preparedText = text;
                preparedVersion = session.Version;
                preparedTask = session.PrepareAsync(text, token);
            }
            return preparedTask;
        }
    }

    /// <summary>Exercises metadata, completion, documentation and highlighting before the first keystroke. No C# runs.</summary>
    internal async Task WarmUpAsync(CancellationToken token)
    {
        if (session.Mode == ReplMode.Agent) return;
        foreach (var text in new[] { "l", "root." })
        {
            var range = await CompletionRangeAsync(text, text.Length, token);
            var items = await CompletionsAsync(text, text.Length, range, token);
            if (items.FirstOrDefault(i => i.DisplayText != "ls") is { } item) await item.DescriptionAsync(token);
            await HighlightAsync(text, token);
        }
    }

    public override bool IsBareCommand(string text) => ReplModes.Controls.Contains(text) ||
        session.Mode.HasShell() && (ReplModes.ShellCommands.Contains(text) ||
            text.StartsWith(':') && ReplModes.ShellCommands.Contains(text[1..]));

    public override async Task<TextRange> CompletionRangeAsync(string text, int caret, CancellationToken token)
    {
        if (ResourcePathCompletion.Span(text, caret) is { } resource) return resource;
        if (AwaitingNodeArgument(text, caret)) return new TextRange(caret, 0);
        if (text.TrimStart().StartsWith(':') && !CommandArgument(text, caret))
            return new TextRange(text.IndexOf(':'), text.Length - text.IndexOf(':'));
        var prepared = await PrepareAsync(text, token);
        if (prepared.Input.CompletionAt(caret) is { } path) return new TextRange(path.Span.Start, path.Span.Length);
        var span = CompletionService.GetService(prepared.Document)!.GetDefaultCompletionListSpan(
            SourceText.From(prepared.Input.Code), prepared.Input.ToGenerated(caret));
        var original = prepared.Input.ToOriginal(span);
        return new TextRange(original.Start, original.Length);
    }

    public override async Task<IReadOnlyList<ReplCompletionItem>> CompletionsAsync(string text, int caret, TextRange range, CancellationToken token)
    {
        if (ResourcePathCompletion.Span(text, caret) is not null)
            return ResourcePathCompletion.Items(text, caret, session.DetachedInput || session.Resources.Length > 0
                ? session.Resources : await session.ResourcePathsAsync(token), session.SceneResources);
        if (text.TrimStart().StartsWith(':') && !CommandArgument(text, caret))
            return ReplModes.Controls.Concat(session.Mode.HasShell() ? new[] { ":ls", ":cd", ":pwd", ":rm", ":cp", ":mv" } : [])
                .Select(c => new ReplCompletionItem(c)).ToArray();
        if (session.Mode == ReplMode.Agent) return [];
        var prepared = await PrepareAsync(text, token);
        var path = prepared.Input.CompletionAt(caret);
        if (path is not null || AwaitingNodeArgument(text, caret))
        {
            var bracket = path is not null && text.AsSpan(path.Span.Start).StartsWith("$[");
            // The menu keeps this list while it stays open. Include descendants and rank
            // them against the current input, so typing '/' immediately reveals children.
            var absolute = path?.Path.StartsWith('/') == true;
            var nodes = prepared.Nodes.Values.DistinctBy(n => n.Path)
                .Select(n => (absolute || n.Path == ".") && n.AbsolutePath is { } full ? n with { Path = full } : n)
                .OrderBy(n => n.Path, StringComparer.Ordinal).ToArray();
            var parents = nodes.Select(n => n.AbsolutePath ?? n.Path)
                .Where(p => p.Contains('/')).Select(p => p[..p.LastIndexOf('/')]).ToHashSet(StringComparer.Ordinal);
            return nodes.Select(n => (ReplCompletionItem)new NodeCompletionItem(n, bracket, parents.Contains(n.AbsolutePath ?? n.Path), path, prepared.Families)).ToArray();
        }
        var service = CompletionService.GetService(prepared.Document)!;
        var list = await service.GetCompletionsAsync(prepared.Document, prepared.Input.ToGenerated(caret), cancellationToken: token);
        var items = list?.ItemsList.Select(item => (ReplCompletionItem)new RoslynItem(this, service, prepared, item)).ToList() ?? [];
        if (!session.Mode.HasShell()) return items;
        foreach (var command in new[] { "cp", "mv" })
        {
            if (command.StartsWith(text.Trim(), StringComparison.Ordinal))
                items.Insert(0, Command(command, command + " $Source $Parent: " + (command == "cp" ? "duplicate a subtree" : "move a node, keeping its global transform") +
                    " into an existing parent. mv $Node \"NewName\" renames it. C# node expressions work in both arguments."));
        }
        if (text.Trim() is "" or "l" or "ls")
            items.Insert(0, Command("ls", "List the selected node's tree, or use ls $Child to list a subtree."));
        if (text.Trim() is "" or "c" or "cd")
            items.Insert(0, Command("cd", "Select a node: cd $Child, cd nodeExpression, cd .., or cd /. :cd is the explicit command form."));
        if (text.Trim() is "" or "r" or "rm")
            items.Insert(0, Command("rm", "Remove a scene node and its descendants: rm $Child or rm nodeExpression. A target is required."));
        if (text.Trim() is "" or "p" or "pw" or "pwd")
            items.Insert(0, Command("pwd", "Show the selected node's absolute path."));
        return items;
    }

    private static ReplCompletionItem Command(string name, string description)
        => new(name, description: _ => Task.FromResult<StyledText>(description));

    public override async Task<CompletionEdit> CommitAsync(ReplCompletionItem item, string text, int caret, CommitKey key, CancellationToken token)
        => item is RoslynItem roslyn ? await roslyn.CommitAsync(text, caret, call: key != CommitKey.Character, token)
            : await base.CommitAsync(item, text, caret, key, token);

    public override bool AcceptsCommit(ReplCompletionItem item, string text, int caret, char key)
    {
        // Inside a quoted node name, '.', '(' and '/' are path characters.
        var path = NodePathInput.Find(text).FirstOrDefault(p => caret > p.Span.Start && caret <= p.Span.End);
        // Slashes in an absolute path are literal separators. Accepting an
        // ancestor's suggested descendant here would change the path being typed.
        if (key == '/' && path is not null && (path.Path.Length == 0 || path.Path.StartsWith('/'))) return false;
        var insideQuotedPath = path is not null && text.AsSpan(path.Span.Start).StartsWith("$[") &&
            (!path.Complete || caret < path.Span.End);
        return !insideQuotedPath && ResourcePathCompletion.Span(text, caret) is null;
    }

    public override bool ShouldOpenCompletion(string text, int caret, char typed)
    {
        // Wait for a path character so $/ opens absolute suggestions immediately.
        // Tab/Ctrl+Space still opens the full list after a bare $.
        if (typed == '$') return false;
        if ((typed is '?' or '/' || char.IsLetterOrDigit(typed)) && NodePathInput.Find(text).Any(p => caret > p.Span.Start && caret <= p.Span.End))
            return true;
        return base.ShouldOpenCompletion(text, caret, typed);
    }

    public override async Task<TabAction> TabAsync(string text, int caret, CancellationToken token)
    {
        if (session.Mode == ReplMode.Agent && !text.TrimStart().StartsWith(':')) return TabAction.Ignore;
        // A complete command name takes its operand next: Tab adds the space and lists nodes.
        if (caret == text.Length && session.Mode.HasShell() && text.TrimStart() is var command &&
            (command.StartsWith(':') ? command[1..] : command) is "cd" or "ls" or "rm" or "cp" or "mv")
            return new TabAction(new CompletionEdit(new TextRange(caret, 0), " "), true);
        if (ResourcePathCompletion.Span(text, caret) is not null) return TabAction.Open;
        var prepared = await PrepareAsync(text, token);
        // Complete expressions advance with '.', '(' or '/', rather than opening suggestions.
        if (await ReplCompletion.ContinuationAsync(prepared, caret, token) is not { } character ||
            caret < text.Length && text[caret] == character ||
            character == '(' && text.AsSpan(caret).TrimStart().StartsWith("(")) return TabAction.Open;
        if (character != '(') return new TabAction(new CompletionEdit(new TextRange(caret, 0), character.ToString()), false);
        var call = await ReplCompletion.CallEditAsync(prepared, new TextSpan(caret, 0), "", token);
        return new TabAction(new CompletionEdit(new TextRange(caret, 0), "()", call.NewCaret ?? caret + 1), false);
    }

    public override async Task<bool> SkipsCloseParenAsync(string text, int caret, CancellationToken token)
    {
        if (session.Mode == ReplMode.Agent || caret >= text.Length || text[caret] != ')') return false;
        var prepared = await PrepareAsync(text, token);
        var root = (await prepared.Document.GetSyntaxRootAsync(token))!;
        var position = prepared.Input.ToGenerated(caret);
        var closing = root.FindToken(position);
        return closing.SpanStart == position && closing.RawKind == (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.CloseParenToken;
    }

    public override async Task<(IReadOnlyList<SignatureItem> Items, int Argument)> SignaturesAsync(string text, int caret, CancellationToken token)
        => session.Mode == ReplMode.Agent ? ([], 0) : await ReplCompletion.OverloadsAsync(await PrepareAsync(text, token), caret, token);

    public override async Task<IReadOnlyList<StyledSpan>> HighlightAsync(string text, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        if (session.Mode == ReplMode.Agent)
            return text.TrimStart().StartsWith(':') ? [new StyledSpan(0, text.Length, TermColor.BrightMagenta)] : [];
        var prepared = await PrepareAsync(text, token);
        var paths = prepared.Input.References.SelectMany(p => NodeColors.Path(text.Substring(p.Span.Start, p.Span.Length), prepared.Families)
            .Spans.Select(s => s.Offset(p.Span.Start))).ToArray();
        session.Mode.TryNavigation(text, out var navigation);
        if (navigation is { Expression: null })
            return [new StyledSpan(navigation.Start, navigation.Length, TermColor.BrightMagenta), .. paths];
        if (session.Mode.TryListing(text, out var listing))
            return [new StyledSpan(listing.Start, listing.Length, TermColor.BrightMagenta), .. paths];
        if (navigation is null && text.TrimStart().StartsWith(':')) return [new StyledSpan(0, text.Length, TermColor.BrightMagenta)];
        var spans = await Classifier.GetClassifiedSpansAsync(prepared.Document, new TextSpan(0, prepared.Input.Code.Length), token);
        var result = new List<StyledSpan>();
        if (navigation is not null) result.Add(new StyledSpan(navigation.Start, navigation.Length, TermColor.BrightMagenta));
        foreach (var classified in spans)
        {
            if (prepared.Input.IsGenerated(classified.TextSpan) || Color(classified.ClassificationType) is not { } color) continue;
            var span = prepared.Input.ToOriginal(classified.TextSpan);
            if (span.Length > 0) result.Add(new StyledSpan(span.Start, span.Length, color));
        }
        result.AddRange(paths);
        return result.OrderBy(s => s.Start).ToArray();
    }

    private static TermColor? Color(string classification) => classification switch
    {
        ClassificationTypeNames.Keyword or ClassificationTypeNames.ControlKeyword => TermColor.BrightBlue,
        ClassificationTypeNames.StringLiteral or ClassificationTypeNames.VerbatimStringLiteral => TermColor.Yellow,
        ClassificationTypeNames.NumericLiteral => TermColor.BrightMagenta,
        ClassificationTypeNames.Comment => TermColor.Green,
        ClassificationTypeNames.ClassName or ClassificationTypeNames.StructName or ClassificationTypeNames.InterfaceName
            or ClassificationTypeNames.EnumName or ClassificationTypeNames.DelegateName => TermColor.BrightCyan,
        ClassificationTypeNames.MethodName or ClassificationTypeNames.ExtensionMethodName => TermColor.BrightYellow,
        ClassificationTypeNames.PropertyName or ClassificationTypeNames.FieldName or ClassificationTypeNames.LocalName
            or ClassificationTypeNames.ParameterName => TermColor.Cyan,
        _ => null,
    };

    private bool CommandArgument(string text, int caret)
        => session.Mode.TryNavigation(text, out var command) && command.InArgument(caret) ||
            session.Mode.TryListing(text, out var listing) && listing.InArgument(caret);

    private bool AwaitingNodeArgument(string text, int caret)
        => session.Mode.TryListing(text, out var listing) && listing.AwaitingTarget && caret >= listing.ArgumentStart ||
            session.Mode.TryNavigation(text, out var navigation) && navigation.AwaitingTargetAt(caret);

    // Roslyn items are matched again against the current text when accepted, since typing may have refined them.
    private sealed class RoslynItem(ReplLanguage language, CompletionService service, PreparedInput prepared, CompletionItem item)
        : ReplCompletionItem(item.DisplayText, item.DisplayTextPrefix + item.DisplayText + item.DisplayTextSuffix, item.FilterText,
            async token => (await service.GetDescriptionAsync(prepared.Document, item, cancellationToken: token))?.Text ?? "")
    {
        public async Task<CompletionEdit> CommitAsync(string text, int caret, bool call, CancellationToken token)
        {
            var current = await language.PrepareAsync(text, token);
            var position = current.Input.ToGenerated(caret);
            var currentSpan = service.GetDefaultCompletionListSpan(SourceText.From(current.Input.Code), position);
            var currentList = await service.GetCompletionsAsync(current.Document, position, cancellationToken: token);
            var currentItem = currentList?.ItemsList.FirstOrDefault(candidate =>
                candidate.DisplayText == item.DisplayText && candidate.DisplayTextPrefix == item.DisplayTextPrefix &&
                candidate.DisplayTextSuffix == item.DisplayTextSuffix && candidate.Tags.SequenceEqual(item.Tags));
            if (currentItem is null)
            {
                var span = current.Input.ToOriginal(currentSpan);
                return new CompletionEdit(new TextRange(span.Start, span.Length), item.DisplayText);
            }
            var change = await service.GetChangeAsync(current.Document, currentItem, cancellationToken: token);
            var original = current.Input.ToOriginal(change.TextChange.Span);
            if (call && (currentItem.Tags.Contains("Method") || currentItem.Tags.Contains("ExtensionMethod")))
                return await ReplCompletion.CallEditAsync(current, original, change.TextChange.NewText ?? item.DisplayText, token);
            return new CompletionEdit(new TextRange(original.Start, original.Length), change.TextChange.NewText ?? "",
                change.NewPosition is { } p ? current.Input.ToOriginal(p) : null);
        }
    }

    private sealed class NodeCompletionItem(NodePathTarget target, bool bracket, bool children, NodePathReference? initialPath, IReadOnlyDictionary<string, NodeFamily> families)
        : ReplCompletionItem(Alias(target.Path + Suffix(target, children, initialPath), bracket),
            display: NodeColors.Path(Alias(target.Path + Suffix(target, children, initialPath), bracket), families),
            description: _ => Task.FromResult(new StyledText(target.DisplayType + "\n") + NodeColors.Path(target.AbsolutePath ?? "/root/" + target.Path, families)),
            commitCharacters: "/",
            edit: (text, caret, _) => Task.FromResult(Edit(target, bracket, children, text, caret)))
    {
        private static CompletionEdit Edit(NodePathTarget target, bool bracket, bool children, string text, int caret)
        {
            var full = NodePathInput.Find(text).FirstOrDefault(p => caret > p.Span.Start && caret <= p.Span.End);
            var path = NodePathInput.CompletionAt(text, caret);
            var name = path?.Path.StartsWith('/') == true ? target.AbsolutePath ?? target.Path : target.Path;
            var quoted = bracket || path is not null && text.AsSpan(path.Span.Start).StartsWith("$[");
            var span = path is null ? new TextRange(caret, 0) : new TextRange(path.Span.Start, path.Span.Length);
            var suffix = Suffix(target, children, path);
            var replacement = Alias(name + suffix, quoted);
            quoted = replacement.StartsWith("$[", StringComparison.Ordinal);
            if (quoted && full is not null && caret < full.Span.End && !text.AsSpan(full.Span.Start).StartsWith("$["))
            {
                // A match with spaces introduces quotes. Wrap the preserved
                // suffix too, and leave the caret between the completion and it.
                var newCaret = span.Start + replacement.Length - 2;
                replacement = Alias(name + suffix + text[caret..full.Span.End], bracket: true);
                return new CompletionEdit(new TextRange(full.Span.Start, full.Span.Length), replacement, newCaret);
            }
            if (quoted && full is not null && caret < full.Span.End && text.AsSpan(full.Span.Start).StartsWith("$["))
            {
                // Keep the literal's existing closing quote/bracket after the
                // caret instead of introducing another pair before its suffix.
                var literalStart = full.Span.Start + 2;
                while (literalStart < text.Length && char.IsWhiteSpace(text[literalStart])) literalStart++;
                var literal = SyntaxFactory.ParseToken(text[literalStart..]);
                var closing = literalStart + literal.Span.End - 1;
                replacement = replacement[..^(caret <= closing ? 2 : 1)];
            }
            return new CompletionEdit(span, replacement);
        }

        public override int Priority(string text, int caret, TextRange range)
        {
            var typed = text[range.Start..caret];
            var search = typed.StartsWith('?');
            var prefix = search ? typed[1..] : typed.StartsWith("$[", StringComparison.Ordinal)
                ? SyntaxFactory.ParseToken(typed[2..].TrimStart()).ValueText
                : typed.TrimStart('$');
            var match = Match(target, prefix, search);
            return match == 0 ? int.MinValue : match * 100_000 - target.Path.Count(c => c == '/');
        }

        // Prefer every matching path (including case-insensitive prefixes) before
        // node-name matches anywhere in the tree. Shallower paths win ties.
        private static int Match(NodePathTarget target, string prefix, bool search)
        {
            var candidate = prefix.StartsWith('/') ? target.AbsolutePath ?? target.Path : target.Path;
            if (!search && (!candidate.StartsWith('/') || prefix.StartsWith('/')))
            {
                if (candidate.StartsWith(prefix, StringComparison.Ordinal)) return 4;
                if (candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return 3;
            }
            // An empty $ lists the current scope, rather than the entire tree.
            if (prefix.Length == 0 && !search) return 0;
            var name = target.Path[(target.Path.LastIndexOf('/') + 1)..];
            return name.StartsWith(prefix, StringComparison.Ordinal) ? 2 :
                name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        }

        private static string Suffix(NodePathTarget target, bool children, NodePathReference? path)
            => children && (path?.Search == true || Match(target, path?.Path ?? "", search: false) is 1 or 2) ? "/" : "";
    }

    private static string Alias(string path, bool bracket) => !bracket && path.All(c => SyntaxFacts.IsIdentifierPartCharacter(c) || c == '/')
        ? "$" + path : "$[" + SymbolDisplay.FormatLiteral(path, quote: true) + "]";
}

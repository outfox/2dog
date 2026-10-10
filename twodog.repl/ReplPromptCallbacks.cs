using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Text;
using PrettyPrompt;
using PrettyPrompt.Completion;
using PrettyPrompt.Consoles;
using PrettyPrompt.Highlighting;
using CompletionItem = PrettyPrompt.Completion.CompletionItem;
using PromptSpan = PrettyPrompt.Documents.TextSpan;

namespace twodog.Repl;

internal sealed class ReplPromptCallbacks(ReplSession session, Func<string?>? takePaste = null) : PromptCallbacks
{
    private bool completionWindowOpen;
    private (string Text, int Caret)? bareCommand;
    private bool enterCompletion;
    private bool IsBareCommand(string text) => ReplModes.Controls.Contains(text) ||
        session.Mode.HasShell() && (ReplModes.ShellCommands.Contains(text) ||
            text.StartsWith(':') && ReplModes.ShellCommands.Contains(text[1..]));
    private bool pairCall;
    private int pairedCaret;
    private bool callOnCommit = true;
    private Task<PreparedInput>? preparedTask;
    private string? preparedText;
    private int preparedVersion;
    private CompletionCycle? cycle;
    private bool refineCycle;
    private (string Text, int Caret)? pendingEdit;
    private static KeyPress IgnoreKey() => new(new ConsoleKeyInfo('\0', ConsoleKey.F24, false, false, false));

    private Task<PreparedInput> PrepareInputAsync(string text, CancellationToken cancellationToken)
    {
        if (preparedTask is null || preparedTask.IsCanceled || preparedTask.IsFaulted || preparedText != text || preparedVersion != session.Version)
        {
            preparedText = text;
            preparedVersion = session.Version;
            preparedTask = session.PrepareAsync(text, cancellationToken);
        }
        return preparedTask;
    }

    internal async Task WarmUpAsync(CancellationToken cancellationToken)
    {
        if (session.Mode == ReplMode.Agent) return;
        // Exercise the same metadata, completion, docs and highlighting paths used by
        // real keystrokes before displaying an input-ready prompt. No C# is executed.
        foreach (var text in new[] { "l", "root." })
        {
            var span = await GetSpanToReplaceByCompletionAsync(text, text.Length, cancellationToken);
            var items = await GetCompletionItemsAsync(text, text.Length, span, cancellationToken);
            if (items.FirstOrDefault(i => i.DisplayText != "ls") is { } item)
                await item.GetExtendedDescriptionAsync(cancellationToken);
            await HighlightCallbackAsync(text, cancellationToken);
        }
    }

    protected override Task CompletionWindowOpenedAsync(string text, int caret, CancellationToken cancellationToken)
    {
        completionWindowOpen = true;
        return Task.CompletedTask;
    }

    protected override Task CompletionWindowClosedAsync(string text, int caret, CancellationToken cancellationToken)
    {
        completionWindowOpen = false;
        return Task.CompletedTask;
    }

    protected override async Task<KeyPress> TransformKeyPressAsync(string text, int caret, KeyPress keyPress, CancellationToken cancellationToken)
    {
        preparedTask = null;
        pendingEdit = null;
        // Submit through a callback, leaving the buffer intact in both input
        // modes. Trailing spaces stay meaningful for argument completion.
        enterCompletion = false;
        bareCommand = keyPress.ConsoleKeyInfo is { Key: ConsoleKey.Enter, Modifiers: 0 } && IsBareCommand(text) ? (text, caret) : null;
        if (bareCommand is not null)
            keyPress = new KeyPress(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, true));
        // Reuse the editor's word deletion, selection and undo behavior. Unlike
        // plain Backspace, this ends completion cycling instead of reverting it.
        if (keyPress.ConsoleKeyInfo is { Key: ConsoleKey.Backspace, Modifiers: ConsoleModifiers.Shift })
            keyPress = new KeyPress(new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, true));
        if (cycle is { } active && active.Matches(text, caret))
        {
            if (keyPress.ConsoleKeyInfo is { Key: ConsoleKey.Tab, Modifiers: 0 or ConsoleModifiers.Shift } tab)
            {
                var direction = tab.Modifiers == ConsoleModifiers.Shift ? -1 : 1;
                if (!active.Applied)
                {
                    var span = await GetSpanToReplaceByCompletionAsync(text, caret, cancellationToken);
                    var matches = active.Items.Select(i => (Item: i, Priority: i.GetCompletionItemPriority(text, caret, span)))
                        .Where(i => i.Priority >= 0).OrderByDescending(i => i.Priority).Select(i => i.Item).ToArray();
                    if (matches.Length == 0) return IgnoreKey();
                    cycle = active = new CompletionCycle(text, caret, matches, direction > 0 ? -1 : 0);
                }
                callOnCommit = true;
                var next = active.Next(direction);
                pendingEdit = active.Apply(await EditAsync(next, active.Original, active.OriginalCaret, cancellationToken));
                return IgnoreKey();
            }
            if (active.Applied && keyPress.ConsoleKeyInfo is { Key: ConsoleKey.Backspace, Modifiers: 0 })
            {
                pendingEdit = (active.Original, active.OriginalCaret);
                cycle = null;
                return IgnoreKey();
            }
        }
        // More letters refine this word's prefix. A word break accepts the current
        // choice; movement, cancellation and submission end the transaction.
        refineCycle = cycle is { } current && current.Matches(text, caret) && caret > 0 &&
            Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsIdentifierPartCharacter(text[caret - 1]) &&
            Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsIdentifierPartCharacter(keyPress.ConsoleKeyInfo.KeyChar) &&
            (keyPress.ConsoleKeyInfo.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0;
        if (!refineCycle) cycle = null;
        if (keyPress.ConsoleKeyInfo is { Key: ConsoleKey.Insert, Modifiers: ConsoleModifiers.Shift } && takePaste?.Invoke() is { } paste)
            return new KeyPress(keyPress.ConsoleKeyInfo, paste);
        // Submission/cancellation can end ReadLine without a window-closed notification.
        // The same callbacks serve the next prompt, whose menu starts closed.
        if (keyPress.ConsoleKeyInfo is { Key: ConsoleKey.Enter } or { Key: ConsoleKey.C, Modifiers: ConsoleModifiers.Control })
            completionWindowOpen = false;
        // With the menu closed, Tab advances complete expressions or opens suggestions.
        // Trigger and commit must stay separate: PrettyPrompt consumes shared bindings as triggers.
        pairCall = false;
        if (session.Mode != ReplMode.Agent && keyPress.ConsoleKeyInfo.KeyChar == ')' && caret < text.Length && text[caret] == ')')
        {
            var prepared = await PrepareInputAsync(text, cancellationToken);
            var root = (await prepared.Document.GetSyntaxRootAsync(cancellationToken))!;
            var position = prepared.Input.ToGenerated(caret);
            var closing = root.FindToken(position);
            if (closing.SpanStart == position && closing.RawKind == (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.CloseParenToken)
                return new KeyPress(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, false, false, false));
        }
        if (!completionWindowOpen && keyPress.ConsoleKeyInfo is { Key: ConsoleKey.Tab, Modifiers: 0 })
        {
            if (session.Mode == ReplMode.Agent && !text.TrimStart().StartsWith(':')) return IgnoreKey();
            var prepared = await PrepareInputAsync(text, cancellationToken);
            var continuation = await ReplCompletion.ContinuationAsync(prepared, caret, cancellationToken);
            if (continuation is { } character && (caret == text.Length || text[caret] != character) &&
                (character != '(' || !text.AsSpan(caret).TrimStart().StartsWith("(")))
            {
                pairCall = character == '(';
                if (pairCall)
                {
                    var call = await ReplCompletion.CallEditAsync(prepared, new TextSpan(caret, 0), "", cancellationToken);
                    pairedCaret = call.NewCaret ?? caret + 1;
                }
                return new KeyPress(new ConsoleKeyInfo(character, character switch
                {
                    '(' => ConsoleKey.D9, '/' => ConsoleKey.Oem2, _ => ConsoleKey.OemPeriod,
                }, false, false, false));
            }
            return new KeyPress(new ConsoleKeyInfo(' ', ConsoleKey.Spacebar, false, false, true));
        }
        return keyPress;
    }

    protected override Task<bool> ConfirmCompletionCommit(string text, int caret, KeyPress keyPress, CancellationToken cancellationToken)
    {
        preparedTask = null;
        cycle = null;
        // PrettyPrompt skips TransformKeyPressAsync when a selection would be
        // committed, so exact command submission must also intercept this path.
        enterCompletion = keyPress.ConsoleKeyInfo is { Key: ConsoleKey.Enter, Modifiers: 0 };
        bareCommand = enterCompletion && IsBareCommand(text) ? (text, caret) : null;
        if (bareCommand is not null)
        {
            completionWindowOpen = false;
            return Task.FromResult(false);
        }
        callOnCommit = keyPress.ConsoleKeyInfo is { Key: ConsoleKey.Tab, Modifiers: 0 or ConsoleModifiers.Shift }
            or { Key: ConsoleKey.Enter, Modifiers: 0 };
        // Inside a quoted node name, '.', '(' and '/' are path characters.
        var path = NodePathInput.Find(text).FirstOrDefault(p => caret > p.Span.Start && caret <= p.Span.End);
        // Slashes in an absolute path are literal separators. Accepting an
        // ancestor's suggested descendant here would change the path being typed.
        if (keyPress.ConsoleKeyInfo.KeyChar == '/' && path is not null &&
            (path.Path.Length == 0 || path.Path.StartsWith('/'))) return Task.FromResult(false);
        var insideQuotedPath = path is not null && text.AsSpan(path.Span.Start).StartsWith("$[") &&
            (!path.Complete || caret < path.Span.End);
        return Task.FromResult(callOnCommit || !insideQuotedPath);
    }

    protected override Task<(string Text, int Caret)> FormatInput(string text, int caret, KeyPress keyPress, CancellationToken cancellationToken)
    {
        // A rejected completion can reach the editor's newline binding. Restore
        // the exact command before rendering, history and the submit callback.
        if (bareCommand is { } command)
        {
            pairCall = false;
            refineCycle = false;
            pendingEdit = null;
            return Task.FromResult(command);
        }
        if (refineCycle)
        {
            refineCycle = false;
            cycle?.Refine(text, caret);
        }
        if (pendingEdit is { } edit)
        {
            pendingEdit = null;
            return Task.FromResult(edit);
        }
        var closeCall = pairCall;
        pairCall = false;
        return Task.FromResult(closeCall && caret > 0 && text[caret - 1] == '('
            ? (text.Insert(caret, ")"), pairedCaret) : (text, caret));
    }

    protected override async Task<(IReadOnlyList<OverloadItem>, int ArgumentIndex)> GetOverloadsAsync(string text, int caret, CancellationToken cancellationToken)
        => session.Mode == ReplMode.Agent ? ([], 0)
            : await ReplCompletion.OverloadsAsync(await PrepareInputAsync(text, cancellationToken), caret, cancellationToken);

    protected override IEnumerable<(KeyPressPattern Pattern, KeyPressCallbackAsync Callback)> GetKeyPressCallbacks()
    {
        yield return (new KeyPressPattern(ConsoleKey.Enter),
            (text, _, _) => Task.FromResult<KeyPressCallbackResult?>(
                bareCommand is { } command ? new KeyPressCallbackResult(command.Text, null) : null));
        yield return (new KeyPressPattern(ConsoleModifiers.Control, ConsoleKey.D),
            (text, _, _) => Task.FromResult<KeyPressCallbackResult?>(
                text.Length == 0 ? new KeyPressCallbackResult(":exit", null) : null));
    }

    protected override Task<bool> ShouldOpenCompletionWindowAsync(string text, int caret, KeyPress keyPress, CancellationToken cancellationToken)
    {
        if (pairCall || refineCycle) return Task.FromResult(false);
        var key = keyPress.ConsoleKeyInfo.KeyChar;
        // Wait for a path character so $/ opens absolute suggestions immediately.
        // Tab/Ctrl+Space still opens the full list after a bare $.
        if (key == '$') return Task.FromResult(false);
        if ((key is '?' or '/' || char.IsLetterOrDigit(key)) && NodePathInput.Find(text).Any(p => caret > p.Span.Start && caret <= p.Span.End))
            return Task.FromResult(true);
        return base.ShouldOpenCompletionWindowAsync(text, caret, keyPress, cancellationToken);
    }

    protected override async Task<IReadOnlyCollection<FormatSpan>> HighlightCallbackAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        if (session.Mode == ReplMode.Agent)
            return text.TrimStart().StartsWith(':') ? [new FormatSpan(0, text.Length, AnsiColor.BrightMagenta)] : [];
        var prepared = await PrepareInputAsync(text, cancellationToken);
        var paths = prepared.Input.References.SelectMany(p => NodeColors.Path(text.Substring(p.Span.Start, p.Span.Length), prepared.Families)
            .FormatSpans.ToArray().Select(s => s.Offset(p.Span.Start))).ToArray();
        session.Mode.TryNavigation(text, out var navigation);
        if (navigation is { Expression: null })
            return new[] { new FormatSpan(navigation.Start, navigation.Length, AnsiColor.BrightMagenta) }
                .Concat(paths).ToArray();
        if (session.Mode.TryListing(text, out var listing))
            return new[] { new FormatSpan(listing.Start, listing.Length, AnsiColor.BrightMagenta) }
                .Concat(paths).ToArray();
        if (navigation is null && text.TrimStart().StartsWith(':')) return [new FormatSpan(0, text.Length, AnsiColor.BrightMagenta)];
        var spans = await Classifier.GetClassifiedSpansAsync(prepared.Document, new TextSpan(0, prepared.Input.Code.Length), cancellationToken);
        var result = new List<FormatSpan>();
        if (navigation is not null) result.Add(new FormatSpan(navigation.Start, navigation.Length, AnsiColor.BrightMagenta));
        foreach (var classified in spans)
        {
            if (prepared.Input.IsGenerated(classified.TextSpan) || Color(classified.ClassificationType) is not { } color) continue;
            var span = prepared.Input.ToOriginal(classified.TextSpan);
            if (span.Length > 0) result.Add(new FormatSpan(span.Start, span.Length, color));
        }
        result.AddRange(paths);
        return result.OrderBy(s => s.Start).ToArray();
    }

    private static AnsiColor? Color(string classification) => classification switch
    {
        ClassificationTypeNames.Keyword or ClassificationTypeNames.ControlKeyword => AnsiColor.BrightBlue,
        ClassificationTypeNames.StringLiteral or ClassificationTypeNames.VerbatimStringLiteral => AnsiColor.Yellow,
        ClassificationTypeNames.NumericLiteral => AnsiColor.BrightMagenta,
        ClassificationTypeNames.Comment => AnsiColor.Green,
        ClassificationTypeNames.ClassName or ClassificationTypeNames.StructName or ClassificationTypeNames.InterfaceName
            or ClassificationTypeNames.EnumName or ClassificationTypeNames.DelegateName => AnsiColor.BrightCyan,
        ClassificationTypeNames.MethodName or ClassificationTypeNames.ExtensionMethodName => AnsiColor.BrightYellow,
        ClassificationTypeNames.PropertyName or ClassificationTypeNames.FieldName or ClassificationTypeNames.LocalName
            or ClassificationTypeNames.ParameterName => AnsiColor.Cyan,
        _ => null,
    };

    protected override async Task<PromptSpan> GetSpanToReplaceByCompletionAsync(string text, int caret, CancellationToken cancellationToken)
    {
        if (AwaitingNodeArgument(text, caret)) return new PromptSpan(caret, 0);
        if (text.TrimStart().StartsWith(':') && !CommandArgument(text, caret))
            return new PromptSpan(text.IndexOf(':'), text.Length - text.IndexOf(':'));
        var prepared = await PrepareInputAsync(text, cancellationToken);
        if (prepared.Input.CompletionAt(caret) is { } path) return new PromptSpan(path.Span.Start, path.Span.Length);
        var span = CompletionService.GetService(prepared.Document)!.GetDefaultCompletionListSpan(
            SourceText.From(prepared.Input.Code), prepared.Input.ToGenerated(caret));
        var original = prepared.Input.ToOriginal(span);
        return new PromptSpan(original.Start, original.Length);
    }

    protected override async Task<IReadOnlyList<CompletionItem>> GetCompletionItemsAsync(string text, int caret, PromptSpan spanToBeReplaced, CancellationToken cancellationToken)
    {
        var items = await CreateCompletionItemsAsync(text, caret, spanToBeReplaced, cancellationToken);
        return items.Select(item => (CompletionItem)new CyclingItem(item,
            (currentText, currentCaret, token) => CommitAsync(item, items, currentText, currentCaret, token))).ToArray();
    }

    private async Task<CompletionEdit> EditAsync(CompletionItem item, string text, int caret, CancellationToken token)
        => item.HasComplexTextEdit ? await item.GetComplexTextEditAsync(text, caret, token)
            : new CompletionEdit(await GetSpanToReplaceByCompletionAsync(text, caret, token), item.ReplacementText);

    private async Task<CompletionEdit> CommitAsync(CompletionItem item, IReadOnlyList<CompletionItem> items, string text, int caret, CancellationToken token)
    {
        var remember = callOnCommit;
        var span = await GetSpanToReplaceByCompletionAsync(text, caret, token);
        var edit = await EditAsync(item, text, caret, token);
        if (remember)
        {
            var matches = items.Select(i => (Item: i, Priority: i.GetCompletionItemPriority(text, caret, span)))
                .Where(i => i.Priority >= 0 || ReferenceEquals(i.Item, item))
                .OrderByDescending(i => i.Priority).Select(i => i.Item).ToArray();
            cycle = new CompletionCycle(text, caret, matches, Array.IndexOf(matches, item));
            cycle.Apply(edit);
        }
        if (enterCompletion)
        {
            var accepted = text.Remove(edit.SpanToReplace.Start, edit.SpanToReplace.Length).Insert(edit.SpanToReplace.Start, edit.NewText);
            if (IsBareCommand(accepted))
            {
                bareCommand = (accepted, edit.NewCaret ?? edit.SpanToReplace.Start + edit.NewText.Length);
                cycle = null;
                completionWindowOpen = false;
            }
        }
        return edit;
    }

    private sealed class CyclingItem(CompletionItem inner, CompletionItem.GetComplexTextEditHandler edit) : CompletionItem(
        inner.ReplacementText, inner.DisplayTextFormatted, inner.FilterText, inner.GetExtendedDescriptionAsync, inner.CommitCharacterRules, edit)
    {
        public override int GetCompletionItemPriority(string text, int caret, PromptSpan span)
            => inner.GetCompletionItemPriority(text, caret, span);
    }

    private bool CommandArgument(string text, int caret)
        => session.Mode.TryNavigation(text, out var command) && command.InArgument(caret) ||
            session.Mode.TryListing(text, out var listing) && listing.InArgument(caret);

    private bool AwaitingNodeArgument(string text, int caret)
        => session.Mode.TryListing(text, out var listing) && listing.AwaitingTarget && caret >= listing.ArgumentStart ||
            session.Mode.TryNavigation(text, out var navigation) && navigation.AwaitingTargetAt(caret);

    private async Task<IReadOnlyList<CompletionItem>> CreateCompletionItemsAsync(string text, int caret, PromptSpan spanToBeReplaced, CancellationToken cancellationToken)
    {
        if (text.TrimStart().StartsWith(':') && !CommandArgument(text, caret))
            return ReplModes.Controls.Concat(session.Mode.HasShell() ? new[] { ":ls", ":cd", ":pwd", ":rm", ":cp", ":mv" } : [])
                .Select(c => new CompletionItem(c)).ToArray();
        if (session.Mode == ReplMode.Agent) return [];
        var prepared = await PrepareInputAsync(text, cancellationToken);
        var path = prepared.Input.CompletionAt(caret);
        if (path is not null || AwaitingNodeArgument(text, caret))
        {
            var bracket = path is not null && text.AsSpan(path.Span.Start).StartsWith("$[");
            // PrettyPrompt retains this list until the menu closes. Include descendants and
            // rank them against the current input so typing '/' immediately reveals children.
            var absolute = path?.Path.StartsWith('/') == true;
            var nodes = prepared.Nodes.Values.DistinctBy(n => n.Path)
                .Select(n => (absolute || n.Path == ".") && n.AbsolutePath is { } full ? n with { Path = full } : n)
                .OrderBy(n => n.Path, StringComparer.Ordinal).ToArray();
            var parents = nodes.Select(n => n.AbsolutePath ?? n.Path)
                .Where(p => p.Contains('/')).Select(p => p[..p.LastIndexOf('/')]).ToHashSet(StringComparer.Ordinal);
            return nodes.Select(n => (CompletionItem)new NodeCompletionItem(n, bracket, parents.Contains(n.AbsolutePath ?? n.Path), path, prepared.Families)).ToArray();
        }
        var service = CompletionService.GetService(prepared.Document)!;
        var list = await service.GetCompletionsAsync(prepared.Document, prepared.Input.ToGenerated(caret), cancellationToken: cancellationToken);
        var items = list?.ItemsList.Select(item => new CompletionItem(item.DisplayText,
            displayText: item.DisplayTextPrefix + item.DisplayText + item.DisplayTextSuffix,
            filterText: item.FilterText,
            getExtendedDescription: async token =>
            {
                var description = await service.GetDescriptionAsync(prepared.Document, item, cancellationToken: token);
                return new FormattedString(description?.Text ?? "");
            },
            getComplexTextEdit: async (currentText, currentCaret, token) =>
            {
                var openCall = callOnCommit;
                callOnCommit = true;
                var current = await PrepareInputAsync(currentText, token);
                var position = current.Input.ToGenerated(currentCaret);
                var currentSpan = service.GetDefaultCompletionListSpan(SourceText.From(current.Input.Code), position);
                var currentList = await service.GetCompletionsAsync(current.Document, position, cancellationToken: token);
                var currentItem = currentList?.ItemsList.FirstOrDefault(candidate =>
                    candidate.DisplayText == item.DisplayText && candidate.DisplayTextPrefix == item.DisplayTextPrefix &&
                    candidate.DisplayTextSuffix == item.DisplayTextSuffix && candidate.Tags.SequenceEqual(item.Tags));
                if (currentItem is null)
                {
                    var span = current.Input.ToOriginal(currentSpan);
                    return new CompletionEdit(new PromptSpan(span.Start, span.Length), item.DisplayText);
                }
                var change = await service.GetChangeAsync(current.Document, currentItem, cancellationToken: token);
                var original = current.Input.ToOriginal(change.TextChange.Span);
                if (openCall && (currentItem.Tags.Contains("Method") || currentItem.Tags.Contains("ExtensionMethod")))
                    return await ReplCompletion.CallEditAsync(current, original, change.TextChange.NewText ?? item.DisplayText, token);
                return new CompletionEdit(new PromptSpan(original.Start, original.Length), change.TextChange.NewText ?? "",
                    change.NewPosition is { } p ? current.Input.ToOriginal(p) : null);
            })).ToList() ?? [];
        if (!session.Mode.HasShell()) return items;
        foreach (var command in new[] { "cp", "mv" })
        {
            if (command.StartsWith(text.Trim(), StringComparison.Ordinal))
                items.Insert(0, new CompletionItem(command, getExtendedDescription: _ => Task.FromResult(
                    new FormattedString(command + " $Source $Parent: " + (command == "cp" ? "duplicate a subtree" : "move a node, keeping its global transform") +
                        " into an existing parent. mv $Node \"NewName\" renames it. C# node expressions work in both arguments."))));
        }
        if (text.Trim() is "" or "l" or "ls")
            items.Insert(0, new CompletionItem("ls", getExtendedDescription: _ => Task.FromResult(
                new FormattedString("List the selected node's tree, or use ls $Child to list a subtree."))));
        if (text.Trim() is "" or "c" or "cd")
            items.Insert(0, new CompletionItem("cd", getExtendedDescription: _ => Task.FromResult(
                new FormattedString("Select a node: cd $Child, cd nodeExpression, cd .., or cd /. :cd is the explicit command form."))));
        if (text.Trim() is "" or "r" or "rm")
            items.Insert(0, new CompletionItem("rm", getExtendedDescription: _ => Task.FromResult(
                new FormattedString("Remove a scene node and its descendants: rm $Child or rm nodeExpression. A target is required."))));
        if (text.Trim() is "" or "p" or "pw" or "pwd")
            items.Insert(0, new CompletionItem("pwd", getExtendedDescription: _ => Task.FromResult(new FormattedString("Show the selected node's absolute path."))));
        return items;
    }

    private sealed class NodeCompletionItem(NodePathTarget target, bool bracket, bool children, NodePathReference? initialPath, IReadOnlyDictionary<string, NodeFamily> families) : CompletionItem(
        Alias(target.Path + Suffix(target, children, initialPath), bracket),
        displayText: NodeColors.Path(Alias(target.Path + Suffix(target, children, initialPath), bracket), families),
        getExtendedDescription: _ => Task.FromResult(new FormattedString(target.DisplayType + "\n") + NodeColors.Path(target.AbsolutePath ?? "/root/" + target.Path, families)),
        commitCharacterRules: System.Collections.Immutable.ImmutableArray.Create(
            new PrettyPrompt.Consoles.CharacterSetModificationRule(PrettyPrompt.Consoles.CharacterSetModificationKind.Add, System.Collections.Immutable.ImmutableArray.Create('/'))),
        getComplexTextEdit: (text, caret, _) =>
        {
            var full = NodePathInput.Find(text).FirstOrDefault(p => caret > p.Span.Start && caret <= p.Span.End);
            var path = NodePathInput.CompletionAt(text, caret);
            var name = path?.Path.StartsWith('/') == true ? target.AbsolutePath ?? target.Path : target.Path;
            var quoted = bracket || path is not null && text.AsSpan(path.Span.Start).StartsWith("$[");
            var span = path is null ? new PromptSpan(caret, 0) : new PromptSpan(path.Span.Start, path.Span.Length);
            var suffix = Suffix(target, children, path);
            var replacement = Alias(name + suffix, quoted);
            quoted = replacement.StartsWith("$[", StringComparison.Ordinal);
            if (quoted && full is not null && caret < full.Span.End && !text.AsSpan(full.Span.Start).StartsWith("$["))
            {
                // A match with spaces introduces quotes. Wrap the preserved
                // suffix too, and leave the caret between the completion and it.
                var newCaret = span.Start + replacement.Length - 2;
                replacement = Alias(name + suffix + text[caret..full.Span.End], bracket: true);
                return Task.FromResult(new CompletionEdit(new PromptSpan(full.Span.Start, full.Span.Length), replacement, newCaret));
            }
            if (quoted && full is not null && caret < full.Span.End && text.AsSpan(full.Span.Start).StartsWith("$["))
            {
                // Keep the literal's existing closing quote/bracket after the
                // caret instead of introducing another pair before its suffix.
                var literalStart = full.Span.Start + 2;
                while (literalStart < text.Length && char.IsWhiteSpace(text[literalStart])) literalStart++;
                var literal = Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseToken(text[literalStart..]);
                var closing = literalStart + literal.Span.End - 1;
                replacement = replacement[..^(caret <= closing ? 2 : 1)];
            }
            return Task.FromResult(new CompletionEdit(span, replacement));
        })
    {
        public override int GetCompletionItemPriority(string text, int caret, PromptSpan spanToBeReplaced)
        {
            var typed = text[spanToBeReplaced.Start..caret];
            var search = typed.StartsWith('?');
            var prefix = search ? typed[1..] : typed.StartsWith("$[", StringComparison.Ordinal)
                ? Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseToken(typed[2..].TrimStart()).ValueText
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

    private static string Alias(string path, bool bracket) => !bracket && path.All(c => Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsIdentifierPartCharacter(c) || c == '/')
        ? "$" + path : "$[" + Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(path, quote: true) + "]";
}

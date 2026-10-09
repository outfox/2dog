using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Text;

namespace twodog.Repl;

internal sealed class ReplSession : IDisposable
{
    private static readonly CSharpParseOptions ParseOptions = new(kind: SourceCodeKind.Script);
    private static readonly string[] Imports =
        ["System", "System.Collections.Generic", "System.Linq", "System.Threading", "System.Threading.Tasks", "Godot"];
    private readonly AdhocWorkspace workspace = new();
    private readonly ScriptOptions options;
    private readonly ReplGlobals globals;
    private readonly EngineDispatcher dispatcher;
    private ScriptState<object>? state;
    private ProjectId? previous;
    private int submission;
    private Document? editingDocument;
    private int navigationVersion;
    internal int Version => submission + navigationVersion;
    public Task<NodePathStyle> ScopeStyleAsync(CancellationToken token)
        => dispatcher.InvokeAsync(() => Task.FromResult(NodeColors.CapturePath(globals.here)), token);
    public Task<string> ScopePathAsync(CancellationToken token)
        => dispatcher.InvokeAsync(() => Task.FromResult(globals.here.GetPath().ToString()), token);

    public ReplSession(ReplGlobals globals, IEnumerable<Assembly> assemblies, EngineDispatcher dispatcher)
    {
        this.globals = globals;
        this.dispatcher = dispatcher;
        // Reference the actual loaded game/binding assemblies, keeping their runtime type identity.
        var paths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Concat(assemblies.Append(typeof(ReplGlobals).Assembly).Append(typeof(Godot.Node).Assembly)
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location)).Select(a => a.Location))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var references = paths.Select(path => MetadataReference.CreateFromFile(path,
            documentation: XmlDocumentationProvider.CreateFromFile(Path.ChangeExtension(path, ".xml")))).ToArray();
        options = ScriptOptions.Default.WithReferences(references).WithImports(Imports)
            .WithMetadataResolver(ScriptMetadataResolver.Default.WithBaseDirectory(Environment.CurrentDirectory).WithSearchPaths(AppContext.BaseDirectory))
            .WithSourceResolver(new SourceFileResolver([], Environment.CurrentDirectory));
    }

    public static bool IsComplete(string text)
    {
        if (ListTreeCommand.TryParse(text, out _) || NavigationCommand.TryParse(text, out _)) return true;
        var input = new NodePathInput(text, new Dictionary<string, NodePathTarget>());
        return input.References.All(p => p.Complete && p.Path.Length > 0) &&
            SyntaxFactory.IsCompleteSubmission(CSharpSyntaxTree.ParseText(input.Code, ParseOptions));
    }

    public async Task<PreparedInput> PrepareAsync(string text, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, NodePathTarget> nodes = new Dictionary<string, NodePathTarget>();
        if (NodePathInput.Find(text).Count > 0 || ListTreeCommand.TryParse(text, out var listing) && listing.AwaitingTarget ||
            NavigationCommand.TryParse(text, out var navigation) && navigation.AwaitingTarget)
            nodes = await dispatcher.InvokeAsync(() => Task.FromResult(NodePathInput.Capture(globals.root, cancellationToken, globals.here)), cancellationToken).ConfigureAwait(false);
        var input = new NodePathInput(text, nodes);
        return new(Document(input.Code), input, nodes, nodes.ToDictionary(n => n.Key, n => n.Value.Family, StringComparer.Ordinal));
    }

    private Document Document(string text)
    {
        var source = SourceText.From(text);
        if (editingDocument is { } editor)
        {
            if (editor.TryGetText(out var current) && current.ContentEquals(source)) return editor;
            return editingDocument = editor.WithText(source);
        }
        var id = ProjectId.CreateNewId();
        var project = ProjectInfo.Create(id, VersionStamp.Create(), $"Submission{submission}",
            $"Submission{submission}", LanguageNames.CSharp, isSubmission: true,
            parseOptions: ParseOptions,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                usings: Imports, metadataReferenceResolver: options.MetadataResolver,
                sourceReferenceResolver: options.SourceResolver),
            metadataReferences: options.MetadataReferences,
            projectReferences: previous is null ? [] : [new ProjectReference(previous)],
            hostObjectType: typeof(ReplGlobals));
        return editingDocument = workspace.CurrentSolution.AddProject(project)
            .AddDocument(DocumentId.CreateNewId(id), "input.csx", source)
            .GetProject(id)!.Documents.Single();
    }

    public async Task<ReplResult> EvaluateAsync(string text, CancellationToken cancellationToken)
    {
        if (NavigationCommand.TryParse(text, out var navigation) &&
            (navigation.Explicit || navigation.Path is not (null or "..") || navigation.Error is not null ||
                state?.Variables.Any(variable => variable.Name == navigation.Name) != true))
        {
            if (navigation.Error is { } navigationError) return new(null, navigationError, false);
            return await dispatcher.InvokeAsync(() =>
            {
                var current = globals.here;
                if (navigation.Name == "pwd")
                {
                    var path = NodeColors.CapturePath(current);
                    return Task.FromResult(new ReplResult(path.Path, null, false, Styled: path.Display));
                }
                var node = navigation.Path is null or "/" ? globals.root :
                    navigation.Path == ".." && current == globals.root ? current : current.GetNodeOrNull<Godot.Node>(navigation.Path);
                if (node is null || !Godot.GodotObject.IsInstanceValid(node) || node.IsQueuedForDeletion() || !node.IsInsideTree())
                    return Task.FromResult(new ReplResult(null, $"Node '{navigation.Path}' was not found from {current.GetPath()}. Paths are case-sensitive; use ls or Tab to explore.", false));
                globals.SelectNode(node);
                navigationVersion++;
                var selected = NodeColors.CapturePath(node);
                return Task.FromResult(new ReplResult(selected.Path, null, false, Styled: selected.Display));
            }, cancellationToken).ConfigureAwait(false);
        }
        if (ListTreeCommand.TryParse(text, out var listing))
        {
            if (listing.Error is { } error) return new(null, error, false);
            return await dispatcher.InvokeAsync(() =>
            {
                var current = globals.here;
                var node = listing.Path is null ? current : current.GetNodeOrNull<Godot.Node>(listing.Path);
                if (node is null || !Godot.GodotObject.IsInstanceValid(node) || node.IsQueuedForDeletion())
                    return Task.FromResult(new ReplResult(null, $"Node '{listing.Path}' was not found from {current.GetPath()}. Paths are case-sensitive; use ls or Tab to explore.", false));
                var hierarchy = NodeColors.Tree(node);
                return Task.FromResult(new ReplResult(null, null, false, Tree: hierarchy.Text, Styled: hierarchy));
            }, cancellationToken).ConfigureAwait(false);
        }
        var prepared = await PrepareAsync(text, cancellationToken).ConfigureAwait(false);
        foreach (var path in prepared.Input.References)
        {
            if (path.Search)
                return new(null, "Node search is for completion. Press Tab to choose a full $ path before running.", false);
            if (!path.Complete || path.Path.Length == 0)
                return new(null, "Incomplete node path. Use $Control/Child or $[\"Control/My Node\"].", false);
            if (!prepared.Nodes.ContainsKey(path.LookupPath))
                return new(null, $"Node '{path.Path}' was not found in the current scope. Use ls or Tab to explore.", false);
        }
        var script = state is null
            ? CSharpScript.Create(prepared.Input.Code, options, typeof(ReplGlobals))
            : state.Script.ContinueWith<object>(prepared.Input.Code, options);
        var diagnostics = script.Compile(cancellationToken);
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            return new(null, string.Join(Environment.NewLine, diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => Diagnostic(d, prepared.Input))), false);

        var document = prepared.Document;
        var hasResult = HasResult(script);
        var result = await dispatcher.InvokeAsync(async () =>
        {
            globals.ct = cancellationToken;
            var next = state is null
                ? await script.RunAsync(globals, catchException: _ => true, cancellationToken: cancellationToken)
                : await script.RunFromAsync(state, catchException: _ => true, cancellationToken: cancellationToken);
            state = next;
            // Formatting may invoke user code (ToString), so it also belongs on the engine thread.
            if (next.Exception is OperationCanceledException) return new ReplResult(null, null, true, Cancelled: true);
            try
            {
                return new ReplResult(next.Exception is null && hasResult
                    ? Format(next.ReturnValue) : null, next.Exception?.ToString(), true);
            }
            catch (Exception error)
            {
                // A user's ToString/enumerator can fail after evaluation committed. Keep both
                // scripting and completion state in sync and allow the next submission.
                return new ReplResult(null, "Result formatting failed: " + error.Message, true);
            }
        }, cancellationToken).ConfigureAwait(false);

        if (result.Committed)
        {
            workspace.TryApplyChanges(document.Project.Solution);
            previous = document.Project.Id;
            submission++;
            editingDocument = null;
        }
        return result;
    }

    private static string Diagnostic(Diagnostic diagnostic, NodePathInput input)
    {
        if (diagnostic.Location.SourceTree is not { FilePath: "" }) return diagnostic.ToString();
        var location = SourceText.From(input.Original).Lines.GetLinePosition(input.ToOriginal(diagnostic.Location.SourceSpan.Start));
        return $"({location.Line + 1},{location.Character + 1}): {diagnostic.Id}: {diagnostic.GetMessage()}";
    }

    private static bool HasResult(Script<object> script)
    {
        var compilation = script.GetCompilation();
        // Loaded files also contribute trees. Inspect the submitted input, regardless of tree order.
        var syntax = compilation.SyntaxTrees.Single(tree => tree.FilePath == script.Options.FilePath);
        if (syntax.GetRoot() is not Microsoft.CodeAnalysis.CSharp.Syntax.CompilationUnitSyntax unit) return false;
        var expression = unit.Members.LastOrDefault() switch
        {
            Microsoft.CodeAnalysis.CSharp.Syntax.GlobalStatementSyntax
                { Statement: Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionStatementSyntax { SemicolonToken.IsMissing: true } e } => e.Expression,
            Microsoft.CodeAnalysis.CSharp.Syntax.GlobalStatementSyntax
                { Statement: Microsoft.CodeAnalysis.CSharp.Syntax.ReturnStatementSyntax r } => r.Expression,
            _ => null,
        };
        return expression is not null && compilation.GetSemanticModel(syntax).GetTypeInfo(expression).Type?.SpecialType != SpecialType.System_Void;
    }

    private static string Format(object? value) => value switch
    {
        null => "null",
        string text => System.Text.Json.JsonSerializer.Serialize(text),
        bool boolean => boolean ? "true" : "false",
        System.Collections.IEnumerable items => FormatItems(items),
        IFormattable number => number.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "null",
    };

    private static string FormatItems(System.Collections.IEnumerable items)
    {
        var values = new List<string>();
        foreach (var item in items)
        {
            if (values.Count == 20) { values.Add("..."); break; }
            // Keep nested/cyclic collections shallow and never traverse arbitrary object properties.
            values.Add(item is string text ? System.Text.Json.JsonSerializer.Serialize(text) : item?.ToString() ?? "null");
        }
        return "[" + string.Join(", ", values) + "]";
    }

    public void Dispose() => workspace.Dispose();
}

internal sealed record PreparedInput(Document Document, NodePathInput Input, IReadOnlyDictionary<string, NodePathTarget> Nodes, IReadOnlyDictionary<string, NodeFamily> Families);

internal sealed record ReplResult(string? Value, string? Error, bool Committed, bool Cancelled = false, string? Tree = null, PrettyPrompt.Highlighting.FormattedString? Styled = null);

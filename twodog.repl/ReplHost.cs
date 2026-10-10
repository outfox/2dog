using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using PrettyPrompt;
using PrettyPrompt.Consoles;
using PrettyPrompt.Highlighting;

namespace twodog.Repl;

/// <summary>A terminal C# prompt sharing the host's live Godot engine.</summary>
public static class ReplHost
{
    /// <summary>Starts, pumps and disposes the engine on the calling thread until the prompt or Godot exits.</summary>
    /// <param name="engine">An engine that has not yet been started.</param>
    /// <param name="references">Game assemblies loaded before startup, sharing their types with live scene instances.</param>
    /// <returns>Zero on normal exit, one if a redirected submission fails.</returns>
    public static int Run(Engine engine, params Assembly[] references)
        => Run(engine, token => new ReplConsole(token), references);

    // Drive the same lifetime and editor loop with a scripted terminal in tests.
    internal static int Run(Engine engine, Func<CancellationToken, IReplTerminal> createTerminal, params Assembly[] references)
    {
        if (!RuntimeFeature.IsDynamicCodeSupported)
            throw new PlatformNotSupportedException("The C# REPL requires a JIT runtime; NativeAOT is unsupported.");
        // Explicit references are evaluated by the caller before Start(), keeping the game's
        // types in the default load context, shared by Roslyn and Godot's scene instances.
        using (engine)
        {
            engine.Start();
            // Let the main scene enter the tree before the first submission.
            if (engine.Iteration()) return 0;
            using var dispatcher = new EngineDispatcher();
            var globals = new ReplGlobals(engine);
            using var scratch = globals.world;
            var scenePath = engine.Tree.CurrentScene?.SceneFilePath ?? "(no game scene loaded)";
            using var lifetime = new CancellationTokenSource();
            var console = createTerminal(lifetime.Token);
            var terminal = Task.Run(() => ReadEvalPrintAsync(globals, scenePath, references, dispatcher, console, lifetime.Token));
            try
            {
                while (!terminal.IsCompleted && !engine.Iteration())
                {
                    dispatcher.Pump();
                    Thread.Sleep(1);
                }
                return terminal.IsCompleted ? terminal.GetAwaiter().GetResult() : 0;
            }
            finally
            {
                lifetime.Cancel();
                dispatcher.Dispose();
                // Join terminal cleanup before restoring process-wide console modes or returning
                // to an embedding caller. ReadKey and queued submissions are cancellable.
                try { terminal.GetAwaiter().GetResult(); }
                finally { console.Restore(); }
            }
        }
    }

    private static async Task<int> ReadEvalPrintAsync(ReplGlobals globals, string scenePath, Assembly[] references, EngineDispatcher dispatcher,
        IReplTerminal console, CancellationToken lifetime)
    {
        var interactive = console.Interactive;
        var output = console.Output;
        if (interactive) output.Message("Preparing C# completion...");
        var assemblies = AppDomain.CurrentDomain.GetAssemblies().Concat(references).ToArray();
        var session = new ReplSession(globals, assemblies, dispatcher);
        Prompt? prompt = null;
        var exitCode = 0;
        var multiline = false;
        var history = console.HistoryFile;
        var promptMode = session.Mode;
        string? promptScope = "/root";
        IReadOnlyDictionary<string, NodeFamily>? promptFamilies = null;
        try
        {
            if (interactive)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(history)!);
                var initialScope = await session.ScopeStyleAsync(lifetime);
                promptScope = initialScope.Path;
                promptFamilies = initialScope.Families;
                prompt = await CreatePromptAsync(session, console, history, cancellationToken: lifetime, scopePath: promptScope, scopeFamilies: promptFamilies);
                output.Banner(scenePath);
            }
            while (!lifetime.IsCancellationRequested)
            {
                string? text;
                CancellationToken submissionToken = lifetime;
                if (prompt is not null)
                {
                    var scope = await session.ScopeStyleAsync(lifetime);
                    if (session.Mode != promptMode || scope.Path != promptScope || promptFamilies is null || !scope.Families.SequenceEqual(promptFamilies))
                    {
                        await prompt.DisposeAsync();
                        prompt = await CreatePromptAsync(session, console, history, multiline, warmUp: false, cancellationToken: lifetime, scopePath: scope.Path, scopeFamilies: scope.Families);
                        promptMode = session.Mode;
                        promptScope = scope.Path;
                        promptFamilies = scope.Families;
                    }
                    var response = await prompt.ReadLineAsync().ConfigureAwait(false);
                    if (!response.IsSuccess) continue;
                    text = response.Text;
                    submissionToken = response.CancellationToken;
                }
                else text = await ReadSubmissionAsync(console.Input, lifetime, session.Mode).ConfigureAwait(false);
                if (text is null) break;
                if (string.IsNullOrWhiteSpace(text)) continue;
                switch (text.Trim())
                {
                    case ":exit": return exitCode;
                    case "exit" when session.Mode.HasShell(): return exitCode;
                    case ":help":
                        output.Help(multiline, session.Mode);
                        continue;
                    case ":clear":
                        if (interactive) console.Editor.Clear();
                        continue;
                    case ":multiline":
                        multiline = !multiline;
                        if (prompt is not null)
                        {
                            await prompt.DisposeAsync();
                            prompt = await CreatePromptAsync(session, console, history,
                                multiline, warmUp: false, cancellationToken: lifetime, scopePath: promptScope, scopeFamilies: promptFamilies);
                        }
                        output.InputMode(multiline);
                        continue;
                    case ":reset":
                        if (interactive) output.Message("Preparing C# completion...");
                        var mode = session.Mode;
                        session.Dispose();
                        session = new ReplSession(globals, assemblies, dispatcher);
                        await session.EvaluateAsync(":" + mode.Name(), lifetime);
                        if (prompt is not null)
                        {
                            await prompt.DisposeAsync();
                            prompt = await CreatePromptAsync(session, console, history,
                                multiline, cancellationToken: lifetime, scopePath: promptScope, scopeFamilies: promptFamilies);
                        }
                        output.Message("C# session reset. The running scene is preserved.");
                        continue;
                }
                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime, submissionToken);
                try
                {
                    var result = await session.EvaluateAsync(text, cancellation.Token).ConfigureAwait(false);
                    if (result.Cancelled)
                    {
                        output.Message("Submission cancelled.");
                        continue;
                    }
                    if (result.Error is { } error)
                    {
                        output.Error(error);
                        if (!interactive) exitCode = 1;
                    }
                    else if (result.Tree is { } hierarchy) output.Tree(hierarchy, result.Styled);
                    else if (result.Value is { } value) output.Result(value, result.Styled);
                }
                catch (OperationCanceledException) when (!lifetime.IsCancellationRequested)
                {
                    output.Message("Submission cancelled.");
                }
            }
            return exitCode;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return exitCode; }
        finally
        {
            session.Dispose();
            if (prompt is not null) await prompt.DisposeAsync();
        }
    }

    private static async Task<Prompt> CreatePromptAsync(ReplSession session, IReplTerminal console, string history, bool multiline = false,
        bool warmUp = true, CancellationToken cancellationToken = default, string? scopePath = null, IReadOnlyDictionary<string, NodeFamily>? scopeFamilies = null)
    {
        var callbacks = new ReplPromptCallbacks(session, console.TakePaste);
        if (warmUp) await callbacks.WarmUpAsync(cancellationToken);
        return new Prompt(persistentHistoryFilepath: history, callbacks: callbacks, console: console.Editor,
            configuration: CreatePromptConfiguration(multiline, scopePath, console.Editor.BufferWidth, scopeFamilies, session.Mode));
    }

    internal static PromptConfiguration CreatePromptConfiguration(bool multiline = false, string? scopePath = null, int? terminalWidth = null, IReadOnlyDictionary<string, NodeFamily>? scopeFamilies = null, ReplMode mode = ReplMode.Auto) => new(
        prompt: CreatePromptText(scopePath, terminalWidth, scopeFamilies, color: !PromptConfiguration.HasUserOptedOutFromColor, mode),
        keyBindings: new KeyBindings(
            commitCompletion: new KeyPressPatterns(new KeyPressPattern(ConsoleKey.Tab), new KeyPressPattern(ConsoleModifiers.Shift, ConsoleKey.Tab),
                new KeyPressPattern(ConsoleKey.Enter), new KeyPressPattern('.'), new KeyPressPattern('(')),
            newLine: new KeyPressPatterns(multiline ? new KeyPressPattern(ConsoleKey.Enter) : new KeyPressPattern(ConsoleModifiers.Shift, ConsoleKey.Enter),
                new KeyPressPattern(ConsoleModifiers.Alt, ConsoleKey.Enter)),
            submitPrompt: new KeyPressPatterns(multiline ? new KeyPressPattern(ConsoleModifiers.Shift, ConsoleKey.Enter) : new KeyPressPattern(ConsoleKey.Enter),
                new KeyPressPattern(ConsoleModifiers.Control, ConsoleKey.Enter)),
            triggerCompletionList: new KeyPressPatterns(new KeyPressPattern(ConsoleModifiers.Control, ConsoleKey.Spacebar)),
            triggerOverloadList: new KeyPressPatterns(new KeyPressPattern('('), new KeyPressPattern(','), new KeyPressPattern(ConsoleKey.Tab),
                new KeyPressPattern(ConsoleModifiers.Control | ConsoleModifiers.Shift, ConsoleKey.Spacebar))));

    internal static FormattedString CreatePromptText(string? scopePath, int? terminalWidth, IReadOnlyDictionary<string, NodeFamily>? scopeFamilies, bool color, ReplMode mode = ReplMode.Auto)
    {
        // Leave room for code and completion even in narrow terminals. pwd always
        // shows the full path; the prompt keeps the end of a long scope visible.
        var fullPath = scopePath;
        var pathBudget = Math.Max(6, (terminalWidth ?? 160) / 4 - 10);
        if (scopePath is { Length: > 0 } && scopePath.Length > pathBudget)
        {
            var tail = scopePath[^(pathBudget - 3)..];
            var separator = tail.IndexOf('/');
            scopePath = "..." + (separator >= 0 ? tail[separator..] : tail);
        }
        var prefix = mode == ReplMode.Auto ? "godot" : "godot:" + mode.Name();
        var text = scopePath is null or "/root" ? prefix + "> " : $"{prefix} [{scopePath}]> ";
        if (!color) return new FormattedString(text);
        var family = scopeFamilies is not null && fullPath is not null && scopeFamilies.TryGetValue(fullPath, out var known) ? known : NodeFamily.Node;
        var spans = new List<FormatSpan> { new(0, 5, NodeColors.Color(family)) };
        if (mode != ReplMode.Auto) spans.Add(new(5, prefix.Length - 5, AnsiColor.BrightMagenta));
        if (scopePath is not null && scopePath != "/root")
        {
            var full = NodeColors.Path(fullPath!, scopeFamilies);
            var hidden = scopePath.StartsWith("...", StringComparison.Ordinal) ? 3 : 0;
            var visibleLength = scopePath.Length - hidden;
            if (hidden > 0) spans.Add(new(prefix.Length + 2, hidden, NodeColors.Grey));
            spans.AddRange(full.Substring(full.Length - visibleLength, visibleLength).FormatSpans.ToArray().Select(s => s.Offset(prefix.Length + 2 + hidden)));
        }
        return new FormattedString(text, spans);
    }

    internal static async Task<string?> ReadSubmissionAsync(TextReader input, CancellationToken cancellationToken, ReplMode mode = ReplMode.Auto)
    {
        var text = new StringBuilder();
        while (true)
        {
            var line = await input.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) return text.Length == 0 ? null : text.ToString();
            text.AppendLine(line);
            if (line.TrimStart().StartsWith(':') || ReplSession.IsComplete(text.ToString(), mode)) return text.ToString();
        }
    }
}

internal interface IReplTerminal
{
    IConsole Editor { get; }
    bool Interactive { get; }
    TextReader Input { get; }
    ReplOutput Output { get; }
    string HistoryFile { get; }
    string? TakePaste();
    void Restore();
}

internal sealed class ReplConsole : SystemConsole, IConsole, IReplTerminal
{
    public IConsole Editor => this;
    public bool Interactive => !Console.IsInputRedirected && !Console.IsOutputRedirected;
    public TextReader Input => Console.In;
    public ReplOutput Output { get; } = ReplOutput.ForConsole();
    public string HistoryFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "2dog", "repl-history");
    private readonly CancellationToken lifetime;
    private readonly ReplInputBuffer input;

    public ReplConsole(CancellationToken lifetime)
    {
        this.lifetime = lifetime;
        input = new ReplInputBuffer(() => Console.KeyAvailable, ReadConsoleKey, OperatingSystem.IsWindows());
    }

    bool IConsole.KeyAvailable
    {
        get
        {
            lifetime.ThrowIfCancellationRequested();
            return input.KeyAvailable;
        }
    }

    public string? TakePaste() => input.TakePaste();

    public override ConsoleKeyInfo ReadKey(bool intercept)
    {
        lifetime.ThrowIfCancellationRequested();
        return input.ReadKey();
    }

    // Supported terminals frame paste text so its newlines never act as submit keys.
    void IConsole.SetModifyOtherKeys(bool enabled)
    {
        SetModifyOtherKeys(enabled);
        // Windows Console.ReadKey strips paste markers in Win32 input mode. Keep
        // native modified-key handling there; Unix can distinguish literal paste.
        if (!OperatingSystem.IsWindows()) Write(enabled ? "\x1b[?2004h" : "\x1b[?2004l");
    }

    private readonly bool originalControlC = !Console.IsInputRedirected && Console.TreatControlCAsInput;

    private ConsoleKeyInfo ReadConsoleKey()
    {
        while (!Console.KeyAvailable)
        {
            lifetime.ThrowIfCancellationRequested();
            Thread.Sleep(15);
        }
        lifetime.ThrowIfCancellationRequested();
        return base.ReadKey(intercept: true);
    }

    public void Restore()
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected) return;
        CaptureControlC = originalControlC;
        SetNewlineAutoReturn(true);
        SetModifyOtherKeys(false);
        if (!OperatingSystem.IsWindows()) Write("\x1b[?2004l");
        ShowCursor();
    }
}

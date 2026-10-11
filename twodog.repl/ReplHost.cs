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

    /// <summary>Runs a host, or connects stdin/stdout to a host with --repl-connect NAME.
    /// --repl-listen NAME accepts concurrent local pipe sessions; remaining arguments go to Godot.</summary>
    public static int Run(string[] args, Func<string[], Engine> createEngine, params Assembly[] references)
    {
        var options = ReplPipeOptions.Parse(args);
        if (options.Connect is { } connect) return ReplPipeClient.RunAsync(connect).GetAwaiter().GetResult();
        return RunCore(createEngine(options.EngineArgs), token => new ReplConsole(token), options.Listen, references);
    }

    // Drive the same lifetime and editor loop with a scripted terminal in tests.
    internal static int Run(Engine engine, Func<CancellationToken, IReplTerminal> createTerminal, params Assembly[] references)
        => RunCore(engine, createTerminal, null, references);

    internal static int Run(Engine engine, Func<CancellationToken, IReplTerminal> createTerminal, string pipeName, params Assembly[] references)
        => RunCore(engine, createTerminal, pipeName, references);

    private static int RunCore(Engine engine, Func<CancellationToken, IReplTerminal> createTerminal, string? pipeName, Assembly[] references)
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
            using var scratch = globals.worlds;
            var scenePath = engine.Tree.CurrentScene?.SceneFilePath ?? "(no game scene loaded)";
            using var lifetime = new CancellationTokenSource();
            var console = createTerminal(lifetime.Token);
            using var server = pipeName is null ? null : new ReplPipeServer(pipeName, engine, references, dispatcher, lifetime.Token);
            if (pipeName is not null) console.Output.Message($"REPL listening on local pipe '{pipeName}'. Connect with --repl-connect {pipeName}.");
            var terminal = Task.Run(() => ReadEvalPrintAsync(globals, scenePath, references, dispatcher, console, lifetime.Token,
                requestHostExit: pipeName is null ? null : () => dispatcher.InvokeAsync(() => { engine.RequestQuit(); return Task.FromResult(true); })));
            try
            {
                // A listening host survives stdin EOF so it can also be launched by a process.
                while ((!terminal.IsCompleted || server is not null && terminal.IsCompletedSuccessfully) && !engine.Iteration())
                {
                    dispatcher.Pump();
                    Thread.Sleep(1);
                }
            }
            finally
            {
                try { lifetime.Cancel(); }
                finally
                {
                    try { dispatcher.Dispose(); }
                    finally
                    {
                        // Join terminal cleanup even if a user's cancellation callback throws.
                        try { terminal.GetAwaiter().GetResult(); }
                        finally { console.Restore(); }
                    }
                }
            }
            // Cancellation and cleanup may complete a terminal still waiting on stdin.
            // Preserve errors accumulated before an independent engine quit.
            return terminal.GetAwaiter().GetResult();
        }
    }

    internal static async Task<int> ReadEvalPrintAsync(ReplGlobals globals, string scenePath, Assembly[] references, EngineDispatcher dispatcher,
        IReplTerminal console, CancellationToken lifetime, bool cancelOnEof = false, Func<Task>? requestHostExit = null)
    {
        var interactive = console.Interactive;
        var output = console.Output;
        if (interactive) output.Message("Preparing C# completion...");
        var assemblies = AppDomain.CurrentDomain.GetAssemblies().Concat(references).ToArray();
        using var session = new ReplSession(globals, assemblies, dispatcher) { DetachedInput = true };
        using var work = new ReplWorkQueue(lifetime);
        Prompt? prompt = null;
        var exitCode = 0;
        var multiline = false;
        var history = console.HistoryFile;
        var promptMode = session.Mode;
        string? promptScope = "/root";
        IReadOnlyDictionary<string, NodeFamily>? promptFamilies = null;
        var readMode = session.Mode;
        void Cancel()
        {
            try { work.Cancel(); }
            catch (AggregateException error) { output.Error("Cancellation callback failed: " + error.Message); }
            readMode = session.Mode;
        }
        void CancelKeyPress(object? sender, ConsoleCancelEventArgs args) { args.Cancel = true; Cancel(); }
        console.SetCancellationHandler(Cancel);
        var consoleSignals = interactive || console is ReplConsole;
        if (consoleSignals) console.Editor.CancelKeyPress += CancelKeyPress;
        async Task DrainAsync()
        {
            work.Complete();
            if (interactive) { await work.Completion; return; }
            // EOF from the client is an orderly drain. Keep reading control lines so
            // Ctrl+C/:cancel still works if the final submitted item loops forever.
            using var draining = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            var drainToken = draining.Token;
            var controls = Task.Run(async () =>
            {
                try
                {
                    while (await console.Input.ReadLineAsync(drainToken).ConfigureAwait(false) is { } line)
                        if (line.Trim() == ":cancel") Cancel();
                    if (cancelOnEof) Cancel();
                }
                catch (OperationCanceledException) when (drainToken.IsCancellationRequested) { }
                catch (IOException) { Cancel(); }
            });
            try { await work.Completion; }
            finally
            {
                draining.Cancel();
                try { await controls.WaitAsync(draining.Token); }
                catch (OperationCanceledException) when (draining.IsCancellationRequested) { }
            }
        }
        try
        {
            await session.RefreshAsync(lifetime);
            if (interactive)
            {
                if (history.Length > 0) Directory.CreateDirectory(Path.GetDirectoryName(history)!);
                var initialScope = await session.ScopeStyleAsync(lifetime);
                promptScope = initialScope.Path;
                promptFamilies = initialScope.Families;
                prompt = await CreatePromptAsync(session, console, history, cancellationToken: lifetime, scopePath: promptScope, scopeFamilies: promptFamilies, cancel: Cancel);
                output.Banner(scenePath);
            }
            while (!lifetime.IsCancellationRequested)
            {
                string? text;
                if (prompt is not null)
                {
                    var scope = await session.ScopeStyleAsync(lifetime);
                    if (session.Mode != promptMode || scope.Path != promptScope || promptFamilies is null || !scope.Families.SequenceEqual(promptFamilies))
                    {
                        await prompt.DisposeAsync();
                        prompt = await CreatePromptAsync(session, console, history, multiline, warmUp: false, cancellationToken: lifetime, scopePath: scope.Path, scopeFamilies: scope.Families, cancel: Cancel);
                        promptMode = session.Mode;
                        promptScope = scope.Path;
                        promptFamilies = scope.Families;
                    }
                    var response = await (console.PromptOutput is { } promptOutput
                        ? promptOutput.ReadLineAsync(prompt, CreatePromptText(promptScope, console.Editor.BufferWidth, promptFamilies, false, promptMode).Length)
                        : prompt.ReadLineAsync()).ConfigureAwait(false);
                    if (!response.IsSuccess) continue;
                    text = response.Text;
                }
                else text = await ReadSubmissionAsync(console.Input, lifetime, readMode).ConfigureAwait(false);
                if (text is null)
                {
                    if (cancelOnEof) Cancel();
                    break;
                }
                if (string.IsNullOrWhiteSpace(text)) continue;
                switch (text.Trim())
                {
                    case ":exit":
                    case "exit" when readMode.HasShell():
                        await DrainAsync();
                        if (requestHostExit is not null) await requestHostExit();
                        return exitCode;
                    case ":cancel":
                        Cancel();
                        continue;
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
                                multiline, warmUp: false, cancellationToken: lifetime, scopePath: promptScope, scopeFamilies: promptFamilies, cancel: Cancel);
                        }
                        output.InputMode(multiline);
                        continue;
                    case ":reset":
                        _ = work.Enqueue(_ =>
                        {
                            session.Reset();
                            output.Message("C# session reset. The running scene is preserved.");
                            return Task.CompletedTask;
                        });
                        continue;
                }
                if (ReplModes.TryParse(text.Trim(), out var selectedMode)) readMode = selectedMode;
                var submitted = text;
                _ = work.Enqueue(async token =>
                {
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        var result = await session.EvaluateAsync(submitted, token).ConfigureAwait(false);
                        if (result.Cancelled) output.Message("Submission cancelled.");
                        else if (result.Error is { } error)
                        {
                            output.Error(error);
                            if (!interactive) exitCode = 1;
                        }
                        else if (result.Tree is { } hierarchy) output.Tree(hierarchy, result.Styled);
                        else if (result.Value is { } value) output.Result(value, result.Styled);
                        await session.RefreshAsync(lifetime).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { output.Message("Submission cancelled."); }
                    catch (Exception error)
                    {
                        output.Error(error.ToString());
                        if (!interactive) exitCode = 1;
                    }
                });
            }
            work.Complete();
            await work.Completion;
            return exitCode;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return exitCode; }
        catch
        {
            Cancel();
            throw;
        }
        finally
        {
            console.SetCancellationHandler(null);
            if (consoleSignals) console.Editor.CancelKeyPress -= CancelKeyPress;
            work.Complete();
            await work.Completion;
            if (prompt is not null) await prompt.DisposeAsync();
        }
    }

    private static async Task<Prompt> CreatePromptAsync(ReplSession session, IReplTerminal console, string history, bool multiline = false,
        bool warmUp = true, CancellationToken cancellationToken = default, string? scopePath = null, IReadOnlyDictionary<string, NodeFamily>? scopeFamilies = null, Action? cancel = null)
    {
        var callbacks = new ReplPromptCallbacks(session, console.TakePaste, cancel, console.PromptOutput);
        if (warmUp) await callbacks.WarmUpAsync(cancellationToken);
        return new Prompt(persistentHistoryFilepath: history.Length > 0 ? history : null, callbacks: callbacks, console: console.Editor,
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
            // Console.In uses a synchronous reader even through ReadLineAsync.
            // Keep an open redirected stdin from holding up engine shutdown.
            var line = ReferenceEquals(input, Console.In)
                ? await Task.Run(input.ReadLine, cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false)
                : await input.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) return text.Length == 0 ? null : text.ToString();
            // Control input must remain reachable even while assembling an unfinished block.
            if (line.Trim() is ":cancel" or ":exit") return line;
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
    ReplPromptOutput? PromptOutput => null;
    bool Color => !PromptConfiguration.HasUserOptedOutFromColor;
    void SetCancellationHandler(Action? cancel) { }
    string HistoryFile { get; }
    string? TakePaste();
    void Restore();
}

internal sealed class ReplConsole : SystemConsole, IConsole, IReplTerminal
{
    public IConsole Editor => this;
    public bool Interactive => !Console.IsInputRedirected && !Console.IsOutputRedirected;
    public TextReader Input => Console.In;
    public ReplOutput Output { get; }
    public ReplPromptOutput? PromptOutput { get; }
    public string HistoryFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "2dog", "repl-history");
    private readonly CancellationToken lifetime;
    private readonly ReplInputBuffer input;

    public ReplConsole(CancellationToken lifetime)
    {
        this.lifetime = lifetime;
        PromptOutput = Interactive ? new ReplPromptOutput(this) : null;
        Output = ReplOutput.ForConsole(PromptOutput);
        input = new ReplInputBuffer(() => Console.KeyAvailable, ReadConsoleKey, OperatingSystem.IsWindows());
    }

    bool IConsole.KeyAvailable
    {
        get
        {
            lifetime.ThrowIfCancellationRequested();
            return PromptOutput?.Restoring != true && input.KeyAvailable;
        }
    }

    public string? TakePaste() => input.TakePaste();

    public override ConsoleKeyInfo ReadKey(bool intercept)
    {
        lifetime.ThrowIfCancellationRequested();
        if (PromptOutput?.ReadRestoreKey() is { } key) return key;
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
            PromptOutput?.CheckForOutput();
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

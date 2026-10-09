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
            var console = new ReplConsole(lifetime.Token);
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
        ReplConsole console, CancellationToken lifetime)
    {
        var interactive = !Console.IsInputRedirected && !Console.IsOutputRedirected;
        if (interactive) Console.WriteLine("Preparing C# completion...");
        var assemblies = AppDomain.CurrentDomain.GetAssemblies().Concat(references).ToArray();
        var session = new ReplSession(globals, assemblies, dispatcher);
        Prompt? prompt = null;
        var exitCode = 0;
        var multiline = false;
        try
        {
            if (interactive)
            {
                var history = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "2dog", "repl-history");
                Directory.CreateDirectory(Path.GetDirectoryName(history)!);
                prompt = await CreatePromptAsync(session, console, history, cancellationToken: lifetime);
                ReplOutput.Banner(scenePath);
            }
            while (!lifetime.IsCancellationRequested)
            {
                string? text;
                CancellationToken submissionToken = lifetime;
                if (prompt is not null)
                {
                    var response = await prompt.ReadLineAsync().ConfigureAwait(false);
                    if (!response.IsSuccess) continue;
                    text = response.Text;
                    submissionToken = response.CancellationToken;
                }
                else text = await ReadSubmissionAsync(lifetime).ConfigureAwait(false);
                if (text is null) break;
                if (string.IsNullOrWhiteSpace(text)) continue;
                switch (text.Trim())
                {
                    case ":quit": case ":exit": case "exit": return exitCode;
                    case ":help":
                        ReplOutput.Help(multiline);
                        continue;
                    case ":clear":
                        if (interactive) Console.Clear();
                        continue;
                    case ":multiline":
                        multiline = !multiline;
                        if (prompt is not null)
                        {
                            await prompt.DisposeAsync();
                            prompt = await CreatePromptAsync(session, console, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "2dog", "repl-history"),
                                multiline, warmUp: false, cancellationToken: lifetime);
                        }
                        ReplOutput.InputMode(multiline);
                        continue;
                    case ":reset":
                        if (interactive) Console.WriteLine("Preparing C# completion...");
                        session.Dispose();
                        session = new ReplSession(globals, assemblies, dispatcher);
                        if (prompt is not null)
                        {
                            await prompt.DisposeAsync();
                            prompt = await CreatePromptAsync(session, console, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "2dog", "repl-history"),
                                multiline, cancellationToken: lifetime);
                        }
                        Console.WriteLine("C# session reset. The running scene is preserved.");
                        continue;
                }
                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime, submissionToken);
                try
                {
                    var result = await session.EvaluateAsync(text, cancellation.Token).ConfigureAwait(false);
                    if (result.Cancelled)
                    {
                        Console.WriteLine("Submission cancelled.");
                        continue;
                    }
                    if (result.Error is { } error)
                    {
                        ReplOutput.Error(error);
                        if (!interactive) exitCode = 1;
                    }
                    else if (result.Tree is { } hierarchy) ReplOutput.Tree(hierarchy);
                    else if (result.Value is { } value) ReplOutput.Result(value);
                }
                catch (OperationCanceledException) when (!lifetime.IsCancellationRequested)
                {
                    Console.WriteLine("Submission cancelled.");
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

    private static async Task<Prompt> CreatePromptAsync(ReplSession session, ReplConsole console, string history, bool multiline = false,
        bool warmUp = true, CancellationToken cancellationToken = default)
    {
        var callbacks = new ReplPromptCallbacks(session, console.TakePaste);
        if (warmUp) await callbacks.WarmUpAsync(cancellationToken);
        return new Prompt(persistentHistoryFilepath: history, callbacks: callbacks, console: console,
            configuration: CreatePromptConfiguration(multiline));
    }

    internal static PromptConfiguration CreatePromptConfiguration(bool multiline = false) => new(
        prompt: PromptConfiguration.HasUserOptedOutFromColor ? new FormattedString("godot> ") :
            new FormattedString("godot> ", new FormatSpan(0, 5, AnsiColor.BrightCyan)),
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

    private static async Task<string?> ReadSubmissionAsync(CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        while (true)
        {
            var line = await Console.In.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) return text.Length == 0 ? null : text.ToString();
            text.AppendLine(line);
            if (line.TrimStart().StartsWith(':') || ReplSession.IsComplete(text.ToString())) return text.ToString();
        }
    }
}

internal sealed class ReplConsole : SystemConsole, IConsole
{
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

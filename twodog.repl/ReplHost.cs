using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

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
    /// A terminal host accepts local pipe sessions (2dog repl) under a per-process name; --repl-listen NAME
    /// chooses the name and also keeps a scripted host listening. Remaining arguments go to Godot.</summary>
    public static int Run(string[] args, Func<string[], Engine> createEngine, params Assembly[] references)
    {
        var options = ReplPipeOptions.Parse(args);
        if (options.Connect is { } connect) return ReplPipeClient.RunAsync(connect).GetAwaiter().GetResult();
        var listen = ListenName(options.Listen, Console.IsInputRedirected, Console.IsOutputRedirected);
        return RunCore(createEngine(options.EngineArgs), token => new ReplConsole(token), listen, references);
    }

    internal static string? ListenName(string? requested, bool inputRedirected, bool outputRedirected)
        => requested ?? (inputRedirected || outputRedirected ? null : "2dog-" + Environment.ProcessId);

    /// <summary>How another terminal starts this host's client: the apphost, or dotnet with the entry assembly.</summary>
    internal static string[] ClientCommand(string? processPath, Assembly? entry)
        => Path.GetFileNameWithoutExtension(processPath) == "dotnet" && entry?.Location is { Length: > 0 } location
            ? [processPath!, location]
            : [processPath ?? ""];

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
            using var published = pipeName is null ? null : Publish(pipeName, console.Output);
            if (pipeName is not null)
                console.Output.Message($"REPL listening on local pipe '{pipeName}'. Connect with 2dog repl, or run code with 2dog repl exec -s NAME CODE.");
            var terminal = Task.Run(() => ReadEvalPrintAsync(globals, scenePath, references, dispatcher, console, lifetime.Token,
                requestHostExit: pipeName is null ? null : () => dispatcher.InvokeAsync(() => { engine.RequestQuit(); return Task.FromResult(true); }),
                pipe: pipeName));
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

    // Clients find listening hosts through these files; a stale file is ignored once its process is gone.
    private static IDisposable? Publish(string pipeName, ReplOutput output)
    {
        var path = ReplDiscovery.PathOf(pipeName);
        try
        {
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            var info = new ReplHostInfo(ReplProtocol.Version, pipeName, Environment.ProcessId, new DateTimeOffset(process.StartTime),
                Godot.ProjectSettings.GlobalizePath("res://"), ClientCommand(Environment.ProcessPath, Assembly.GetEntryAssembly()));
            Directory.CreateDirectory(ReplDiscovery.Root);
            File.WriteAllText(path, ReplProtocol.Serialize(info));
            return new Published(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            output.Error($"Could not publish the REPL host for 2dog repl ({error.Message}). Use --repl-connect {pipeName} instead.");
            return null;
        }
    }

    private sealed class Published(string path) : IDisposable
    {
        public void Dispose()
        {
            try { File.Delete(path); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* Clients skip files of exited hosts. */ }
        }
    }

    internal static async Task<int> ReadEvalPrintAsync(ReplGlobals globals, string scenePath, Assembly[] references, EngineDispatcher dispatcher,
        IReplTerminal console, CancellationToken lifetime, bool cancelOnEof = false, Func<Task>? requestHostExit = null, string? pipe = null)
    {
        var interactive = console.Interactive;
        var output = console.Output;
        if (interactive) output.Message("Preparing C# completion...");
        var assemblies = AppDomain.CurrentDomain.GetAssemblies().Concat(references).ToArray();
        using var session = new ReplSession(globals, assemblies, dispatcher) { DetachedInput = true };
        using var work = new ReplWorkQueue(lifetime);
        ReplFrontEnd? frontEnd = null;
        var exitCode = 0;
        var multiline = false;
        var readMode = session.Mode;
        void Cancel()
        {
            try { work.Cancel(); }
            catch (AggregateException error) { output.Error("Cancellation callback failed: " + error.Message); }
            readMode = session.Mode;
        }
        // A redirected process console still cancels its work on Ctrl+C. Interactive terminals read it as a key.
        void CancelKeyPress(object? sender, ConsoleCancelEventArgs args) { args.Cancel = true; Cancel(); }
        console.SetCancellationHandler(Cancel);
        var consoleSignals = !interactive && console is ReplConsole;
        if (consoleSignals) Console.CancelKeyPress += CancelKeyPress;
        async Task UpdatePromptAsync()
        {
            if (frontEnd is null) return;
            var scope = await session.ScopeStyleAsync(lifetime).ConfigureAwait(false);
            frontEnd.SetPrompt(scope.Path, scope.Families, session.Mode);
        }
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
                var language = new ReplLanguage(session);
                await language.WarmUpAsync(lifetime);
                frontEnd = new ReplFrontEnd(console, language, work, pipe);
                await UpdatePromptAsync();
                output.Banner(scenePath);
            }
            while (!lifetime.IsCancellationRequested)
            {
                string? text;
                ReplSubmission? submission = null;
                // Queued submissions appear in the scrollback when they run, directly above their results.
                void Echo()
                {
                    if (submission is not null) frontEnd!.Echo(submission);
                }
                if (frontEnd is not null)
                {
                    submission = await frontEnd.ReadLineAsync(lifetime).ConfigureAwait(false);
                    // Ctrl+C abandons the draft and cancels the session's running and queued work.
                    if (submission is null)
                    {
                        Cancel();
                        continue;
                    }
                    text = submission.Text;
                }
                else
                {
                    text = await ReadSubmissionAsync(console.Input, lifetime, readMode).ConfigureAwait(false);
                    if (text is null)
                    {
                        if (cancelOnEof) Cancel();
                        break;
                    }
                }
                if (string.IsNullOrWhiteSpace(text))
                {
                    Echo();
                    continue;
                }
                switch (text.Trim())
                {
                    case ":exit":
                    case "exit" when readMode.HasShell():
                        await DrainAsync();
                        Echo();
                        if (requestHostExit is not null) await requestHostExit();
                        return exitCode;
                    case ":cancel":
                        Echo();
                        Cancel();
                        continue;
                    case ":help":
                        Echo();
                        output.Help(multiline, session.Mode);
                        continue;
                    case ":clear":
                        console.Screen?.Clear();
                        continue;
                    case ":multiline":
                        Echo();
                        multiline = !multiline;
                        frontEnd?.SetMultiline(multiline);
                        output.InputMode(multiline);
                        continue;
                    case ":reset":
                        var reset = submission;
                        _ = work.Enqueue(_ =>
                        {
                            if (reset is not null) frontEnd!.Echo(reset);
                            session.Reset();
                            output.Message("C# session reset. The running scene is preserved.");
                            return Task.CompletedTask;
                        });
                        continue;
                }
                if (ReplModes.TryParse(text.Trim(), out var selectedMode)) readMode = selectedMode;
                var submitted = text;
                var queued = submission;
                _ = work.Enqueue(async token =>
                {
                    if (queued is not null) frontEnd!.Echo(queued);
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
                        await UpdatePromptAsync().ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { output.Message("Submission cancelled."); }
                    catch (Exception error)
                    {
                        output.Error(error.ToString());
                        if (!interactive) exitCode = 1;
                    }
                }, skipped: queued is null ? null : () =>
                {
                    frontEnd!.Echo(queued);
                    output.Message("Submission cancelled.");
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
            if (consoleSignals) Console.CancelKeyPress -= CancelKeyPress;
            work.Complete();
            await work.Completion;
        }
    }

    internal static StyledText CreatePromptText(string? scopePath, int? terminalWidth, IReadOnlyDictionary<string, NodeFamily>? scopeFamilies, bool color, ReplMode mode = ReplMode.Auto)
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
        if (!color) return new StyledText(text);
        var family = scopeFamilies is not null && fullPath is not null && scopeFamilies.TryGetValue(fullPath, out var known) ? known : NodeFamily.Node;
        var spans = new List<StyledSpan> { new(0, 5, NodeColors.Color(family)) };
        if (mode != ReplMode.Auto) spans.Add(new(5, prefix.Length - 5, TermColor.BrightMagenta));
        if (scopePath is not null && scopePath != "/root")
        {
            var full = NodeColors.Path(fullPath!, scopeFamilies);
            var hidden = scopePath.StartsWith("...", StringComparison.Ordinal) ? 3 : 0;
            var visibleLength = scopePath.Length - hidden;
            if (hidden > 0) spans.Add(new(prefix.Length + 2, hidden, NodeColors.Grey));
            spans.AddRange(full.Substring(full.Length - visibleLength, visibleLength).Spans.Select(s => s.Offset(prefix.Length + 2 + hidden)));
        }
        return new StyledText(text, spans);
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

using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;

namespace twodog.Repl;

// A session addressed by name outlives the requests that use it, until it is closed or the host stops.
// An unnamed request gets a private session that closes with the request.
internal sealed class ReplExecSession : IDisposable
{
    private readonly CancellationTokenSource lifetime;
    private int busy;
    private long lastUsed = Environment.TickCount64;
    public string? Name { get; }
    public ReplGlobals Globals { get; }
    public ReplSession Session { get; }
    public ReplWorkQueue Work { get; }

    public ReplExecSession(string? name, Engine engine, Assembly[] references, EngineDispatcher dispatcher, CancellationToken stopped)
    {
        Name = name;
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(stopped);
        Globals = new ReplGlobals(engine);
        Session = new ReplSession(Globals, AppDomain.CurrentDomain.GetAssemblies().Concat(references), dispatcher);
        Work = new ReplWorkQueue(lifetime.Token);
    }

    public IDisposable Use()
    {
        Interlocked.Increment(ref busy);
        return new Release(this);
    }

    /// <summary>Keeps the session busy until work that a request gave up on really ends.</summary>
    public void Hold(Task work)
    {
        var release = Use();
        work.ContinueWith(ended =>
        {
            _ = ended.Exception;
            release.Dispose();
        }, TaskScheduler.Default);
    }

    /// <summary>Live state (the selected node and open worlds) is read only on the owner thread.</summary>
    public ReplSessionInfo Describe(bool live)
    {
        var active = Volatile.Read(ref busy) > 0;
        return new(Name!, Session.Mode.Name(), live ? Globals.here.GetPath().ToString() : null,
            live ? Globals.worlds.Names.Where(world => Globals.worlds[world].Viewport is not null).ToArray() : null, active,
            active ? 0 : (Environment.TickCount64 - Interlocked.Read(ref lastUsed)) / 1000.0);
    }

    /// <summary>Cancels running and queued work and stops accepting more.</summary>
    public void Cancel()
    {
        try { lifetime.Cancel(); }
        catch (AggregateException) { /* A submission's cancellation callback cannot keep its session open. */ }
        Work.Complete();
    }

    /// <summary>Releases worlds on the owner thread, once the work has ended.</summary>
    /// <returns>False if the dispatcher has stopped and the worlds still need releasing.</returns>
    public async Task<bool> ReleaseWorldsAsync(EngineDispatcher dispatcher)
    {
        try { return await dispatcher.InvokeAsync(() => { Globals.worlds.Dispose(); return Task.FromResult(true); }).ConfigureAwait(false); }
        catch (OperationCanceledException) { return false; }
    }

    public void Dispose()
    {
        Session.Dispose();
        Work.Dispose();
        lifetime.Dispose();
    }

    private sealed class Release(ReplExecSession session) : IDisposable
    {
        public void Dispose()
        {
            Interlocked.Exchange(ref session.lastUsed, Environment.TickCount64);
            Interlocked.Decrement(ref session.busy);
        }
    }
}

// Serves one request per connection: a JSON request line in, JSON event lines out, ending with "done".
internal sealed class ReplExecHost(Engine engine, Assembly[] references, EngineDispatcher dispatcher, CancellationToken stopped) : IDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<ReplExecSession>> named = new(StringComparer.Ordinal);
    // Names whose close is still waiting for work to stop; a request for one fails rather than reopening it.
    private readonly ConcurrentDictionary<string, bool> closing = new(StringComparer.Ordinal);
    private readonly ConcurrentBag<ReplWorlds> unreleased = new();

    /// <summary>How long cancelled work may take to stop before a request or close stops waiting for it.</summary>
    internal static TimeSpan CancelGrace { get; set; } = TimeSpan.FromSeconds(2);
    private readonly Lock loggerGate = new();
    private Task? logger;
    private ReplCapture.GodotLog? godotLog;

    public async Task ServeAsync(string hello, TextReader reader, TextWriter output, CancellationToken connection)
    {
        void Send(ReplEvent message) => output.WriteLine(ReplProtocol.Serialize(message));
        void Fail(string message) => Send(new(ReplEvents.Done, message, Error: true, Status: ReplStatus.Error));
        var version = hello[ReplProtocol.ExecHello.Length..];
        if (version != ReplProtocol.Version.ToString(System.Globalization.CultureInfo.InvariantCulture))
        {
            Fail($"this host speaks REPL protocol {ReplProtocol.Version}, the client '{version}' - use the 2dog tool version matching the host's 2dog.repl package");
            return;
        }
        ReplRequest? request;
        try { request = await reader.ReadLineAsync(connection).ConfigureAwait(false) is { } line ? ReplProtocol.Deserialize<ReplRequest>(line) : null; }
        catch (JsonException) { request = null; }
        if (request is null) { Fail("expected one JSON request line"); return; }

        // The client keeps the pipe open until "done"; reaching EOF early means it went away.
        using var active = CancellationTokenSource.CreateLinkedTokenSource(connection);
        var watching = Task.Run(async () =>
        {
            try { while (await reader.ReadLineAsync(active.Token).ConfigureAwait(false) is not null) { } }
            catch (Exception error) when (error is IOException or OperationCanceledException) { }
            try { active.Cancel(); }
            catch (AggregateException) { /* Disconnect cleanup continues even if a user callback throws. */ }
        });
        try
        {
            switch (request.Op)
            {
                case ReplOps.Exec: await ExecAsync(request, Send, Fail, active.Token).ConfigureAwait(false); break;
                case ReplOps.Sessions:
                    Send(new(ReplEvents.Sessions, Sessions: await DescribeAsync(active.Token).ConfigureAwait(false)));
                    Send(new(ReplEvents.Done, Status: ReplStatus.Ok));
                    break;
                case ReplOps.Close:
                    if (request.Session is not { } name) { Fail("close needs a session name"); break; }
                    if (!named.TryGetValue(name, out var session)) { Fail($"no session named '{name}'"); break; }
                    closing[name] = true;
                    named.TryRemove(name, out _);
                    var settled = await CloseAsync(session.Value).ConfigureAwait(false);
                    closing.TryRemove(name, out _);
                    Send(new(ReplEvents.Done, settled ? $"closed session '{name}'"
                        : $"closed session '{name}'; a submission that ignores cancellation is still running, and its worlds close when it ends",
                        Status: ReplStatus.Ok, Session: name));
                    break;
                default: Fail($"unknown operation '{request.Op}'"); break;
            }
        }
        catch (OperationCanceledException) when (active.IsCancellationRequested) { /* The client is gone. */ }
        finally
        {
            try { active.Cancel(); }
            catch (AggregateException) { /* The request has finished; only the watcher still listens. */ }
            await watching.ConfigureAwait(false);
        }
    }

    // A synchronous submission can hold the owner thread indefinitely; the list must still answer.
    private async Task<ReplSessionInfo[]> DescribeAsync(CancellationToken token)
    {
        var sessions = named.Values.Where(lazy => lazy.IsValueCreated).Select(lazy => lazy.Value)
            .OrderBy(session => session.Name, StringComparer.Ordinal).ToArray();
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(token);
        var live = dispatcher.InvokeAsync(() => Task.FromResult(sessions.Select(session => session.Describe(live: true)).ToArray()), waiting.Token);
        if (await Task.WhenAny(live, Task.Delay(DescribeTimeout, token)).ConfigureAwait(false) == live) return await live.ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        waiting.Cancel();
        return sessions.Select(session => session.Describe(live: false)).ToArray();
    }

    internal static TimeSpan DescribeTimeout { get; set; } = TimeSpan.FromMilliseconds(500);

    private async Task ExecAsync(ReplRequest request, Action<ReplEvent> send, Action<string> fail, CancellationToken token)
    {
        if (request.Session is { } name && !ReplDiscovery.IsValidName(name))
        {
            fail($"'{name}' is not a session name (up to 40 letters, digits, '-', '_' and '.')");
            return;
        }
        if (request.Timeout is { } seconds && !(seconds > 0 && seconds <= int.MaxValue / 1000))
        {
            fail("the timeout must be a positive number of seconds");
            return;
        }
        if (request.Session is { } closed && closing.ContainsKey(closed))
        {
            fail($"session '{closed}' is closing - wait for '2dog repl close' to finish, then run again");
            return;
        }
        await EnsureLoggerAsync(token).ConfigureAwait(false);
        var session = request.Session is { } existing
            ? named.GetOrAdd(existing, key => new(() => new ReplExecSession(key, engine, references, dispatcher, stopped))).Value
            : new ReplExecSession(null, engine, references, dispatcher, stopped);
        using var timeout = new CancellationTokenSource();
        if (request.Timeout is { } limit) timeout.CancelAfter(TimeSpan.FromSeconds(limit));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
        // Output from Godot's other threads may arrive after the client left; it is no longer anyone's.
        var logged = 0;
        var capture = new ReplCapture((text, error, log) =>
        {
            if (error && log) Interlocked.Increment(ref logged);
            try { send(new(log ? ReplEvents.Log : ReplEvents.Output, text, error)); }
            catch (IOException) { }
        });
        Func<int>? errors = request.Strict ? () => Volatile.Read(ref logged) : null;
        try
        {
            string status;
            using (session.Use()) status = await RunAsync(session, request.Code ?? "", capture, send, errors, cancellation.Token, timeout.Token).ConfigureAwait(false);
            capture.Close();
            send(new(ReplEvents.Done, Status: status, Session: session.Name));
        }
        finally
        {
            capture.Close();
            if (session.Name is null)
            {
                await CloseAsync(session).ConfigureAwait(false);
            }
        }
    }

    /// <param name="errors">Under --strict, the count of Godot errors and warnings logged so far.</param>
    private static async Task<string> RunAsync(ReplExecSession session, string code, ReplCapture capture, Action<ReplEvent> send,
        Func<int>? errors, CancellationToken token, CancellationToken timeout)
    {
        foreach (var (text, line, readMode) in await SubmissionsAsync(code, session.Session.Mode).ConfigureAwait(false))
        {
            var trimmed = text.Trim();
            if (trimmed.Length == 0 || trimmed is ":cancel" or ":clear" or ":multiline") continue;
            if (trimmed == ":exit" || trimmed == "exit" && readMode.HasShell()) break;
            ReplEvent? result = null;
            var started = false;
            var before = errors?.Invoke() ?? 0;
            try
            {
                var queued = session.Work.Enqueue(async queuedToken =>
                {
                    started = true;
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(queuedToken, token);
                    linked.Token.ThrowIfCancellationRequested();
                    result = trimmed switch
                    {
                        ":reset" => Reset(session),
                        ":help" => Help(session),
                        ":help agents" => new(ReplEvents.Result, ReplOutput.AgentGuide, Status: ReplStatus.Ok),
                        _ => Result(await session.Session.EvaluateAsync(text, linked.Token, capture, line).ConfigureAwait(false)),
                    };
                });
                // Awaits that ignore the token, such as ToSignal, keep running; the request stops waiting for them.
                if (!await SettledAsync(queued, token).ConfigureAwait(false))
                {
                    session.Hold(queued);
                    result = new(ReplEvents.Result, started
                        ? "the submission does not observe cancellation and keeps running; later requests to this session wait for it"
                        : "a submission that does not observe cancellation is still running in this session", Error: true, Status: ReplStatus.Cancelled);
                }
                else await queued.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { result = new(ReplEvents.Result, Status: ReplStatus.Cancelled); }
            catch (Exception error) { result = new(ReplEvents.Result, error.ToString(), Error: true, Status: ReplStatus.Error); }
            if (result!.Status == ReplStatus.Cancelled && timeout.IsCancellationRequested) result = result with { Status = ReplStatus.Timeout };
            if (result.Status == ReplStatus.Ok && errors?.Invoke() - before is > 0 and var count)
                result = new(ReplEvents.Result, $"Godot logged {count} error(s) or warning(s); --strict fails the submission", Error: true, Status: ReplStatus.Error);
            send(result with { Input = text.TrimEnd('\r', '\n') });
            if (result.Status != ReplStatus.Ok) return result.Status!;
        }
        return ReplStatus.Ok;
    }

    /// <summary>Splits a request like piped input, then joins consecutive C# pieces that also parse together: such a
    /// run compiles once, runs without frames in between, and runs not at all if any of it fails to compile.</summary>
    /// <returns>Each submission with its first line in the request and the mode it is read in.</returns>
    internal static async Task<List<(string Text, int Line, ReplMode Mode)>> SubmissionsAsync(string code, ReplMode mode)
    {
        var submissions = new List<(string Text, int Line, ReplMode Mode)>();
        using var input = new StringReader(code);
        var line = 1;
        var joinable = false;
        // The code is already in memory: reading never waits.
        while (await ReplHost.ReadSubmissionAsync(input, CancellationToken.None, mode).ConfigureAwait(false) is { } text)
        {
            var isCode = IsCode(text, mode);
            if (isCode && joinable && Parses(submissions[^1].Text + text)) submissions[^1] = submissions[^1] with { Text = submissions[^1].Text + text };
            else submissions.Add((text, line, mode));
            joinable = isCode;
            if (ReplModes.TryParse(text.Trim(), out var next)) mode = next;
            line += Math.Max(1, text.Count(c => c == '\n'));
        }
        return submissions;
    }

    private static bool IsCode(string text, ReplMode mode)
    {
        var trimmed = text.Trim();
        return mode is ReplMode.Auto or ReplMode.CSharp && trimmed.Length > 0 && trimmed[0] != ':' && !(trimmed == "exit" && mode.HasShell()) &&
               !mode.TryNavigation(text, out _) && !mode.TryListing(text, out _);
    }

    private static bool Parses(string code) => !Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(
            new NodePathInput(code, new Dictionary<string, NodePathTarget>()).Code,
            new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(kind: Microsoft.CodeAnalysis.SourceCodeKind.Script))
        .GetDiagnostics().Any(diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);

    // True once the work has ended; after cancellation it gets a grace period to stop on its own.
    private static async Task<bool> SettledAsync(Task work, CancellationToken token)
    {
        if (await Task.WhenAny(work, Task.Delay(Timeout.Infinite, token)).ConfigureAwait(false) == work) return true;
        return await Task.WhenAny(work, Task.Delay(CancelGrace, CancellationToken.None)).ConfigureAwait(false) == work;
    }

    private static ReplEvent Result(ReplResult result)
    {
        if (result.Cancelled) return new(ReplEvents.Result, Status: ReplStatus.Cancelled);
        if (result.Error is { } error) return new(ReplEvents.Result, error, Error: true, Status: ReplStatus.Error);
        return new(ReplEvents.Result, result.Tree?.TrimEnd() ?? result.Value, Status: ReplStatus.Ok, Tree: result.Nodes);
    }

    private static ReplEvent Reset(ReplExecSession session)
    {
        session.Session.Reset();
        return new(ReplEvents.Result, "C# session reset. The running scene is preserved.", Status: ReplStatus.Ok);
    }

    private static ReplEvent Help(ReplExecSession session)
    {
        using var text = new StringWriter();
        new ReplOutput(text, text, false, false).Help(multiline: false, session.Session.Mode);
        return new(ReplEvents.Result, text.ToString().Trim(), Status: ReplStatus.Ok);
    }

    // Registered once, on the owner thread, when the first request needs per-submission logs.
    private Task EnsureLoggerAsync(CancellationToken token)
    {
        lock (loggerGate)
            logger ??= dispatcher.InvokeAsync(() =>
            {
                godotLog = new ReplCapture.GodotLog();
                Godot.OS.AddLogger(godotLog);
                return Task.FromResult(true);
            });
        return logger.WaitAsync(token);
    }

    /// <returns>False when work ignored cancellation: the session is released once that work ends.</returns>
    private async Task<bool> CloseAsync(ReplExecSession session)
    {
        session.Cancel();
        if (await Task.WhenAny(session.Work.Completion, Task.Delay(CancelGrace)).ConfigureAwait(false) == session.Work.Completion)
        {
            await ReleaseAsync(session).ConfigureAwait(false);
            return true;
        }
        _ = session.Work.Completion.ContinueWith(_ => ReleaseAsync(session), TaskScheduler.Default).Unwrap();
        return false;
    }

    private async Task ReleaseAsync(ReplExecSession session)
    {
        try
        {
            if (!await session.ReleaseWorldsAsync(dispatcher).ConfigureAwait(false)) unreleased.Add(session.Globals.worlds);
        }
        finally { session.Dispose(); }
    }

    /// <summary>Closes every session; the owner thread must keep pumping until this completes.</summary>
    public Task CloseAllAsync() => Task.WhenAll(named.Keys.Select(name => named.TryRemove(name, out var lazy) && lazy.IsValueCreated
        ? CloseAsync(lazy.Value) : Task.CompletedTask));

    /// <summary>Owner thread, after <see cref="CloseAllAsync"/>.</summary>
    public void Dispose()
    {
        foreach (var worlds in unreleased) worlds.Dispose();
        unreleased.Clear();
        // A request racing host shutdown may have created a session after CloseAllAsync looked.
        foreach (var lazy in named.Values.Where(lazy => lazy.IsValueCreated))
        {
            lazy.Value.Globals.worlds.Dispose();
            lazy.Value.Dispose();
        }
        named.Clear();
        if (godotLog is not { } registered) return;
        godotLog = null;
        Godot.OS.RemoveLogger(registered);
        registered.Dispose();
    }
}

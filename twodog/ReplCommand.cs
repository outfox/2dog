using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using twodog.cli.Repl;

namespace twodog.cli;

/// <summary>
/// The repl verb: finds a running REPL host by its published file and sends it one request over the local pipe, or
/// attaches this terminal through the host's own client. The tool itself needs no engine and no Roslyn.
/// </summary>
internal static class ReplCommand
{
    /// <summary>Runs the host's terminal client in this console; tests replace it.</summary>
    internal static Func<ProcessStartInfo, int> Launch = info =>
    {
        using var process = Process.Start(info)!;
        process.WaitForExit();
        return process.ExitCode;
    };

    public static int Run(ReplOptions options, Report report)
    {
        var host = ReplHosts.Find(options.Pipe, Environment.CurrentDirectory, ReplDiscovery.Root);
        var repl = report.Repl = new ReportRepl(host.Pipe, host.Project);
        return options.Op switch
        {
            ReplOp.Attach => Attach(host),
            ReplOp.Sessions => Sessions(host, repl),
            ReplOp.Close => Close(host, options.Session!, repl),
            _ => Exec(host, options, repl),
        };
    }

    private static int Attach(ReplHostInfo host)
    {
        if (host.Client is not [{ Length: > 0 } program, ..])
            throw new ToolException($"REPL host '{host.Pipe}' published no client - run its executable with --repl-connect {host.Pipe}");
        var info = new ProcessStartInfo(program) { UseShellExecute = false };
        foreach (var argument in host.Client.Skip(1).Append("--repl-connect").Append(host.Pipe)) info.ArgumentList.Add(argument);
        // The client shares this console, so Ctrl+C belongs to the REPL and never ends the wait here.
        try { return Launch(info); }
        catch (Win32Exception ex) { throw new ToolException($"could not start the REPL client {program}: {ex.Message}"); }
    }

    private static int Exec(ReplHostInfo host, ReplOptions options, ReportRepl repl)
    {
        var code = options.Code ?? (Out.Mode.Console.InputRedirected
            ? Console.In.ReadToEnd()
            : throw new UsageException("2dog repl exec needs code: pass it as arguments, or pipe it in", Verb.Repl));
        repl.Session = options.Session;
        var results = repl.Results = [];
        var output = new StringBuilder();
        var log = new StringBuilder();
        // The host answers a timeout itself; this deadline only catches a host that cannot answer at all.
        var deadline = options.Timeout is { } limit ? TimeSpan.FromSeconds(limit) + DeadlineMargin : (TimeSpan?)null;
        var done = Request(host, new ReplRequest(ReplOps.Exec, options.Session, code, options.Timeout, options.Strict), deadline, message =>
        {
            if (message.Kind is ReplEvents.Output or ReplEvents.Log)
            {
                (message.Kind == ReplEvents.Log ? log : output).Append(message.Text);
                // Godot's own prints read like stdout; its errors and warnings go to stderr.
                if (!Out.Mode.Json) (message.Error ? Out.ErrorWriter : Out.Writer).Write(message.Text);
                return;
            }
            if (message.Kind != ReplEvents.Result) return;
            var ok = message.Status == ReplStatus.Ok;
            results.Add(new ReportReplResult(message.Input ?? "", message.Status ?? ReplStatus.Error, ok ? message.Text : null,
                ok ? null : message.Text, Take(output), Take(log), message.Tree));
            if (Out.Mode.Json)
            {
                if (!ok) Out.Errors.Add(message.Text ?? $"submission {message.Status}");
                return;
            }
            if (ok && message.Text is { } value) Out.Writer.WriteLine(value);
            else if (message.Status == ReplStatus.Timeout)
                Out.ErrorLine($"timed out after {options.Timeout?.ToString(System.Globalization.CultureInfo.InvariantCulture)} s; " +
                              (message.Text ?? "cancelled, and the session and its earlier results remain"));
            else if (message.Status == ReplStatus.Cancelled) Out.ErrorLine(message.Text ?? "the submission was cancelled");
            else if (!ok) Out.ErrorWriter.WriteLine(message.Text);
        });
        repl.Status = done.Status;
        // Output that arrived after the last result, e.g. Godot's deferred errors, belongs to it.
        if (results is [.., var last] && (output.Length > 0 || log.Length > 0))
            results[^1] = last with { Output = Join(last.Output, Take(output)), Log = Join(last.Log, Take(log)) };
        if (done.Text is { } unanswered) Out.ErrorLine(unanswered);
        return done.Status switch
        {
            ReplStatus.Ok => ExitCodes.Ok,
            ReplStatus.Timeout => ExitCodes.TimedOut,
            ReplStatus.Cancelled => ExitCodes.Cancelled,
            _ => ExitCodes.SubmissionFailed,
        };
    }

    private static string? Join(string? first, string? second) => first is null ? second : second is null ? first : first + second;

    private static string? Take(StringBuilder text)
    {
        if (text.Length == 0) return null;
        var value = text.ToString();
        text.Clear();
        return value;
    }

    private static int Sessions(ReplHostInfo host, ReportRepl repl)
    {
        ReplSessionInfo[] sessions = [];
        Request(host, new ReplRequest(ReplOps.Sessions), null, message => sessions = message.Sessions ?? sessions);
        repl.Sessions = sessions.ToList();
        if (sessions.Length == 0) Out.Info($"no named sessions in REPL host '{host.Pipe}' - '2dog repl exec -s NAME' starts one");
        var rows = sessions.Select(s => new[]
        {
            s.Name, ":" + s.Mode, s.Scope ?? "(engine busy)", s.Worlds is null ? "?" : s.Worlds.Length == 0 ? "-" : string.Join(",", s.Worlds),
            s.Busy ? "busy" : "idle " + Duration(s.IdleSeconds),
        }).ToList();
        var widths = Enumerable.Range(0, 5).Select(column => rows.Select(row => row[column].Length).DefaultIfEmpty(0).Max()).ToArray();
        foreach (var row in rows) Out.Plain(string.Join("  ", row.Select((cell, column) => cell.PadRight(widths[column]))).TrimEnd());
        return ExitCodes.Ok;
    }

    private static string Duration(double seconds) => seconds switch
    {
        < 60 => $"{seconds:0}s",
        < 3600 => $"{seconds / 60:0}m",
        _ => $"{seconds / 3600:0.#}h",
    };

    private static int Close(ReplHostInfo host, string session, ReportRepl repl)
    {
        repl.Session = session;
        var done = Request(host, new ReplRequest(ReplOps.Close, session), null, _ => { });
        repl.Status = done.Status;
        Out.Plain(done.Text ?? $"closed session '{session}'");
        return ExitCodes.Ok;
    }

    /// <summary>Beyond --timeout: the host's own grace period for work that ignores cancellation, plus slack.</summary>
    internal static TimeSpan DeadlineMargin { get; set; } = TimeSpan.FromSeconds(10);

    private static ReplEvent Request(ReplHostInfo host, ReplRequest request, TimeSpan? deadline, Action<ReplEvent> onEvent)
        => RequestAsync(host.Pipe, request, onEvent, Cancellation.Token, deadline).GetAwaiter().GetResult();

    /// <summary>One request per connection; returns the final event. A failed request throws its message. Past the
    /// deadline, a synthetic timed-out "done" carries the explanation.</summary>
    internal static async Task<ReplEvent> RequestAsync(string pipe, ReplRequest request, Action<ReplEvent> onEvent, CancellationToken token,
        TimeSpan? deadline = null)
    {
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(token);
        if (deadline is { } limit) waiting.CancelAfter(limit);
        try { return await ExchangeAsync(pipe, request, onEvent, waiting.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && deadline is { } expired)
        {
            return new ReplEvent(ReplEvents.Done, $"REPL host '{pipe}' gave no answer within {expired.TotalSeconds:0} s; its engine thread may be " +
                                                  "blocked - '2dog repl sessions' shows whether it still responds", Status: ReplStatus.Timeout);
        }
        catch (IOException ex)
        {
            throw new ToolException($"the connection to REPL host '{pipe}' broke ({ex.Message}) - '2dog repl sessions' shows whether it still runs");
        }
    }

    private static async Task<ReplEvent> ExchangeAsync(string pipe, ReplRequest request, Action<ReplEvent> onEvent, CancellationToken token)
    {
        await using var stream = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try { await stream.ConnectAsync(5000, token).ConfigureAwait(false); }
        catch (TimeoutException) { throw new ToolException($"REPL host '{pipe}' did not accept a connection within 5 s"); }
        using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync((ReplProtocol.ExecHello + ReplProtocol.Version).AsMemory(), token).ConfigureAwait(false);
        await writer.WriteLineAsync(ReplProtocol.Serialize(request).AsMemory(), token).ConfigureAwait(false);
        while (await reader.ReadLineAsync(token).ConfigureAwait(false) is { } line)
        {
            ReplEvent message;
            try { message = ReplProtocol.Deserialize<ReplEvent>(line) ?? throw new JsonException("null event"); }
            catch (JsonException) { throw new ToolException($"REPL host '{pipe}' sent an unreadable reply: {line}"); }
            if (message.Kind != ReplEvents.Done)
            {
                onEvent(message);
                continue;
            }
            if (message is { Error: true, Text: { } failure }) throw new ToolException(failure);
            return message;
        }
        throw new ToolException($"REPL host '{pipe}' closed the connection before answering - '2dog repl sessions' shows whether it still runs");
    }
}

/// <summary>The listening REPL hosts of this user, from the files they publish while running.</summary>
internal static class ReplHosts
{
    /// <summary>Newest first. Files of hosts that have exited are removed on the way.</summary>
    internal static List<ReplHostInfo> Running(string root)
    {
        var hosts = new List<ReplHostInfo>();
        if (!Directory.Exists(root)) return hosts;
        foreach (var file in Directory.EnumerateFiles(root, "*.json"))
        {
            ReplHostInfo? host;
            // A host may be writing its file right now; a later run reads it.
            try { host = ReplProtocol.Deserialize<ReplHostInfo>(File.ReadAllText(file)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { continue; }
            if (host is null || !ReplDiscovery.IsValidName(host.Pipe ?? "")) continue;
            if (IsRunning(host)) hosts.Add(host);
            else TryDelete(file);
        }

        return hosts.OrderByDescending(h => h.Started).ToList();
    }

    /// <summary>The same process that published the file: a reused process id has a different start time.</summary>
    internal static bool IsRunning(ReplHostInfo host)
    {
        try
        {
            using var process = Process.GetProcessById(host.Pid);
            return !process.HasExited && Math.Abs((new DateTimeOffset(process.StartTime) - host.Started).TotalSeconds) < 1;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (Win32Exception) { return true; }
    }

    private static void TryDelete(string file)
    {
        try { File.Delete(file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Another client may be removing it too. */ }
    }

    /// <summary>
    /// A named pipe, else the host whose project contains the directory, else the only running host. Several
    /// candidates are listed rather than guessed between.
    /// </summary>
    internal static ReplHostInfo Find(string? pipe, string directory, string root)
    {
        var hosts = Running(root);
        ReplHostInfo host;
        if (pipe is not null)
        {
            host = hosts.FirstOrDefault(h => h.Pipe == pipe)
                   ?? throw new ToolException($"no running REPL host listens on '{pipe}'" + Listing(hosts, "running hosts"));
        }
        else
        {
            var here = hosts.Where(h => Contains(h.Project, directory)).ToList();
            var candidates = here.Count > 0 ? here : hosts;
            if (candidates.Count == 0)
                throw new ToolException("no REPL host is running - start one with 'dotnet run --project <Game>.repl'; a terminal " +
                                        "host listens automatically, a scripted one needs --repl-listen NAME");
            if (candidates.Count > 1)
                throw new ToolException($"{candidates.Count} REPL hosts are running{(here.Count > 0 ? " for this project" : "")} - " +
                                        "choose one with --pipe" + Listing(candidates, "hosts"));
            host = candidates[0];
            if (here.Count == 0) Out.Note($"using the only running REPL host, '{host.Pipe}' for {host.Project}");
        }

        if (host.Protocol != ReplProtocol.Version)
            throw new ToolException($"REPL host '{host.Pipe}' speaks protocol {host.Protocol}, this tool {ReplProtocol.Version} - " +
                                    "use the 2dog tool version matching the host's 2dog.repl package");
        return host;
    }

    private static string Listing(List<ReplHostInfo> hosts, string title) => hosts.Count == 0
        ? ""
        : $"; {title}: " + string.Join(", ", hosts.Select(h => $"{h.Pipe} ({h.Project})"));

    internal static bool Contains(string project, string directory)
    {
        if (string.IsNullOrEmpty(project)) return false;
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(project));
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        return path.Equals(root, comparison) || path.StartsWith(root + Path.DirectorySeparatorChar, comparison);
    }
}

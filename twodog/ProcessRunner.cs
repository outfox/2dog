using System.ComponentModel;
using System.Diagnostics;

namespace twodog.cli;

/// <summary>A subprocess to run: always with captured output, a bounded wait, and a label for the spinner.</summary>
internal sealed record ProcessRequest(
    string FileName,
    IReadOnlyList<string> Args,
    string WorkingDir,
    TimeSpan? Timeout = null,
    string? Label = null);

/// <summary>What a subprocess did: its combined output in order, how it ended, how long it took.</summary>
internal sealed record ProcessResult(
    string CommandLine,
    int ExitCode,
    IReadOnlyList<string> Output,
    TimeSpan Elapsed,
    bool TimedOut,
    bool Cancelled)
{
    public bool Ok => ExitCode == 0 && !TimedOut && !Cancelled;

    public IEnumerable<string> Tail(int lines) => Output.Skip(Math.Max(0, Output.Count - lines));

    /// <summary>How it ended, for messages: "exit 1", "timed out", "cancelled".</summary>
    public string Outcome => TimedOut ? "timed out" : Cancelled ? "cancelled" : $"exit {ExitCode}";
}

/// <summary>Runs subprocesses; doctor tests inject a fake.</summary>
internal interface IProcessRunner
{
    ProcessResult Run(ProcessRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// The real runner. Both streams are always redirected and merged in arrival order (a tty is never inherited, so
/// output stays capturable); a spinner runs while the label is shown; --verbose echoes every line instead.
/// </summary>
internal sealed class ProcessRunner : IProcessRunner
{
    public static readonly ProcessRunner Default = new();

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);

    public ProcessResult Run(ProcessRequest request, CancellationToken cancellationToken = default) =>
        request.Label is { } label
            ? Out.Status(label, () => RunCore(request, cancellationToken))
            : RunCore(request, cancellationToken);

    /// <summary>The dotnet muxer with the given arguments, in a directory.</summary>
    public static ProcessRequest Dotnet(string workingDir, string? label, TimeSpan? timeout, params string[] args) =>
        new(DotnetMuxer.Path, args, workingDir, timeout, label);

    private static ProcessResult RunCore(ProcessRequest request, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo(request.FileName)
        {
            WorkingDirectory = request.WorkingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        // ArgumentList, never a joined string: the runtime quotes spaces and quotes per platform.
        foreach (var arg in request.Args) psi.ArgumentList.Add(arg);
        psi.Environment["DOTNET_NOLOGO"] = "1";
        if (Out.Mode.Plain || Out.Mode.NoColor || Out.Mode.Console.OutputRedirected) psi.Environment["NO_COLOR"] = "1";

        var commandLine = Describe(request);
        Out.Verbose($"run: {commandLine} (in {request.WorkingDir})");

        var output = new List<string>();
        using var process = new Process { StartInfo = psi };
        void OnLine(object _, DataReceivedEventArgs e)
        {
            if (e.Data is null) return;
            lock (output) output.Add(e.Data);
            Out.Echo(e.Data);
        }

        process.OutputDataReceived += OnLine;
        process.ErrorDataReceived += OnLine;

        var watch = Stopwatch.StartNew();
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new ToolException($"could not start '{request.FileName}': {ex.Message}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var registration = cancellationToken.Register(() => TryKill(process));
        var timedOut = !process.WaitForExit((int)(request.Timeout ?? DefaultTimeout).TotalMilliseconds);
        if (timedOut) TryKill(process);
        // The parameterless wait drains the async readers, so the tail is complete.
        process.WaitForExit();
        watch.Stop();

        lock (output)
            return new ProcessResult(commandLine, process.ExitCode, output.ToList(), watch.Elapsed, timedOut,
                cancellationToken.IsCancellationRequested);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { /* already gone */ }
        catch (Win32Exception) { /* no permission or already gone */ }
    }

    /// <summary>The command as a human would type it (display only).</summary>
    internal static string Describe(ProcessRequest request)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(request.FileName);
        return string.Join(' ', request.Args.Select(Quote).Prepend(name));

        static string Quote(string arg) => arg.Length == 0 || arg.Any(char.IsWhiteSpace) ? $"\"{arg}\"" : arg;
    }

    /// <summary>The standard failure report: what ran, how it ended, its last lines, and how to see the rest.</summary>
    public static void ReportFailure(ProcessResult result, int tailLines = 10)
    {
        Out.ErrorLine($"'{result.CommandLine}' failed ({result.Outcome})");
        if (Out.Mode.Verbose) return;
        foreach (var line in result.Tail(tailLines)) Out.Detail(line);
        if (result.Output.Count > tailLines) Out.Hint("--verbose shows the full output");
    }
}

/// <summary>Locates the dotnet executable: the launcher's own, DOTNET_ROOT, then PATH.</summary>
internal static class DotnetMuxer
{
    private static readonly Lazy<string> Resolved = new(() => Resolve(Environment.GetEnvironmentVariable, File.Exists));

    public static string Path => Resolved.Value;

    internal static string Resolve(Func<string, string?> env, Func<string, bool> exists)
    {
        // Set by the dotnet host for tools it launches (dnx, dotnet tool run): the exact muxer already in use.
        if (env("DOTNET_HOST_PATH") is { Length: > 0 } host && exists(host)) return host;

        var exe = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        if (env("DOTNET_ROOT") is { Length: > 0 } root && exists(System.IO.Path.Combine(root, exe)))
            return System.IO.Path.Combine(root, exe);

        foreach (var dir in (env("PATH") ?? "").Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = System.IO.Path.Combine(dir.Trim('"'), exe);
            if (exists(candidate)) return candidate;
        }

        throw new ToolException("dotnet not found - install the .NET 10 SDK, or set DOTNET_HOST_PATH to the dotnet executable");
    }
}

using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Godot;
using Engine = twodog.Engine;

namespace twodog.GdUnit;

internal static class EmbeddedRunner
{
    public static void Import(string project, string logPath, CancellationToken cancellationToken = default,
        TimeSpan? timeout = null, string? projectAssemblyDirectory = null)
        => InvokeHelper(project, logPath, [], cancellationToken, timeout, projectAssemblyDirectory);

    public static void Export(string project, string output, string logPath)
        => InvokeHelper(project, logPath, ["--export-pack", "Probe", "--output", output]);

    public static string ListPack(string pack, string logPath)
        => InvokeHelper(null, logPath, ["--list-pack", pack]);

    private static string InvokeHelper(string? project, string logPath, string[] options,
        CancellationToken cancellationToken = default, TimeSpan? timeout = null, string? projectAssemblyDirectory = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var paths = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "gdunit.import.paths"));
        if (paths.Length != 4 || paths.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("Expected helper, editor native, API, and tools paths in gdunit.import.paths.");
        var start = new ProcessStartInfo(System.Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in new[] { "exec", paths[0], "--libgodot", paths[1], "--api-dir", paths[2], "--tools-dir", paths[3] }.Concat(options))
            start.ArgumentList.Add(argument);
        if (project is not null) start.ArgumentList.Add(project);
        // Import needs the same game assembly metadata as runtime scene loading.
        start.Environment["GODOT_PROJECT_ASSEMBLY_DIR"] = projectAssemblyDirectory ?? AppContext.BaseDirectory;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the import helper.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            process.WaitForExitAsync(cancellationToken).WaitAsync(timeout ?? TimeSpan.FromMinutes(2), cancellationToken).GetAwaiter().GetResult();
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
            File.WriteAllText(logPath, stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
        }
        var log = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
        if (process.ExitCode != 0 || Regex.IsMatch(log, @"(?m)^(SCRIPT ERROR|ERROR):"))
            throw new InvalidOperationException($"Godot import failed ({process.ExitCode}); see {logPath}\n{log}");
        return log;
    }

    // This method is synchronous deliberately: the thread that starts Godot also pumps and destroys it.
    public static JsonElement[] Run(StagedProject project, string artifactDirectory,
        Func<JsonElement[], string[]?>? selectTests = null, Action<JsonElement>? onMessage = null,
        CancellationToken cancellationToken = default, TimeSpan? timeoutLimit = null, string? projectAssemblyDirectory = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var engine = new Engine("2dog.gdunit", project.Root, "--headless", "--log-file", Path.Combine(artifactDirectory, "godot.log"))
        {
            CaptureErrors = true,
            ProjectAssemblyDir = projectAssemblyDirectory ?? AppContext.BaseDirectory
        };
        var messages = new List<JsonElement>();
        var timeout = Stopwatch.StartNew();
        try
        {
            engine.Start();
            Node? bridge = null;
            var selectionSent = false;
            var cancellationSent = false;
            var cancellationTime = TimeSpan.Zero;
            while (true)
            {
                if (!engine.Update()) throw new InvalidOperationException("Godot quit before the host received completion.");
                bridge ??= engine.Tree.Root.GetNodeOrNull<Node>("TestHost");
                if (bridge is not null)
                {
                    foreach (var payload in bridge.Call("drain_messages").AsStringArray())
                    {
                        var message = JsonSerializer.Deserialize<JsonElement>(payload);
                        messages.Add(message);
                        onMessage?.Invoke(message);
                    }
                    if (!selectionSent && selectTests is not null && bridge.Get("discovery_done").AsBool() && !bridge.Get("bridge_done").AsBool())
                    {
                        selectionSent = true;
                        var selected = selectTests(messages.Where(message => message.GetProperty("kind").GetString() == "discovered")
                            .Select(message => message.GetProperty("test")).ToArray());
                        if (selected is null) bridge.Call("finish_discovery");
                        else bridge.Call("begin_run", Variant.From(selected));
                    }
                    if (cancellationToken.IsCancellationRequested && !cancellationSent && !bridge.Get("bridge_done").AsBool())
                    {
                        cancellationSent = true;
                        cancellationTime = timeout.Elapsed;
                        bridge.Call("cancel_run");
                    }
                    if (bridge.Get("bridge_done").AsBool()) break;
                }
                if (cancellationSent && timeout.Elapsed - cancellationTime > TimeSpan.FromSeconds(5))
                    throw new OperationCanceledException(cancellationToken);
                if (timeout.Elapsed > (timeoutLimit ?? TimeSpan.FromMinutes(5)))
                    throw new TimeoutException("Embedded gdUnit exceeded the host timeout.");
                Thread.Sleep(1);
            }
        }
        finally
        {
            engine.Dispose();
            engine.Completion.GetAwaiter().GetResult();
            File.WriteAllText(Path.Combine(artifactDirectory, "events.json"), JsonSerializer.Serialize(messages, new JsonSerializerOptions { WriteIndented = true }));
        }
        cancellationToken.ThrowIfCancellationRequested();
        var discoveryError = messages.FirstOrDefault(message => message.GetProperty("kind").GetString() == "discovery_error");
        if (discoveryError.ValueKind != JsonValueKind.Undefined)
            throw new InvalidDataException("gdUnit discovery failed: " + discoveryError.GetProperty("message").GetString());
        var errors = engine.Errors.Drain();
        if (errors.Length > 0) throw new InvalidOperationException("Unexpected native diagnostics:\n" + string.Join("\n", errors));
        return messages.ToArray();
    }
}

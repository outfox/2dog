using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Godot;
using Engine = twodog.Engine;

namespace GdScriptHost;

internal static class EmbeddedRunner
{
    public static void Import(string project, string logPath)
        => InvokeHelper(project, logPath);

    public static void Export(string project, string output, string logPath)
        => InvokeHelper(project, logPath, "--export-pack", "Probe", "--output", output);

    public static string ListPack(string pack, string logPath)
        => InvokeHelper(null, logPath, "--list-pack", pack);

    private static string InvokeHelper(string? project, string logPath, params string[] options)
    {
        var paths = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "import.paths"));
        if (paths.Length != 4 || paths.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("Expected helper, editor native, API, and tools paths in import.paths.");
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
        start.Environment["GODOT_PROJECT_ASSEMBLY_DIR"] = AppContext.BaseDirectory;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the import helper.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new TimeoutException($"Import exceeded two minutes: {project}");
        }
        var log = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
        File.WriteAllText(logPath, log);
        if (process.ExitCode != 0 || Regex.IsMatch(log, @"(?m)^(SCRIPT ERROR|ERROR):"))
            throw new InvalidOperationException($"Godot import failed ({process.ExitCode}); see {logPath}\n{log}");
        return log;
    }

    // This method is synchronous deliberately: the thread that starts Godot also pumps and destroys it.
    public static JsonElement[] Run(StagedProject project, string artifactDirectory)
    {
        using var engine = new Engine("2dog.gdscript.probe", project.Root, "--headless", "--log-file", Path.Combine(artifactDirectory, "godot.log"))
        {
            CaptureErrors = true,
            ProjectAssemblyDir = AppContext.BaseDirectory
        };
        var messages = new List<JsonElement>();
        engine.Start();
        var timeout = Stopwatch.StartNew();
        try
        {
            Node? bridge = null;
            while (true)
            {
                if (engine.Iteration()) throw new InvalidOperationException("Godot quit before the host received completion.");
                bridge ??= engine.Tree.Root.GetNodeOrNull<Node>("TestHost");
                if (bridge is not null)
                {
                    foreach (var payload in bridge.Call("drain_messages").AsStringArray())
                        messages.Add(JsonSerializer.Deserialize<JsonElement>(payload));
                    if (bridge.Get("bridge_done").AsBool()) break;
                }
                if (timeout.Elapsed > TimeSpan.FromSeconds(30))
                    throw new TimeoutException("Embedded GdUnit did not complete within 30 seconds.");
                Thread.Sleep(1);
            }
        }
        finally
        {
            engine.Dispose();
            engine.Completion.GetAwaiter().GetResult();
            File.WriteAllText(Path.Combine(artifactDirectory, "events.json"), JsonSerializer.Serialize(messages, new JsonSerializerOptions { WriteIndented = true }));
        }
        var errors = engine.Errors.Drain();
        if (errors.Length > 0) throw new InvalidOperationException("Unexpected native diagnostics:\n" + string.Join("\n", errors));
        return messages.ToArray();
    }
}

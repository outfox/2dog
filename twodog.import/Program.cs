using System.Diagnostics;

string? editorPath = null;
string? projectPath = null;

for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--editor" && i + 1 < args.Length)
        editorPath = args[++i];
    else if (projectPath == null)
        projectPath = args[i];
}

editorPath ??= Environment.GetEnvironmentVariable("GODOT_EDITOR");
projectPath = projectPath != null ? Path.GetFullPath(projectPath) : null;

if (editorPath == null || projectPath == null || !File.Exists(Path.Combine(projectPath, "project.godot")))
{
    Console.Error.WriteLine("Usage: twodog.import [--editor <godot-binary>] <path-to-godot-project>");
    Console.Error.WriteLine();
    Console.Error.WriteLine("  --editor <path>   Path to Godot editor binary");
    Console.Error.WriteLine("                     Falls back to GODOT_EDITOR environment variable");
    Console.Error.WriteLine();
    Console.Error.WriteLine("  The project path must contain a project.godot file.");
    return 1;
}

if (!File.Exists(editorPath))
{
    Console.Error.WriteLine($"Editor binary not found: {editorPath}");
    return 1;
}

var process = new Process
{
    StartInfo = new ProcessStartInfo
    {
        FileName = editorPath,
        ArgumentList = { "--headless", "--import", "--path", projectPath },
        UseShellExecute = false,
    }
};

process.Start();
process.WaitForExit();
return process.ExitCode;

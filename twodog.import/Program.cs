using twodog;

// Parse arguments
string? projectPath = null;

for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--path" && i + 1 < args.Length)
        projectPath = args[++i];
    else if (projectPath == null && !args[i].StartsWith("--"))
        projectPath = args[i];
}

// Default to current directory if no path specified
projectPath ??= Directory.GetCurrentDirectory();
projectPath = Path.GetFullPath(projectPath);

// Validate project directory
if (!File.Exists(Path.Combine(projectPath, "project.godot")))
{
    Console.Error.WriteLine("Usage: 2dog-import [--path] <path-to-godot-project>");
    Console.Error.WriteLine();
    Console.Error.WriteLine("  <path-to-godot-project>  Path to directory containing project.godot");
    Console.Error.WriteLine("                           Defaults to current directory if not specified");
    Console.Error.WriteLine();
    Console.Error.WriteLine($"Error: No project.godot found in: {projectPath}");
    return 1;
}

Console.WriteLine($"Importing Godot project: {projectPath}");
Console.WriteLine("This may take a moment...");
Console.WriteLine();

try
{
    // Create and start Godot engine in editor mode with import flag
    // The first argument is the application name, followed by --path and the project directory
    using var engine = new Engine("2dog-import", projectPath, "--headless", "--import", "--quit");
    using var godot = engine.Start();

    // Run until Godot quits (import completes)
    while (!godot.Iteration())
    {
        // Let Godot run its import process
    }

    Console.WriteLine();
    Console.WriteLine("Import completed successfully!");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"Import failed: {ex.Message}");
    return 1;
}

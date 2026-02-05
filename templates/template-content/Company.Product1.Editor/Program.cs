using twodog;

// Parse arguments
string? projectPath = null;
var additionalArgs = new List<string>();

for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--path" && i + 1 < args.Length)
        projectPath = args[++i];
    else
        additionalArgs.Add(args[i]);
}

// Resolve project directory from assembly metadata if not specified
projectPath ??= Engine.ResolveProjectDir();
projectPath = Path.GetFullPath(projectPath);

// Validate project directory
if (!File.Exists(Path.Combine(projectPath, "project.godot")))
{
    Console.Error.WriteLine($"Error: No project.godot found in: {projectPath}");
    return 1;
}

Console.WriteLine($"Starting Godot Editor for project: {projectPath}");

try
{
    // Create and start Godot engine in editor mode
    var engineArgs = new List<string> { "--headless" };
    engineArgs.AddRange(additionalArgs);

    using var engine = new Engine("TPLRAWNAME.Editor", projectPath, engineArgs.ToArray());
    using var godot = engine.Start();

    // Run until Godot quits
    while (!godot.Iteration())
    {
        // Let Godot run
    }

    Console.WriteLine("Editor closed.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Editor failed: {ex.Message}");
    return 1;
}

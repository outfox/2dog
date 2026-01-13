using System.Reflection;
using Engine = twodog.Engine;

// Resolve the project path relative to the assembly location, not the current working directory
var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
var projectPath = Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "..", "..", "project"));

using var engine = new Engine("demo", projectPath);
using var godotInstance = engine.Start();

// You can access the SceneTree via engine.Tree

Console.WriteLine("Godot is running, close window or press 'Q' to quit.");
while (!godotInstance.Iteration())
{
    if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Q) break;
}

Console.WriteLine("Godot is shutting down. Thank you for using 2dog. 🦴");

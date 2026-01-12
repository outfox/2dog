using Engine = twodog.Engine;

using var engine = new Engine("demo", "project");
using var godotInstance = engine.Start();

// You can access the SceneTree via engine.Tree

Console.WriteLine("Godot is running, close window or press 'Q' to quit.");
while (!godotInstance.Iteration())
{
    if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Q) break;
}

Console.WriteLine("Godot is shutting down. Thank you for using 2dog. 🦴");

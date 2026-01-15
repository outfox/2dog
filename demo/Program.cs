using System.Reflection;
using Godot;
using Engine = twodog.Engine;

// Resolve the project path relative to the assembly location
var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
var projectPath = "../game";

using var engine = new Engine("demo", projectPath);
using var godotInstance = engine.Start();
GD.Print("Hello from GodotSharp.");
GD.Print("Scene Root: ", engine.Tree.CurrentScene.Name);
GD.Print("Ticker: ", engine.Tree.CurrentScene.GetNode<Ticker>("Ticker"));

// You can access the SceneTree via engine.Tree


Console.WriteLine("Godot is running, close window or press 'Q' to quit.");
Console.WriteLine("Godot is will iterate twice...");

while (!godotInstance.Iteration())
    if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Q)
        break;
godotInstance.Iteration();
godotInstance.Iteration();
Console.WriteLine("Godot is shutting down. Thank you for using 2dog. 🦴");
using Engine = twodog.Engine;

using var engine = new Engine("game", "project");
using var godotInstance = engine.Start();

// Run until quit or finished
var finished = godotInstance.Iteration();
Console.WriteLine("Godot is running, close window or press 'Q' to quit.");
while (!finished && (!Console.KeyAvailable || Console.ReadKey(true).Key != ConsoleKey.Q))
{
    finished = godotInstance.Iteration();
}

Console.WriteLine("Godot is shutting down.");


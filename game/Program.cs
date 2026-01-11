using Engine = twodog.Engine;

using var engine = new Engine("game", ".");
using var godotInstance = engine.Start();

// Run until quit or finished
var finished = godotInstance.Iteration();
while (!finished && !Console.KeyAvailable)
{
    finished = godotInstance.Iteration();
}

Console.WriteLine("Godot is shutting down.");


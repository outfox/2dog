using Godot;
using Engine = twodog.Engine;

using var engine = new Engine("demo", Engine.ResolveProjectDir());
using var godotInstance = engine.Start();
GD.Print("Hello from GodotSharp.");
GD.Print("Scene Root: ", engine.Tree.CurrentScene.Name);

var ticker = engine.Tree.CurrentScene.GetNode<Ticker>("Ticker");
GD.Print("Ticker: ", ticker);

// You can access the SceneTree via engine.Tree


Console.WriteLine("Godot is running, close window or press 'Q' to quit.");

while (!godotInstance.Iteration())
{
    if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Q)
        break;
}

Console.WriteLine($"Engine exited after {ticker.localAccumulator} ticks. (iterations / _process calls)");

Console.WriteLine("Godot is shutting down. Thank you for using 2dog. 🦴");
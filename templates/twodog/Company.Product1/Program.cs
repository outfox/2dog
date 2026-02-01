using Godot;
using Engine = twodog.Engine;

// Create and start the Godot engine with your project
using var engine = new Engine("Company.Product1", Engine.ResolveProjectDir());
using var godot = engine.Start();

// Load your main scene
var scene = GD.Load<PackedScene>("res://main.tscn");
engine.Tree.Root.AddChild(scene.Instantiate());

GD.Print("2dog is running! Close window or press 'Q' to quit.");
Console.WriteLine("Press 'Q' to quit.");

// Main game loop - runs until window closes or 'Q' is pressed
while (!godot.Iteration())
{
    if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Q)
        break;
    
    // Your per-frame logic here
}

Console.WriteLine("Shutting down...");

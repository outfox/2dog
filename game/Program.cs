using twodog;

var engine = new Engine("game", ".");
using var godotInstance = engine.Start();

Console.WriteLine("Godot is running. Press Enter to quit.");
Console.ReadLine(); 


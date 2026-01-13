namespace twodog.tests;

public class GodotFixture : IDisposable
{
    public GodotFixture()
    {
        // Initialize libgodot once
        Console.WriteLine("Initializing Godot...");
        //TODO: Apply code as seen in demo...
    }

    public void Dispose()
    {
        // Cleanup libgodot once
        Console.WriteLine("Shutting down Godot...");
        //TODO: Apply code as seen in demo...
    }
}
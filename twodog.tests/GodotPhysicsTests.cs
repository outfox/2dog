namespace twodog.tests;

[Collection("Godot")]
public class GodotPhysicsTests
{
    private readonly GodotFixture _godot;

    public GodotPhysicsTests(GodotFixture godot)
    {
        _godot = godot;
    }

    [Fact]
    public void Physics_StepSimulation_Works()
    {
        // Same fixture instance as above
    }
}
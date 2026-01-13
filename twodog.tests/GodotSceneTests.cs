namespace twodog.tests;

[Collection("Godot")]
public class GodotSceneTests
{
    private readonly GodotFixture _godot;

    public GodotSceneTests(GodotFixture godot)
    {
        _godot = godot; // Injected by xUnit
    }

    [Fact]
    public void LoadScene_ValidPath_Succeeds()
    {
        // Use _godot fixture here
    }
}
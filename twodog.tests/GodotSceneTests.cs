namespace twodog.tests;

[Collection("Godot")]
public class GodotSceneTests(GodotHeadlessFixture godot)
{
    [Fact]
    public void LoadScene_ValidPath_Succeeds()
    {
        // Use _godot fixture here
    }
}
using Godot;

public partial class Smoke : Node
{
    public override void _Ready()
    {
        if (Name != "Smoke" || GetParent() != GetTree().Root)
            throw new System.InvalidOperationException("Smoke script is not bound to the scene root.");
        GD.Print("2DOG_ANDROID_CSHARP_SMOKE_PASSED");
    }
}

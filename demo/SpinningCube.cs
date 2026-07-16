using Godot;

// Blue cubes: spun by the Godot project itself, once per _Process frame.
// The red cubes do the same from GDScript (spinning_cube.gd); the white cubes
// have no script - the 2dog host rotates them from its run loop.
[GlobalClass]
public partial class SpinningCube : MeshInstance3D
{
    [Export] public Vector3 SpinAxis { get; set; } = Vector3.Up;
    [Export] public float SpinSpeed { get; set; } = 1.5f; // radians per second

    public override void _Process(double delta)
    {
        Rotate(SpinAxis.Normalized(), SpinSpeed * (float)delta);
    }
}

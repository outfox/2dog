using Godot;

[Tool]
public partial class SetupNode : Node3D
{
    public override void _Ready()
    {
        for (int i = 0; i < 100; i++)
        {
            Node3D node = (i % 8) switch
            {
                0 => new Node3D(),
                1 => new Camera3D(),
                2 => new OmniLight3D(),
                3 => new MeshInstance3D(),
                4 => new Marker3D(),
                5 => new PinJoint3D(),
                6 => new Path3D(),
                _ => new Skeleton3D(),
            };
            node.Name = $"Lifetime{i}";
            AddChild(node);
            node.Owner = this;
        }
    }
}

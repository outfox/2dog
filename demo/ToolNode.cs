using Godot;

[Tool]
[GlobalClass]
public partial class ToolNode : Node
{
    [Export]
    public bool ReadyCalled { get; set; }

    [Export]
    public int ProcessCount { get; set; }

    public override void _Ready()
    {
        ReadyCalled = true;
    }

    public override void _Process(double delta)
    {
        ProcessCount++;
    }
}

using Godot;

[GlobalClass]
public partial class Ticker : Node
{
    public static int staticAccumulator;
    public int localAccumulator;

    public override void _Process(double delta)
    {
        ++localAccumulator;
        ++staticAccumulator;
    }
}
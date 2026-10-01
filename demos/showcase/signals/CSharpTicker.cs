using Godot;

// Signal table source: a C# [Signal], emitted once per second. Its siblings emit from GDScript
// (gdscript_ticker.gd), an engine Timer and the C GDExtension (TwoDogTicker).
namespace showcase;

[GlobalClass]
public partial class CSharpTicker : Node
{
    [Signal] public delegate void TickedEventHandler(int count);

    [Export] public double Interval { get; set; } = 1.0; // seconds

    public int Count { get; private set; }

    private double _elapsed;

    public override void _Process(double delta)
    {
        _elapsed += delta;
        if (_elapsed < Interval) return;
        _elapsed -= Interval;
        Tick();
    }

    public int Tick()
    {
        EmitSignal(SignalName.Ticked, ++Count);
        return Count;
    }
}

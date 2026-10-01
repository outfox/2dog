using Godot;

// Signal table cell: counts what one source emits, received in C# (signal_counter.gd is the GDScript column).
// Each source kind is connected the way C# code usually does it.
namespace showcase;

[GlobalClass]
public partial class SignalCounter : Label
{
    [Export] public NodePath Source { get; set; }

    public int Received { get; private set; }

    // The tickers' payload; Timer.timeout has none.
    public int LastCount { get; private set; } = -1;

    public override void _Ready()
    {
        switch (GetNodeOrNull(Source))
        {
            case CSharpTicker ticker:
                ticker.Ticked += OnTicked; // the [Signal]'s generated event
                break;
            case Timer timer:
                timer.Timeout += OnTimeout; // GodotSharp's event for an engine signal
                break;
            case { } node when node.HasSignal("ticked"):
                node.Connect("ticked", Callable.From<int>(OnTicked)); // GDScript and GDExtension signals, by name
                break;
            default:
                Text = "n/a";
                return;
        }
        Text = "0";
    }

    private void OnTicked(int count)
    {
        LastCount = count;
        Count();
    }

    private void OnTimeout() => Count();

    private void Count() => Text = (++Received).ToString();
}

using Godot;

namespace twodog.bindings.tests;

// Referenced by shim_scene.tscn as res://ShimScriptNode.cs AND compiled into the
// test assembly - the dual nature every gdext script class has.
public partial class ShimScriptNode : Node
{
    [Export] public int Sprouts { get; set; } = 3;
    [Export] public string[] Tags { get; set; } = [];

    [Signal] public delegate void SproutedEventHandler();

    public int Frames;
    public bool ReadyRan;

    public override void _Ready() => ReadyRan = true;

    public override void _Process(double delta) => Frames++;

    public int Bump(int by) => Sprouts += by;
}

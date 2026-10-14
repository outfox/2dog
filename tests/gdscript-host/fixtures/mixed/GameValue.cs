using Godot;

namespace GdScriptHostFixture;

public partial class GameValue : Node
{
    [Export] public int Value { get; set; } = 42;

    public int DoubleValue() => Value * 2;
}

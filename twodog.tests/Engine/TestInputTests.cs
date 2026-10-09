using Godot;
using twodog.Testing;
using twodog.Testing.Xunit;

namespace twodog.tests.EngineTests;

[Collection<HeadlessCollection>]
public class TestInputTests(HeadlessFixture godot)
{
    [Fact]
    public void CollectionFixtureClicksRespectForegroundControls()
    {
        var viewport = new SubViewport { Size = new Vector2I(300, 200) };
        try
        {
            godot.Tree.Root.AddChild(viewport);
            var button = new Button { Position = new Vector2(20, 20), Size = new Vector2(100, 50) };
            viewport.AddChild(button);
            var overlay = new Control { Position = button.Position, Size = button.Size };
            viewport.AddChild(overlay);
            godot.Engine.Iteration();
            using var pressed = GodotAssert.ExpectSignal(button, BaseButton.SignalName.Pressed);
            godot.Input.Click(button);
            pressed.AssertEmitted(0);
            overlay.Free();
            godot.Input.Click(button);
            pressed.AssertEmitted();
        }
        finally { viewport.Free(); }
    }

    [Fact]
    public void CollectionFixtureCanSimulateGlobalInputPolling()
    {
        using var down = new InputEventKey { Keycode = Key.F13, Pressed = true };
        using var up = new InputEventKey { Keycode = Key.F13, Pressed = false };
        try
        {
            godot.Input.Send(down);
            Assert.True(Input.IsKeyPressed(Key.F13));
        }
        finally { godot.Input.Send(up); }
        Assert.False(Input.IsKeyPressed(Key.F13));
    }
}

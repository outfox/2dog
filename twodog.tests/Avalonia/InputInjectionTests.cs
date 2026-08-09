using Godot;
using twodog.Testing;
using twodog.Testing.Xunit;

namespace twodog.tests.AvaloniaTests;

// Validates the injection pattern the Avalonia input forwarder relies on: synthesize a
// Godot InputEvent, hand it to the forwarder's own Dispatch (Input.ParseInputEvent plus
// immediate disposal - the engine's buffered Ref keeps the native object alive), and
// observe the state after the next iteration flushes the buffer.
[Collection<HeadlessCollection>]
public class InputInjectionTests(HeadlessFixture godot)
{
    private static void Inject(InputEvent ev) => GodotInputForwarder.Dispatch(ev);

    [Fact]
    public void KeyPress_IsObservable_AfterFlush()
    {
        Inject(new InputEventKey { Keycode = Key.F13, PhysicalKeycode = Key.F13, Pressed = true });
        godot.GodotInstance.Iteration();
        Assert.True(Input.IsKeyPressed(Key.F13));

        Inject(new InputEventKey { Keycode = Key.F13, PhysicalKeycode = Key.F13, Pressed = false });
        godot.GodotInstance.Iteration();
        Assert.False(Input.IsKeyPressed(Key.F13));
    }

    [Fact]
    public void MouseButton_UpdatesButtonMask()
    {
        var pos = new Vector2(10, 10);
        Inject(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left, Pressed = true,
            Position = pos, GlobalPosition = pos, ButtonMask = MouseButtonMask.Left,
        });
        godot.GodotInstance.Iteration();
        Assert.True(Input.IsMouseButtonPressed(MouseButton.Left));

        Inject(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left, Pressed = false,
            Position = pos, GlobalPosition = pos,
        });
        godot.GodotInstance.Iteration();
        Assert.False(Input.IsMouseButtonPressed(MouseButton.Left));
    }

    [Fact]
    public void MouseMotion_Burst_SurvivesImmediateDisposal()
    {
        // Fire-hose synthesis: disposing each wrapper right after injection must never
        // race the engine's GCHandle bookkeeping (regression: fatal 0xC0000005 /
        // "Handle is not initialized" when wrappers were left to the finalizer).
        // Delivery is observed via the root window's input signal. Input accumulation may
        // merge same-frame motions, but it sums Relative and keeps the last Position, so
        // both survive as invariants of the injected stream.
        var relativeSum = Vector2.Zero;
        var lastPosition = Vector2.Zero;
        Window.WindowInputEventHandler onInput = ev =>
        {
            if (ev is InputEventMouseMotion motion)
            {
                relativeSum += motion.Relative;
                lastPosition = motion.Position;
            }
        };

        godot.Tree.Root.WindowInput += onInput;
        try
        {
            for (var frame = 0; frame < 10; frame++)
            {
                for (var i = 0; i < 100; i++)
                {
                    var pos = new Vector2(i, frame);
                    Inject(new InputEventMouseMotion
                    {
                        Position = pos, GlobalPosition = pos,
                        Relative = new Vector2(1, 0), ButtonMask = 0,
                    });
                }
                godot.GodotInstance.Iteration();
            }
        }
        finally
        {
            godot.Tree.Root.WindowInput -= onInput;
        }

        Assert.Equal(new Vector2(1000, 0), relativeSum);
        Assert.Equal(new Vector2(99, 9), lastPosition);
    }
}

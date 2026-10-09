using System;
using Godot;

namespace twodog.Testing;

/// <summary>Simulates input through Godot's input server or viewport router, without calling control handlers.</summary>
public sealed class TestInput
{
    private readonly FixtureBase _fixture;
    private readonly int _ownerThread = System.Environment.CurrentManagedThreadId;

    internal TestInput(FixtureBase fixture) => _fixture = fixture;

    /// <summary>
    /// Parses a device-style event through Godot's input server and flushes buffered events immediately.
    /// Updates global Input polling state and uses normal window delivery. Does not advance a frame.
    /// The caller owns the event; send a fresh event object for each press and release.
    /// </summary>
    public void Send(InputEvent inputEvent)
    {
        CheckThread();
        Validate(inputEvent, nameof(inputEvent));
        ValidateViewport(_fixture.Tree.Root);
        Godot.Input.ParseInputEvent(inputEvent);
        Godot.Input.FlushBufferedEvents();
    }

    /// <summary>
    /// Routes an event through a viewport's input, GUI and unhandled-input stages, in viewport coordinates.
    /// Defaults to the fixture's root viewport. Preserves input suppression and event consumption, but does
    /// not update global Input polling state or simulate forwarding from an outer viewport. The caller owns the event.
    /// </summary>
    public void PushToViewport(InputEvent inputEvent, Viewport? viewport = null)
    {
        CheckThread();
        Validate(inputEvent, nameof(inputEvent));
        viewport ??= _fixture.Tree.Root;
        ValidateViewport(viewport);
        viewport.PushInput(inputEvent, inLocalCoords: true);
    }

    /// <summary>
    /// Aims at a control's center (or a control-local point) and clicks through its owning viewport.
    /// Uses the control's canvas transform. Overlays, clipping, disabled controls and input filters still apply;
    /// the control is not guaranteed to receive the click. Does not update global Input polling state.
    /// </summary>
    public void Click(Control control, Vector2? localPosition = null, MouseButton button = MouseButton.Left)
    {
        CheckThread();
        Validate(control, nameof(control));
        if (!control.IsInsideTree())
            throw new ArgumentException("The control must be inside the fixture's scene tree.", nameof(control));
        var viewport = control.GetViewport();
        ValidateViewport(viewport);
        Click(control.GetGlobalTransformWithCanvas() * (localPosition ?? control.Size / 2), viewport, button);
    }

    /// <summary>
    /// Sends mouse motion, press and release through the given viewport at a viewport-local point.
    /// Defaults to the fixture's root viewport. Does not advance frames, change focus explicitly, or update global Input state.
    /// </summary>
    public void Click(Vector2 position, Viewport? viewport = null, MouseButton button = MouseButton.Left)
    {
        CheckThread();
        viewport ??= _fixture.Tree.Root;
        ValidateViewport(viewport);
        var mask = button switch
        {
            MouseButton.Left => MouseButtonMask.Left,
            MouseButton.Right => MouseButtonMask.Right,
            MouseButton.Middle => MouseButtonMask.Middle,
            MouseButton.Xbutton1 => MouseButtonMask.MbXbutton1,
            MouseButton.Xbutton2 => MouseButtonMask.MbXbutton2,
            _ => throw new ArgumentOutOfRangeException(nameof(button), "Use a mouse button, not a wheel event.")
        };
        using var motion = new InputEventMouseMotion { Position = position, GlobalPosition = position };
        viewport.PushInput(motion, inLocalCoords: true);
        using var press = new InputEventMouseButton
        {
            Position = position, GlobalPosition = position, ButtonIndex = button, ButtonMask = mask, Pressed = true
        };
        try { viewport.PushInput(press, inLocalCoords: true); }
        finally
        {
            // A handler may have removed or freed the viewport during the press.
            if (GodotObject.IsInstanceValid(viewport) && viewport.IsInsideTree())
            {
                using var release = new InputEventMouseButton
                {
                    Position = position, GlobalPosition = position, ButtonIndex = button, Pressed = false
                };
                viewport.PushInput(release, inLocalCoords: true);
            }
        }
    }

    private void CheckThread()
    {
        if (System.Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("Simulate input on the thread that owns the Godot fixture.");
    }

    private static void Validate(GodotObject value, string parameter)
    {
        if (!GodotObject.IsInstanceValid(value))
            throw new ArgumentException("A live Godot object is required.", parameter);
    }

    private void ValidateViewport(Viewport viewport)
    {
        Validate(viewport, nameof(viewport));
        if (!viewport.IsInsideTree() || viewport.GetTree() != _fixture.Tree)
            throw new ArgumentException("The viewport must be inside the fixture's scene tree.", nameof(viewport));
    }
}

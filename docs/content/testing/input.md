---
title: Simulating Input
description: "Click Godot controls and send keyboard events in xUnit and NUnit tests."
tagline: "Learn how to click buttons and press keys in your Godot tests."
---

# Simulating input

Use the fixture's `Input` helper to interact with your game through Godot's
input system. The examples use `godot`; in NUnit, add `var godot = EngineFixture;`
inside your test.

## Click a control

Run one engine iteration to update the layout. Click the control. Then check
the result:

```csharp
godot.Engine.Iteration();
using var pressed = GodotAssert.ExpectSignal(playButton, BaseButton.SignalName.Pressed);
godot.Input.Click(playButton);
pressed.AssertEmitted();
```

`Click(control)` aims at the control's center in its owning viewport. Godot
handles hit testing and event consumption, so an overlay or input handler can
block the click. The helper sends motion, press, and release events; it does
not advance frames.

| Aim at                       | Call                                                                |
|------------------------------|---------------------------------------------------------------------|
| A point within a control     | `godot.Input.Click(playButton, localPosition: new Vector2(10, 10))` |
| A point in the root viewport | `godot.Input.Click(new Vector2(80, 40))`                            |
| A point in another viewport  | `godot.Input.Click(new Vector2(80, 40), viewport: gameViewport)`    |

::: tip Give headless UI tests enough room
In headless mode, the root window can start at its minimum size. This can make
layout and hit testing incorrect. Before you load your scene, set the root
window to the project's viewport size. Then run one engine iteration to update
the layout:

```csharp
var root = godot.Tree.Root;
var originalSize = root.Size;
try
{
    root.Size = new Vector2I(
        ProjectSettings.GetSettingWithOverride("display/window/size/viewport_width").AsInt32(),
        ProjectSettings.GetSettingWithOverride("display/window/size/viewport_height").AsInt32());
    // Add your scene here.
    godot.Engine.Iteration();
    // Click controls and assert the results.
}
finally { root.Size = originalSize; }
```

The fixture is shared, so remove your scene and restore the size afterwards.
For isolated controls, use a `SubViewport` with an explicit size:

```csharp
var viewport = new SubViewport { Size = new Vector2I(400, 300) };
try
{
    godot.Tree.Root.AddChild(viewport);
    // Add your controls here, then advance a frame before clicking.
}
finally { viewport.Free(); }
```
:::

::: details What can block a click?
Godot handles the click normally, so each of these can block it:

- Foreground controls
- Mouse filters
- Clipping
- Hidden or disabled controls
- Paused processing
- Viewport input suppression

An `_Input` handler can consume the event before the GUI receives it. To
confirm that the click arrived, check a signal or a state change.

The control overload sends the click directly to the viewport that owns the
control. To test an outer overlay or `SubViewportContainer` forwarding, click a
point in the outer viewport's coordinates. To test desktop window focus or OS
event delivery, write a windowed integration test.
:::

## Send keyboard or custom events

`PushToViewport` sends an `InputEvent` in viewport coordinates. Keyboard events
go to the control that has focus. To test how a control gets focus, first click
it or press Tab until it has focus.

```csharp
using var down = new InputEventKey { Keycode = Key.Space, Pressed = true };
using var up = new InputEventKey { Keycode = Key.Space, Pressed = false };
try { godot.Input.PushToViewport(down, gameViewport); }
finally { godot.Input.PushToViewport(up, gameViewport); }
```

Construct touch, drag, or wheel events the same way. Dispose custom events
that your test creates.

::: details Hover in a standalone SubViewport
For a manually managed `SubViewport`, call `viewport.NotifyMouseEntered()`
when the pointer enters and `NotifyMouseExited()` when it leaves. Windows and
`SubViewportContainer` normally handle these notifications during forwarding;
the helper does not send them with every click.
:::

## Test code that polls Input

Choose the delivery method based on what your game reads:

| Method                     | Delivery                                | Updates global `Godot.Input` state? |
|----------------------------|-----------------------------------------|-------------------------------------|
| `Click` / `PushToViewport` | Local viewport routing                  | No                                  |
| `Send`                     | Godot's input server and window routing | Yes                                 |

If your game polls `IsKeyPressed`, `IsMouseButtonPressed`, or input actions,
use `Send`:

```csharp
using var down = new InputEventKey { Keycode = Key.F13, Pressed = true };
using var up = new InputEventKey { Keycode = Key.F13, Pressed = false };
try
{
    godot.Input.Send(down);
    // Godot.Input.IsKeyPressed(Key.F13) is now true.
    godot.Engine.Iteration(); // Let game code react.
}
finally { godot.Input.Send(up); }
```

## Spoof mouse movement

Send a mouse-motion event to test hover, `_Input`, or code that polls the
root viewport's mouse position:

```csharp
using var motion = new InputEventMouseMotion
{
    Position = new Vector2(80, 40),
    GlobalPosition = new Vector2(80, 40),
    Relative = new Vector2(10, 0),
    ScreenRelative = new Vector2(10, 0)
};
godot.Input.Send(motion);
```

`Send` uses window coordinates. If the project uses stretch scaling,
convert a logical viewport point with
`godot.Tree.Root.GetFinalTransform() * viewportPosition`. If the code under test
uses movement deltas, for example a camera, set `Relative`.

`PushToViewport(motion, viewport)` uses viewport coordinates and delivers
motion and hover too. It leaves global `Input` state unchanged, including the
mouse position used by the headless root's `GetMousePosition()`. A standalone
`SubViewport` tracks its local mouse position from the events it receives.
Neither method moves the OS cursor; mouse warping is unnecessary for these
tests.

::: warning Release held input
Always release keys, actions, and mouse buttons in a `finally` block. The
fixture is shared between tests, so held input affects the next test. Use a
new event for each press and for each release. Do not send one event through
both delivery methods, because Godot then receives it twice.
:::

::: details Input-server behavior
`Send` calls `Input.ParseInputEvent` and immediately flushes buffered input,
including other pending events. The project's input emulation settings and
window routing apply. Coordinates use Godot's device and window conventions. If
a GUI control consumes the event, Godot does not clear the global polling state.

See Godot's [viewport delivery](https://docs.godotengine.org/en/stable/classes/class_viewport.html#class-viewport-method-push-input)
and [input server](https://docs.godotengine.org/en/stable/classes/class_input.html#class-input-method-parse-input-event)
references for the underlying APIs.
:::
